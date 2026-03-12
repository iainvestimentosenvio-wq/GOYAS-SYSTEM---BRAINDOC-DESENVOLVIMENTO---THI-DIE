using FluentAssertions;
using Microsoft.Data.Sqlite;
using Protons.Core.Clientes.Models;
using Protons.Core.Login.Models;
using Protons.Core.Tarefas.Models;
using Protons.Infrastructure.Clientes.Repositories;
using Protons.Infrastructure.Login.Database;
using Protons.Infrastructure.Login.Repositories;
using Protons.Infrastructure.Tarefas.Repositories;
using Xunit;

namespace Protons.Infrastructure.Tests.Repositories.Tarefas;

/// <summary>
/// C5 — Gates G2 (schema) e G3 (repos): Persistência da fila de execução e leases.
/// Usa SQLite em arquivo temporário, sem Postgres, sem rede.
/// </summary>
[Trait("Checklist", "C5")]
public sealed class AncorarPdfChecklist05PersistenceTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // G2: Schema v10
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("ChecklistGate", "C5_G2_Schema")]
    [Trait("Category", "C5_G2_Schema")]
    public void C5_G2_SchemaV10_DeveCriarTabelasDeFilaELease()
    {
        var dbPath = DbPath("schema_v10");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();

            using var conn = new SqliteConnection(db.ConnectionString);
            conn.Open();

            foreach (var tabela in new[] { "AncorarPdfExecucaoFila", "AncorarPdfExecucaoLease" })
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT name FROM sqlite_master WHERE type='table' AND name='{tabela}'";
                cmd.ExecuteScalar()?.ToString().Should().Be(tabela,
                    $"tabela {tabela} deve existir após schema v10");
            }

            using var versaoCmd = conn.CreateCommand();
            versaoCmd.CommandText = "SELECT CAST(Valor AS INTEGER) FROM SchemaMetadata WHERE Chave = 'SchemaVersion' LIMIT 1";
            var versao = Convert.ToInt32(versaoCmd.ExecuteScalar());
            versao.Should().BeGreaterThanOrEqualTo(10, "schema deve ser ao menos v10 após C5");
        }
        finally { LimparDb(dbPath); }
    }

    [Fact]
    [Trait("ChecklistGate", "C5_G2_Schema")]
    [Trait("Category", "C5_G2_Schema")]
    public void C5_G2_IndicesDaFila_DevemExistir()
    {
        var dbPath = DbPath("schema_indices");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();

            using var conn = new SqliteConnection(db.ConnectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT name FROM sqlite_master
WHERE type='index'
  AND name IN (
    'UX_AncorarPdfExecucaoFila_Tarefa_Ciclo',
    'IX_AncorarPdfExecucaoFila_Cliente_Status',
    'UX_AncorarPdfExecucaoLease_FilaItem_Ativa',
    'IX_AncorarPdfExecucaoLease_Expirado'
  )
ORDER BY name;";

            var indices = new List<string>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                indices.Add(reader.GetString(0));

            indices.Should().HaveCount(4, "todos os 4 índices de fila e lease devem existir");
        }
        finally { LimparDb(dbPath); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // G3: Repositórios de Fila
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("ChecklistGate", "C5_G3_Repos")]
    [Trait("Category", "C5_G3_Repos")]
    public void C5_G3_Enfileirar_Idempotente_SegundaChamadaRetornaFalse()
    {
        var dbPath = DbPath("enfileirar_idem");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();
            var tarefaId = CriarTarefa(db, out var clienteId, out _);
            var repo = new SqliteAncorarPdfExecucaoFilaRepository(db);

            var item = CriarFilaItem(tarefaId, clienteId, $"{tarefaId}:ciclo-001");

            var r1 = repo.Enfileirar(item);
            r1.Enfileirado.Should().BeTrue("primeira inserção deve ter sucesso");
            r1.FilaItemId.Should().NotBeNullOrEmpty();

            // Mesmo item (mesmo FilaItemId) → ON CONFLICT DO NOTHING
            var r2 = repo.Enfileirar(item);
            r2.Enfileirado.Should().BeFalse("segunda inserção com mesmo FilaItemId deve ser rejeitada");
        }
        finally { LimparDb(dbPath); }
    }

    [Fact]
    [Trait("ChecklistGate", "C5_G3_Repos")]
    [Trait("Category", "C5_G3_Repos")]
    public void C5_G3_SegundoCiclo_MesmasTarefaId_PermiteEnfileirar()
    {
        var dbPath = DbPath("enfileirar_ciclos");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();
            var tarefaId = CriarTarefa(db, out var clienteId, out _);
            var repo = new SqliteAncorarPdfExecucaoFilaRepository(db);

            // Primeiro ciclo concluído
            var item1 = CriarFilaItem(tarefaId, clienteId, $"{tarefaId}:ciclo-001");
            repo.Enfileirar(item1);
            repo.AtualizarStatus(item1.FilaItemId, AncorarPdfFilaStatus.Concluido,
                finalizadoEmUtc: DateTime.UtcNow);

            // Segundo ciclo (CicloId diferente) deve funcionar
            var item2 = CriarFilaItem(tarefaId, clienteId, $"{tarefaId}:ciclo-002");
            var r2 = repo.Enfileirar(item2);
            r2.Enfileirado.Should().BeTrue("novo ciclo para mesma tarefa deve ser permitido");
        }
        finally { LimparDb(dbPath); }
    }

    [Fact]
    [Trait("ChecklistGate", "C5_G3_Repos")]
    [Trait("Category", "C5_G3_Repos")]
    public void C5_G3_TentarReclamar_SegundoWorker_NaoObteMesmoItem()
    {
        var dbPath = DbPath("claim_exclusivo");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();
            var tarefaId = CriarTarefa(db, out var clienteId, out _);
            var repo = new SqliteAncorarPdfExecucaoFilaRepository(db);

            var item = CriarFilaItem(tarefaId, clienteId, $"{tarefaId}:ciclo-001");
            repo.Enfileirar(item);

            // Worker 1 reivindica
            var claimed1 = repo.TentarReclamar("worker-1");
            claimed1.Should().NotBeNull("worker-1 deve conseguir reclamar");
            claimed1!.Status.Should().Be(AncorarPdfFilaStatus.EmProcessamento);

            // Worker 2 não encontra mais itens Aguardando
            var claimed2 = repo.TentarReclamar("worker-2");
            claimed2.Should().BeNull("worker-2 não deve obter o mesmo item já reclamado");
        }
        finally { LimparDb(dbPath); }
    }

    [Fact]
    [Trait("ChecklistGate", "C5_G3_Repos")]
    [Trait("Category", "C5_G3_Repos")]
    public void C5_G3_TentarCancelar_ItemAguardando_Sucesso()
    {
        var dbPath = DbPath("cancelar_aguardando");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();
            var tarefaId = CriarTarefa(db, out var clienteId, out _);
            var repo = new SqliteAncorarPdfExecucaoFilaRepository(db);

            var item = CriarFilaItem(tarefaId, clienteId, $"{tarefaId}:ciclo-001");
            repo.Enfileirar(item);

            var cancelado = repo.TentarCancelar(item.FilaItemId, "operador", DateTime.UtcNow);
            cancelado.Should().BeTrue("item Aguardando deve poder ser cancelado");

            var lista = repo.Listar(new AncorarPdfFilaFiltro(Status: AncorarPdfFilaStatus.Cancelado));
            lista.Should().HaveCount(1);
            lista[0].Status.Should().Be(AncorarPdfFilaStatus.Cancelado);
        }
        finally { LimparDb(dbPath); }
    }

    [Fact]
    [Trait("ChecklistGate", "C5_G3_Repos")]
    [Trait("Category", "C5_G3_Repos")]
    public void C5_G3_TentarCancelar_ItemEmProcessamento_Falha()
    {
        var dbPath = DbPath("cancelar_em_processamento");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();
            var tarefaId = CriarTarefa(db, out var clienteId, out _);
            var repo = new SqliteAncorarPdfExecucaoFilaRepository(db);

            var item = CriarFilaItem(tarefaId, clienteId, $"{tarefaId}:ciclo-001");
            repo.Enfileirar(item);
            repo.AtualizarStatus(item.FilaItemId, AncorarPdfFilaStatus.EmProcessamento,
                iniciadoEmUtc: DateTime.UtcNow);

            var cancelado = repo.TentarCancelar(item.FilaItemId, "operador", DateTime.UtcNow);
            cancelado.Should().BeFalse("item EmProcessamento não deve poder ser cancelado");
        }
        finally { LimparDb(dbPath); }
    }

    [Fact]
    [Trait("ChecklistGate", "C5_G3_Repos")]
    [Trait("Category", "C5_G3_Repos")]
    public void C5_G3_ListarParaReidratar_RetornaApenasAguardandoSemLease()
    {
        var dbPath = DbPath("reidratar");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();
            var tarefaId = CriarTarefa(db, out var clienteId, out _);
            var filaRepo = new SqliteAncorarPdfExecucaoFilaRepository(db);
            var leaseRepo = new SqliteAncorarPdfExecucaoLeaseRepository(db);

            // Item 1: Aguardando sem lease → deve aparecer na reidratação
            var item1 = CriarFilaItem(tarefaId, clienteId, $"{tarefaId}:ciclo-001");
            filaRepo.Enfileirar(item1);

            // Item 2: Aguardando COM lease ativo → NÃO deve aparecer
            var item2 = CriarFilaItem(tarefaId, clienteId, $"{tarefaId}:ciclo-002",
                prioridade: 1);
            filaRepo.Enfileirar(item2);
            leaseRepo.TentarAcquirir(item2.FilaItemId, tarefaId, clienteId,
                "worker-x", TimeSpan.FromMinutes(5));

            var para_reidratar = filaRepo.ListarParaReidratar();
            para_reidratar.Should().HaveCount(1,
                "apenas o item sem lease ativo deve ser reidratado");
            para_reidratar[0].FilaItemId.Should().Be(item1.FilaItemId);
        }
        finally { LimparDb(dbPath); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // G3: Repositório de Leases
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("ChecklistGate", "C5_G3_Repos")]
    [Trait("Category", "C5_G3_Repos")]
    public void C5_G3_Lease_AcquirirDuasVezes_SegundaRetornaNull()
    {
        var dbPath = DbPath("lease_exclusivo");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();
            var tarefaId = CriarTarefa(db, out var clienteId, out _);
            var filaRepo = new SqliteAncorarPdfExecucaoFilaRepository(db);
            var leaseRepo = new SqliteAncorarPdfExecucaoLeaseRepository(db);

            var item = CriarFilaItem(tarefaId, clienteId, $"{tarefaId}:ciclo-001");
            filaRepo.Enfileirar(item);

            var lease1 = leaseRepo.TentarAcquirir(
                item.FilaItemId, tarefaId, clienteId, "worker-1", TimeSpan.FromMinutes(5));
            lease1.Should().NotBeNull("primeiro acquire deve ter sucesso");
            lease1!.Ativa.Should().BeTrue();

            var lease2 = leaseRepo.TentarAcquirir(
                item.FilaItemId, tarefaId, clienteId, "worker-2", TimeSpan.FromMinutes(5));
            lease2.Should().BeNull("ON CONFLICT garante apenas 1 lease ativo por item");
        }
        finally { LimparDb(dbPath); }
    }

    [Fact]
    [Trait("ChecklistGate", "C5_G3_Repos")]
    [Trait("Category", "C5_G3_Repos")]
    public void C5_G3_Lease_Expirado_AparecaNaLista()
    {
        var dbPath = DbPath("lease_expirado");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();
            var tarefaId = CriarTarefa(db, out var clienteId, out _);
            var filaRepo = new SqliteAncorarPdfExecucaoFilaRepository(db);
            var leaseRepo = new SqliteAncorarPdfExecucaoLeaseRepository(db);

            var item = CriarFilaItem(tarefaId, clienteId, $"{tarefaId}:ciclo-001");
            filaRepo.Enfileirar(item);

            // Acquire com duração negativa → expiry no passado
            var lease = leaseRepo.TentarAcquirir(
                item.FilaItemId, tarefaId, clienteId,
                "worker-1", TimeSpan.FromMilliseconds(-1));
            lease.Should().NotBeNull("acquire deve inserir mesmo com expiry no passado");

            var expirados = leaseRepo.ListarExpirados(DateTime.UtcNow.AddSeconds(1));
            expirados.Should().HaveCountGreaterThanOrEqualTo(1, "lease expirado deve aparecer na lista");
            expirados.Should().Contain(l => l.LeaseId == lease!.LeaseId);
        }
        finally { LimparDb(dbPath); }
    }

    [Fact]
    [Trait("ChecklistGate", "C5_G3_Repos")]
    [Trait("Category", "C5_G3_Repos")]
    public void C5_G3_Lease_Liberar_TornaInativo()
    {
        var dbPath = DbPath("lease_liberar");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();
            var tarefaId = CriarTarefa(db, out var clienteId, out _);
            var filaRepo = new SqliteAncorarPdfExecucaoFilaRepository(db);
            var leaseRepo = new SqliteAncorarPdfExecucaoLeaseRepository(db);

            var item = CriarFilaItem(tarefaId, clienteId, $"{tarefaId}:ciclo-001");
            filaRepo.Enfileirar(item);

            var lease = leaseRepo.TentarAcquirir(
                item.FilaItemId, tarefaId, clienteId, "worker-1", TimeSpan.FromMinutes(5));
            lease.Should().NotBeNull();

            leaseRepo.Liberar(lease!.LeaseId, "concluido");

            // Após liberar, outro worker pode adquirir
            var lease2 = leaseRepo.TentarAcquirir(
                item.FilaItemId, tarefaId, clienteId, "worker-2", TimeSpan.FromMinutes(5));
            lease2.Should().NotBeNull(
                "após liberar lease, outro worker deve conseguir adquirir");
        }
        finally { LimparDb(dbPath); }
    }

    [Fact]
    [Trait("ChecklistGate", "C5_G3_Repos")]
    [Trait("Category", "C5_G3_Repos")]
    public void C5_G3_ContarAguardando_RefletiEstado()
    {
        var dbPath = DbPath("contar_aguardando");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();
            var tarefaId = CriarTarefa(db, out var clienteId, out _);
            var repo = new SqliteAncorarPdfExecucaoFilaRepository(db);

            repo.ContarAguardando().Should().Be(0);

            repo.Enfileirar(CriarFilaItem(tarefaId, clienteId, $"{tarefaId}:c1"));
            repo.Enfileirar(CriarFilaItem(tarefaId, clienteId, $"{tarefaId}:c2", prioridade: 2));
            repo.ContarAguardando().Should().Be(2);

            // Marcar um como concluído
            var items = repo.Listar(new AncorarPdfFilaFiltro(
                Status: AncorarPdfFilaStatus.Aguardando));
            repo.AtualizarStatus(items[0].FilaItemId, AncorarPdfFilaStatus.Concluido,
                finalizadoEmUtc: DateTime.UtcNow);
            repo.ContarAguardando().Should().Be(1);
        }
        finally { LimparDb(dbPath); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static string DbPath(string sufixo)
        => Path.Combine(Path.GetTempPath(), $"protons_c5_{sufixo}_{Guid.NewGuid():N}.db");

    private static void LimparDb(string dbPath)
    {
        if (File.Exists(dbPath)) File.Delete(dbPath);
    }

    private static int CriarTarefa(SqliteDb db, out int clienteId, out int userId)
    {
        var userRepo = new UserRepository(db);
        var clienteRepo = new SqliteClienteRepository(db);
        var tarefaRepo = new SqliteTarefaRepository(db);

        userId = userRepo.Create(new User
        {
            Empresa = "Protons",
            Nome = $"Operador C5 {Guid.NewGuid():N}"[..22],
            Cpf = "11144477735",
            Cargo = "Operador",
            Email = $"op.c5.{Guid.NewGuid():N}@protons.local",
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
            CodigoCliente = $"C5-{Guid.NewGuid():N}"[..14],
            Nome = "Cliente C5",
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
            Titulo = "AncorarPdf C5",
            VencimentoUtc = DateTime.UtcNow.AddHours(1),
            ResponsavelUserId = userId,
            Status = TarefaStatus.Agendada,
            Recorrencia = TarefaRecorrencia.Nenhuma,
            EsteiraId = 1,
            Ativa = true,
            CriadoPorUserId = userId,
            CriadoEmUtc = DateTime.UtcNow,
            AtualizadoEmUtc = DateTime.UtcNow
        });
    }

    private static AncorarPdfFilaItem CriarFilaItem(
        int tarefaId,
        int clienteId,
        string cicloId,
        int prioridade = 3)
    {
        var now = DateTime.UtcNow;
        return new AncorarPdfFilaItem
        {
            FilaItemId = Guid.NewGuid().ToString("N"),
            TarefaId = tarefaId,
            ClienteId = clienteId,
            CicloId = cicloId,
            JanelaAlvoUtc = now,
            PrioridadeExecucao = prioridade,
            Status = AncorarPdfFilaStatus.Aguardando,
            Motivo = "scheduler_dispatch",
            EnfileiradoPorUserId = 0,
            EnfileiradoPorNome = "scheduler",
            EnfileiradoEmUtc = now,
            TentativasMaximas = 3,
            CorrelationId = Guid.NewGuid().ToString("N"),
            CriadoEmUtc = now
        };
    }
}
