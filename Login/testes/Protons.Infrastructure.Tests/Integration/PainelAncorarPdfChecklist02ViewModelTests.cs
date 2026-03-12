using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Protons.Core.Login.Models;
using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Services;
using Protons.UI.Painel.Funcionalidades.Ferramentas.AncorarPdf.Interface;
using Protons.UI.Painel.Funcionalidades.Ferramentas.AncorarPdf.ModelosDeVisao;
using Protons.UI.Painel.ViewModels;

namespace Protons.Infrastructure.Tests.Integration;

public sealed class PainelAncorarPdfChecklist02ViewModelTests
{
    private const int HarnessUserId = 77;

    // Abre o modal completo (AncorarPdfConfiguracao) diretamente via IniciarNovaPorDrop,
    // pois C11 mudou o drop command para abrir o wizard básico (AncorarPdfAgendamentoBasico).
    private static void AbrirModalConfiguracao(
        PainelViewModel vm,
        int clienteId = 1,
        bool solicitanteEhAdmin = false)
    {
        vm.AncorarPdfConfiguracao.IniciarNovaPorDrop(
            new NovaTarefaDropPayload(
                FerramentaId: "ancorar_pdf",
                NomeFerramenta: "Ancorar PDF",
                EsteiraId: clienteId,
                TempoAlvoUtc: DateTime.UtcNow),
            clienteId: clienteId,
            solicitanteUserId: HarnessUserId,
            solicitanteNome: "Checklist 02",
            somenteLeitura: false,
            solicitanteEhAdmin: solicitanteEhAdmin);
    }

    [Fact]
    [Trait("ChecklistGate", "C2_P2")]
    [Trait("Category", "C2_P2")]
    public void C2_P2_CampoCrossClienteDeveRespeitarPerfilDoSolicitante()
    {
        using var harnessUsuario = new PainelAncorarPdfChecklist01TestHarness(UserRole.Usuario);
        AbrirModalConfiguracao(harnessUsuario.ViewModel, solicitanteEhAdmin: false);

        var vmUsuario = harnessUsuario.ViewModel.AncorarPdfConfiguracao;
        vmUsuario.PodeAtivarPdfModeloCrossCliente.Should().BeFalse();
        vmUsuario.PdfModeloCrossCliente = true;
        vmUsuario.PdfModeloCrossCliente.Should().BeFalse();

        using var harnessAdmin = new PainelAncorarPdfChecklist01TestHarness(UserRole.Admin);
        AbrirModalConfiguracao(harnessAdmin.ViewModel, solicitanteEhAdmin: true);

        harnessAdmin.ViewModel.AncorarPdfConfiguracao.PodeAtivarPdfModeloCrossCliente.Should().BeTrue();
    }

    [Fact]
    [Trait("ChecklistGate", "C2_P3")]
    [Trait("Category", "C2_P3")]
    public void C2_P3_JustificativaCrossClienteDeveSerEnviadaNoSalvar()
    {
        using var harness = new PainelAncorarPdfChecklist01TestHarness(UserRole.Admin);
        harness.ViewModel.ClienteContextoId = 5;

        AbrirModalConfiguracao(harness.ViewModel, clienteId: 5, solicitanteEhAdmin: true);

        var vm = harness.ViewModel.AncorarPdfConfiguracao;
        vm.NomeTarefaPersonalizado = "Cross Cliente";
        vm.PastaMonitoradaPath = "/tmp/c2";
        vm.PdfModeloPath = "/tmp/c2-modelo.pdf";
        vm.PdfModeloCrossCliente = true;
        vm.PdfModeloCrossClienteJustificativa = "Treinamento com template validado em cliente homologado.";

        vm.SalvarConfiguracaoCommand.Execute(null);

        harness.AncorarPdfConfiguracaoService.Verify(x =>
            x.CriarOuAtualizar(
                It.Is<AncorarPdfSalvarEntrada>(e =>
                    e.PdfModeloCrossCliente &&
                    e.PdfModeloCrossClienteJustificativa == "Treinamento com template validado em cliente homologado."),
                It.IsAny<int>()),
            Times.Once);
    }

