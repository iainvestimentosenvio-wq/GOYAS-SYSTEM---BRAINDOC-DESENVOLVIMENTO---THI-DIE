using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Protons.Core.Tarefas.Models;
using Protons.UI.Painel.ViewModels;

namespace Protons.Infrastructure.Tests.Integration;

public sealed class PainelAncorarPdfChecklist01ViewModelTests
{
    private sealed class FixedTimeProviderF1 : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;
        private readonly TimeZoneInfo _localTimeZone;

        public FixedTimeProviderF1(DateTimeOffset utcNow, TimeZoneInfo localTimeZone)
        {
            _utcNow = utcNow;
            _localTimeZone = localTimeZone;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
        public override TimeZoneInfo LocalTimeZone => _localTimeZone;
    }
    [Fact]
    [Trait("ChecklistGate", "G4")]
    [Trait("ChecklistGate", "F1")]
    [Trait("Category", "F1")]
    public async Task F1_DropCanonico_DeveAbrirFluxoAncorarPdfComCamposEsperados()
    {
        // TimeProvider determinístico: "agora" = 22/02/2026 14:00 UTC (antes do payload 14:30) para evitar clamp.
        var instanteUtc = new DateTimeOffset(2026, 2, 22, 14, 0, 0, TimeSpan.Zero);
        var fuso = TimeZoneInfo.CreateCustomTimeZone("UTC-03-F1", TimeSpan.FromHours(-3), "UTC-03", "UTC-03");
        using var harness = new PainelAncorarPdfChecklist01TestHarness(timeProvider: new FixedTimeProviderF1(instanteUtc, fuso));
        await harness.SelecionarClienteAsync(1, "Cliente F1");

        var payload = new NovaTarefaDropPayload(
            FerramentaId: "ancorar_pdf",
            NomeFerramenta: "Ancorar PDF",
            EsteiraId: 1,
            TempoAlvoUtc: new DateTime(2026, 2, 22, 14, 30, 47, DateTimeKind.Utc));

        harness.ViewModel.AbrirConfigNovaTarefaPorDropCommand.Execute(payload);

        // 14:30 UTC em UTC-3 = 11:30 local.
        harness.ViewModel.AgendamentoBasicoAberto.Should().BeTrue();
        harness.ViewModel.AncorarPdfAgendamentoBasico.NomeTarefa.Should().Be("Ancorar PDF");
        harness.ViewModel.AncorarPdfAgendamentoBasico.HoraSelecionado.Should().Be(11);
        harness.ViewModel.AncorarPdfAgendamentoBasico.MinutoSelecionado.Should().Be(30);
    }

    [Fact]
    [Trait("ChecklistGate", "G4")]
    [Trait("ChecklistGate", "F1")]
    [Trait("Category", "F1")]
    public async Task F1_DropCanonico_DeveAbrirWizardBasico()
    {
        using var harness = new PainelAncorarPdfChecklist01TestHarness();
        await harness.SelecionarClienteAsync(1, "Cliente Wizard");
        var payload = new NovaTarefaDropPayload(
            FerramentaId: "ancorar_pdf",
            NomeFerramenta: "Ancorar PDF",
            EsteiraId: 1,
            TempoAlvoUtc: new DateTime(2026, 2, 22, 18, 5, 9, DateTimeKind.Utc));

        harness.ViewModel.AbrirConfigNovaTarefaPorDropCommand.Execute(payload);

        harness.ViewModel.AgendamentoBasicoAberto.Should().BeTrue();
        harness.ViewModel.AncorarPdfAgendamentoBasico.NomeTarefa.Should().Be("Ancorar PDF");
    }

