using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Services;
using Protons.UI.Painel.Funcionalidades.Ferramentas.AncorarPdf.Interface;
using Protons.UI.Painel.Funcionalidades.Ferramentas.AncorarPdf.ModelosDeVisao;
using Protons.UI.Painel.ViewModels;

namespace Protons.Infrastructure.Tests.Integration;

public sealed class AncorarPdfChecklist02ExecutionFlowTests
{
    // Abre o modal completo (AncorarPdfConfiguracao) diretamente via IniciarNovaPorDrop,
    // pois C11 mudou o drop command para abrir o wizard básico (AncorarPdfAgendamentoBasico).
    private const int HarnessUserId = 77;

    private static void AbrirModalConfiguracao(PainelViewModel vm, int clienteId = 1)
    {
        vm.AncorarPdfConfiguracao.IniciarNovaPorDrop(
            new NovaTarefaDropPayload(
                FerramentaId: "ancorar_pdf",
                NomeFerramenta: "Ancorar PDF",
                EsteiraId: clienteId,
                TempoAlvoUtc: System.DateTime.UtcNow),
            clienteId: clienteId,
            solicitanteUserId: HarnessUserId,
            solicitanteNome: "Checklist 02",
            somenteLeitura: false,
            solicitanteEhAdmin: false);
    }

    [Fact]
    [Trait("ChecklistGate", "C2_F5")]
    [Trait("Category", "C2_F5")]
    public async Task C2_F5_PickerDeveRespeitarCancelamentoSemAlterarCampos()
    {
        using var harness = new PainelAncorarPdfChecklist01TestHarness();
        harness.ViewModel.ClienteContextoId = 1;

        var vm = harness.ViewModel.AncorarPdfConfiguracao;
        AbrirModalConfiguracao(harness.ViewModel);
        vm.PastaMonitoradaPath = "/tmp/original";
        vm.PdfModeloPath = "/tmp/modelo-original.pdf";

        await vm.SelecionarPastaMonitoradaCommand.ExecuteAsync(null);
        await vm.SelecionarPdfModeloCommand.ExecuteAsync(null);

        vm.PastaMonitoradaPath.Should().Be("/tmp/original");
        vm.PdfModeloPath.Should().Be("/tmp/modelo-original.pdf");
    }

    [Fact]
    [Trait("ChecklistGate", "C2_P1")]
    [Trait("Category", "C2_P1")]
    public async Task C2_P1_WizardDeveUsarPickerDePastaParaPdfModelo()
    {
        using var harness = new PainelAncorarPdfChecklist01TestHarness();
        harness.ViewModel.ClienteContextoId = 1;

        harness.ViewModel.AbrirConfigNovaTarefaPorDropCommand.Execute(new NovaTarefaDropPayload(
            FerramentaId: "ancorar_pdf",
            NomeFerramenta: "Ancorar PDF",
            EsteiraId: 1,
            TempoAlvoUtc: DateTime.UtcNow));

        var wizardVm = harness.ViewModel.AncorarPdfAgendamentoBasico;
        var pickerSpy = new FilePickerSpy(pdfPastaWizardRetorno: Path.GetTempPath());
        wizardVm.AtualizarFilePicker(pickerSpy);

        await wizardVm.SelecionarPdfModeloCommand.ExecuteAsync(null);

        pickerSpy.SelecionarPdfModeloPastaWizardChamadas.Should().Be(1);
        pickerSpy.SelecionarPdfModeloArquivoChamadas.Should().Be(0);
        wizardVm.PdfModeloPath.Should().Be(Path.GetTempPath());
        wizardVm.Mensagem.Should().Contain("Modo wizard: pasta selecionada");
    }

    [Fact]
    [Trait("ChecklistGate", "C2_F7")]
    [Trait("Category", "C2_F7")]
    public void C2_F7_MesmaCorDevePedirConfirmacaoESubstituirQuandoConfirmado()
    {
        using var harness = new PainelAncorarPdfChecklist01TestHarness();
        harness.ViewModel.ClienteContextoId = 1;

        var vm = harness.ViewModel.AncorarPdfConfiguracao;
        AbrirModalConfiguracao(harness.ViewModel);
        vm.SelecionarCorCommand.Execute("#4A90D9");
        vm.AdicionarAncoraCommand.Execute(null);
        vm.Ancoras.Should().HaveCount(1);

        vm.AdicionarAncoraCommand.Execute(null);
        vm.ConfirmacaoSubstituicaoCorAberta.Should().BeTrue();

        vm.ConfirmarSubstituicaoCorCommand.Execute("True");
        vm.ConfirmacaoSubstituicaoCorAberta.Should().BeFalse();
        vm.Ancoras.Should().HaveCount(1);
    }