    [Fact]
    [Trait("ChecklistGate", "C2_G4_Integracao_Painel")]
    [Trait("ChecklistGate", "C2_F1")]
    [Trait("Category", "C2_F1")]
    public async Task C2_F1_DropCanonicoOuAlias_DeveAbrirModalRealDoAncorarPdf()
    {
        using var harness = new PainelAncorarPdfChecklist01TestHarness();
        await harness.SelecionarClienteAsync(1, "Cliente C2");

        // C11: drop abre o wizard, nao o modal completo.
        harness.ViewModel.AbrirConfigNovaTarefaPorDropCommand.Execute(new NovaTarefaDropPayload(
            FerramentaId: "ancorar_pdf",
            NomeFerramenta: "Ancorar PDF",
            EsteiraId: 1,
            TempoAlvoUtc: new DateTime(2026, 2, 22, 12, 10, 37, DateTimeKind.Utc)));

        harness.ViewModel.AgendamentoBasicoAberto.Should().BeTrue();

        // Fecha o wizard e reabre com ferramenta canonica.
        harness.ViewModel.AncorarPdfAgendamentoBasico.FecharCommand.Execute(null);
        harness.ViewModel.AgendamentoBasicoAberto.Should().BeFalse();

        harness.ViewModel.AbrirConfigNovaTarefaPorDropCommand.Execute(new NovaTarefaDropPayload(
            FerramentaId: "ancorar_pdf",
            NomeFerramenta: "Ancorar PDF",
            EsteiraId: 1,
            TempoAlvoUtc: new DateTime(2026, 2, 22, 12, 10, 37, DateTimeKind.Utc)));

        harness.ViewModel.AgendamentoBasicoAberto.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Regua")]
    public async Task Regua_TrocarCliente_DeveFecharWizardAberto()
    {
        using var harness = new PainelAncorarPdfChecklist01TestHarness();
        await harness.SelecionarClienteAsync(5, "Cliente A");

        harness.ViewModel.AbrirConfigNovaTarefaPorDropCommand.Execute(new NovaTarefaDropPayload(
            FerramentaId: "ancorar_pdf",
            NomeFerramenta: "Ancorar PDF",
            EsteiraId: 1,
            TempoAlvoUtc: new DateTime(2026, 2, 22, 12, 10, 37, DateTimeKind.Utc)));

        harness.ViewModel.AgendamentoBasicoAberto.Should().BeTrue();

        await harness.SelecionarClienteAsync(6, "Cliente B");

        harness.ViewModel.AgendamentoBasicoAberto.Should().BeFalse();
        harness.ViewModel.AncorarPdfAgendamentoBasico.EstaAtivo.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Regua")]
    public async Task Regua_LimparCliente_DeveFecharConfiguracaoAvancada()
    {
        using var harness = new PainelAncorarPdfChecklist01TestHarness();
        await harness.SelecionarClienteAsync(5, "Cliente Avancado");

        AbrirModalConfiguracao(harness.ViewModel, clienteId: 5);
        harness.ViewModel.AncorarPdfConfiguracao.EstaAtiva.Should().BeTrue();

        harness.LimparClienteSelecionado();

        harness.ViewModel.AncorarPdfConfiguracao.EstaAtiva.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Regua")]
    public async Task Regua_TrocaRapidaDeCliente_NaoDeveAplicarHistoricoDeCargaStale()
    {
        using var harness = new PainelAncorarPdfChecklist01TestHarness();

        harness.DefinirClientes(
            new Protons.Core.Clientes.Models.Cliente
            {
                Id = 5,
                CodigoCliente = "CLI-005",
                Nome = "Cliente A",
                Documento = "00000000005",
                TipoDocumento = Protons.Core.Clientes.Models.TipoDocumentoCliente.CPF,
                Ativo = true,
                CriadoPorUserId = HarnessUserId,
                CriadoEmUtc = DateTime.UtcNow,
                AtualizadoEmUtc = DateTime.UtcNow
            },
            new Protons.Core.Clientes.Models.Cliente
            {
                Id = 6,
                CodigoCliente = "CLI-006",
                Nome = "Cliente B",
                Documento = "00000000006",
                TipoDocumento = Protons.Core.Clientes.Models.TipoDocumentoCliente.CPF,
                Ativo = true,
                CriadoPorUserId = HarnessUserId,
                CriadoEmUtc = DateTime.UtcNow,
                AtualizadoEmUtc = DateTime.UtcNow
            });

        var primeiraCargaHistoricoIniciada = new ManualResetEventSlim(false);
        var liberarPrimeiraCargaHistorico = new ManualResetEventSlim(false);
        var primeiraCargaBloqueada = 0;
        var agora = DateTime.UtcNow;

        var tarefasClienteA = new[]
        {
            PainelAncorarPdfChecklist01TestHarness.CriarTarefa(51, 5, "Tarefa Cliente A", agora.AddMinutes(15))
        };
        var tarefasClienteB = new[]
        {
            PainelAncorarPdfChecklist01TestHarness.CriarTarefa(61, 6, "Tarefa Cliente B", agora.AddMinutes(30))
        };
        var historicoClienteA = new[]
        {
            PainelAncorarPdfChecklist01TestHarness.CriarTarefa(101, 5, "Historico Antigo", agora.AddMinutes(-20), TarefaStatus.Concluida)
        };
        var historicoClienteB = new[]
        {
            PainelAncorarPdfChecklist01TestHarness.CriarTarefa(202, 6, "Historico Atual", agora.AddMinutes(-5), TarefaStatus.Concluida)
        };

        harness.TarefaService
            .Setup(x => x.Buscar(It.IsAny<TarefaFiltroConsulta>(), It.IsAny<int>()))
            .Returns((TarefaFiltroConsulta filtro, int _) => filtro.ClienteId == 5 ? tarefasClienteA : tarefasClienteB);
        harness.TarefaService
            .Setup(x => x.BuscarHistoricoGlobal(It.IsAny<int>(), It.IsAny<int>()))
            .Returns(() =>
            {
                if (harness.ViewModel.ClienteContextoId == 5
                    && Interlocked.CompareExchange(ref primeiraCargaBloqueada, 1, 0) == 0)
                {
                    primeiraCargaHistoricoIniciada.Set();
                    liberarPrimeiraCargaHistorico.Wait(TimeSpan.FromSeconds(5));
                    return historicoClienteA;
                }

                return historicoClienteB;
            });
        harness.TarefaService
            .Setup(x => x.BuscarPorClienteIds(It.IsAny<IReadOnlyList<int>>(), It.IsAny<string?>(), It.IsAny<TarefaStatus?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<bool>(), It.IsAny<int>()))
            .Returns(Array.Empty<Tarefa>());

        harness.ViewModel.ClienteContextoId = 5;
        harness.ViewModel.ClienteContextoNome = "Cliente A";
        var primeiraCarga = harness.ViewModel.AtualizarTarefasClienteCommand.ExecuteAsync(null);

        primeiraCargaHistoricoIniciada.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();

        harness.ViewModel.ClienteContextoId = 6;
        harness.ViewModel.ClienteContextoNome = "Cliente B";
        var segundaCarga = harness.ViewModel.AtualizarTarefasClienteCommand.ExecuteAsync(null);

        await segundaCarga;

        liberarPrimeiraCargaHistorico.Set();
        await primeiraCarga;

        harness.ViewModel.HistoricoExecucao.Should().ContainSingle(x => x.Id == 202);
        harness.ViewModel.HistoricoExecucao.Should().NotContain(x => x.Id == 101);
        harness.ViewModel.ResumoHistoricoExecucao.Should().Contain("Histórico global ativo");
    }

    [Fact]
    [Trait("ChecklistGate", "C2_G4_Integracao_Painel")]
    [Trait("ChecklistGate", "C2_F2")]
    [Trait("Category", "C2_F2")]
    public void C2_F2_ModalDeveAbrirComDefaultsObrigatorios()
    {
        using var harness = new PainelAncorarPdfChecklist01TestHarness();

        var vm = harness.ViewModel.AncorarPdfConfiguracao;
        AbrirModalConfiguracao(harness.ViewModel);
        vm.RecorrenciaSelecionada.Should().Be("Unica");
        vm.ValidacaoClienteAtiva.Should().BeTrue();
        vm.MonitorarSubpastas.Should().BeFalse();
        vm.LimiarSimilaridadeNome.Should().Be(0.75);
        vm.OcrFallbackAtivo.Should().BeFalse();
        vm.OcrDpi.Should().Be(300);
        vm.OcrLang.Should().Be("por+eng");
        vm.FerramentaInteracaoSelecionada.Should().Be(AncorarPdfFerramentaInteracao.Navegar);
        AncorarPdfConfiguracaoViewModel.CoresPaleta.Should().HaveCount(10);
    }

    [Fact]
    [Trait("ChecklistGate", "C2_UI")]
    [Trait("Category", "C2_UI")]
    public void C2_UI_ModoApenasAncorasDeveEntrarComFerramentaManual()
    {
        using var harness = new PainelAncorarPdfChecklist01TestHarness();

        var vm = harness.ViewModel.AncorarPdfConfiguracao;
        vm.EnterAnchorOnlyMode("/tmp/modelo.pdf", _ => { });

        vm.ModoApenasAncoras.Should().BeTrue();
        vm.FerramentaInteracaoSelecionada.Should().Be(AncorarPdfFerramentaInteracao.RetanguloManual);
    }

    [Fact]
    [Trait("ChecklistGate", "C2_P1")]
    [Trait("Category", "C2_P1")]
    public async Task C2_P1_ModalDeveCarregarCamposOcrAoAbrirConfiguracaoExistente()
    {
        using var harness = new PainelAncorarPdfChecklist01TestHarness(UserRole.Admin);
        var tarefa = PainelAncorarPdfChecklist01TestHarness.CriarTarefa(
            id: 999,
            clienteId: 7,
            titulo: "OCR existente",
            vencimentoUtc: new DateTime(2026, 3, 2, 18, 0, 0, DateTimeKind.Utc),
            status: TarefaStatus.Agendada,
            responsavelUserId: HarnessUserId,
            esteiraId: 4);

        harness.AncorarPdfConfiguracaoService
            .Setup(x => x.ObterPorTarefaId(tarefa.Id, HarnessUserId))
            .Returns(new AncorarPdfConfiguracaoTarefa
            {
                TarefaId = tarefa.Id,
                ClienteId = 7,
                EsteiraId = 4,
                NomeTarefaPersonalizado = "OCR existente",
                PastaMonitoradaPath = "/tmp/ocr",
                PdfModeloPath = "/tmp/ocr/modelo.pdf",
                NomeReferenciaArquivo = "modelo",
                MonitorarSubpastas = false,
                ValidacaoClienteAtiva = true,
                LimiarSimilaridadeNome = 0.75,
                HighlightOpacity = 0.40,
                ModoSelecao = AncorarPdfModoSelecao.RetanguloLivre,
                PdfModeloCrossCliente = false,
                Recorrencia = TarefaRecorrencia.Nenhuma,
                AgendamentoSegundo = 0,
                TimezoneId = "UTC",
                PrioridadeExecucao = 3,
                OcrFallbackAtivo = true,
                OcrDpi = 450,
                OcrLang = "eng+por",
                TemplateAncoras = [],
                ProgramadoPorUserId = HarnessUserId,
                ProgramadoPorNome = "Checklist 02",
                ProgramadoEmUtc = new DateTime(2026, 3, 2, 17, 0, 0, DateTimeKind.Utc),
                AtualizadoPorUserId = HarnessUserId,
                AtualizadoEmUtc = new DateTime(2026, 3, 2, 17, 30, 0, DateTimeKind.Utc),
                VersaoTemplate = 2
            });

        var vm = harness.ViewModel.AncorarPdfConfiguracao;
        await vm.AbrirExistenteAsync(tarefa, HarnessUserId, "Checklist 02", somenteLeitura: false, solicitanteEhAdmin: true);

        vm.OcrFallbackAtivo.Should().BeTrue();
        vm.OcrDpi.Should().Be(450);
        vm.OcrLang.Should().Be("eng+por");
    }

    [Fact]
    [Trait("ChecklistGate", "C2_G4_Integracao_Painel")]
    [Trait("ChecklistGate", "C2_F4")]
    [Trait("Category", "C2_F4")]
    public void C2_F4_RecorrenciaDoModalNaoDeveExporHoraria()
    {
        // RecorrenciasPermitidas é propriedade estática — independe do wizard ou modal estar aberto.
        AncorarPdfConfiguracaoViewModel.RecorrenciasPermitidas.Should().NotContain("Horaria");
        AncorarPdfConfiguracaoViewModel.RecorrenciasPermitidas.Should().Contain(["Unica", "Diaria", "Semanal", "Mensal"]);
    }

    [Fact]
    [Trait("ChecklistGate", "C2_G4_Integracao_Painel")]
    [Trait("ChecklistGate", "C2_F8")]
    [Trait("Category", "C2_F8")]
    public void C2_F8_DevePermitirUndoRedoEAcaoDeleteNoEditorDeAncoras()
    {
        using var harness = new PainelAncorarPdfChecklist01TestHarness();

        var vm = harness.ViewModel.AncorarPdfConfiguracao;
        AbrirModalConfiguracao(harness.ViewModel);
        vm.AdicionarAncoraCommand.Execute(null);
        vm.Ancoras.Should().HaveCount(1);

        vm.DesfazerCommand.Execute(null);
        vm.Ancoras.Should().BeEmpty();

        vm.RefazerCommand.Execute(null);
        vm.Ancoras.Should().HaveCount(1);

        vm.RemoverAncoraSelecionadaCommand.Execute(null);
        vm.Ancoras.Should().BeEmpty();
        vm.AncoraSelecionada.Should().BeNull();
        vm.RemoverAncoraSelecionadaCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    [Trait("ChecklistGate", "C2_UI")]
    [Trait("Category", "C2_UI")]
    public void C2_UI_NovaAncoraDeveVirSelecionadaEAoRemoverPrimeiraDeveSelecionarProxima()
    {
        using var harness = new PainelAncorarPdfChecklist01TestHarness();

        var vm = harness.ViewModel.AncorarPdfConfiguracao;
        AbrirModalConfiguracao(harness.ViewModel);

        vm.AdicionarAncoraCommand.Execute(null);
        var primeira = vm.AncoraSelecionada;

        vm.SelecionarCorCommand.Execute(AncorarPdfConfiguracaoViewModel.CoresPaleta[1]);
        vm.AdicionarAncoraCommand.Execute(null);

        vm.Ancoras.Should().HaveCount(2);
        vm.AncoraSelecionada.Should().BeSameAs(vm.Ancoras[1]);

        vm.SelecionarAncoraCommand.Execute(primeira);
        vm.RemoverAncoraSelecionadaCommand.Execute(null);

        vm.Ancoras.Should().HaveCount(1);
        vm.AncoraSelecionada.Should().BeSameAs(vm.Ancoras[0]);
    }

    [Fact]
    [Trait("ChecklistGate", "C2_G4_Integracao_Painel")]
    [Trait("ChecklistGate", "C2_F9")]
    [Trait("Category", "C2_F9")]
    public void C2_F9_PosicaoResumoDeveAtualizarQuandoCoordenadasMudam()
    {
        using var harness = new PainelAncorarPdfChecklist01TestHarness();

        var vm = harness.ViewModel.AncorarPdfConfiguracao;
        AbrirModalConfiguracao(harness.ViewModel);
        vm.AdicionarAncoraCommand.Execute(null);
        vm.Ancoras.Should().HaveCount(1);

        var ancora = vm.Ancoras[0];
        var resumoAnterior = ancora.PosicaoResumo;

        ancora.XRel = 0.42;
        ancora.YRel = 0.31;

        ancora.PosicaoResumo.Should().NotBe(resumoAnterior);
        ancora.PosicaoResumo.Should().Contain("x=0.420");
        ancora.PosicaoResumo.Should().Contain("y=0.310");
    }

    [Fact]
    [Trait("ChecklistGate", "C2_UI")]
    [Trait("Category", "C2_UI")]
    public void C2_UI_PreviewDeveUsarBaseGeometricaUnificadaNoEixoY()
    {
        using var harness = new PainelAncorarPdfChecklist01TestHarness();

        var vm = harness.ViewModel.AncorarPdfConfiguracao;
        AbrirModalConfiguracao(harness.ViewModel);
        vm.AdicionarAncoraCommand.Execute(null);

        var ancora = vm.Ancoras[0];
        ancora.YRel = 0.5;
        ancora.AlturaRel = 0.25;

        vm.PreviewSurfaceHeight.Should().Be(1075);
        ancora.PreviewY.Should().Be(537.5);
        ancora.PreviewAltura.Should().Be(268.75);
    }

    [Fact]
    [Trait("ChecklistGate", "C2_G4_Integracao_Painel")]
    [Trait("ChecklistGate", "C2_DatePicker")]
    [Trait("Category", "C2_DatePicker")]
    public void C2_DatePicker_DataSelecionadaDeveSerDateTimeOffsetSemExcecaoDeBinding()
    {
        // Regressão: getter antigo usava new DateTimeOffset(DateTime.Local, TimeSpan.Zero) → exceção em timezone != UTC.
        var service = new Mock<IAncorarPdfConfiguracaoService>(MockBehavior.Strict);
        var instanteUtc = new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);
        var fuso = TimeZoneInfo.CreateCustomTimeZone("UTC-03", TimeSpan.FromHours(-3), "UTC-03", "UTC-03");
        var vm = new AncorarPdfConfiguracaoViewModel(
            service.Object,
            new AncorarPdfNullFilePicker(),
            new AncorarPdfCanvasFallbackPreviewAdapter(),
            () => { },
            (_, _) => { },
            (_, _, _, _) => { },
            onSaveSucesso: null,
            timeProvider: new FixedTimeProvider(instanteUtc, fuso));

        vm.Reset();

        // DataSelecionada é DateTimeOffset?; getter não deve lançar mesmo com DateTime que teve Kind=Local.
        vm.DataSelecionada.Should().NotBeNull();
        vm.DataSelecionada!.Value.Year.Should().Be(2026);
        vm.DataSelecionada.Value.Month.Should().Be(3);
        vm.DataSelecionada.Value.Day.Should().Be(15);

        // Setter do DatePicker (binding bidirecional) não deve lançar.
        vm.DataSelecionada = new DateTimeOffset(2026, 4, 20, 0, 0, 0, TimeSpan.Zero);
        vm.DataSelecionada!.Value.Day.Should().Be(20);
        vm.DataSelecionada.Value.Month.Should().Be(4);
    }

    [Fact]
    [Trait("ChecklistGate", "C2_G4_Integracao_Painel")]
    [Trait("ChecklistGate", "C2_F2")]
    [Trait("Category", "C2_F2")]
    public void C2_F2_ResetDoModalDeveUsarTimeProviderDeterministico()
    {
        var service = new Mock<IAncorarPdfConfiguracaoService>(MockBehavior.Strict);
        var fuso = TimeZoneInfo.CreateCustomTimeZone("UTC-03-C2", TimeSpan.FromHours(-3), "UTC-03", "UTC-03");
        var instanteUtc = new DateTimeOffset(2026, 2, 22, 19, 45, 0, TimeSpan.Zero);

        var vm = new AncorarPdfConfiguracaoViewModel(
            service.Object,
            new AncorarPdfNullFilePicker(),
            () => { },
            (_, _) => { },
            (_, _, _, _) => { },
            onSaveSucesso: null,
            timeProvider: new FixedTimeProvider(instanteUtc, fuso));

        vm.Reset();

        // DataSelecionada é DateTimeOffset? date-only (offset=0) para evitar exceções de binding no DatePicker.
        vm.DataSelecionada.Should().Be(new DateTimeOffset(2026, 2, 22, 0, 0, 0, TimeSpan.Zero));
        vm.HoraSelecionada.Should().Be(16);
        vm.MinutoSelecionado.Should().Be(45);
    }

    [Fact]
    [Trait("ChecklistGate", "C2_P1")]
    [Trait("Category", "C2_P1")]
    public async Task C2_P1_SelecionarPdfModeloNoModalDeveUsarPickerDeArquivo()
    {
        using var harness = new PainelAncorarPdfChecklist01TestHarness();

        var vm = harness.ViewModel.AncorarPdfConfiguracao;
        AbrirModalConfiguracao(harness.ViewModel);
        var pickerSpy = new FilePickerSpy(pdfArquivoRetorno: "/tmp/modelo_modal.pdf");
        vm.AtualizarFilePicker(pickerSpy);

        await vm.SelecionarPdfModeloCommand.ExecuteAsync(null);

        pickerSpy.SelecionarPdfModeloArquivoChamadas.Should().Be(1);
        pickerSpy.SelecionarPdfModeloPastaWizardChamadas.Should().Be(0);
        vm.PdfModeloPath.Should().Be("/tmp/modelo_modal.pdf");
    }

    [Fact]
    [Trait("ChecklistGate", "C2_P1")]
    [Trait("Category", "C2_P1")]
    public void C2_P1_ModalDeveBloquearPastaDigitadaNoCampoPdfModelo()
    {
        using var harness = new PainelAncorarPdfChecklist01TestHarness();

        var vm = harness.ViewModel.AncorarPdfConfiguracao;
        AbrirModalConfiguracao(harness.ViewModel);
        vm.NomeTarefaPersonalizado = "Teste Modal Pasta";
        vm.PastaMonitoradaPath = "/tmp";
        vm.PdfModeloPath = Path.GetTempPath();

        vm.SalvarConfiguracaoCommand.Execute(null);

        vm.Mensagem.Should().Be("No modal avançado, PDF modelo deve ser arquivo .pdf. Pasta é permitida apenas no wizard básico.");
        harness.AncorarPdfConfiguracaoService.Verify(
            x => x.CriarOuAtualizar(It.IsAny<AncorarPdfSalvarEntrada>(), It.IsAny<int>()),
            Times.Never);
    }

    [Fact]
    [Trait("ChecklistGate", "C2_P1")]
    [Trait("Category", "C2_P1")]
    public async Task C2_P1_ModalDeveGerarErroTipadoQuandoValidacaoFalha()
    {
        using var harness = new PainelAncorarPdfChecklist01TestHarness();

        var vm = harness.ViewModel.AncorarPdfConfiguracao;
        AbrirModalConfiguracao(harness.ViewModel);
        vm.NomeTarefaPersonalizado = "Teste contrato erro";
        vm.PastaMonitoradaPath = "/tmp";
        vm.PdfModeloPath = Path.GetTempPath();

        await vm.SalvarConfiguracaoCommand.ExecuteAsync(null);

        vm.UltimoErroTipado.Should().NotBeNull();
        vm.UltimoErroTipado!.Code.Should().Be(AncorarPdfErrorCode.Validation);
        vm.UltimoErroTipado.Category.Should().Be(AncorarPdfErrorCategory.Negocio);
        vm.UltimoErroTipado.Retryable.Should().BeFalse();
        vm.UltimoErroTipado.CorrelationId.Should().NotBeNullOrWhiteSpace();
        vm.UltimoErroTipado.Details.Should().Be("No modal avançado, PDF modelo deve ser arquivo .pdf. Pasta é permitida apenas no wizard básico.");
    }

    [Fact]
    [Trait("ChecklistGate", "C2_P1")]
    [Trait("Category", "C2_P1")]
    public async Task C2_P1_ModalDeveGerarErroTipadoParaNomeDuplicado()
    {
        using var harness = new PainelAncorarPdfChecklist01TestHarness();

        harness.AncorarPdfConfiguracaoService
            .Setup(x => x.CriarOuAtualizar(It.IsAny<AncorarPdfSalvarEntrada>(), It.IsAny<int>()))
            .Throws(new InvalidOperationException("Já existe tarefa ativa com o mesmo nome neste cliente/esteira."));

        var vm = harness.ViewModel.AncorarPdfConfiguracao;
        AbrirModalConfiguracao(harness.ViewModel);
        vm.NomeTarefaPersonalizado = "Duplicada";
        vm.PastaMonitoradaPath = "/tmp";
        vm.PdfModeloPath = "/tmp/modelo.pdf";

        await vm.SalvarConfiguracaoCommand.ExecuteAsync(null);

        vm.UltimoErroTipado.Should().NotBeNull();
        vm.UltimoErroTipado!.Code.Should().Be(AncorarPdfErrorCode.DuplicateTaskName);
        vm.UltimoErroTipado.Category.Should().Be(AncorarPdfErrorCategory.Negocio);
        vm.UltimoErroTipado.Retryable.Should().BeFalse();
        vm.UltimoErroTipado.CorrelationId.Should().NotBeNullOrWhiteSpace();
        vm.Mensagem.Should().Be("Já existe uma tarefa com este nome neste cliente/esteira. Escolha um nome diferente.");
    }

    [Fact]
    [Trait("ChecklistGate", "C2_P1")]
    [Trait("Category", "C2_P1")]
    public async Task C2_P1_ModalDeveEnviarCamposOcrNoSalvar()
    {
        using var harness = new PainelAncorarPdfChecklist01TestHarness(UserRole.Admin);

        var vm = harness.ViewModel.AncorarPdfConfiguracao;
        AbrirModalConfiguracao(harness.ViewModel, solicitanteEhAdmin: true);
        vm.NomeTarefaPersonalizado = "OCR save";
        vm.PastaMonitoradaPath = "/tmp";
        vm.PdfModeloPath = "/tmp/modelo.pdf";
        vm.OcrFallbackAtivo = true;
        vm.OcrDpi = 400;
        vm.OcrLang = "POR + eng";

        await vm.SalvarConfiguracaoCommand.ExecuteAsync(null);

        harness.AncorarPdfConfiguracaoService.Verify(x =>
            x.CriarOuAtualizar(
                It.Is<AncorarPdfSalvarEntrada>(e =>
                    e.OcrFallbackAtivo &&
                    e.OcrDpi == 400 &&
                    e.OcrLang == "por + eng"),
                It.IsAny<int>()),
            Times.Once);
    }

    [Fact]
    [Trait("ChecklistGate", "C2_P1")]
    [Trait("Category", "C2_P1")]
    public async Task C2_P1_ModalDeveBloquearSalvarQuandoOcrDpiInvalido()
    {
        using var harness = new PainelAncorarPdfChecklist01TestHarness(UserRole.Admin);

        var vm = harness.ViewModel.AncorarPdfConfiguracao;
        AbrirModalConfiguracao(harness.ViewModel, solicitanteEhAdmin: true);
        vm.NomeTarefaPersonalizado = "OCR dpi inválido";
        vm.PastaMonitoradaPath = "/tmp";
        vm.PdfModeloPath = "/tmp/modelo.pdf";
        vm.OcrFallbackAtivo = true;
        vm.OcrDpi = 700;
        vm.OcrLang = "por+eng";

        await vm.SalvarConfiguracaoCommand.ExecuteAsync(null);

        vm.Mensagem.Should().Be("OCR DPI deve estar entre 150 e 600.");
        harness.AncorarPdfConfiguracaoService.Verify(
            x => x.CriarOuAtualizar(It.IsAny<AncorarPdfSalvarEntrada>(), It.IsAny<int>()),
            Times.Never);
    }

    [Fact]
    [Trait("ChecklistGate", "C2_P1")]
    [Trait("Category", "C2_P1")]
    public async Task C2_P1_ModalDeveBloquearSalvarQuandoOcrLangInvalido()
    {
        using var harness = new PainelAncorarPdfChecklist01TestHarness(UserRole.Admin);

        var vm = harness.ViewModel.AncorarPdfConfiguracao;
        AbrirModalConfiguracao(harness.ViewModel, solicitanteEhAdmin: true);
        vm.NomeTarefaPersonalizado = "OCR lang inválido";
        vm.PastaMonitoradaPath = "/tmp";
        vm.PdfModeloPath = "/tmp/modelo.pdf";
        vm.OcrFallbackAtivo = true;
        vm.OcrDpi = 300;
        vm.OcrLang = "por++eng";

        await vm.SalvarConfiguracaoCommand.ExecuteAsync(null);

        vm.Mensagem.Should().Be("OCR idiomas deve usar formato Tesseract, por exemplo: por+eng.");
        harness.AncorarPdfConfiguracaoService.Verify(
            x => x.CriarOuAtualizar(It.IsAny<AncorarPdfSalvarEntrada>(), It.IsAny<int>()),
            Times.Never);
    }

    private sealed class FilePickerSpy : IAncorarPdfFilePicker
    {
        private readonly string? _pdfArquivoRetorno;
        private readonly string? _pdfPastaWizardRetorno;

        public int SelecionarPdfModeloArquivoChamadas { get; private set; }
        public int SelecionarPdfModeloPastaWizardChamadas { get; private set; }

        public FilePickerSpy(string? pdfArquivoRetorno = null, string? pdfPastaWizardRetorno = null)
        {
            _pdfArquivoRetorno = pdfArquivoRetorno;
            _pdfPastaWizardRetorno = pdfPastaWizardRetorno;
        }

        public Task<string?> SelecionarPastaAsync()
        {
            return Task.FromResult<string?>(null);
        }

        public Task<string?> SelecionarPdfModeloArquivoAsync()
        {
            SelecionarPdfModeloArquivoChamadas++;
            return Task.FromResult(_pdfArquivoRetorno);
        }

        public Task<string?> SelecionarPdfModeloPastaWizardAsync()
        {
            SelecionarPdfModeloPastaWizardChamadas++;
            return Task.FromResult(_pdfPastaWizardRetorno);
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;
        private readonly TimeZoneInfo _localTimeZone;

        public FixedTimeProvider(DateTimeOffset utcNow, TimeZoneInfo localTimeZone)
        {
            _utcNow = utcNow;
            _localTimeZone = localTimeZone;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public override TimeZoneInfo LocalTimeZone => _localTimeZone;
    }
}
