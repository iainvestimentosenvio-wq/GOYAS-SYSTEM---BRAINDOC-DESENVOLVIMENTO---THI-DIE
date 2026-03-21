using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Protons.Core.Tarefas.Models;
using Protons.UI.Common;
using Protons.UI.Painel.Funcionalidades.Ferramentas.AncorarPdf.ModelosDeVisao;
using Protons.UI.Painel.ViewModels;

namespace Protons.Infrastructure.Tests.Integration;

/// <summary>
/// Valida que os logs de âncoras funcionam: formato correto e gravação em log_ops.jsonl.
/// Ref: LOGS_ANCORAS_DEBUG.md
/// </summary>
public sealed class AncorarPdfLogAncorasValidationTests
{
    [Fact]
    [Trait("Category", "AncorarPdf")]
    public void LogAncoraHelper_FormatarContextoAncora_DeveIncluirNomeChavePaginaCoordenadas()
    {
        var ancora = new AncorarPdfAncoraItemViewModel
        {
            NomeExibido = "CPF do Cliente",
            ChaveTecnica = "cpf",
            Pagina = 2,
            XRel = 0.15,
            YRel = 0.32,
            LarguraRel = 0.25,
            AlturaRel = 0.05,
            ModoAncora = AncorarPdfModoAncora.RegiaoFixa,
            TipoEsperado = "cpf",
            CorHex = "#4A90D9"
        };

        var ctx = AncorarPdfLogAncoraHelper.FormatarContextoAncora(ancora, "#4A90D9");

        ctx.Should().Contain("nome=CPF_do_Cliente");
        ctx.Should().Contain("chave=cpf");
        ctx.Should().Contain("pagina=2");
        ctx.Should().Contain("xRel=0.150", "coordenadas devem usar InvariantCulture (ponto decimal)");
        ctx.Should().Contain("yRel=0.320");
        ctx.Should().Contain("wRel=0.250");
        ctx.Should().Contain("hRel=0.050");
        ctx.Should().Contain("modo=RegiaoFixa");
        ctx.Should().Contain("tipo=cpf");
        ctx.Should().Contain("cor=#4A90D9");
    }

    [Fact]
    [Trait("Category", "AncorarPdf")]
    public void LogAncoraHelper_Sanitizar_DeveTruncarLongosEReplaceEspacos()
    {
        AncorarPdfLogAncoraHelper.Sanitizar(null).Should().Be("_");
        AncorarPdfLogAncoraHelper.Sanitizar("").Should().Be("_");
        AncorarPdfLogAncoraHelper.Sanitizar("  a b  ").Should().Be("a_b");
        AncorarPdfLogAncoraHelper.Sanitizar("curto", 10).Should().Be("curto");
        AncorarPdfLogAncoraHelper.Sanitizar("texto_muito_longo_que_excede_limite", 15).Should().EndWith("…");
    }

    [Fact]
    [Trait("Category", "AncorarPdf")]
    [Trait("ChecklistGate", "C2_Logs")]
    public async Task AdicionarAncora_DeveGravarEventoComNomeChaveEmLogOps()
    {
        var logDir = Path.Combine(Path.GetTempPath(), $"protons_log_test_{Guid.NewGuid():N}");
        var logPath = Path.Combine(logDir, "log_ops.jsonl");

        OpsLogger.Initialize(logDir);

        try
        {
            using var harness = new PainelAncorarPdfChecklist01TestHarness();
            harness.ViewModel.AncorarPdfConfiguracao.IniciarNovaPorDrop(
                new NovaTarefaDropPayload(
                    FerramentaId: "ancorar_pdf",
                    NomeFerramenta: "Ancorar PDF",
                    EsteiraId: 1,
                    TempoAlvoUtc: DateTime.UtcNow),
                clienteId: 1,
                solicitanteUserId: 77,
                solicitanteNome: "Log Test",
                somenteLeitura: false,
                solicitanteEhAdmin: true);

            var vm = harness.ViewModel.AncorarPdfConfiguracao;
            vm.AdicionarAncoraCommand.Execute(null);

            vm.Ancoras.Should().HaveCount(1);
            vm.Ancoras[0].NomeExibido.Should().Be("Variável 1");
            vm.Ancoras[0].ChaveTecnica.Should().Be("variavel_1");

            OpsLogger.FlushAndStop(2000);

            await Task.Delay(300);

            File.Exists(logPath).Should().BeTrue();
            var lines = await File.ReadAllLinesAsync(logPath);
            var anchorAddLines = lines.Where(l => l.Contains("ancorar_pdf_c2_anchor_add")).ToArray();
            anchorAddLines.Should().NotBeEmpty();

            var lastAnchorAdd = anchorAddLines[^1];
            lastAnchorAdd.Should().Contain("ancorar_pdf_c2_anchor_add");
            lastAnchorAdd.Should().Contain("nome=");
            lastAnchorAdd.Should().Contain("chave=");
            lastAnchorAdd.Should().Contain("pagina=");
            lastAnchorAdd.Should().Contain("xRel=");
            lastAnchorAdd.Should().Contain("variavel_1");
        }
        finally
        {
            if (Directory.Exists(logDir))
                Directory.Delete(logDir, recursive: true);
        }
    }
}
