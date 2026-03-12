using FluentAssertions;
using Protons.Infrastructure.Tarefas.Services;
using Xunit;

namespace Protons.Infrastructure.Tests.Integration;

/// <summary>
/// C11 — Gate G2 (blocking): Seletor híbrido (Jaro-Winkler + fallback mais antigo).
/// Cobre as mudanças de Fix 2 (C11): tiebreak oldest entre matches + fallback oldest quando sem match.
/// Anti-duplicidade: C9 T11 cobre seletor com 2 candidatos por score; C11 cobre fallback sem match e
/// tiebreak por mtime asc (mais antigo) que C9 não cobre.
/// </summary>
[Trait("Checklist", "C11")]
[Trait("Category", "C11_G2_SeletorHibrido")]
public sealed class AncorarPdfChecklist11SeletorHibridoTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AncorarPdfSeletorArquivoPasta _seletor = new();

    public AncorarPdfChecklist11SeletorHibridoTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"c11_seletor_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private string CriarPdf(string nome, DateTime mtime)
    {
        var path = Path.Combine(_tempDir, $"{nome}.pdf");
        File.WriteAllText(path, $"%PDF-1.4 {nome}");
        File.SetLastWriteTimeUtc(path, mtime);
        return path;
    }

    // --- T01: match por nome → retorna mais antigo dos matches ---

    [Fact]
    public async Task T01_Seletor_hibrido_match_nome_retorna_mais_antigo_dos_matches()
    {
        // Arrange: dois arquivos com nomes de MESMO comprimento e estrutura idêntica para garantir
        // Jaro-Winkler idêntico — apenas o mtime difere. O mais antigo deve ser selecionado.
        // "relatorio_mensal_01" e "relatorio_mensal_02" têm scores iguais: mesma length, mesmo prefix,
        // sufixo "01"/"02" — dígitos que não existem em "relatorio_mensal" → matches idênticos.
        var t_antigo = DateTime.UtcNow.AddDays(-10);
        var t_novo   = DateTime.UtcNow.AddDays(-1);
        CriarPdf("relatorio_mensal_01", t_antigo);
        CriarPdf("relatorio_mensal_02", t_novo);

        // Act
        var resultado = await _seletor.SelecionarMelhorAsync(
            _tempDir, "relatorio_mensal", limiarSimilaridade: 0.6,
            monitorarSubpastas: false, cicloId: "c11_t01",
            CancellationToken.None);

        // Assert: mais antigo entre os dois matches deve ser selecionado (tiebreak ThenBy mtime asc).
        resultado.Should().NotBeNull();
        resultado!.ArquivoPath.Should().Contain("_01",
            "arquivo mais antigo deve ganhar o tiebreak entre matches de mesma similaridade");
    }

    // --- T02: sem match por nome → retorna mais antigo absoluto da pasta ---

    [Fact]
    public async Task T02_Seletor_hibrido_sem_match_nome_retorna_mais_antigo_absoluto()
    {
        // Arrange: arquivos sem similaridade com o nome de referência.
        var t_antigo = DateTime.UtcNow.AddDays(-5);
        var t_novo   = DateTime.UtcNow.AddDays(-1);
        CriarPdf("xyz_completamente_diferente_1", t_antigo);
        CriarPdf("xyz_completamente_diferente_2", t_novo);

        // Act: limiar alto para garantir que nenhum passa.
        var resultado = await _seletor.SelecionarMelhorAsync(
            _tempDir, "relatorio_mensal", limiarSimilaridade: 0.99,
            monitorarSubpastas: false, cicloId: "c11_t02",
            CancellationToken.None);

        // Assert: fallback deve usar o mais antigo absoluto da pasta.
        resultado.Should().NotBeNull("fallback deve retornar o mais antigo mesmo sem match por nome");
        resultado!.ArquivoPath.Should().Contain("1",
            "arquivo _1 (mais antigo) deve ser o fallback");
    }

    // --- T03: pasta vazia → retorna null ---

    [Fact]
    public async Task T03_Seletor_hibrido_pasta_vazia_retorna_null()
    {
        // Arrange: pasta vazia (sem PDFs).
        var resultado = await _seletor.SelecionarMelhorAsync(
            _tempDir, "qualquer_nome", limiarSimilaridade: 0.5,
            monitorarSubpastas: false, cicloId: "c11_t03",
            CancellationToken.None);

        resultado.Should().BeNull("pasta sem PDFs deve retornar null mesmo com fallback");
    }

    // --- T04: múltiplos matches → ordena por mtime asc (mais antigo primeiro) ---

    [Fact]
    public async Task T04_Seletor_hibrido_multiplos_matches_ordena_por_mtime_asc()
    {
        // Arrange: 4 arquivos que passam no limiar.
        // bases[3]=20 → "fatura_cliente_03" será o mais antigo (-20 dias).
        var bases = new[] { 10, 5, 2, 20 }; // dias atrás
        foreach (var (dias, idx) in bases.Select((d, i) => (d, i)))
            CriarPdf($"fatura_cliente_{idx:D2}", DateTime.UtcNow.AddDays(-dias));

        var resultado = await _seletor.SelecionarMelhorAsync(
            _tempDir, "fatura_cliente", limiarSimilaridade: 0.7,
            monitorarSubpastas: false, cicloId: "c11_t04",
            CancellationToken.None);

        resultado.Should().NotBeNull();
        // O arquivo _03 tem mtime -20 dias (mais antigo) — deve ser selecionado.
        resultado!.ArquivoPath.Should().Contain("_03",
            "arquivo com mtime mais antigo deve ganhar quando há múltiplos matches");
    }

    // --- T05: similaridade abaixo do limiar → usa fallback ---

    [Fact]
    public async Task T05_Seletor_hibrido_similaridade_abaixo_limiar_usa_fallback()
    {
        // Arrange: arquivo com nome completamente diferente — abaixo do limiar.
        var t_ref = DateTime.UtcNow.AddDays(-3);
        CriarPdf("aaabbbccc", t_ref); // sem similaridade com "nota_fiscal"

        var resultado = await _seletor.SelecionarMelhorAsync(
            _tempDir, "nota_fiscal", limiarSimilaridade: 0.75,
            monitorarSubpastas: false, cicloId: "c11_t05",
            CancellationToken.None);

        resultado.Should().NotBeNull("fallback garante que algum arquivo seja retornado quando pasta não é vazia");
        resultado!.ArquivoPath.Should().Contain("aaabbbccc",
            "único arquivo disponível deve ser retornado como fallback");
    }
}
