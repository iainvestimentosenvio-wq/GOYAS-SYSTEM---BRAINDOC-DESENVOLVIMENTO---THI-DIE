using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Protons.Core.Tarefas.Services;
using Xunit;

namespace Protons.Core.Tests.Services.Tarefas;

/// <summary>
/// C14 — Gate G2 (blocking): Contratos da camada Core do renderer de preview.
/// Cobre: modelos imutáveis, discriminated union Sucedido/Falhou, métricas,
/// NullRenderer, fallback-sem-chain e retrocompatibilidade da interface.
/// </summary>
[Trait("Checklist", "C14")]
[Trait("Category", "C14_G2_RendererCore")]
public sealed class AncorarPdfChecklist14PreviewRendererContractTests
{
    // ─── CT01: PdfRenderRequest valores default ────────────────────────────

    [Fact]
    public void CT01_PdfRenderRequest_ValoresDefault()
    {
        var req = new PdfRenderRequest("/tmp/test.pdf");

        req.PdfPath.Should().Be("/tmp/test.pdf");
        req.Pagina.Should().Be(1);
        req.Dpi.Should().Be(300);
        req.DpiNormalizado.Should().Be(300);
    }

    // ─── CT02: DpiNormalizado clampeia para [72, 1200] ────────────────────

    [Theory]
    [InlineData(0, 72)]
    [InlineData(71, 72)]
    [InlineData(72, 72)]
    [InlineData(300, 300)]
    [InlineData(432, 432)]
    [InlineData(1200, 1200)]
    [InlineData(1201, 1200)]
    [InlineData(9999, 1200)]
    public void CT02_PdfRenderRequest_DpiNormalizado_Clamp(int dpiEntrada, int dpiEsperado)
    {
        var req = new PdfRenderRequest("/tmp/test.pdf", Dpi: dpiEntrada);
        req.DpiNormalizado.Should().Be(dpiEsperado);
    }

    // ─── CT03: PdfRenderResult.Sucedido — propriedades e Sucesso=true ─────

    [Fact]
    public void CT03_PdfRenderResult_Sucedido_Propriedades()
    {
        var utc = DateTimeOffset.UtcNow;
        var metricas = new PdfRenderMetrics
        {
            NomeRenderer = "TestRenderer",
            DpiRealizado = 300,
            DuracaoMs = 42,
            ViouFallback = false,
            OcorreuEmUtc = utc
        };
        using var ms = new MemoryStream(new byte[] { 0x89, 0x50, 0x4E, 0x47 }); // PNG header
        var resultado = new PdfRenderResult.Sucedido(ms, metricas);

        resultado.Sucesso.Should().BeTrue();
        resultado.ImagemPng.Should().BeSameAs(ms);
        resultado.Metricas.NomeRenderer.Should().Be("TestRenderer");
        resultado.Metricas.DpiRealizado.Should().Be(300);
        resultado.Metricas.DuracaoMs.Should().Be(42);
        resultado.Metricas.ViouFallback.Should().BeFalse();
        resultado.Metricas.OcorreuEmUtc.Should().Be(utc);
    }

    // ─── CT04: PdfRenderResult.Falhou — propriedades e Sucesso=false ──────

    [Fact]
    public void CT04_PdfRenderResult_Falhou_Propriedades()
    {
        var metricas = new PdfRenderMetrics { NomeRenderer = "GS", DpiRealizado = 72, DuracaoMs = 5 };
        var resultado = new PdfRenderResult.Falhou("Arquivo não encontrado.", "GS_FILE_NOT_FOUND", metricas);

        resultado.Sucesso.Should().BeFalse();
        resultado.MensagemErro.Should().Be("Arquivo não encontrado.");
        resultado.CodigoErro.Should().Be("GS_FILE_NOT_FOUND");
        resultado.Metricas.NomeRenderer.Should().Be("GS");
    }

    // ─── CT05: PdfRenderMetrics — struct, campos, ViouFallback ───────────

    [Fact]
    public void CT05_PdfRenderMetrics_Struct_Campos()
    {
        var utc = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        var m = new PdfRenderMetrics
        {
            NomeRenderer = "Docnet.PDFium",
            DpiRealizado = 432,
            DuracaoMs = 150,
            ViouFallback = true,
            OcorreuEmUtc = utc
        };

        m.NomeRenderer.Should().Be("Docnet.PDFium");
        m.DpiRealizado.Should().Be(432);
        m.DuracaoMs.Should().BeGreaterThanOrEqualTo(0);
        m.ViouFallback.Should().BeTrue();
        m.OcorreuEmUtc.Offset.Should().Be(TimeSpan.Zero, "OcorreuEmUtc deve ser UTC");
    }

