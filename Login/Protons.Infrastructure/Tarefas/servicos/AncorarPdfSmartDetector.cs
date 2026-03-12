using System.Globalization;
using System.Text;
using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Services;

namespace Protons.Infrastructure.Tarefas.Services;

/// <summary>
/// Implementação do detector automático de variáveis por smart click.
/// Algoritmo puro (sem I/O): hit-test → agrupamento → classificação → label → sugestão.
/// Usa regexes do AncorarPdfRegexCatalog (source-generated, zero alocações).
/// </summary>
public sealed class AncorarPdfSmartDetector : IAncorarPdfSmartDetector
{
    // Tolerância de eixo Y para palavras na mesma linha (1.5% da altura da página).
    private const double ToleranciaLinhaRel = 0.015;

    // Gap horizontal máximo entre palavras do mesmo grupo (2.5% da largura).
    private const double MaxGapHorizontalRel = 0.025;

    // Gap vertical máximo para busca de label acima (3 alturas de linha típica ~= 5% da altura).
    private const double MaxGapLabelAcimaRel = 0.07;

    // Comprimento máximo do label (chars).
    private const int MaxLabelChars = 50;

    public SmartDeteccaoResultado Detectar(
        IReadOnlyList<PdfPaginaTexto> paginas,
        SmartDeteccaoEntrada entrada)
    {
        if (paginas.Count == 0)
            return SmartDeteccaoResultado.NaoEncontrado("sem_paginas");

        // Selecionar página (1-based → 0-based index, clamped).
        var pageIdx = Math.Clamp(entrada.Pagina - 1, 0, paginas.Count - 1);
        var pagina = paginas[pageIdx];

        if (pagina.Palavras.Count == 0)
            return SmartDeteccaoResultado.NaoEncontrado("pagina_sem_palavras");

        // 1. HitTest: palavra(s) que contêm o ponto de clique.
        var hitWord = EncontrarPalavraNoClique(pagina.Palavras, entrada.XRel, entrada.YRel);
        if (hitWord is null)
            return SmartDeteccaoResultado.NaoEncontrado("nenhum_texto_no_ponto");

        // 2. Agrupar palavras adjacentes na mesma linha.
        var grupo = AgruparPalavrasNaLinha(hitWord, pagina.Palavras);

        // 3. Construir candidatos de texto (com e sem espaço entre palavras).
        var textComEspaco = string.Join(" ", grupo.Palavras.Select(p => p.Texto));
        var textSemEspaco = string.Concat(grupo.Palavras.Select(p => p.Texto));

        // 4. Classificar.
        var classificacao = Classificar(textComEspaco, textSemEspaco);

        // TextoBruto: usa o valor extraído pelo regex quando disponível (evita retornar
        // o grupo inteiro quando o valor está imerso numa linha com muitas outras palavras).
        var textoBruto = classificacao.TextoExtraido ?? textComEspaco;

        // 5. Normalizar texto (usa o valor extraído, não o grupo completo).
        var textoNormalizado = NormalizarTexto(textoBruto, textoBruto, classificacao.Tipo, classificacao.Regra);

        // 6. Inferir label próximo (acima ou à esquerda).
        var label = InferirLabel(grupo.Bbox, pagina.Palavras);

        // 7. Construir sugestão de nome e chave.
        var nomeSugerido = !string.IsNullOrWhiteSpace(label)
            ? label
            : TipoParaString(classificacao.Tipo);
        var chaveTecnica = ToSnakeCase(RemoverAcentos(nomeSugerido));

        return new SmartDeteccaoResultado(
            Encontrado: true,
            TextoBruto: textoBruto,
            Tipo: classificacao.Tipo,
            RegraNormalizacao: classificacao.Regra,
            TextoNormalizado: textoNormalizado,
            LabelInferido: label,
            NomeSugerido: nomeSugerido,
            ChaveTecnicaSugerida: chaveTecnica,
            Pagina: entrada.Pagina,
            Bbox: grupo.Bbox,
            Confianca: classificacao.Confianca);
    }