    [Fact]
    [Trait("ChecklistGate", "C2_F9")]
    [Trait("Category", "C2_F9")]
    public void C2_F9_SelecaoDeAncoraDeveRefletirPreviewImediatoNoAdapter()
    {
        using var harness = new PainelAncorarPdfChecklist01TestHarness();
        harness.ViewModel.ClienteContextoId = 1;

        var vm = harness.ViewModel.AncorarPdfConfiguracao;
        AbrirModalConfiguracao(harness.ViewModel);
        vm.Ancoras.Should().BeEmpty();

        vm.AdicionarAncoraCommand.Execute(null);

        vm.Ancoras.Should().HaveCount(1);
        vm.Ancoras[0].CorHex.Should().Be(vm.CorSelecionada);
        vm.PreviewSnapshot.Ancoras.Should().HaveCount(1);
        vm.PreviewSnapshot.Ancoras[0].CorHex.Should().Be(vm.CorSelecionada);
        vm.PreviewSnapshot.HighlightOpacity.Should().Be(vm.HighlightOpacity);
    }

    [Fact]
    [Trait("ChecklistGate", "C2_F10")]
    [Trait("Category", "C2_F10")]
    public async Task C2_F10_DeveSalvarMetadadosEditadosPorAncora()
    {
        using var harness = new PainelAncorarPdfChecklist01TestHarness();
        harness.ViewModel.ClienteContextoId = 5;

        var vm = harness.ViewModel.AncorarPdfConfiguracao;
        AbrirModalConfiguracao(harness.ViewModel, clienteId: 5);
        vm.PastaMonitoradaPath = "/tmp/c2";
        vm.PdfModeloPath = "/tmp/c2-modelo.pdf";
        vm.NomeReferenciaArquivo = "holerite";
        vm.NomeTarefaPersonalizado = "Ancorar Metadado";
        vm.AdicionarAncoraCommand.Execute(null);

        var ancora = vm.Ancoras[0];
        ancora.NomeExibido = "Valor Liquido";
        ancora.ChaveTecnica = "valor_liquido";
        ancora.TipoEsperado = "moeda";
        ancora.ExemploEsperado = "R$ 1.234,56";
        ancora.RegraNormalizacao = "trim|moeda_br";

        await vm.SalvarConfiguracaoCommand.ExecuteAsync(null);

        harness.AncorarPdfConfiguracaoService.Verify(x =>
            x.CriarOuAtualizar(
                It.Is<AncorarPdfSalvarEntrada>(e =>
                    e.TemplateAncoras.Count == 1 &&
                    e.TemplateAncoras[0].Metadado.NomeExibido == "Valor Liquido" &&
                    e.TemplateAncoras[0].Metadado.ChaveTecnica == "valor_liquido" &&
                    e.TemplateAncoras[0].Metadado.ExemploEsperado == "R$ 1.234,56" &&
                    e.TemplateAncoras[0].Metadado.RegraNormalizacao == "trim|moeda_br"),
                It.IsAny<int>()),
            Times.Once);
    }

    [Fact]
    [Trait("ChecklistGate", "C2_F11")]
    [Trait("Category", "C2_F11")]
    public void C2_F11_HoverNaListaDeveDestacarAncoraCorrespondenteNoAdapter()
    {
        using var harness = new PainelAncorarPdfChecklist01TestHarness();
        harness.ViewModel.ClienteContextoId = 1;

        var vm = harness.ViewModel.AncorarPdfConfiguracao;
        AbrirModalConfiguracao(harness.ViewModel);
        vm.AdicionarAncoraCommand.Execute(null);

        var alvo = vm.Ancoras[0];
        vm.DestacarAncoraPorHoverCommand.Execute(alvo);

        vm.AncoraSelecionada.Should().BeSameAs(alvo);
        vm.PreviewSnapshot.ChaveAncoraDestacada.Should().Be(alvo.ChaveTecnica);
        alvo.DestacadaNoPreview.Should().BeTrue();

        vm.DestacarAncoraPorHoverCommand.Execute(null);
        vm.PreviewSnapshot.ChaveAncoraDestacada.Should().BeNull();
    }

