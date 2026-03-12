using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Moq;
using Protons.Core.Clientes.Models;
using Protons.Core.Login.Models;
using Protons.Core.Tarefas.Models;
using Protons.Infrastructure.Clientes.Repositories;
using Protons.Infrastructure.Login.Database;
using Protons.Infrastructure.Login.Repositories;
using Protons.Infrastructure.Tarefas.Repositories;
using Protons.Infrastructure.Tarefas.Services;
using Protons.UI.Painel.ViewModels;
using Protons.UI.Painel.Funcionalidades.Ferramentas.AncorarPdf.Dominio;

namespace Protons.Infrastructure.Tests.Integration;

public sealed class AncorarPdfChecklist01ExecutionFlowTests
{
    [Fact]
    [Trait("ChecklistGate", "F2")]
    [Trait("Category", "F2")]
    public async Task F2_DeveManterPrecisaoDeSegundosNoItemDaRegua()
    {
        using var harness = new PainelAncorarPdfChecklist01TestHarness();
        var vencimentoComSegundos = new DateTime(2026, 2, 22, 13, 44, 37, DateTimeKind.Utc);

        harness.TarefaService
            .Setup(x => x.Buscar(It.IsAny<TarefaFiltroConsulta>(), It.IsAny<int>()))
            .Returns((TarefaFiltroConsulta filtro, int _) =>
            [
                PainelAncorarPdfChecklist01TestHarness.CriarTarefa(
                    id: 1001,
                    clienteId: filtro.ClienteId,
                    titulo: "Ancorar PDF com segundos",
                    vencimentoUtc: vencimentoComSegundos,
                    status: TarefaStatus.Agendada,
                    esteiraId: 1)
            ]);

        harness.ViewModel.ClienteContextoId = 9001;
        harness.ViewModel.ClienteContextoNome = "Cliente Segundos";

        await harness.ViewModel.AtualizarTarefasClienteCommand.ExecuteAsync(null);

        var item = harness.ViewModel.Esteiras
            .SelectMany(e => e.Tarefas)
            .First(t => t.TarefaId == 1001);

        item.VencimentoUtc.Second.Should().Be(37);
    }

    [Fact]
    [Trait("ChecklistGate", "F3")]
    [Trait("Category", "F3")]
    public async Task F3_DeveConsultarTarefasNoEscopoDoClienteSelecionado()
    {
        using var harness = new PainelAncorarPdfChecklist01TestHarness();
        var clientesConsultados = new List<int>();

        harness.TarefaService
            .Setup(x => x.Buscar(It.IsAny<TarefaFiltroConsulta>(), It.IsAny<int>()))
            .Returns((TarefaFiltroConsulta filtro, int _) =>
            {
                clientesConsultados.Add(filtro.ClienteId);
                return Array.Empty<Tarefa>();
            });

        harness.ViewModel.ClienteContextoId = 10;
        await harness.ViewModel.AtualizarTarefasClienteCommand.ExecuteAsync(null);

        harness.ViewModel.ClienteContextoId = 20;
        await harness.ViewModel.AtualizarTarefasClienteCommand.ExecuteAsync(null);

        clientesConsultados.Should().Contain(10);
        clientesConsultados.Should().Contain(20);
    }

    [Fact]
    [Trait("ChecklistGate", "F5")]
    [Trait("Category", "F5")]
    public async Task F5_TarefaPassada_DeveAbrirEmModoLeitura()
    {
        using var harness = new PainelAncorarPdfChecklist01TestHarness();

        await harness.ViewModel.AbrirConfigTarefaCommand.ExecuteAsync(1002);

        harness.ViewModel.ConfiguracaoTarefaSubtitulo.Should().Contain(
            "Leitura",
            "Checklist 01 exige abertura de tarefa passada sem permitir edicao.");
    }

    [Fact]
    [Trait("ChecklistGate", "F6")]
    [Trait("Category", "F6")]
    public void F6_DevePersistirCamposDeAssinaturaNoContratoDeConfiguracao()
    {
        var agoraUtc = DateTime.UtcNow;
        var agoraLocal = agoraUtc.ToLocalTime();

        var config = new AncorarPdfConfig
        {
            ClienteId = 700,
            EsteiraId = 1,
            NomeTarefa = "Ancorar PDF - Assinatura",
            Recorrencia = AncorarPdfRecorrencia.Unica,
            ProgramadoPorUserId = 99,
            ProgramadoPorNome = "Operador Teste",
            ProgramadoEmUtc = agoraUtc,
            ProgramadoEmLocal = agoraLocal
        };

        config.ProgramadoPorUserId.Should().Be(99);
        config.ProgramadoPorNome.Should().Be("Operador Teste");
        config.ProgramadoEmUtc.Should().Be(agoraUtc);
        config.ProgramadoEmLocal.Should().Be(agoraLocal);
        config.ClienteId.Should().Be(700);
    }