    // ─── CT06: PdfRendererCapabilities — record, igualdade estrutural ─────

    [Fact]
    public void CT06_PdfRendererCapabilities_RecordEquality()
    {
        var cap1 = new PdfRendererCapabilities(true, true, true, 72, 600, true, "gs");
        var cap2 = new PdfRendererCapabilities(true, true, true, 72, 600, true, "gs");

        cap1.Should().Be(cap2);
        cap1.ExternoNecessario.Should().Be("gs");
        cap1.RequereExternoInstalado.Should().BeTrue();
    }

    // ─── CT07: AncorarPdfNullPreviewRenderer — sempre Disponivel=false ────

    [Fact]
    public void CT07_NullRenderer_Disponivel_False()
    {
        var renderer = new AncorarPdfNullPreviewRenderer();

        renderer.Disponivel.Should().BeFalse();
        renderer.NomeRenderer.Should().Be("Null");
        renderer.Capabilities.RequereExternoInstalado.Should().BeFalse();
    }

    // ─── CT08: NullRenderer.RenderizarAsync → Falhou com NULL_RENDERER ────

    [Fact]
    public async Task CT08_NullRenderer_RenderizarAsync_RetornaFalha()
    {
        var renderer = new AncorarPdfNullPreviewRenderer();
        var req = new PdfRenderRequest("/nao/existe.pdf", Dpi: 300);

        var resultado = await renderer.RenderizarAsync(req, CancellationToken.None);

        resultado.Sucesso.Should().BeFalse();
        resultado.Should().BeOfType<PdfRenderResult.Falhou>();
        var falhou = (PdfRenderResult.Falhou)resultado;
        falhou.CodigoErro.Should().Be("NULL_RENDERER");
        falhou.Metricas.DuracaoMs.Should().Be(0);
        falhou.Metricas.NomeRenderer.Should().Be("Null");
    }

    // ─── CT09: Sucedido imutável — construção com métricas atualizadas ───

    [Fact]
    public void CT09_Sucedido_MetricasAtualizadas_NaoMutaOriginal()
    {
        using var ms = new MemoryStream(new byte[4]);
        var metricasOriginais = new PdfRenderMetrics { NomeRenderer = "A", DuracaoMs = 10, ViouFallback = false };
        var original = new PdfRenderResult.Sucedido(ms, metricasOriginais);

        // Criar nova instância com métricas atualizadas via with em PdfRenderMetrics (struct)
        var metricasAtualizadas = original.Metricas with { ViouFallback = true };
        var novoResultado = new PdfRenderResult.Sucedido(original.ImagemPng, metricasAtualizadas);

        novoResultado.ImagemPng.Should().BeSameAs(ms, "stream pode ser compartilhado");
        novoResultado.Metricas.ViouFallback.Should().BeTrue();
        original.Metricas.ViouFallback.Should().BeFalse("original não foi mutado");
    }

    // ─── CT10: Falhou — construção com campos alterados preserva outros ──

    [Fact]
    public void CT10_Falhou_NovaConstrucao_PreservaCodigo()
    {
        var m = new PdfRenderMetrics { NomeRenderer = "X", DuracaoMs = 5 };
        var original = new PdfRenderResult.Falhou("msg original", "CODE_A", m);

        // Criar nova instância com mensagem diferente mas mesmo código
        var clone = new PdfRenderResult.Falhou("nova msg", original.CodigoErro, original.Metricas);

        clone.CodigoErro.Should().Be("CODE_A");
        clone.MensagemErro.Should().Be("nova msg");
        original.MensagemErro.Should().Be("msg original", "original não foi mutado");
    }

    // ─── CT11: PdfRenderRequest imutável (record) ─────────────────────────

    [Fact]
    public void CT11_PdfRenderRequest_RecordEquality()
    {
        var r1 = new PdfRenderRequest("/tmp/a.pdf", Pagina: 2, Dpi: 300);
        var r2 = new PdfRenderRequest("/tmp/a.pdf", Pagina: 2, Dpi: 300);

        r1.Should().Be(r2);
    }

    // ─── CT12: PdfRenderResult pattern matching (discriminated union) ─────

    [Fact]
    public void CT12_PdfRenderResult_PatternMatching()
    {
        var m = new PdfRenderMetrics { NomeRenderer = "T" };
        using var ms = new MemoryStream(new byte[1]);
        PdfRenderResult resultado = new PdfRenderResult.Sucedido(ms, m);

        string descricao = resultado switch
        {
            PdfRenderResult.Sucedido s => $"ok:{s.Metricas.NomeRenderer}",
            PdfRenderResult.Falhou f   => $"err:{f.CodigoErro}",
            _                          => "desconhecido"
        };

        descricao.Should().Be("ok:T");
    }
}
