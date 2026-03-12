using FluentAssertions;
using Microsoft.Data.Sqlite;
using Protons.Core.Clientes.Models;
using Protons.Core.Login.Models;
using Protons.Core.Tarefas.Models;
using Protons.Infrastructure.Clientes.Repositories;
using Protons.Infrastructure.Login.Database;
using Protons.Infrastructure.Login.Repositories;
using Protons.Infrastructure.Tarefas.Repositories;
using Protons.Infrastructure.Tarefas.Services;
using Xunit;

namespace Protons.Infrastructure.Tests.Repositories.Tarefas;

/// <summary>
/// Testes de integracao para misfire do scheduler AncorarPdf com SQLite real.
///
/// Cenarios cobertos:
///   C4_G4_Schema     — tabelas do scheduler existem no schema v9.
///   C4_G4_Misfire    — tarefa atrasada >= limiar gera backlog pendente.
///   C4_G4_Dedup      — segundo tick para mesma janela nao duplica backlog (ON CONFLICT DO NOTHING).
///   C4_G4_Disparo    — tarefa dentro do limiar dispara via TryMarkAsInProgress.
///   C4_G4_TryMark    — TryMarkAsInProgress e atomico: apenas um de dois workers consecutivos ganha.
///   C4_G4_Resolver   — backlog pode ser resolvido e nao aparece mais como pendente.
///   C4_G4_Evento     — eventos do scheduler sao persistidos corretamente.
/// </summary>
[Trait("Checklist", "C4")]
[Trait("Category", "C4_G4_Misfire")]
public sealed class AncorarPdfChecklist04MisfireTests
{
    // ------------------------------------------------------------------
    // C4_G4_Schema — schema v9 cria tabelas do scheduler
    // ------------------------------------------------------------------

    [Fact]
    [Trait("ChecklistGate", "C4_G4_Schema")]
    public void C4_G4_SchemaV9_DeveCriarTabelasDoScheduler()
    {
        var dbPath = CriarDbPath("schema_v9");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();

            using var connection = new SqliteConnection(db.ConnectionString);
            connection.Open();

            foreach (var tabela in new[]
                     {
                         "AncorarPdfSchedulerBacklogPendente",
                         "AncorarPdfSchedulerEventos"
                     })
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText =
                    $"SELECT name FROM sqlite_master WHERE type='table' AND name='{tabela}'";
                cmd.ExecuteScalar()?.ToString()
                   .Should().Be(tabela, $"tabela {tabela} deve existir no schema v9");
            }

            using var versaoCmd = connection.CreateCommand();
            versaoCmd.CommandText =
                "SELECT CAST(Valor AS INTEGER) FROM SchemaMetadata WHERE Chave='SchemaVersion' LIMIT 1";
            var versao = Convert.ToInt32(versaoCmd.ExecuteScalar());
            versao.Should().BeGreaterThanOrEqualTo(9, "schema deve ser ao menos v9 apos C4");
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    // ------------------------------------------------------------------
    // C4_G4_Misfire — tarefa atrasada cria backlog
    // ------------------------------------------------------------------