    [Fact]
    [Trait("ChecklistGate", "F7")]
    [Trait("Category", "F7")]
    public void F7_DeveAplicarIdempotenciaPorNomeEsperadoNoMesmoCiclo()
    {
        SelecaoNomeAproximadoPorCiclo.PodeProcessarNoCiclo(false).Should().BeTrue();
        SelecaoNomeAproximadoPorCiclo.PodeProcessarNoCiclo(true).Should().BeFalse();
    }

    [Fact]
    [Trait("ChecklistGate", "F8")]
    [Trait("Category", "F8")]
    public void F8_DeveExistirComponenteDeBloqueioPorValidacaoDeCliente()
    {
        var assembly = typeof(PainelViewModel).Assembly;
        var existeComponenteValidacaoCliente = assembly
            .GetTypes()
            .Any(t =>
                t.Name.Contains("AncorarPdf", StringComparison.OrdinalIgnoreCase) &&
                t.Name.Contains("ValidacaoCliente", StringComparison.OrdinalIgnoreCase));

        existeComponenteValidacaoCliente.Should().BeTrue(
            "o bloqueio por validacao de cliente ainda precisa de implementacao explicita no fluxo de execucao.");
    }

    [Fact]
    [Trait("ChecklistGate", "F9")]
    [Trait("Category", "F9")]
    public void F9_DeveUsarRetryTecnicoPadrao52060NoContrato()
    {
        var config = new AncorarPdfConfig();

        config.RetryTentativas.Should().Be(3);
        config.RetryBackoffSegundos.Should().Equal(5, 20, 60);
    }

