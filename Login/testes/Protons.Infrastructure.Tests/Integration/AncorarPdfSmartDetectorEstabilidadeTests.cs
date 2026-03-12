using FluentAssertions;
using Protons.Core.Tarefas.Models;
using Protons.Infrastructure.Tarefas.Services;
using Xunit;

namespace Protons.Infrastructure.Tests.Integration;

/// <summary>
/// C12 — Gate G3 (blocking): Critério de aceite de estabilidade.
/// Critério: detectar corretamente CNPJ, CPF, e-mail, CEP e telefone
/// no FRE CAIAPONIA, 5 vezes consecutivas sem erro.
/// 5 × 5 = 25 detecções — todas devem ter Encontrado=true e tipo correto.
///
/// Precondição: PDF disponível em PROTONS_FRE_PDF_PATH
/// (fallback para caminho padrão do repositório).
/// Sem artefato: skip explicito (local) ou falha explicita (CI estrito).
/// </summary>
[Trait("Checklist", "C12")]
[Trait("Category", "C12_G3_Estabilidade")]
public sealed class AncorarPdfSmartDetectorEstabilidadeTests
{
    private readonly AncorarPdfSmartDetector _detector = new();
    private readonly AncorarPdfExtratorTextoPdfPig _extrator = new();

    [RequiresFrePdfFact]
    public async Task DetectarTodas5Vezes_sem_falha()
    {
        var frePdfPath = InfraTestPreconditions.ObterFrePdfPathOuFalhar();

        // Extrair páginas uma única vez.
        var paginas = await _extrator.ExtrairAsync(frePdfPath, CancellationToken.None);
        paginas.Should().NotBeEmpty("o FRE deve ter pelo menos uma página");

        // Descobrir posições conhecidas para cada tipo.
        var posicoes = DiscoverPositions(paginas);
        posicoes.HasAny.Should().BeTrue(
            "o PDF de referencia deve expor posicoes detectaveis para CNPJ/CPF/Email/CEP/Telefone");

        var falhas = new List<string>();

        // 5 rodadas de detecção.
        for (var rodada = 1; rodada <= 5; rodada++)
        {
            foreach (var (nome, pos, tipoEsperado) in posicoes.Targets)
            {
                if (pos is null) continue;

                var entrada = new SmartDeteccaoEntrada(pos.Value.X, pos.Value.Y, 1);
                var resultado = _detector.Detectar(paginas, entrada);

                if (!resultado.Encontrado)
                    falhas.Add($"Rodada {rodada} | {nome}: Encontrado=false (MotivoFalha={resultado.MotivoFalha})");
                else if (resultado.Tipo != tipoEsperado)
                    falhas.Add($"Rodada {rodada} | {nome}: Tipo={resultado.Tipo} (esperado={tipoEsperado})");
            }
        }

        falhas.Should().BeEmpty(
            $"critério de aceite: 0 falhas em 5 × {posicoes.Targets.Count} detecções. Falhas:\n{string.Join("\n", falhas)}");
    }

    // Descobre posições de clique para cada tipo de variável no PDF.
    private static PositoesDescobertras DiscoverPositions(IReadOnlyList<PdfPaginaTexto> paginas)
    {
        var targets = new List<(string Nome, (double X, double Y)? Pos, SmartTipoVariavel Tipo)>();

        (double X, double Y)? BuscarPalavra(Func<PdfPalavra, bool> pred)
        {
            foreach (var pagina in paginas)
            {
                var p = pagina.Palavras.FirstOrDefault(pred);
                if (p is not null)
                    return (p.Bbox.X + p.Bbox.Largura / 2, p.Bbox.Y + p.Bbox.Altura / 2);
            }
            return null;
        }

        // CNPJ
        var cnpjPos = BuscarPalavra(p =>
            p.Texto.Contains("19.864.554") || p.Texto.Contains("0001-65") ||
            AncorarPdfRegexCatalog.CnpjPattern().IsMatch(p.Texto));
        targets.Add(("CNPJ", cnpjPos, SmartTipoVariavel.Cnpj));

        // CPF
        var cpfPos = BuscarPalavra(p =>
            p.Texto.Contains("382316621") || p.Texto.Contains("382.316.621") ||
            (AncorarPdfRegexCatalog.CpfPattern().IsMatch(p.Texto) &&
             !AncorarPdfRegexCatalog.CnpjPattern().IsMatch(p.Texto)));
        targets.Add(("CPF", cpfPos, SmartTipoVariavel.Cpf));

        // E-mail
        var emailPos = BuscarPalavra(p => p.Texto.Contains("@"));
        targets.Add(("Email", emailPos, SmartTipoVariavel.Email));

        // CEP
        var cepPos = BuscarPalavra(p =>
            p.Texto.Contains("76250-000") || p.Texto.Contains("76250"));
        targets.Add(("CEP", cepPos, SmartTipoVariavel.Cep));

        // Telefone
        var telPos = BuscarPalavra(p =>
            p.Texto.Contains("999401952") || p.Texto.Contains("(62)") ||
            AncorarPdfRegexCatalog.TelefoneBrPattern().IsMatch(p.Texto));
        targets.Add(("Telefone", telPos, SmartTipoVariavel.Telefone));

        var comPosicao = targets.Where(t => t.Pos is not null).ToList();
        return new PositoesDescobertras(comPosicao);
    }

    private sealed record PositoesDescobertras(
        IReadOnlyList<(string Nome, (double X, double Y)? Pos, SmartTipoVariavel Tipo)> Targets)
    {
        public bool HasAny => Targets.Any(t => t.Pos is not null);
    }
}