    [Fact]
    [Trait("ChecklistGate", "C4_G4_Misfire")]
    public void C4_G4_TarefaAtrasada_CriaBacklogPendente()
    {
        var dbPath = CriarDbPath("misfire_backlog");
        var db     = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();

            var tarefaId  = CriarTarefaSchedulerAtiva(db, out var clienteId, out _,
                                                        vencimentoOffsetSeconds: -120); // 2 min atraso

            var cfgRepo    = new SqliteAncorarPdfConfiguracaoRepository(db);
            var tarefaRepo = new SqliteTarefaRepository(db);

            CriarConfiguracaoPadrao(cfgRepo, tarefaId, clienteId);

            var referenciaUtc = DateTime.UtcNow;
            var agendamentos  = cfgRepo.ListarAgendamentosAtivosAte(referenciaUtc, 100);

            agendamentos.Should().Contain(a => a.TarefaId == tarefaId,
                "tarefa ativa agendada com vencimento no passado deve aparecer na lista");

            // O handler usa limiar=30s; a tarefa tem 120s de atraso → BacklogRegistrado.
            var handler = new AncorarPdfJobHandler(cfgRepo, tarefaRepo,
                                                   misfireAtrasoMinimoSegundos: 30);

            var agendamento = agendamentos.First(a => a.TarefaId == tarefaId);
            var resultado   = handler.Processar(agendamento, referenciaUtc);

            resultado.Decisao.Should().Be(AncorarPdfJobDecision.BacklogRegistrado,
                "atraso de 120s >= limiar 30s deve gerar backlog");
            resultado.BacklogId.Should().NotBeNull("backlog deve ter ID persistido");

            // Verifica persistencia no banco.
            var backlog = cfgRepo.ObterBacklogPendentePorId(resultado.BacklogId!.Value);
            backlog.Should().NotBeNull("backlog deve estar persistido no SQLite");
            backlog!.TarefaId.Should().Be(tarefaId);
            backlog.AtrasoSegundos.Should().BeGreaterThanOrEqualTo(90,
                "atraso deve ser ao menos 90s (margem de execucao do teste)");
            backlog.Status.Should().Be(AncorarPdfBacklogStatus.Pendente,
                "backlog recem-criado deve ter status Pendente");
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    // ------------------------------------------------------------------
    // C4_G4_Dedup — backlog duplicado e bloqueado por ON CONFLICT
    // ------------------------------------------------------------------

    [Fact]
    [Trait("ChecklistGate", "C4_G4_Misfire")]
    public void C4_G4_BacklogDuplicado_EBloqueadoPorIdempotencia()
    {
        var dbPath = CriarDbPath("backlog_dedup");
        var db     = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();

            var tarefaId  = CriarTarefaSchedulerAtiva(db, out var clienteId, out _,
                                                        vencimentoOffsetSeconds: -200);

            var cfgRepo    = new SqliteAncorarPdfConfiguracaoRepository(db);
            var tarefaRepo = new SqliteTarefaRepository(db);

            CriarConfiguracaoPadrao(cfgRepo, tarefaId, clienteId);

            var referenciaUtc = DateTime.UtcNow;
            var agendamentos  = cfgRepo.ListarAgendamentosAtivosAte(referenciaUtc, 100);
            var agendamento   = agendamentos.First(a => a.TarefaId == tarefaId);

            var handler = new AncorarPdfJobHandler(cfgRepo, tarefaRepo,
                                                   misfireAtrasoMinimoSegundos: 30);

            // Primeira chamada: cria backlog.
            var r1 = handler.Processar(agendamento, referenciaUtc);
            r1.Decisao.Should().Be(AncorarPdfJobDecision.BacklogRegistrado);

            // Segunda chamada com mesma janela: ON CONFLICT DO NOTHING no SQLite;
            // o repositorio deve retornar o ID do registro existente, nao um novo.
            var r2 = handler.Processar(agendamento, referenciaUtc.AddSeconds(1));
            r2.Decisao.Should().Be(AncorarPdfJobDecision.BacklogRegistrado);

            // Deve existir apenas UM backlog para a mesma (TarefaId, JanelaAlvoUtc).
            var pendentes = cfgRepo.ListarBacklogPendente(clienteId, 100);
            pendentes.Where(b => b.TarefaId == tarefaId).Should().HaveCount(1,
                "ON CONFLICT(TarefaId, JanelaAlvoUtc) DO NOTHING garante idempotencia");
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    // ------------------------------------------------------------------
    // C4_G4_Disparo — tarefa dentro do limiar dispara via TryMarkAsInProgress
    // ------------------------------------------------------------------

    [Fact]
    [Trait("ChecklistGate", "C4_G4_Misfire")]
    public void C4_G4_TarefaDentroDoLimiar_DisparaViaTryMarkAsInProgress()
    {
        var dbPath = CriarDbPath("disparo_ok");
        var db     = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();

            // Vencida ha apenas 5s — dentro do limiar de 30s.
            var tarefaId  = CriarTarefaSchedulerAtiva(db, out var clienteId, out _,
                                                        vencimentoOffsetSeconds: -5);

            var cfgRepo    = new SqliteAncorarPdfConfiguracaoRepository(db);
            var tarefaRepo = new SqliteTarefaRepository(db);

            CriarConfiguracaoPadrao(cfgRepo, tarefaId, clienteId);

            var referenciaUtc = DateTime.UtcNow;
            var agendamentos  = cfgRepo.ListarAgendamentosAtivosAte(referenciaUtc, 100);
            var agendamento   = agendamentos.First(a => a.TarefaId == tarefaId);

            var handler = new AncorarPdfJobHandler(cfgRepo, tarefaRepo,
                                                   misfireAtrasoMinimoSegundos: 30);

            var resultado = handler.Processar(agendamento, referenciaUtc);

            resultado.Decisao.Should().Be(AncorarPdfJobDecision.Disparado,
                "atraso de 5s < limiar de 30s deve disparar a tarefa");
            resultado.AtrasoSegundos.Should().BeGreaterThanOrEqualTo(0);
            resultado.AtrasoSegundos.Should().BeLessThan(30);

            // Tarefa deve ter status atualizado para EmAndamento no banco.
            using var connection = new SqliteConnection(db.ConnectionString);
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT Status FROM Tarefas WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", tarefaId);
            var status = cmd.ExecuteScalar()?.ToString();
            status.Should().Be(TarefaStatus.EmAndamento.ToString(),
                "TryMarkAsInProgress deve mudar status para EmAndamento");
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    // ------------------------------------------------------------------
    // C4_G4_TryMark — atomicidade: dois workers consecutivos, apenas um ganha
    // ------------------------------------------------------------------

    [Fact]
    [Trait("ChecklistGate", "C4_G4_Misfire")]
    public void C4_G4_TryMarkAsInProgress_Atomico_ApenasUmWorkerGanha()
    {
        var dbPath = CriarDbPath("atomic_claim");
        var db     = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();

            var tarefaId  = CriarTarefaSchedulerAtiva(db, out _, out _,
                                                        vencimentoOffsetSeconds: -1);

            var tarefaRepo = new SqliteTarefaRepository(db);
            var agora      = DateTime.UtcNow;

            // Primeira chamada: deve ter sucesso.
            var w1 = tarefaRepo.TryMarkAsInProgress(tarefaId, agora);

            // Segunda chamada para a mesma tarefa (status ja = EmAndamento): deve falhar.
            var w2 = tarefaRepo.TryMarkAsInProgress(tarefaId, agora.AddMilliseconds(1));

            w1.Should().BeTrue("primeiro worker deve conquistar a tarefa");
            w2.Should().BeFalse("segundo worker deve falhar pois status ja e EmAndamento");
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    // ------------------------------------------------------------------
    // C4_G4_Resolver — backlog resolvido nao aparece mais como pendente
    // ------------------------------------------------------------------

    [Fact]
    [Trait("ChecklistGate", "C4_G4_Misfire")]
    public void C4_G4_ResolverBacklog_RemoveDaListaPendente()
    {
        var dbPath = CriarDbPath("resolver_backlog");
        var db     = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();

            var tarefaId  = CriarTarefaSchedulerAtiva(db, out var clienteId, out _,
                                                        vencimentoOffsetSeconds: -300);

            var cfgRepo    = new SqliteAncorarPdfConfiguracaoRepository(db);
            var tarefaRepo = new SqliteTarefaRepository(db);

            CriarConfiguracaoPadrao(cfgRepo, tarefaId, clienteId);

            var referenciaUtc = DateTime.UtcNow;
            var agendamento   = cfgRepo
                .ListarAgendamentosAtivosAte(referenciaUtc, 100)
                .First(a => a.TarefaId == tarefaId);

            var handler = new AncorarPdfJobHandler(cfgRepo, tarefaRepo,
                                                   misfireAtrasoMinimoSegundos: 30);
            var resultado = handler.Processar(agendamento, referenciaUtc);

            resultado.Decisao.Should().Be(AncorarPdfJobDecision.BacklogRegistrado);
            var backlogId = resultado.BacklogId!.Value;

            // Resolve o backlog.
            var resolvido = cfgRepo.ResolverBacklogPendente(
                backlogId,
                AncorarPdfBacklogStatus.Executar,
                decididoPorUserId: 1,
                decididoPorNome: "Admin C4",
                decididoEmUtc: DateTime.UtcNow,
                observacao: "reprocessado em teste C4");

            resolvido.Should().BeTrue("resolucao deve retornar true quando backlog existe");

            // Nao deve mais aparecer na lista de pendentes.
            var pendentes = cfgRepo.ListarBacklogPendente(clienteId, 100);
            pendentes.Should().NotContain(b => b.BacklogId == backlogId,
                "backlog resolvido nao deve aparecer na lista de pendentes");
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    // ------------------------------------------------------------------
    // C4_G4_Evento — eventos do scheduler sao persistidos
    // ------------------------------------------------------------------

    [Fact]
    [Trait("ChecklistGate", "C4_G4_Misfire")]
    public void C4_G4_RegistrarEvento_PersistidoNoBanco()
    {
        var dbPath = CriarDbPath("evento_scheduler");
        var db     = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();

            var tarefaId = CriarTarefaSchedulerAtiva(db, out var clienteId, out _);
            var cfgRepo  = new SqliteAncorarPdfConfiguracaoRepository(db);

            cfgRepo.RegistrarEventoScheduler(new AncorarPdfSchedulerEventoRegistro
            {
                TarefaId     = tarefaId,
                ClienteId    = clienteId,
                TipoEvento   = "scheduler_disparado",
                Detalhes     = "timezone=UTC prioridade=1",
                OcorreuEmUtc = DateTime.UtcNow
            });

            // Verifica que pelo menos um evento foi gravado.
            using var connection = new SqliteConnection(db.ConnectionString);
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText =
                $"SELECT COUNT(*) FROM AncorarPdfSchedulerEventos WHERE TarefaId={tarefaId} AND TipoEvento='scheduler_disparado'";
            var count = Convert.ToInt32(cmd.ExecuteScalar());

            count.Should().BeGreaterThanOrEqualTo(1,
                "evento do scheduler deve ser persistido na tabela AncorarPdfSchedulerEventos");
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static string CriarDbPath(string sufixo)
        => Path.Combine(Path.GetTempPath(), $"protons_c4_{sufixo}_{Guid.NewGuid():N}.db");

    private static void LimparDb(string dbPath)
    {
        if (File.Exists(dbPath))
            File.Delete(dbPath);
    }

    /// <summary>
    /// Cria User + Cliente + Tarefa com FerramentaId=ancora_pdf e Status=Agendada,
    /// com VencimentoUtc = UtcNow + vencimentoOffsetSeconds.
    /// </summary>
    private static int CriarTarefaSchedulerAtiva(
        SqliteDb db,
        out int clienteId,
        out int userId,
        int vencimentoOffsetSeconds = -60)
    {
        var userRepo    = new UserRepository(db);
        var clienteRepo = new SqliteClienteRepository(db);
        var tarefaRepo  = new SqliteTarefaRepository(db);

        userId = userRepo.Create(new User
        {
            Empresa         = "Protons",
            Nome            = "Scheduler C4",
            Cpf             = "11144477735",
            Cargo           = "Operador",
            Email           = $"scheduler.c4.{Guid.NewGuid():N}@protons.local",
            SenhaHash       = "hash",
            SenhaSalt       = "salt",
            IteracoesPbkdf2 = 100_000,
            Status          = UserStatus.Ativo,
            Role            = UserRole.Admin,
            CriadoEmUtc     = DateTime.UtcNow,
            AtualizadoEmUtc = DateTime.UtcNow
        });

        clienteId = clienteRepo.Create(new Cliente
        {
            CodigoCliente   = $"CLI-C4-{Guid.NewGuid():N}"[..14],
            Nome            = "Cliente Scheduler C4",
            TipoDocumento   = TipoDocumentoCliente.CNPJ,
            Documento       = "04252011000110",
            Ativo           = true,
            CriadoPorUserId = userId,
            CriadoEmUtc     = DateTime.UtcNow,
            AtualizadoEmUtc = DateTime.UtcNow
        });

        return tarefaRepo.Create(new Tarefa
        {
            ClienteId          = clienteId,
            FerramentaId       = FerramentaTarefaIds.AncorarPdfCanonico,
            Titulo             = "Ancorar PDF C4",
            VencimentoUtc      = DateTime.UtcNow.AddSeconds(vencimentoOffsetSeconds),
            ResponsavelUserId  = userId,
            Status             = TarefaStatus.Agendada,
            Recorrencia        = TarefaRecorrencia.Nenhuma,
            EsteiraId          = 1,
            Ativa              = true,
            CriadoPorUserId    = userId,
            CriadoEmUtc        = DateTime.UtcNow,
            AtualizadoEmUtc    = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Persiste uma configuracao minima para que a tarefa apareca em ListarAgendamentosAtivosAte.
    /// </summary>
    private static void CriarConfiguracaoPadrao(
        SqliteAncorarPdfConfiguracaoRepository cfgRepo,
        int tarefaId,
        int clienteId)
    {
        cfgRepo.Salvar(new AncorarPdfConfiguracaoTarefa
        {
            TarefaId              = tarefaId,
            ClienteId             = clienteId,
            EsteiraId             = 1,
            NomeTarefaPersonalizado = "Ancorar PDF Scheduler C4",
            PastaMonitoradaPath   = "/tmp/monitored",
            PdfModeloPath         = "/tmp/modelo.pdf",
            NomeReferenciaArquivo = "nota_fiscal",
            TimezoneId            = "UTC",
            PrioridadeExecucao    = 3,
            DstHorarioInvalidoPolicy = AncorarPdfDstHorarioInvalidoPolicy.AvancarParaProximoHorarioValido,
            DstHorarioAmbiguoPolicy  = AncorarPdfDstHorarioAmbiguoPolicy.PreferirOffsetMaisCedo,
            ProgramadoPorUserId   = 1,
            ProgramadoPorNome     = "Admin C4",
            ProgramadoEmUtc       = DateTime.UtcNow,
            AtualizadoPorUserId   = 1,
            AtualizadoEmUtc       = DateTime.UtcNow
        }, historicoAlteracao: null);
    }
}
