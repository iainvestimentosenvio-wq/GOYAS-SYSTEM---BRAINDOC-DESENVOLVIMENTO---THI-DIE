using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Services;

namespace Protons.UI.Painel.Funcionalidades.Ferramentas.AncorarPdf.ModelosDeVisao;

/// <summary>
/// Encapsula a lógica de Smart Click (C12): detecção automática, confirmação e aplicação.
/// Extraído de AncorarPdfConfiguracaoViewModel para reduzir acoplamento.
/// </summary>
internal sealed class AncorarPdfSmartClickHandler
{
    private const double ProximitySearchRadius = 0.015;
    private readonly IAncorarPdfSmartDetector? _smartDetector;
    private readonly IAncorarPdfDocumentoAnalyzer? _documentAnalyzer;
    private readonly Action<string, string?> _registrarEvento;
    private readonly IAncorarPdfSmartClickContext _context;

    public AncorarPdfSmartClickHandler(
        IAncorarPdfSmartDetector? smartDetector,
        IAncorarPdfDocumentoAnalyzer? documentAnalyzer,
        Action<string, string?> registrarEvento,
        IAncorarPdfSmartClickContext context)
    {
        _smartDetector = smartDetector;
        _documentAnalyzer = documentAnalyzer;
        _registrarEvento = registrarEvento;
        _context = context;
    }

    public async Task ExecutarSmartClickAsync(SmartDeteccaoEntrada entrada, CancellationToken ct = default)
    {
        if (_smartDetector is null)
        {
            _context.Mensagem = "Detecção automática não disponível (detector não configurado).";
            return;
        }

        if (_context.PdfPaginasCache is null && _documentAnalyzer is null)
        {
            _context.Mensagem = "Texto do PDF ainda não extraído. Aguarde ou selecione um PDF.";
            return;
        }

        var pagina = entrada.Pagina <= 0 ? _context.PaginaPreviewAtual : entrada.Pagina;
        var entradaNormalizada = entrada with { Pagina = Math.Max(1, pagina) };

        var resultado = DetectarComProximidade(_context.PdfPaginasCache, entradaNormalizada);
        if (!resultado.Encontrado)
            resultado = await DetectarComFallbackRegionalAsync(entradaNormalizada, ct).ConfigureAwait(false);

        if (!resultado.Encontrado)
        {
            _context.Mensagem = $"Nenhum texto encontrado no ponto clicado ({resultado.MotivoFalha}).";
            return;
        }

        _context.SmartDeteccaoAtual = resultado;
        _context.SmartNomeEditavel = resultado.NomeSugerido;
        _context.SmartChaveEditavel = resultado.ChaveTecnicaSugerida;
        _context.SmartTipoEditavel = MapearTipoVariavel(resultado.Tipo);
        _context.SetDestaquesRelBboxCache(EncontrarOcorrenciasTexto(resultado.TextoBruto, resultado.Pagina));
        _context.SetDestaqueCorCache(_context.CorSelecionada);
        _context.SetDestaquesPaginaCache(resultado.Pagina);
        _context.ReconstruirDestaquesSmart();
        _context.Mensagem = $"Detectado: {resultado.Tipo} ({resultado.Confianca:P0}) — edite e confirme.";
    }

    public void ConfirmarSmartDeteccao()
    {
        if (_context.SmartDeteccaoAtual is null || !_context.PodeEditar) return;

        var indice = _context.AncorasCount;
        var resultado = _context.SmartDeteccaoAtual;
        var ancora = new AncorarPdfTemplateAncora
        {
            Ordem = indice,
            CorHex = _context.CorSelecionada,
            Pagina = resultado.Pagina,
            XRel = Math.Clamp(resultado.Bbox.X, 0, 1),
            YRel = Math.Clamp(resultado.Bbox.Y, 0, 1),
            LarguraRel = Math.Clamp(resultado.Bbox.Largura, 0.01, 1),
            AlturaRel = Math.Clamp(resultado.Bbox.Altura, 0.005, 1),
            Metadado = new AncorarPdfTemplateMetadado
            {
                NomeExibido = string.IsNullOrWhiteSpace(_context.SmartNomeEditavel)
                    ? resultado.NomeSugerido
                    : _context.SmartNomeEditavel.Trim(),
                ChaveTecnica = string.IsNullOrWhiteSpace(_context.SmartChaveEditavel)
                    ? resultado.ChaveTecnicaSugerida
                    : _context.SmartChaveEditavel.Trim(),
                TipoEsperado = string.IsNullOrWhiteSpace(_context.SmartTipoEditavel)
                    ? "texto"
                    : _context.SmartTipoEditavel.Trim(),
                ExemploEsperado = resultado.TextoNormalizado,
                RegraNormalizacao = resultado.RegraNormalizacao
            }
        };

        var novoEstado = _context.ConverterAncorasAtuais();
        novoEstado.Add(ancora);
        _context.AplicarEstadoComHistorico(novoEstado);
        _context.AncoraSelecionada = _context.Ancoras.LastOrDefault(x =>
            string.Equals(x.CorHex, ancora.CorHex, StringComparison.OrdinalIgnoreCase));

        _context.SmartDeteccaoAtual = null;
        _context.SetDestaquesRelBboxCache([]);
        _context.SetDestaquesPaginaCache(_context.PaginaPreviewAtual);
        _context.ReconstruirDestaquesSmart();
        _context.Mensagem = $"Âncora '{ancora.Metadado.NomeExibido}' criada pelo smart click.";
        _registrarEvento("ancorar_pdf_c12_smart_click_confirmado",
            $"tipo={ancora.Metadado.TipoEsperado} chave={ancora.Metadado.ChaveTecnica}");
    }