    [Fact]
    [Trait("ChecklistGate", "C2_F12")]
    [Trait("Category", "C2_F12")]
    public async Task C2_F12_ZoomEScrollDevemPreservarPersistenciaRelativa()
    {
        using var harness = new PainelAncorarPdfChecklist01TestHarness();
        harness.ViewModel.ClienteContextoId = 5;

        var vm = harness.ViewModel.AncorarPdfConfiguracao;
        AbrirModalConfiguracao(harness.ViewModel, clienteId: 5);
        vm.PastaMonitoradaPath = "/tmp/c2";
        vm.PdfModeloPath = "/tmp/c2-modelo.pdf";
        vm.NomeReferenciaArquivo = "folha";
        vm.AdicionarAncoraCommand.Execute(null);

        vm.Ancoras[0].XRel = 1.7;
        vm.Ancoras[0].YRel = -0.3;
        vm.Ancoras[0].LarguraRel = 2.0;
        vm.Ancoras[0].AlturaRel = 5.0;
        vm.ZoomPreview = 2.5;
        vm.AtualizarViewportPreview(0.40, 0.60);

        await vm.SalvarConfiguracaoCommand.ExecuteAsync(null);

        harness.AncorarPdfConfiguracaoService.Verify(
            x => x.CriarOuAtualizar(It.IsAny<AncorarPdfSalvarEntrada>(), It.IsAny<int>()),
            Times.Once);

        var entrada = harness.AncorarPdfConfiguracaoService.Invocations
            .Where(x => x.Method.Name == nameof(IAncorarPdfConfiguracaoService.CriarOuAtualizar))
            .Select(x => x.Arguments[0])
            .OfType<AncorarPdfSalvarEntrada>()
            .Single();

        entrada.TemplateAncoras.Should().NotBeEmpty();
        entrada.TemplateAncoras.All(a =>
            a.XRel >= 0 && a.XRel <= 1 &&
            a.YRel >= 0 && a.YRel <= 1 &&
            a.LarguraRel >= 0 && a.LarguraRel <= 1 &&
            a.AlturaRel >= 0 && a.AlturaRel <= 1).Should().BeTrue();
        vm.PreviewSnapshot.Viewport.Zoom.Should().Be(2.5);
        vm.PreviewSnapshot.Viewport.ScrollXRel.Should().Be(0.40);
        vm.PreviewSnapshot.Viewport.ScrollYRel.Should().Be(0.60);
    }

    [Fact]
    [Trait("ChecklistGate", "C2_F16")]
    [Trait("Category", "C2_F16")]
    public void C2_F16_DeveExibirAlertaQuandoHouverConflitoMesmoHorarioNaEsteira()
    {
        using var harness = new PainelAncorarPdfChecklist01TestHarness();
        harness.ViewModel.ClienteContextoId = 1;
        harness.ViewModel.AdicionarEsteiraCommand.Execute(null);

        var horarioConflito = new System.DateTime(2026, 2, 22, 15, 0, 30, System.DateTimeKind.Utc);
        harness.ViewModel.Esteiras[0].Tarefas.Add(new TarefaReguaItem(
            111,
            "Existente",
            horarioConflito,
            "15:00",
            "Agendada",
            "#22C55E",
            false,
            false,
            1,
            "Operador"));

        // C11: drop abre o wizard; conflito é detectado no PainelViewModel e informado via wizard.Mensagem.
        harness.ViewModel.AbrirConfigNovaTarefaPorDropCommand.Execute(new NovaTarefaDropPayload(
            FerramentaId: "ancorar_pdf",
            NomeFerramenta: "Ancorar PDF",
            EsteiraId: 1,
            TempoAlvoUtc: horarioConflito));

        harness.ViewModel.AncorarPdfAgendamentoBasico.Mensagem.Should().Contain("Conflito");
    }

    [Fact]
    [Trait("ChecklistGate", "C2_F17")]
    [Trait("Category", "C2_F17")]
    public async Task C2_F17_SalvarDeveEnviarAssinaturaObrigatoria()
    {
        using var harness = new PainelAncorarPdfChecklist01TestHarness();
        harness.ViewModel.ClienteContextoId = 5;

        var vm = harness.ViewModel.AncorarPdfConfiguracao;
        AbrirModalConfiguracao(harness.ViewModel, clienteId: 5);
        vm.PastaMonitoradaPath = "/tmp/c2";
        vm.PdfModeloPath = "/tmp/c2-modelo.pdf";
        vm.NomeReferenciaArquivo = "folha";
        await vm.SalvarConfiguracaoCommand.ExecuteAsync(null);

        harness.AncorarPdfConfiguracaoService.Verify(
            x => x.CriarOuAtualizar(It.IsAny<AncorarPdfSalvarEntrada>(), 77),
            Times.Once);

        var entrada = harness.AncorarPdfConfiguracaoService.Invocations
            .Where(x => x.Method.Name == nameof(IAncorarPdfConfiguracaoService.CriarOuAtualizar))
            .Select(x => x.Arguments[0])
            .OfType<AncorarPdfSalvarEntrada>()
            .Single();

        entrada.ProgramadoPorUserId.Should().Be(77);
        string.IsNullOrWhiteSpace(entrada.ProgramadoPorNome).Should().BeFalse();
    }