    // ── Hit Test ─────────────────────────────────────────────────────────────

    private static PdfPalavra? EncontrarPalavraNoClique(
        IReadOnlyList<PdfPalavra> palavras, double x, double y)
    {
        PdfPalavra? melhor = null;
        double menorDistancia = double.MaxValue;

        foreach (var palavra in palavras)
        {
            var b = palavra.Bbox;
            // HitTest exato: ponto dentro do bbox.
            if (x >= b.X && x < b.X + b.Largura && y >= b.Y && y < b.Y + b.Altura)
            {
                // Em caso de sobreposição, escolher a palavra mais próxima do centro do clique.
                var cx = b.X + b.Largura / 2;
                var cy = b.Y + b.Altura / 2;
                var dist = Math.Abs(x - cx) + Math.Abs(y - cy);
                if (dist < menorDistancia)
                {
                    menorDistancia = dist;
                    melhor = palavra;
                }
            }
        }

        return melhor;
    }

    // ── Agrupamento ──────────────────────────────────────────────────────────

    private sealed record GrupoPalavras(
        IReadOnlyList<PdfPalavra> Palavras,
        BboxRelativo Bbox);

    private static GrupoPalavras AgruparPalavrasNaLinha(
        PdfPalavra hitWord, IReadOnlyList<PdfPalavra> todas)
    {
        var centerY = hitWord.Bbox.Y + hitWord.Bbox.Altura / 2;

        // Filtrar palavras na mesma linha (tolerância de eixo Y).
        var mesmaLinha = todas
            .Where(p =>
            {
                var pCenterY = p.Bbox.Y + p.Bbox.Altura / 2;
                return Math.Abs(pCenterY - centerY) <= ToleranciaLinhaRel;
            })
            .OrderBy(p => p.Bbox.X)
            .ToList();

        // Encontrar índice da palavra hit na lista da linha.
        var hitIdx = mesmaLinha.FindIndex(p =>
            p.Bbox.X == hitWord.Bbox.X && p.Texto == hitWord.Texto);
        if (hitIdx < 0)
            hitIdx = 0;

        // Expandir para a esquerda enquanto gap < MaxGapHorizontalRel.
        var inicio = hitIdx;
        while (inicio > 0)
        {
            var esquerda = mesmaLinha[inicio - 1];
            var atual = mesmaLinha[inicio];
            var gap = atual.Bbox.X - (esquerda.Bbox.X + esquerda.Bbox.Largura);
            if (gap < 0 || gap <= MaxGapHorizontalRel)
                inicio--;
            else
                break;
        }

        // Expandir para a direita enquanto gap < MaxGapHorizontalRel.
        var fim = hitIdx;
        while (fim < mesmaLinha.Count - 1)
        {
            var atual = mesmaLinha[fim];
            var direita = mesmaLinha[fim + 1];
            var gap = direita.Bbox.X - (atual.Bbox.X + atual.Bbox.Largura);
            if (gap < 0 || gap <= MaxGapHorizontalRel)
                fim++;
            else
                break;
        }

        var grupo = mesmaLinha.GetRange(inicio, fim - inicio + 1);
        var bbox = CalcularBboxEnvolvente(grupo);
        return new GrupoPalavras(grupo, bbox);
    }

    private static BboxRelativo CalcularBboxEnvolvente(IReadOnlyList<PdfPalavra> palavras)
    {
        if (palavras.Count == 0)
            return new BboxRelativo(0, 0, 0, 0);

        var minX = double.MaxValue;
        var minY = double.MaxValue;
        var maxX = double.MinValue;
        var maxY = double.MinValue;

        foreach (var p in palavras)
        {
            if (p.Bbox.X < minX) minX = p.Bbox.X;
            if (p.Bbox.Y < minY) minY = p.Bbox.Y;
            var x2 = p.Bbox.X + p.Bbox.Largura;
            var y2 = p.Bbox.Y + p.Bbox.Altura;
            if (x2 > maxX) maxX = x2;
            if (y2 > maxY) maxY = y2;
        }

        return new BboxRelativo(minX, minY, maxX - minX, maxY - minY);
    }

