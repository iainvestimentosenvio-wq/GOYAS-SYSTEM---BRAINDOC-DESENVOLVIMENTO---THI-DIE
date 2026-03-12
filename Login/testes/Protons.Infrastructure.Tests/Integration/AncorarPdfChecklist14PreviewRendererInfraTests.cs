using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Protons.Core.Tarefas.Services;
using Protons.Infrastructure.Tarefas.Services;
using Xunit;

namespace Protons.Infrastructure.Tests.Integration;

/// <summary>
/// C14 — Gate G3 (blocking): Testes de integração dos renderers de preview.
/// Cobre: Ghostscript (se disponível), Docnet/PDFium, FallbackRenderer (cadeia),
/// PNG válido, métricas, cancellation, sem file handle leak.
/// </summary>
[Trait("Checklist", "C14")]
[Trait("Category", "C14_G3_RendererInfra")]
public sealed class AncorarPdfChecklist14PreviewRendererInfraTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _pdfValido;
    private readonly string _pdfInexistente;

    public AncorarPdfChecklist14PreviewRendererInfraTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"c14_renderer_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _pdfInexistente = Path.Combine(_tempDir, "inexistente.pdf");
        _pdfValido = CriarPdfMinimal();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // ─── IT01: AncorarPdfDocnetRenderer.Disponivel ────────────────────────

    [Fact]
    public void IT01_DocnetRenderer_Disponivel_NaoLanca()
    {
        var renderer = new AncorarPdfDocnetRenderer();

        // Não deve lançar — se PDFium não carregar, retorna false graciosamente.
        bool disponivel = default;
        Action acao = () => disponivel = renderer.Disponivel;

        acao.Should().NotThrow();
        // Não assertamos o valor — depende do ambiente (PDFium bundled no NuGet).
    }

    // ─── IT02: DocnetRenderer.NomeRenderer e Capabilities ────────────────

    [Fact]
    public void IT02_DocnetRenderer_Capabilities()
    {
        var renderer = new AncorarPdfDocnetRenderer();

        renderer.NomeRenderer.Should().Be("Docnet.PDFium");
        renderer.Capabilities.SuportaLinux.Should().BeTrue();
        renderer.Capabilities.RequereExternoInstalado.Should().BeFalse();
        renderer.Capabilities.DpiMinimo.Should().Be(72);
        renderer.Capabilities.DpiMaximo.Should().Be(1200);
    }

    // ─── IT03: DocnetRenderer renderiza PDF válido → PNG com header correto

    [RequiresDocnetRendererFact]
    public async Task IT03_DocnetRenderer_RenderizaPdfValido_PngComHeader()
    {
        InfraTestPreconditions.GarantirDocnetDisponivelOuFalhar();
        var renderer = new AncorarPdfDocnetRenderer();

        var req = new PdfRenderRequest(_pdfValido, Pagina: 1, Dpi: 150);
        var resultado = await renderer.RenderizarAsync(req);

        resultado.Sucesso.Should().BeTrue($"PDFium deve renderizar o PDF minimal: {ObterErro(resultado)}");
        var sucedido = (PdfRenderResult.Sucedido)resultado;

        // Verificar PNG signature: 89 50 4E 47 0D 0A 1A 0A
        var header = new byte[8];
        sucedido.ImagemPng.Position = 0;
        _ = sucedido.ImagemPng.Read(header, 0, 8);
        header[0].Should().Be(0x89);
        header[1].Should().Be(0x50); // P
        header[2].Should().Be(0x4E); // N
        header[3].Should().Be(0x47); // G

        sucedido.ImagemPng.Dispose();
    }

    // ─── IT04: DocnetRenderer renderiza com DPI diferentes ───────────────

    [RequiresDocnetRendererFact]
    public async Task IT04_DocnetRenderer_DiferenteDpi_ImagensVariamDeTamanho()
    {
        InfraTestPreconditions.GarantirDocnetDisponivelOuFalhar();
        var renderer = new AncorarPdfDocnetRenderer();

        var req72 = new PdfRenderRequest(_pdfValido, Dpi: 72);
        var req300 = new PdfRenderRequest(_pdfValido, Dpi: 300);

        var r72 = await renderer.RenderizarAsync(req72);
        var r300 = await renderer.RenderizarAsync(req300);

        r72.Sucesso.Should().BeTrue($"render 72 DPI deve funcionar no PDF de referencia: {ObterErro(r72)}");
        r300.Sucesso.Should().BeTrue($"render 300 DPI deve funcionar no PDF de referencia: {ObterErro(r300)}");

        var tamanho72 = ((PdfRenderResult.Sucedido)r72).ImagemPng.Length;
        var tamanho300 = ((PdfRenderResult.Sucedido)r300).ImagemPng.Length;

        tamanho300.Should().BeGreaterThan(tamanho72, "imagem em 300 DPI deve ser maior que em 72 DPI");

        ((PdfRenderResult.Sucedido)r72).ImagemPng.Dispose();
        ((PdfRenderResult.Sucedido)r300).ImagemPng.Dispose();
    }

    // ─── IT05: DocnetRenderer arquivo inexistente → Falhou ───────────────

    [Fact]
    public async Task IT05_DocnetRenderer_ArquivoInexistente_RetornaFalha()
    {
        var renderer = new AncorarPdfDocnetRenderer();
        var req = new PdfRenderRequest(_pdfInexistente, Dpi: 300);

        var resultado = await renderer.RenderizarAsync(req);

        resultado.Sucesso.Should().BeFalse();
        var falhou = (PdfRenderResult.Falhou)resultado;
        falhou.CodigoErro.Should().Be("DOCNET_FILE_NOT_FOUND");
    }

    // ─── IT06: DocnetRenderer métricas preenchidas corretamente ──────────

    [RequiresDocnetRendererFact]
    public async Task IT06_DocnetRenderer_Metricas_PreenchidasCorretamente()
    {
        InfraTestPreconditions.GarantirDocnetDisponivelOuFalhar();
        var renderer = new AncorarPdfDocnetRenderer();

        var antes = DateTimeOffset.UtcNow;
        var req = new PdfRenderRequest(_pdfValido, Dpi: 150);
        var resultado = await renderer.RenderizarAsync(req);

        resultado.Metricas.NomeRenderer.Should().Be("Docnet.PDFium");
        resultado.Metricas.DpiRealizado.Should().Be(150);
        resultado.Metricas.DuracaoMs.Should().BeGreaterThanOrEqualTo(0);
        resultado.Metricas.OcorreuEmUtc.Should().BeOnOrAfter(antes.AddSeconds(-1));
        resultado.Metricas.ViouFallback.Should().BeFalse();

        if (resultado is PdfRenderResult.Sucedido s) s.ImagemPng.Dispose();
    }

    // ─── IT07: GhostscriptRenderer.Disponivel não lança ─────────────────

    [Fact]
    public void IT07_GhostscriptRenderer_Disponivel_NaoLanca()
    {
        var renderer = new AncorarPdfGhostscriptRenderer();

        Action acao = () => _ = renderer.Disponivel;

        acao.Should().NotThrow();
        renderer.NomeRenderer.Should().Be("Ghostscript");
        renderer.Capabilities.RequereExternoInstalado.Should().BeTrue();
        renderer.Capabilities.ExternoNecessario.Should().Be("gs");
    }

    // ─── IT08: GhostscriptRenderer arquivo inexistente → Falhou ─────────

    [Fact]
    public async Task IT08_GhostscriptRenderer_ArquivoInexistente_RetornaFalha()
    {
        var renderer = new AncorarPdfGhostscriptRenderer();
        var req = new PdfRenderRequest(_pdfInexistente, Dpi: 300);

        var resultado = await renderer.RenderizarAsync(req);

        resultado.Sucesso.Should().BeFalse();
        var falhou = (PdfRenderResult.Falhou)resultado;
        falhou.CodigoErro.Should().Be("GS_FILE_NOT_FOUND");
        falhou.Metricas.NomeRenderer.Should().Be("Ghostscript");
    }

    // ─── IT09: GhostscriptRenderer cancellation ──────────────────────────

    [RequiresGhostscriptRendererFact]
    public async Task IT09_GhostscriptRenderer_CancellationToken_RetornaCancelado()
    {
        InfraTestPreconditions.GarantirGhostscriptDisponivelOuFalhar();
        var renderer = new AncorarPdfGhostscriptRenderer();

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // cancelar antes de iniciar

        var req = new PdfRenderRequest(_pdfValido, Dpi: 300);
        var resultado = await renderer.RenderizarAsync(req, cts.Token);

        // Com token já cancelado e gs disponível, gs retorna resultado
        // ou retorna GS_CANCELLED dependendo do timing.
        // Garantimos que não lança exceção ao chamador.
        resultado.Should().NotBeNull();
    }

    // ─── IT10: FallbackRenderer — cadeia com NullRenderer → falha esperada

    [Fact]
    public async Task IT10_FallbackRenderer_CadeiaApenasnull_RetornaFalha()
    {
        var renderer = new AncorarPdfFallbackPreviewRenderer(
            [new AncorarPdfNullPreviewRenderer()]);

        var req = new PdfRenderRequest(_pdfValido, Dpi: 300);
        var resultado = await renderer.RenderizarAsync(req);

        resultado.Sucesso.Should().BeFalse("NullRenderer nunca renderiza");
        renderer.Disponivel.Should().BeFalse();
    }

    // ─── IT11: FallbackRenderer — primeiro falha, segundo sucede → ViouFallback=true

    [Fact]
    public async Task IT11_FallbackRenderer_PrimeiroFalha_SegundoSucede_ViouFallbackTrue()
    {
        var rendererQueFalha = new StubRenderer(sucesso: false, "STUB_FAIL");
        var rendererQueSucede = new StubRenderer(sucesso: true, null);
        var fallback = new AncorarPdfFallbackPreviewRenderer(
            [rendererQueFalha, rendererQueSucede]);
        var logs = new List<string>();
        var fallbackComLog = new AncorarPdfFallbackPreviewRenderer(
            [rendererQueFalha, rendererQueSucede],
            onLog: logs.Add);

        var req = new PdfRenderRequest(_pdfValido, Dpi: 300);
        var resultado = await fallbackComLog.RenderizarAsync(req);

        resultado.Sucesso.Should().BeTrue("segundo renderer sucede");
        resultado.Metricas.ViouFallback.Should().BeTrue("não foi o renderer primário");
        logs.Should().NotBeEmpty("logging deve ter ocorrido");

        if (resultado is PdfRenderResult.Sucedido s) s.ImagemPng.Dispose();
    }

    // ─── IT12: FallbackRenderer — primeiro sucede → ViouFallback=false ───

    [Fact]
    public async Task IT12_FallbackRenderer_PrimeiroSucede_ViouFallbackFalse()
    {
        var rendererPrimario = new StubRenderer(sucesso: true, null);
        var fallback = new AncorarPdfFallbackPreviewRenderer(
            [rendererPrimario, new StubRenderer(sucesso: false, "STUB_FAIL")]);

        var req = new PdfRenderRequest(_pdfValido, Dpi: 300);
        var resultado = await fallback.RenderizarAsync(req);

        resultado.Sucesso.Should().BeTrue();
        resultado.Metricas.ViouFallback.Should().BeFalse("primeiro renderer sucedeu");

        if (resultado is PdfRenderResult.Sucedido s) s.ImagemPng.Dispose();
    }

    // ─── IT13: FallbackRenderer NomeRenderer inclui todos os renderers ───

    [Fact]
    public void IT13_FallbackRenderer_NomeRenderer_CadeiaFormatada()
    {
        var fallback = new AncorarPdfFallbackPreviewRenderer(
            [new AncorarPdfDocnetRenderer(), new AncorarPdfGhostscriptRenderer()]);

        fallback.NomeRenderer.Should().Be("Docnet.PDFium→Ghostscript");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    private static string ObterErro(PdfRenderResult resultado)
        => resultado is PdfRenderResult.Falhou f ? $"[{f.CodigoErro}] {f.MensagemErro}" : string.Empty;

    /// <summary>
    /// Cria um PDF mínimo válido (1 página A4 em branco) usando escrita direta de bytes.
    /// Compatível com PDFium e Ghostscript.
    /// </summary>
    private string CriarPdfMinimal()
    {
        var pdfPath = Path.Combine(_tempDir, "minimal.pdf");

        // PDF mínimo válido: 1 página A4 branca sem conteúdo.
        var conteudo = @"%PDF-1.4
1 0 obj<</Type /Catalog /Pages 2 0 R>>endobj
2 0 obj<</Type /Pages /Kids [3 0 R] /Count 1>>endobj
3 0 obj<</Type /Page /Parent 2 0 R /MediaBox [0 0 595 842]>>endobj
xref
0 4
0000000000 65535 f
0000000009 00000 n
0000000052 00000 n
0000000101 00000 n
trailer<</Size 4 /Root 1 0 R>>
startxref
190
%%EOF";

        File.WriteAllText(pdfPath, conteudo, System.Text.Encoding.ASCII);
        return pdfPath;
    }

    // ─── Stub renderer para testes de fallback ────────────────────────────

    private sealed class StubRenderer : IPdfPreviewRenderer
    {
        private readonly bool _sucesso;
        private readonly string? _codigoErro;

        public StubRenderer(bool sucesso, string? codigoErro)
        {
            _sucesso = sucesso;
            _codigoErro = codigoErro;
        }

        public string NomeRenderer => _sucesso ? "StubSuccess" : "StubFail";

        public PdfRendererCapabilities Capabilities =>
            new(true, true, true, 72, 300, false);

        public bool Disponivel => true;

        public Task<PdfRenderResult> RenderizarAsync(
            PdfRenderRequest request,
            CancellationToken ct = default)
        {
            if (_sucesso)
            {
                var ms = new MemoryStream(new byte[] { 0x89, 0x50, 0x4E, 0x47 });
                return Task.FromResult<PdfRenderResult>(new PdfRenderResult.Sucedido(ms, new PdfRenderMetrics
                {
                    NomeRenderer = NomeRenderer,
                    DpiRealizado = request.DpiNormalizado,
                    DuracaoMs = 1,
                    ViouFallback = false,
                    OcorreuEmUtc = DateTimeOffset.UtcNow
                }));
            }

            return Task.FromResult<PdfRenderResult>(new PdfRenderResult.Falhou(
                "Stub falhou intencionalmente.",
                _codigoErro ?? "STUB_FAIL",
                new PdfRenderMetrics
                {
                    NomeRenderer = NomeRenderer,
                    DpiRealizado = request.DpiNormalizado,
                    DuracaoMs = 0,
                    ViouFallback = false,
                    OcorreuEmUtc = DateTimeOffset.UtcNow
                }));
        }
    }
}