    [Fact]
    [Trait("Category", "Regua")]
    public void Regua_SemCliente_DropNaoDeveAbrirWizard()
    {
        using var harness = new PainelAncorarPdfChecklist01TestHarness();
        var payload = new NovaTarefaDropPayload(
            FerramentaId: "ancorar_pdf",
            NomeFerramenta: "Ancorar PDF",
            EsteiraId: 1,
            TempoAlvoUtc: new DateTime(2026, 2, 22, 18, 5, 9, DateTimeKind.Utc));

        harness.ViewModel.AbrirConfigNovaTarefaPorDropCommand.Execute(payload);

        harness.ViewModel.AgendamentoBasicoAberto.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Regua")]
    public void Regua_SemCliente_AdicionarEsteiraNaoDeveAlterarColecao()
    {
        using var harness = new PainelAncorarPdfChecklist01TestHarness();

        harness.ViewModel.Esteiras.Should().BeEmpty();
        harness.ViewModel.AdicionarEsteiraCommand.Execute(null);

        harness.ViewModel.Esteiras.Should().BeEmpty();
    }

    [Fact]
    [Trait("ChecklistGate", "G4")]
    [Trait("ChecklistGate", "F4")]
    [Trait("Category", "F4")]
    public async Task F4_CliqueNaRegua_DeveReabrirConfiguracaoDaTarefa()
    {
        using var harness = new PainelAncorarPdfChecklist01TestHarness();

        await harness.ViewModel.AbrirConfigTarefaCommand.ExecuteAsync(321);

        harness.ViewModel.ConfiguracaoTarefaAberta.Should().BeTrue();
        harness.ViewModel.ConfiguracaoTarefaId.Should().Be(321);
        harness.ViewModel.ConfiguracaoTarefaTitulo.Should().Be("Tarefa #321");
        harness.ViewModel.ConfiguracaoTarefaSubtitulo.Should().Contain("tarefa");
    }

    [Fact]
    [Trait("ChecklistGate", "G4")]
    public void CatalogoLateral_DeveUsarIdCanonicoAncorarPdf()
    {
        using var harness = new PainelAncorarPdfChecklist01TestHarness();

        var itemAncorarPdf = harness.ViewModel.FerramentasTarefasLateral
            .FirstOrDefault(x => string.Equals(x.FerramentaId, "ancorar_pdf", StringComparison.OrdinalIgnoreCase));

        itemAncorarPdf.Should().NotBeNull();
        itemAncorarPdf!.Nome.Should().Be("Ancorar PDF");
    }

    [Fact]
    [Trait("ChecklistGate", "C7_AUTO_1")]
    [Trait("Category", "Regua")]
    public async Task C7_ExecucaoAutomatica_MesmoCicloNaoDeveExecutarDuasVezes()
    {
        using var harness = new PainelAncorarPdfChecklist01TestHarness();
        harness.ViewModel.ClienteContextoId = 42;

        var cicloUtc = new DateTime(2026, 2, 26, 15, 20, 30, DateTimeKind.Utc);

        await harness.ViewModel.ExecutarTarefaAutomaticaAsync(901, cicloUtc);
        await harness.ViewModel.ExecutarTarefaAutomaticaAsync(901, cicloUtc);

        harness.TarefaService.Verify(
            x => x.AlterarStatus(901, TarefaStatus.Concluida, It.IsAny<int>()),
            Times.Once);
    }

    [Fact]
    [Trait("ChecklistGate", "C7_AUTO_2")]
    [Trait("Category", "Regua")]
    public async Task C7_RemoverEsteira_ComTarefas_DeveInformarOcultacaoNaSessao()
    {
        using var harness = new PainelAncorarPdfChecklist01TestHarness();
        await harness.SelecionarClienteAsync(42, "Cliente Esteira");

        harness.ViewModel.AdicionarEsteiraCommand.Execute(null);
        var esteira = harness.ViewModel.Esteiras[1];
        esteira.Tarefas.Add(new TarefaReguaItem(
            1200,
            "Tarefa para remoção",
            DateTime.UtcNow.AddMinutes(10),
            "10:00",
            "AGENDADA",
            "#F59E0B",
            false,
            false,
            esteira.Id,
            "Operador"));

        harness.ViewModel.SolicitarRemoverEsteiraCommand.Execute(esteira.Id);

        harness.ViewModel.ConfirmacaoRemoverEsteiraAberta.Should().BeTrue();
        harness.ViewModel.ConfirmacaoRemoverEsteiraMensagem.Should().Contain("ocultadas");
        harness.ViewModel.ConfirmacaoRemoverEsteiraMensagem.Should().Contain("sessão");
        harness.ViewModel.ConfirmacaoRemoverEsteiraMensagem.Should().NotContain("canceladas");
    }

    [Fact]
    [Trait("Category", "Regua")]
    public async Task Regua_LimparCliente_DeveEsvaziarEsteirasEFecharDock()
    {
        using var harness = new PainelAncorarPdfChecklist01TestHarness();
        await harness.SelecionarClienteAsync(100, "Cliente Limpeza");
        harness.ViewModel.PainelEsteirasDockAberto = true;

        harness.LimparClienteSelecionado();

        harness.ViewModel.Esteiras.Should().BeEmpty();
        harness.ViewModel.PainelEsteirasDockAberto.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Regua")]
    public async Task Regua_SelecionarClienteSemLayout_DeveCriarUmaEsteiraPadrao()
    {
        using var harness = new PainelAncorarPdfChecklist01TestHarness();

        harness.ViewModel.Esteiras.Should().BeEmpty();
        await harness.SelecionarClienteAsync(200, "Cliente Novo");

        harness.ViewModel.Esteiras.Should().HaveCount(1);
        harness.ViewModel.Esteiras[0].Nome.Should().Be("Esteira 1");
    }

    [Fact]
    [Trait("Category", "Regua")]
    public async Task Regua_AlternarEntreClientes_DevePreservarLayoutsSeparados()
    {
        using var harness = new PainelAncorarPdfChecklist01TestHarness();

        await harness.SelecionarClienteAsync(1, "Cliente A");
        harness.ViewModel.AdicionarEsteiraCommand.Execute(null);
        harness.ViewModel.Esteiras.Should().HaveCount(2);

        await harness.SelecionarClienteAsync(2, "Cliente B");
        harness.ViewModel.Esteiras.Should().HaveCount(1);
        harness.ViewModel.AdicionarEsteiraCommand.Execute(null);
        harness.ViewModel.Esteiras.Should().HaveCount(2);

        await harness.SelecionarClienteAsync(1, "Cliente A");
        harness.ViewModel.Esteiras.Should().HaveCount(2);

        await harness.SelecionarClienteAsync(2, "Cliente B");
        harness.ViewModel.Esteiras.Should().HaveCount(2);
    }
}