    // ── Classificação ────────────────────────────────────────────────────────

    // TextoExtraido: substring extraída pelo regex (evita retornar o grupo inteiro quando o
    // valor detectado está imerso em uma linha com muitas outras palavras).
    private sealed record Classificacao(SmartTipoVariavel Tipo, double Confianca, string? Regra, string? TextoExtraido = null);

    private static Classificacao Classificar(string textComEspaco, string textSemEspaco)
    {
        // Testa ambos candidatos (com e sem espaço). Prioridade decrescente.
        foreach (var candidato in new[] { textSemEspaco, textComEspaco })
        {
            var t = candidato.Trim();
            System.Text.RegularExpressions.Match m;

            // 1º CNPJ (14 dígitos — testar antes de CPF para evitar falso positivo)
            m = AncorarPdfRegexCatalog.CnpjPattern().Match(t);
            if (m.Success)
                return new Classificacao(SmartTipoVariavel.Cnpj, 1.0, "cnpj", m.Value);

            // 2º CPF (11 dígitos)
            m = AncorarPdfRegexCatalog.CpfPattern().Match(t);
            if (m.Success)
                return new Classificacao(SmartTipoVariavel.Cpf, 1.0, "cpf", m.Value);

            // 3º E-mail
            m = AncorarPdfRegexCatalog.EmailPattern().Match(t);
            if (m.Success)
                return new Classificacao(SmartTipoVariavel.Email, 1.0, null, m.Value);

            // 4º CEP com hífen (exato)
            m = AncorarPdfRegexCatalog.CepComHifenPattern().Match(t);
            if (m.Success)
                return new Classificacao(SmartTipoVariavel.Cep, 1.0, null, m.Value);

            // 5º Telefone
            m = AncorarPdfRegexCatalog.TelefoneBrPattern().Match(t);
            if (m.Success)
                return new Classificacao(SmartTipoVariavel.Telefone, 0.95, null, m.Value);

            // 6º Data brasileira DD/MM/AAAA
            m = AncorarPdfRegexCatalog.DataBrPattern().Match(t);
            if (m.Success)
                return new Classificacao(SmartTipoVariavel.DataBr, 1.0, "data_br", m.Value);

            // 7º Data por extenso PT-BR
            m = AncorarPdfRegexCatalog.DataExtensoPattern().Match(t);
            if (m.Success)
                return new Classificacao(SmartTipoVariavel.DataExtenso, 0.9, "data_br", m.Value);

            // 8º Moeda BRL (deve ter R$ ou formato dígitos com vírgula decimal)
            if (EhMoedaBrl(t))
            {
                var mm = AncorarPdfRegexCatalog.MoedaBrlPattern().Match(t);
                return new Classificacao(SmartTipoVariavel.MoedaBrl, 0.95, "moeda_brl", mm.Success ? mm.Value : null);
            }

            // 9º UF (exatamente 2 letras maiúsculas, no conjunto dos estados BR)
            if (AncorarPdfRegexCatalog.UfsBrasil.Contains(t))
                return new Classificacao(SmartTipoVariavel.Uf, 0.85, null, t);

            // 10º CEP sem hífen (8 dígitos isolados — menor prioridade que CEP com hífen)
            m = AncorarPdfRegexCatalog.CepSemHifenPattern().Match(t);
            if (m.Success && EhSoDigitos(t))
                return new Classificacao(SmartTipoVariavel.Cep, 0.8, null, t);

            // 11º Inteiro (somente dígitos)
            if (EhSoDigitos(t))
                return new Classificacao(SmartTipoVariavel.Inteiro, 0.7, "inteiro", t);
        }

        // 12º Texto fallback
        return new Classificacao(SmartTipoVariavel.Texto, 0.5, null, null);
    }