    [Fact]
    [Trait("ChecklistGate", "F10")]
    [Trait("Category", "F10")]
    public void F10_DeveRegistrarMisfireEBacklogComDecisaoExplicita()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"protons_checklist01_f10_{Guid.NewGuid():N}.db");
        var db = new SqliteDb(dbPath);
        db.EnsureCreated();

        try
        {
            var tarefaId = CriarTarefaAncorarPdf(db, DateTime.UtcNow.AddMinutes(-5), out var clienteId, out var userId);
            var configRepo = new SqliteAncorarPdfConfiguracaoRepository(db);
            var tarefaRepo = new SqliteTarefaRepository(db);

            configRepo.Salvar(new AncorarPdfConfiguracaoTarefa
            {
                TarefaId = tarefaId,
                ClienteId = clienteId,
                EsteiraId = 1,
                NomeTarefaPersonalizado = "Ancorar PDF F10",
                PastaMonitoradaPath = "/tmp/f10",
                PdfModeloPath = "/tmp/f10-modelo.pdf",
                NomeReferenciaArquivo = "f10",
                MonitorarSubpastas = false,
                ValidacaoClienteAtiva = true,
                LimiarSimilaridadeNome = 0.75,
                HighlightOpacity = 0.40,
                ModoSelecao = AncorarPdfModoSelecao.RetanguloLivre,
                PdfModeloCrossCliente = false,
                Recorrencia = TarefaRecorrencia.Nenhuma,
                AgendamentoSegundo = 0,
                TimezoneId = TimeZoneInfo.Local.Id,
                PrioridadeExecucao = 3,
                DstHorarioInvalidoPolicy = AncorarPdfDstHorarioInvalidoPolicy.AvancarParaProximoHorarioValido,
                DstHorarioAmbiguoPolicy = AncorarPdfDstHorarioAmbiguoPolicy.PreferirOffsetMaisCedo,
                TemplateAncoras = [],
                ProgramadoPorUserId = userId,
                ProgramadoPorNome = "Operador F10",
                ProgramadoEmUtc = DateTime.UtcNow.AddMinutes(-6),
                AtualizadoPorUserId = userId,
                AtualizadoEmUtc = DateTime.UtcNow.AddMinutes(-6),
                VersaoTemplate = 1
            }, historicoAlteracao: null);

            var referenciaUtc = DateTime.UtcNow;
            var agendamento = configRepo
                .ListarAgendamentosAtivosAte(referenciaUtc, 10)
                .Single(x => x.TarefaId == tarefaId);

            var handler = new AncorarPdfJobHandler(
                configRepo,
                tarefaRepo,
                timeProvider: TimeProvider.System,
                misfireAtrasoMinimoSegundos: 30);

            var resultado = handler.Processar(agendamento, referenciaUtc);

            resultado.Decisao.Should().Be(
                AncorarPdfJobDecision.BacklogRegistrado,
                "Checklist 01 F10 exige comportamento real de misfire/backlog no scheduler.");
            resultado.AtrasoSegundos.Should().BeGreaterThan(30);

            var backlogPendentes = configRepo.ListarBacklogPendente(clienteId, limite: 20);
            backlogPendentes.Should().ContainSingle(x => x.TarefaId == tarefaId);
            var backlogId = backlogPendentes[0].BacklogId;

            var tarefaAposMisfire = tarefaRepo.GetById(tarefaId);
            tarefaAposMisfire.Should().NotBeNull();
            tarefaAposMisfire!.Status.Should().Be(
                TarefaStatus.Agendada,
                "misfire deve aguardar decisão explícita do usuário antes de executar.");

            var decidiuExecutar = configRepo.ResolverBacklogPendente(
                backlogId,
                AncorarPdfBacklogStatus.Executar,
                userId,
                "Operador F10",
                DateTime.UtcNow,
                "executar agora");

            decidiuExecutar.Should().BeTrue("backlog deve aceitar decisão explícita de execução.");
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    private static int CriarTarefaAncorarPdf(SqliteDb db, DateTime vencimentoUtc, out int clienteId, out int userId)
    {
        var userRepo = new UserRepository(db);
        var clienteRepo = new SqliteClienteRepository(db);
        var tarefaRepo = new SqliteTarefaRepository(db);

        userId = userRepo.Create(new User
        {
            Empresa = "Protons",
            Nome = "Operador F10",
            Cpf = "11144477735",
            Cargo = "Operador",
            Email = $"operador.f10.{Guid.NewGuid():N}@protons.local",
            SenhaHash = "hash",
            SenhaSalt = "salt",
            IteracoesPbkdf2 = 100000,
            Status = UserStatus.Ativo,
            Role = UserRole.Admin,
            CriadoEmUtc = DateTime.UtcNow,
            AtualizadoEmUtc = DateTime.UtcNow
        });

        clienteId = clienteRepo.Create(new Cliente
        {
            CodigoCliente = $"CLI-F10-{Guid.NewGuid():N}"[..14],
            Nome = "Cliente F10",
            TipoDocumento = TipoDocumentoCliente.CNPJ,
            Documento = "04252011000110",
            Ativo = true,
            CriadoPorUserId = userId,
            CriadoEmUtc = DateTime.UtcNow,
            AtualizadoEmUtc = DateTime.UtcNow
        });

        return tarefaRepo.Create(new Tarefa
        {
            ClienteId = clienteId,
            FerramentaId = FerramentaTarefaIds.AncorarPdfCanonico,
            Titulo = "Ancorar PDF F10",
            VencimentoUtc = vencimentoUtc,
            ResponsavelUserId = userId,
            Status = TarefaStatus.Agendada,
            Recorrencia = TarefaRecorrencia.Nenhuma,
            EsteiraId = 1,
            Ativa = true,
            CriadoPorUserId = userId,
            CriadoEmUtc = DateTime.UtcNow.AddMinutes(-10),
            AtualizadoEmUtc = DateTime.UtcNow.AddMinutes(-10)
        });
    }

    [Fact]
    [Trait("ChecklistGate", "F11")]
    [Trait("Category", "F11")]
    public async Task F11_HistoricoDeveExibirEstadosCompletos()
    {
        using var harness = new PainelAncorarPdfChecklist01TestHarness();
        var agora = DateTime.UtcNow;

        harness.ClienteService
            .Setup(x => x.ListarTodosPorUsuario(It.IsAny<int>()))
            .Returns([
                new Cliente { Id = 1, Nome = "Cliente A", CodigoCliente = "CLI-A", Documento = "11111111111" }
            ]);

        harness.TarefaService
            .Setup(x => x.BuscarHistoricoGlobal(It.IsAny<int>(), It.IsAny<int>()))
            .Returns([
                PainelAncorarPdfChecklist01TestHarness.CriarTarefa(1, 1, "Concluida", agora.AddMinutes(-1), TarefaStatus.Concluida),
                PainelAncorarPdfChecklist01TestHarness.CriarTarefa(2, 1, "Erro", agora.AddMinutes(-2), TarefaStatus.Bloqueada),
                PainelAncorarPdfChecklist01TestHarness.CriarTarefa(3, 1, "Atrasada", agora.AddMinutes(-30), TarefaStatus.Agendada),
                PainelAncorarPdfChecklist01TestHarness.CriarTarefa(4, 1, "Pendente", agora.AddMinutes(30), TarefaStatus.Agendada)
            ]);

        await harness.ViewModel.AtualizarTarefasClienteCommand.ExecuteAsync(null);

        var estados = harness.ViewModel.HistoricoExecucao
            .Select(x => x.Status)
            .ToList();

        estados.Should().Contain("SUCESSO");
        estados.Should().Contain("ERRO");
        estados.Should().Contain("CANCELADO");
        estados.Should().Contain("PENDENTE");
    }

    [Fact]
    [Trait("ChecklistGate", "F12")]
    [Trait("Category", "F12")]
    public async Task F12_IndicadoresDoPainel_DevemRefletirResultadoRealDaExecucao()
    {
        using var harness = new PainelAncorarPdfChecklist01TestHarness();
        var agora = DateTime.UtcNow;

        var tarefasKpi = new[]
        {
            PainelAncorarPdfChecklist01TestHarness.CriarTarefa(11, 777, "Concluida", agora.AddMinutes(-2), TarefaStatus.Concluida),
            PainelAncorarPdfChecklist01TestHarness.CriarTarefa(12, 777, "Bloqueada", agora.AddMinutes(-1), TarefaStatus.Bloqueada),
            PainelAncorarPdfChecklist01TestHarness.CriarTarefa(13, 777, "Em andamento", agora.AddMinutes(10), TarefaStatus.EmAndamento)
        };
        harness.TarefaService
            .Setup(x => x.Buscar(It.IsAny<TarefaFiltroConsulta>(), It.IsAny<int>()))
            .Returns((TarefaFiltroConsulta filtro, int _) => tarefasKpi);
        harness.TarefaService
            .Setup(x => x.BuscarPorClienteIds(It.IsAny<IReadOnlyList<int>>(), It.IsAny<string?>(), It.IsAny<TarefaStatus?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<bool>(), It.IsAny<int>()))
            .Returns(tarefasKpi);

        harness.DefinirClientes(new Cliente
        {
            Id = 777,
            CodigoCliente = "CLI-777",
            Nome = "Cliente KPI",
            Documento = "77777777777",
            TipoDocumento = TipoDocumentoCliente.CPF,
            Ativo = true,
            CriadoPorUserId = 77,
            CriadoEmUtc = DateTime.UtcNow,
            AtualizadoEmUtc = DateTime.UtcNow
        });
        harness.ViewModel.ClienteContextoId = 777;
        harness.ViewModel.ClienteContextoNome = "Cliente KPI";

        await harness.ViewModel.AtualizarTarefasClienteCommand.ExecuteAsync(null);

        harness.ViewModel.ErrosCriticos.Should().Be(1);
        harness.ViewModel.NotasHoje.Should().BeGreaterThan(0);
        harness.ViewModel.ResumoTarefasCliente.Should().Contain("com erro");
    }

    [Fact]
    [Trait("ChecklistGate", "F13")]
    [Trait("Category", "F13")]
    public void F13_DeveExistirPersistenciaParaSaidaAncoradaPorCiclo()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"protons_checklist01_f13_{Guid.NewGuid():N}.db");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();

            using var connection = new SqliteConnection(db.ConnectionString);
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
SELECT name
FROM sqlite_master
WHERE type='table'
  AND (name LIKE '%ArquivoProcessado%' OR name LIKE '%AncorarPdf%')
LIMIT 1";

            var tabela = cmd.ExecuteScalar()?.ToString();
            tabela.Should().NotBeNullOrWhiteSpace(
                "Checklist 01 exige persistencia de saida ancorada para consumo futuro.");
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }
}