    [Fact]
    [Trait("ChecklistGate", "C2_P7")]
    [Trait("Category", "C2_P7")]
    public async Task C2_P7_SalvarNaoDevePermitirReentradaConcorrente()
    {
        using var harness = new PainelAncorarPdfChecklist01TestHarness();
        harness.ViewModel.ClienteContextoId = 5;

        var vm = harness.ViewModel.AncorarPdfConfiguracao;
        AbrirModalConfiguracao(harness.ViewModel, clienteId: 5);
        vm.PastaMonitoradaPath = "/tmp/c2";
        vm.PdfModeloPath = "/tmp/c2-modelo.pdf";
        vm.NomeReferenciaArquivo = "folha";
        vm.NomeTarefaPersonalizado = "Salvar sem reentrada";

        var iniciouPrimeiroSalvar = new ManualResetEventSlim(false);
        var liberarPrimeiroSalvar = new ManualResetEventSlim(false);
        var chamadas = 0;

        harness.AncorarPdfConfiguracaoService
            .Setup(x => x.CriarOuAtualizar(It.IsAny<AncorarPdfSalvarEntrada>(), It.IsAny<int>()))
            .Returns((AncorarPdfSalvarEntrada entrada, int _) =>
            {
                Interlocked.Increment(ref chamadas);
                iniciouPrimeiroSalvar.Set();
                liberarPrimeiroSalvar.Wait(TimeSpan.FromSeconds(2));

                return new AncorarPdfConfiguracaoTarefa
                {
                    TarefaId = entrada.TarefaId ?? 999,
                    ClienteId = entrada.ClienteId,
                    EsteiraId = entrada.EsteiraId,
                    NomeTarefaPersonalizado = entrada.NomeTarefaPersonalizado,
                    PastaMonitoradaPath = entrada.PastaMonitoradaPath,
                    PdfModeloPath = entrada.PdfModeloPath,
                    NomeReferenciaArquivo = entrada.NomeReferenciaArquivo,
                    MonitorarSubpastas = entrada.MonitorarSubpastas,
                    ValidacaoClienteAtiva = entrada.ValidacaoClienteAtiva,
                    LimiarSimilaridadeNome = entrada.LimiarSimilaridadeNome,
                    HighlightOpacity = entrada.HighlightOpacity,
                    ModoSelecao = entrada.ModoSelecao,
                    PdfModeloCrossCliente = entrada.PdfModeloCrossCliente,
                    PdfModeloCrossClienteJustificativa = entrada.PdfModeloCrossClienteJustificativa,
                    Recorrencia = entrada.Recorrencia,
                    AgendamentoSegundo = entrada.AgendamentoSegundo,
                    TemplateAncoras = entrada.TemplateAncoras,
                    ProgramadoPorUserId = entrada.ProgramadoPorUserId,
                    ProgramadoPorNome = entrada.ProgramadoPorNome,
                    ProgramadoEmUtc = DateTime.UtcNow,
                    AtualizadoPorUserId = entrada.ProgramadoPorUserId,
                    AtualizadoEmUtc = DateTime.UtcNow,
                    VersaoTemplate = 1
                };
            });

        var salvar1 = vm.SalvarConfiguracaoCommand.ExecuteAsync(null);
        iniciouPrimeiroSalvar.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();

        var salvar2 = vm.SalvarConfiguracaoCommand.ExecuteAsync(null);

        await Task.Delay(80);
        Volatile.Read(ref chamadas).Should().Be(1);

        liberarPrimeiroSalvar.Set();
        await Task.WhenAll(salvar1, salvar2);

        Volatile.Read(ref chamadas).Should().Be(1);
    }