    private static bool EhMoedaBrl(string t)
    {
        // Deve ter R$, ou conter vírgula decimal com dígitos (formato BRL típico).
        if (t.Contains("R$") || t.Contains("R $"))
            return true;

        // Formato "1.234,56" (ponto milhar + vírgula decimal) com pelo menos 3 chars de dígitos+formato
        if (t.Contains(',') && t.Length >= 4)
        {
            var m = AncorarPdfRegexCatalog.MoedaBrlPattern().Match(t);
            if (m.Success && m.Groups[1].Value.Contains(','))
                return true;
        }

        return false;
    }

    private static bool EhSoDigitos(string t)
    {
        if (string.IsNullOrEmpty(t)) return false;
        foreach (var c in t)
            if (!char.IsAsciiDigit(c))
                return false;
        return true;
    }

    // ── Normalização ─────────────────────────────────────────────────────────

    private static string NormalizarTexto(
        string textComEspaco, string textSemEspaco,
        SmartTipoVariavel tipo, string? regra)
    {
        var raw = tipo is SmartTipoVariavel.Cnpj or SmartTipoVariavel.Cpf
            ? textSemEspaco  // sem espaço para remover corretamente a pontuação
            : textComEspaco;

        return AncorarPdfNormalizadorValores.Normalizar(raw, regra);
    }

    // ── Inferência de Label ───────────────────────────────────────────────────

    private string? InferirLabel(BboxRelativo grupoBbox, IReadOnlyList<PdfPalavra> todas)
    {
        // Centro Y do grupo detectado.
        var grupoCenterY = grupoBbox.Y + grupoBbox.Altura / 2;

        // Buscar palavras candidatas a label: acima do grupo (Y_center < grupoBbox.Y)
        // dentro de MaxGapLabelAcimaRel, ou à esquerda na mesma linha.
        var candidatos = new List<(PdfPalavra Palavra, double Distancia)>();

        foreach (var palavra in todas)
        {
            // Pular palavras dentro do grupo detectado.
            if (palavra.Bbox.X >= grupoBbox.X && palavra.Bbox.X < grupoBbox.X + grupoBbox.Largura
                && palavra.Bbox.Y >= grupoBbox.Y && palavra.Bbox.Y < grupoBbox.Y + grupoBbox.Altura)
                continue;

            var pCenterY = palavra.Bbox.Y + palavra.Bbox.Altura / 2;
            var pCenterX = palavra.Bbox.X + palavra.Bbox.Largura / 2;

            // Label acima: Y_center < grupoBbox.Y e gap vertical dentro do limite.
            var gapAcima = grupoBbox.Y - (palavra.Bbox.Y + palavra.Bbox.Altura);
            if (pCenterY < grupoBbox.Y && gapAcima >= 0 && gapAcima <= MaxGapLabelAcimaRel)
            {
                var dist = gapAcima + Math.Abs(pCenterX - (grupoBbox.X + grupoBbox.Largura / 2));
                candidatos.Add((palavra, dist));
                continue;
            }

            // Label à esquerda na mesma linha: mesma linha (tolerância Y), à esquerda do grupo.
            if (Math.Abs(pCenterY - grupoCenterY) <= ToleranciaLinhaRel
                && palavra.Bbox.X + palavra.Bbox.Largura < grupoBbox.X)
            {
                var gap = grupoBbox.X - (palavra.Bbox.X + palavra.Bbox.Largura);
                if (gap <= MaxGapHorizontalRel * 20) // labels podem ter gap maior
                {
                    var dist = gap;
                    candidatos.Add((palavra, dist));
                }
            }
        }

        if (candidatos.Count == 0)
            return null;

        // Agregar candidatos próximos (mesma linha) em frases.
        // Ordenar por posição X para agrupar palavras consecutivas.
        var acima = candidatos
            .Where(c => (c.Palavra.Bbox.Y + c.Palavra.Bbox.Altura) < grupoBbox.Y)
            .OrderBy(c => c.Palavra.Bbox.Y)
            .ThenBy(c => c.Palavra.Bbox.X)
            .ToList();

        var esquerda = candidatos
            .Where(c => Math.Abs((c.Palavra.Bbox.Y + c.Palavra.Bbox.Altura / 2) - grupoCenterY) <= ToleranciaLinhaRel)
            .OrderBy(c => c.Distancia)
            .ToList();

        // Priorizar label acima (mais próxima linha acima).
        string? labelText = null;
        if (acima.Count > 0)
        {
            // Pegar a última linha de labels acima (linha mais próxima do grupo).
            var maxY = acima.Max(c => c.Palavra.Bbox.Y + c.Palavra.Bbox.Altura);
            var linhaMaisProxima = acima
                .Where(c => Math.Abs((c.Palavra.Bbox.Y + c.Palavra.Bbox.Altura) - maxY) <= ToleranciaLinhaRel * 2)
                .OrderBy(c => c.Palavra.Bbox.X)
                .ToList();

            labelText = string.Join(" ", linhaMaisProxima.Select(c => c.Palavra.Texto));
        }
        else if (esquerda.Count > 0)
        {
            // Pegar palavras à esquerda (mesma linha, mais próximas).
            var labelsEsquerda = esquerda
                .OrderBy(c => c.Palavra.Bbox.X)
                .ToList();

            labelText = string.Join(" ", labelsEsquerda.Select(c => c.Palavra.Texto));
        }

        if (string.IsNullOrWhiteSpace(labelText) || labelText.Length > MaxLabelChars || labelText.Length < 2)
            return null;

        // Filtrar labels que são eles mesmos valores detectáveis (ex: outro CNPJ como label não faz sentido).
        if (EhValorDetectavel(labelText))
            return null;

        return labelText.Trim();
    }