    public void CancelarSmartDeteccao()
    {
        _context.SmartDeteccaoAtual = null;
        _context.SetDestaquesRelBboxCache([]);
        _context.SetDestaquesPaginaCache(_context.PaginaPreviewAtual);
        _context.ReconstruirDestaquesSmart();
        _context.Mensagem = string.Empty;
    }

    private IReadOnlyList<BboxRelativo> EncontrarOcorrenciasTexto(string textoBruto, int pagina)
    {
        var cache = _context.PdfPaginasCache;
        if (cache is null || string.IsNullOrEmpty(textoBruto)) return [];
        if (pagina < 1 || pagina > cache.Count) return [];
        return cache[pagina - 1].Palavras
            .Where(p => string.Equals(p.Texto, textoBruto, StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Bbox)
            .ToList();
    }

    private SmartDeteccaoResultado DetectarComProximidade(
        IReadOnlyList<PdfPaginaTexto>? paginas,
        SmartDeteccaoEntrada entrada)
    {
        if (_smartDetector is null || paginas is null || paginas.Count == 0)
            return SmartDeteccaoResultado.NaoEncontrado("sem_paginas");

        var resultado = _smartDetector.Detectar(paginas, entrada);
        if (resultado.Encontrado || !string.Equals(resultado.MotivoFalha, "nenhum_texto_no_ponto", StringComparison.OrdinalIgnoreCase))
            return resultado;

        if (entrada.Pagina < 1 || entrada.Pagina > paginas.Count)
            return resultado;

        var pagina = paginas[entrada.Pagina - 1];
        var proxima = EncontrarPalavraMaisProxima(pagina.Palavras, entrada.XRel, entrada.YRel, ProximitySearchRadius);
        if (proxima is null)
            return resultado;

        return _smartDetector.Detectar(
            paginas,
            entrada with
            {
                XRel = proxima.Bbox.X + (proxima.Bbox.Largura / 2.0),
                YRel = proxima.Bbox.Y + (proxima.Bbox.Altura / 2.0)
            });
    }

    private async Task<SmartDeteccaoResultado> DetectarComFallbackRegionalAsync(
        SmartDeteccaoEntrada entrada,
        CancellationToken ct)
    {
        if (_documentAnalyzer is null || string.IsNullOrWhiteSpace(_context.PdfModeloPath))
            return SmartDeteccaoResultado.NaoEncontrado("nenhum_texto_no_ponto");

        var regiaoBase = CriarRegiaoInterativa(entrada.XRel, entrada.YRel, expandida: false);
        var options = _context.BuildDocumentoAnalyzerOptions();
        var tokens = await _documentAnalyzer.AnalisarRegiaoAsync(
            _context.PdfModeloPath,
            new AncorarPdfAnaliseRegiaoRequest(
                Pagina: entrada.Pagina,
                Regiao: regiaoBase,
                OcrDpi: Math.Clamp(options.OcrDpi, 150, 450),
                OcrLang: options.OcrLang,
                TimeoutMs: Math.Clamp(options.TimeoutRegiaoOcrMs, 250, 5000),
                RetryExpandido: true),
            options,
            ct).ConfigureAwait(false);

        if (tokens.Count == 0)
            return SmartDeteccaoResultado.NaoEncontrado("nenhum_texto_na_regiao");

        var palavra = EncontrarTokenMaisProximo(tokens, entrada.XRel, entrada.YRel);
        if (palavra is null)
            return SmartDeteccaoResultado.NaoEncontrado("nenhum_texto_na_regiao");

        var paginaSintetica = new PdfPaginaTexto(
            entrada.Pagina,
            1,
            1,
            tokens.Select(t => new PdfPalavra(t.Texto, t.Bbox)).ToArray());

        return DetectarComProximidade(
            [paginaSintetica],
            new SmartDeteccaoEntrada(
                palavra.Bbox.X + (palavra.Bbox.Largura / 2.0),
                palavra.Bbox.Y + (palavra.Bbox.Altura / 2.0),
                entrada.Pagina));
    }

    private static BboxRelativo CriarRegiaoInterativa(double xRel, double yRel, bool expandida)
    {
        var largura = expandida ? 0.14 : 0.08;
        var altura = expandida ? 0.08 : 0.04;
        var x = Math.Max(0, xRel - (largura / 2.0));
        var y = Math.Max(0, yRel - (altura / 2.0));
        var x2 = Math.Min(1, x + largura);
        var y2 = Math.Min(1, y + altura);
        return new BboxRelativo(x, y, x2 - x, y2 - y);
    }

    private static PdfPalavra? EncontrarPalavraMaisProxima(
        IReadOnlyList<PdfPalavra> palavras,
        double xRel,
        double yRel,
        double raio)
    {
        PdfPalavra? melhor = null;
        var menorDistancia = double.MaxValue;

        foreach (var palavra in palavras)
        {
            var distancia = DistanciaPontoParaBbox(xRel, yRel, palavra.Bbox);
            if (distancia > raio || distancia >= menorDistancia)
                continue;

            menorDistancia = distancia;
            melhor = palavra;
        }

        return melhor;
    }

    private static AncorarPdfTokenAnalise? EncontrarTokenMaisProximo(
        IReadOnlyList<AncorarPdfTokenAnalise> tokens,
        double xRel,
        double yRel)
    {
        AncorarPdfTokenAnalise? melhor = null;
        var menorDistancia = double.MaxValue;

        foreach (var token in tokens)
        {
            var distancia = DistanciaPontoParaBbox(xRel, yRel, token.Bbox);
            if (distancia >= menorDistancia)
                continue;

            menorDistancia = distancia;
            melhor = token;
        }

        return melhor;
    }

    private static double DistanciaPontoParaBbox(double xRel, double yRel, BboxRelativo bbox)
    {
        var clampedX = Math.Clamp(xRel, bbox.X, bbox.X + bbox.Largura);
        var clampedY = Math.Clamp(yRel, bbox.Y, bbox.Y + bbox.Altura);
        var dx = xRel - clampedX;
        var dy = yRel - clampedY;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static string MapearTipoVariavel(SmartTipoVariavel tipo) => tipo switch
    {
        SmartTipoVariavel.Cnpj        => "cnpj",
        SmartTipoVariavel.Cpf         => "cpf",
        SmartTipoVariavel.Email       => "email",
        SmartTipoVariavel.Cep         => "cep",
        SmartTipoVariavel.Telefone    => "telefone",
        SmartTipoVariavel.DataBr      => "data_br",
        SmartTipoVariavel.DataExtenso => "data_extenso",
        SmartTipoVariavel.Uf          => "uf",
        SmartTipoVariavel.MoedaBrl    => "moeda_brl",
        SmartTipoVariavel.Inteiro     => "inteiro",
        _                             => "texto"
    };
}

/// <summary>
/// Contrato mínimo para o handler de Smart Click interagir com a VM.
/// </summary>
internal interface IAncorarPdfSmartClickContext
{
    IReadOnlyList<PdfPaginaTexto>? PdfPaginasCache { get; }
    bool PodeEditar { get; }
    string CorSelecionada { get; }
    int AncorasCount { get; }
    AncorarPdfAncoraItemViewModel? AncoraSelecionada { get; set; }
    IEnumerable<AncorarPdfAncoraItemViewModel> Ancoras { get; }
    SmartDeteccaoResultado? SmartDeteccaoAtual { get; set; }
    string SmartNomeEditavel { get; set; }
    string SmartChaveEditavel { get; set; }
    string SmartTipoEditavel { get; set; }
    string Mensagem { get; set; }
    void SetDestaquesRelBboxCache(IReadOnlyList<BboxRelativo> bboxes);
    void SetDestaqueCorCache(string cor);
    void SetDestaquesPaginaCache(int pagina);
    void ReconstruirDestaquesSmart();
    List<AncorarPdfTemplateAncora> ConverterAncorasAtuais();
    void AplicarEstadoComHistorico(List<AncorarPdfTemplateAncora> estado);
    string PdfModeloPath { get; }
    int PaginaPreviewAtual { get; }
    AncorarPdfDocumentoAnalyzerOptions BuildDocumentoAnalyzerOptions();
}