    [Fact]
    [Trait("ChecklistGate", "C2_CONFLICT_CONFIRM")]
    [Trait("Category", "C2_CONFLICT_CONFIRM")]
    public async Task C2_ConflitoMesmoHorario_DeveExigirConfirmacaoAntesDeSalvar()
    {
        using var harness = new PainelAncorarPdfChecklist01TestHarness();
        harness.ViewModel.ClienteContextoId = 5;

        var tempoAlvo = DateTime.UtcNow.AddMinutes(3);
        harness.ViewModel.AbrirConfigNovaTarefaPorDropCommand.Execute(new NovaTarefaDropPayload(
            FerramentaId: "ancorar_pdf",
            NomeFerramenta: "Ancorar PDF",
            EsteiraId: 1,
            TempoAlvoUtc: tempoAlvo));

        var vm = harness.ViewModel.AncorarPdfAgendamentoBasico;
        InjetarStagingAncora(vm);
        vm.PastaMonitoradaPath = "/tmp/monitorada";
        vm.EtapaAtual = AncorarPdfEtapaFluxo.Confirmacao;

        var minutoConflito = new DateTime(
            tempoAlvo.Year,
            tempoAlvo.Month,
            tempoAlvo.Day,
            tempoAlvo.Hour,
            tempoAlvo.Minute,
            0,
            DateTimeKind.Utc);

        harness.TarefaService
            .Setup(x => x.Buscar(It.IsAny<TarefaFiltroConsulta>(), It.IsAny<int>()))
            .Returns(new List<Tarefa>
            {
                new()
                {
                    Id = 991,
                    ClienteId = 5,
                    EsteiraId = 1,
                    Titulo = "Conflito existente",
                    VencimentoUtc = minutoConflito.AddSeconds(14),
                    ResponsavelUserId = 77,
                    CriadoPorUserId = 77,
                    Status = TarefaStatus.Agendada,
                    Ativa = true,
                    CriadoEmUtc = DateTime.UtcNow.AddMinutes(-5),
                    AtualizadoEmUtc = DateTime.UtcNow.AddMinutes(-1)
                }
            });

        await vm.ConfirmarAgendamentoCommand.ExecuteAsync(null);

        vm.ConflitoMesmoHorarioDetectado.Should().BeTrue();
        vm.ConflitoMensagem.Should().Contain("Conflito de horário");
        harness.AncorarPdfConfiguracaoService.Verify(
            x => x.CriarOuAtualizar(It.IsAny<AncorarPdfSalvarEntrada>(), It.IsAny<int>()),
            Times.Never);

        await vm.ConfirmarConflitoMesmoHorarioCommand.ExecuteAsync(null);

        vm.ConflitoMesmoHorarioDetectado.Should().BeFalse();
        harness.AncorarPdfConfiguracaoService.Verify(
            x => x.CriarOuAtualizar(It.IsAny<AncorarPdfSalvarEntrada>(), It.IsAny<int>()),
            Times.Once);
    }

    private static void InjetarStagingAncora(AncorarPdfAgendamentoBasicoViewModel vm)
    {
        var estado = new AncorarPdfFluxoEstadoAncoras(
            new List<AncorarPdfTemplateAncora>
            {
                new()
                {
                    Ordem = 0,
                    CorHex = "#4A90D9",
                    Pagina = 1,
                    Metadado = new AncorarPdfTemplateMetadado
                    {
                        ChaveTecnica = "campo_0",
                        NomeExibido = "Campo 0",
                        TipoEsperado = "texto"
                    }
                }
            },
            "/tmp/modelo.pdf");

        var campo = typeof(AncorarPdfAgendamentoBasicoViewModel)
            .GetField("_ancoraStagging", BindingFlags.Instance | BindingFlags.NonPublic);
        campo.Should().NotBeNull();
        campo!.SetValue(vm, estado);

        var metodo = typeof(AncorarPdfAgendamentoBasicoViewModel)
            .GetMethod("NotifyAncoraProps", BindingFlags.Instance | BindingFlags.NonPublic);
        metodo.Should().NotBeNull();
        metodo!.Invoke(vm, null);
    }

    private sealed class FilePickerSpy : IAncorarPdfFilePicker
    {
        private readonly string? _pdfPastaWizardRetorno;

        public int SelecionarPdfModeloArquivoChamadas { get; private set; }
        public int SelecionarPdfModeloPastaWizardChamadas { get; private set; }

        public FilePickerSpy(string? pdfPastaWizardRetorno = null)
        {
            _pdfPastaWizardRetorno = pdfPastaWizardRetorno;
        }

        public Task<string?> SelecionarPastaAsync()
        {
            return Task.FromResult<string?>(null);
        }

        public Task<string?> SelecionarPdfModeloArquivoAsync()
        {
            SelecionarPdfModeloArquivoChamadas++;
            return Task.FromResult<string?>(null);
        }

        public Task<string?> SelecionarPdfModeloPastaWizardAsync()
        {
            SelecionarPdfModeloPastaWizardChamadas++;
            return Task.FromResult(_pdfPastaWizardRetorno);
        }
    }
}