    private static bool EhValorDetectavel(string text)
    {
        var t = text.Trim();
        return AncorarPdfRegexCatalog.CnpjPattern().IsMatch(t)
            || AncorarPdfRegexCatalog.CpfPattern().IsMatch(t)
            || AncorarPdfRegexCatalog.EmailPattern().IsMatch(t)
            || AncorarPdfRegexCatalog.CepComHifenPattern().IsMatch(t)
            || AncorarPdfRegexCatalog.DataBrPattern().IsMatch(t)
            || EhSoDigitos(t);
    }

    // ── Sugestão de Nome e Chave ──────────────────────────────────────────────

    private static string TipoParaString(SmartTipoVariavel tipo) => tipo switch
    {
        SmartTipoVariavel.Cnpj => "CNPJ",
        SmartTipoVariavel.Cpf => "CPF",
        SmartTipoVariavel.Email => "E-mail",
        SmartTipoVariavel.Cep => "CEP",
        SmartTipoVariavel.Telefone => "Telefone",
        SmartTipoVariavel.DataBr => "Data",
        SmartTipoVariavel.DataExtenso => "Data por Extenso",
        SmartTipoVariavel.Uf => "UF",
        SmartTipoVariavel.MoedaBrl => "Valor",
        SmartTipoVariavel.Inteiro => "Numero",
        _ => "Texto"
    };

    // Converte texto em snake_case ASCII sem acentos, truncado em 50 chars.
    internal static string ToSnakeCase(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return "variavel";

        var sb = new StringBuilder(texto.Length + 8);
        var ultimoFoiSeparador = true;

        foreach (var c in texto.Trim())
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
                ultimoFoiSeparador = false;
            }
            else if (!ultimoFoiSeparador && sb.Length > 0)
            {
                sb.Append('_');
                ultimoFoiSeparador = true;
            }
        }

        // Remover underscore final.
        if (sb.Length > 0 && sb[^1] == '_')
            sb.Remove(sb.Length - 1, 1);

        var resultado = sb.Length > 50 ? sb.ToString(0, 50) : sb.ToString();
        return string.IsNullOrEmpty(resultado) ? "variavel" : resultado;
    }

    // Remove acentos usando normalização Unicode NFD → filtra caracteres não-ASCII.
    internal static string RemoverAcentos(string texto)
    {
        var normalizado = texto.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalizado.Length);
        foreach (var c in normalizado)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
