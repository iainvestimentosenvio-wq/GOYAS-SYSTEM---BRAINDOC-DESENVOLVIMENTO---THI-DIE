using System.Diagnostics;
using FluentAssertions;
using Protons.Infrastructure.Tarefas.Services;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace Protons.Infrastructure.Tests.Integration;

/// <summary>
/// Gates de SLO de performance com o extrator REAL (PdfPig), não mocks.
/// Converte metas documentadas no checklist em asserções automatizadas executáveis.
///
/// SLOs alvo (conforme plano C6/C8):
///   - Extração: p95 ≤ 900ms por PDF (≤ 1800ms extremo)
///   - Throughput sustentado: > 0 erros em 20 extrações consecutivas
///   - Memória: sem crescimento linear detectável em 50 extrações
/// </summary>
[Trait("Checklist", "SLO")]
[Trait("Category", "SLO_Performance")]
public sealed class AncorarPdfPerformanceSloTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AncorarPdfExtratorTextoPdfPig _extrator = new();

    public AncorarPdfPerformanceSloTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"protons_slo_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    /// <summary>
    /// SLO_P01: Latência de extração de PDF típico (1 página, ~100 palavras) ≤ 900ms.
    /// Executa 10 vezes; todas devem passar (simula distribuição p100 ≤ 900ms).
    /// Em ambiente de CI fraco, pode-se usar 1800ms como limite.
    /// </summary>
    [Fact]
    [Trait("Category", "SLO_P01")]
    public async Task SLO_P01_Extracao_pdf_1pagina_100palavras_abaixo_900ms()
    {
        var pdfPath = CriarPdfComPalavras(_tempDir, "slo-p01.pdf", paginas: 1, palavrasPorPagina: 100);

        const int iteracoes = 10;
        const int sloBoundMs = 900;

        var tempos = new List<long>(iteracoes);
        for (int i = 0; i < iteracoes; i++)
        {
            var sw = Stopwatch.StartNew();
            var paginas = await _extrator.ExtrairAsync(pdfPath, CancellationToken.None);
            sw.Stop();

            paginas.Should().NotBeEmpty("PDF com texto deve ter páginas extraídas");
            paginas[0].Palavras.Should().NotBeEmpty("página deve ter palavras");
            tempos.Add(sw.ElapsedMilliseconds);
        }

        var p95 = Percentil(tempos, 0.95);
        var max = tempos.Max();

        p95.Should().BeLessThanOrEqualTo(sloBoundMs,
            $"p95 de extração deve ser ≤ {sloBoundMs}ms; tempos={string.Join(",", tempos)}ms");
        max.Should().BeLessThanOrEqualTo(sloBoundMs * 2,
            $"máximo deve ser ≤ {sloBoundMs * 2}ms (p99 bound)");
    }

    /// <summary>
    /// SLO_P02: Latência de extração de PDF maior (5 páginas, ~200 palavras/página) ≤ 3000ms.
    /// Representa documentos longos como relatórios completos.
    /// Bound 3000ms permite margem para CI/máquinas lentas (alvo ideal 1800ms).
    /// </summary>
    [Fact]
    [Trait("Category", "SLO_P02")]
    public async Task SLO_P02_Extracao_pdf_5paginas_200palavras_abaixo_3000ms()
    {
        var pdfPath = CriarPdfComPalavras(_tempDir, "slo-p02.pdf", paginas: 5, palavrasPorPagina: 200);

        const int sloBoundMs = 3000;
        var sw = Stopwatch.StartNew();
        var paginas = await _extrator.ExtrairAsync(pdfPath, CancellationToken.None);
        sw.Stop();

        paginas.Should().HaveCount(5, "PDF de 5 páginas deve retornar 5 páginas");
        sw.ElapsedMilliseconds.Should().BeLessThanOrEqualTo(sloBoundMs,
            $"extração de 5 páginas deve ser ≤ {sloBoundMs}ms; obtido={sw.ElapsedMilliseconds}ms");
    }

    /// <summary>
    /// SLO_P03: Throughput sustentado — 20 extrações consecutivas sem erro.
    /// Verifica que o extrator não vaza handles de arquivo nem degrada com extrações repetidas.
    /// </summary>
    [Fact]
    [Trait("Category", "SLO_P03")]
    public async Task SLO_P03_Throughput_20_extrações_consecutivas_sem_erro()
    {
        var pdfs = Enumerable.Range(0, 20)
            .Select(i => CriarPdfComPalavras(_tempDir, $"slo-p03-{i:D2}.pdf", paginas: 1, palavrasPorPagina: 50))
            .ToList();

        int erros = 0;
        long totalMs = 0;

        foreach (var pdfPath in pdfs)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var paginas = await _extrator.ExtrairAsync(pdfPath, CancellationToken.None);
                sw.Stop();
                totalMs += sw.ElapsedMilliseconds;

                if (paginas.Count == 0 || paginas[0].Palavras.Count == 0)
                    erros++;
            }
            catch
            {
                erros++;
            }
        }

        erros.Should().Be(0, "20 extrações consecutivas não devem produzir erros");

        var mediaMs = totalMs / pdfs.Count;
        mediaMs.Should().BeLessThanOrEqualTo(900,
            $"média de extração deve ser ≤ 900ms; obtido={mediaMs}ms por PDF");
    }

    /// <summary>
    /// SLO_P04: Extração sem vazamento de handles — arquivo permanece acessível após ExtrairAsync.
    /// Garante que o PdfDocument é disposto imediatamente (sem file lock residual).
    /// </summary>
    [Fact]
    [Trait("Category", "SLO_P04")]
    public async Task SLO_P04_Nao_vaza_file_handle_arquivo_legivel_apos_extracao()
    {
        var pdfPath = CriarPdfComPalavras(_tempDir, "slo-p04.pdf", paginas: 1, palavrasPorPagina: 10);

        await _extrator.ExtrairAsync(pdfPath, CancellationToken.None);

        // Arquivo deve ser acessível para leitura/escrita após extração (sem lock)
        var act = () => File.OpenWrite(pdfPath);
        act.Should().NotThrow("extrator não pode manter file handle após disposição do PdfDocument");
    }

    /// <summary>
    /// SLO_P05: Cancelamento via CancellationToken não produz exceção interna não tratada.
    /// </summary>
    [Fact]
    [Trait("Category", "SLO_P05")]
    public async Task SLO_P05_Cancelamento_via_token_nao_causa_excecao_interna()
    {
        var pdfPath = CriarPdfComPalavras(_tempDir, "slo-p05.pdf", paginas: 1, palavrasPorPagina: 10);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // OperationCanceledException é esperada — nenhuma outra exceção deve vazar
        Func<Task> act = () => _extrator.ExtrairAsync(pdfPath, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>(
            "cancellation deve propagar como OperationCanceledException, não exceção interna");
    }

    /// <summary>
    /// SLO_P06: Memória — 50 extrações consecutivas sem crescimento linear detectável.
    /// Força GC antes/depois e verifica que o crescimento de heap está dentro de limite (20MB).
    /// </summary>
    [Fact]
    [Trait("Category", "SLO_P06")]
    public async Task SLO_P06_Memoria_50_extracaoes_sem_crescimento_linear()
    {
        var pdfPath = CriarPdfComPalavras(_tempDir, "slo-p06.pdf", paginas: 1, palavrasPorPagina: 50);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var memAntes = GC.GetTotalMemory(forceFullCollection: true);

        const int iteracoes = 50;
        for (int i = 0; i < iteracoes; i++)
        {
            var paginas = await _extrator.ExtrairAsync(pdfPath, CancellationToken.None);
            paginas.Should().NotBeEmpty();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var memDepois = GC.GetTotalMemory(forceFullCollection: true);
        var crescimento = (long)(memDepois - memAntes);

        // Limite: 20MB para 50 extrações (extrator deve liberar recursos entre chamadas)
        const long limiteBytes = 20L * 1024 * 1024;
        crescimento.Should().BeLessThanOrEqualTo(limiteBytes,
            $"crescimento de memória em 50 extrações deve ser ≤ {limiteBytes / (1024 * 1024)}MB; obtido={crescimento / (1024 * 1024):F1}MB");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // --- Helpers ---

    private static string CriarPdfComPalavras(string dir, string nome, int paginas, int palavrasPorPagina)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        // Conteúdo realista: CPF/CNPJ + números + texto para simular relatório contábil
        var cpf = "111.444.777-35";
        var cnpj = "04.252.011/0001-10";
        var palavraBase = $"Palavra {cpf} CNPJ {cnpj} Valor";

        for (int p = 0; p < paginas; p++)
        {
            var page = builder.AddPage(PageSize.A4);
            for (int i = 0; i < palavrasPorPagina; i++)
            {
                var y = 780.0 - (i % 50) * 14.0;
                if (y < 50) break;
                page.AddText($"{palavraBase}_{p}_{i}", 9, new UglyToad.PdfPig.Core.PdfPoint(50, y), font);
            }
        }

        var path = Path.Combine(dir, nome);
        File.WriteAllBytes(path, builder.Build());
        return path;
    }

    private static long Percentil(IList<long> valores, double p)
    {
        var sorted = valores.OrderBy(v => v).ToList();
        var idx = (int)Math.Ceiling(p * sorted.Count) - 1;
        return sorted[Math.Max(0, Math.Min(idx, sorted.Count - 1))];
    }
}
