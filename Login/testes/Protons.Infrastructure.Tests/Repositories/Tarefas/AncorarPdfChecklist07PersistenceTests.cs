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
/// C7 — Gate G2 (blocking): Persistência dos eventos operacionais (schema v11).
/// Testa: migração idempotente, RegistrarEventoScheduler com campos C7,
/// e ListarEventosOperacionais com filtros variados.
/// </summary>
[Trait("Checklist", "C7")]
[Trait("Category", "C7_G2_Persistencia")]
public sealed class AncorarPdfChecklist07PersistenceTests
{
    // --- T01: Schema v11 cria/adiciona colunas idempotentemente ---

    [Fact]
    public void T01_SchemaV11_adiciona_colunas_C7_em_AncorarPdfSchedulerEventos()
    {
        var dbPath = CriarDbPath("schema_v11");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();

            using var connection = new SqliteConnection(db.ConnectionString);
            connection.Open();

            // Verifica que as 5 novas colunas C7 existem.
            var colunasEsperadas = new[]
            {
                "CorrelationId",
                "StatusAnterior",
                "StatusNovo",
                "ErroCodigo",
                "ExecutadaComAtraso"
            };

            using var infoCmd = connection.CreateCommand();
            infoCmd.CommandText = "PRAGMA table_info(AncorarPdfSchedulerEventos);";
            var colunasExistentes = new List<string>();
            using var infoReader = infoCmd.ExecuteReader();
            while (infoReader.Read())
                colunasExistentes.Add(infoReader.GetString(1)); // coluna "name"

            colunasExistentes.Should().Contain(colunasEsperadas,
                "todas as 5 colunas C7 devem existir em AncorarPdfSchedulerEventos após v11");
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    [Fact]
    public void T01b_Schema_versao_deve_ser_ao_menos_11()
    {
        var dbPath = CriarDbPath("schema_versao");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();

            using var connection = new SqliteConnection(db.ConnectionString);
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT CAST(Valor AS INTEGER) FROM SchemaMetadata WHERE Chave = 'SchemaVersion' LIMIT 1";
            var versao = Convert.ToInt32(cmd.ExecuteScalar());
            versao.Should().BeGreaterThanOrEqualTo(11, "schema deve ser v11+ após C7");
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    // --- T02: RegistrarEventoScheduler persiste campos C7 ---

    [Fact]
    public void T02_RegistrarEventoScheduler_persiste_campos_C7()
    {
        var dbPath = CriarDbPath("registrar_evento");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();
            var (tarefaId, clienteId) = CriarTarefaMinima(db);
            var repo = new SqliteAncorarPdfConfiguracaoRepository(db);

            var correlationId = Guid.NewGuid().ToString("N");
            var agora = DateTime.UtcNow;

            repo.RegistrarEventoScheduler(new AncorarPdfSchedulerEventoRegistro
            {
                TarefaId = tarefaId,
                ClienteId = clienteId,
                TipoEvento = AncorarPdfErroCodigos.TarefaAgendada,
                Detalhes = "timezone=UTC prioridade=3",
                OcorreuEmUtc = agora,
                CorrelationId = correlationId,
                StatusAnterior = AncorarPdfErroCodigos.StatusAgendada,
                StatusNovo = AncorarPdfErroCodigos.StatusEmAndamento,
                ErroCodigo = AncorarPdfErroCodigos.Nenhum,
                ExecutadaComAtraso = false
            });

            // Verifica diretamente via SQL para garantir persistência real.
            using var connection = new SqliteConnection(db.ConnectionString);
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
SELECT TipoEvento, CorrelationId, StatusAnterior, StatusNovo, ErroCodigo, ExecutadaComAtraso
FROM AncorarPdfSchedulerEventos
WHERE TarefaId = $tarefaId AND ClienteId = $clienteId
ORDER BY Id DESC LIMIT 1;";
            cmd.Parameters.AddWithValue("$tarefaId", tarefaId);
            cmd.Parameters.AddWithValue("$clienteId", clienteId);

            using var reader = cmd.ExecuteReader();
            reader.Read().Should().BeTrue("evento deve estar persistido");
            reader.GetString(0).Should().Be(AncorarPdfErroCodigos.TarefaAgendada);
            reader.GetString(1).Should().Be(correlationId);
            reader.GetString(2).Should().Be(AncorarPdfErroCodigos.StatusAgendada);
            reader.GetString(3).Should().Be(AncorarPdfErroCodigos.StatusEmAndamento);
            reader.GetString(4).Should().Be(AncorarPdfErroCodigos.Nenhum);
            reader.GetInt32(5).Should().Be(0, "ExecutadaComAtraso=false deve ser 0");
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    [Fact]
    public void T02b_RegistrarEventoScheduler_ExecutadaComAtraso_true_persiste_como_1()
    {
        var dbPath = CriarDbPath("atraso_true");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();
            var (tarefaId, clienteId) = CriarTarefaMinima(db);
            var repo = new SqliteAncorarPdfConfiguracaoRepository(db);

            repo.RegistrarEventoScheduler(new AncorarPdfSchedulerEventoRegistro
            {
                TarefaId = tarefaId,
                ClienteId = clienteId,
                TipoEvento = AncorarPdfErroCodigos.ExecucaoFalhou,
                OcorreuEmUtc = DateTime.UtcNow,
                ErroCodigo = AncorarPdfErroCodigos.NegPdfSemTexto,
                ExecutadaComAtraso = true
            });

            using var connection = new SqliteConnection(db.ConnectionString);
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
SELECT ErroCodigo, ExecutadaComAtraso FROM AncorarPdfSchedulerEventos
WHERE TarefaId = $id ORDER BY Id DESC LIMIT 1;";
            cmd.Parameters.AddWithValue("$id", tarefaId);
            using var reader = cmd.ExecuteReader();
            reader.Read().Should().BeTrue();
            reader.GetString(0).Should().Be(AncorarPdfErroCodigos.NegPdfSemTexto);
            reader.GetInt32(1).Should().Be(1, "ExecutadaComAtraso=true deve ser 1");
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    // --- T03–T07: ListarEventosOperacionais ---

    [Fact]
    public void T03_ListarEventosOperacionais_retorna_por_clienteId()
    {
        var dbPath = CriarDbPath("listar_cliente");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();
            var (tarefaId, clienteId) = CriarTarefaMinima(db);
            var (tarefaId2, clienteId2) = CriarTarefaMinima(db);
            var repo = new SqliteAncorarPdfConfiguracaoRepository(db);

            RegistrarEvento(repo, tarefaId, clienteId, AncorarPdfErroCodigos.TarefaAgendada);
            RegistrarEvento(repo, tarefaId, clienteId, AncorarPdfErroCodigos.ExecucaoConcluida);
            RegistrarEvento(repo, tarefaId2, clienteId2, AncorarPdfErroCodigos.TarefaAgendada);

            var eventos = repo.ListarEventosOperacionais(new AncorarPdfEventoHistoricoFiltro
            {
                ClienteId = clienteId
            });

            eventos.Should().HaveCount(2, "apenas eventos do clienteId devem ser retornados");
            eventos.Should().AllSatisfy(e => e.ClienteId.Should().Be(clienteId));
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    [Fact]
    public void T04_ListarEventosOperacionais_filtra_por_tarefaId()
    {
        var dbPath = CriarDbPath("listar_tarefa");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();
            var (tarefaId, clienteId) = CriarTarefaMinima(db);
            var (tarefaId2, _) = CriarTarefaMinima(db, clienteId);
            var repo = new SqliteAncorarPdfConfiguracaoRepository(db);

            RegistrarEvento(repo, tarefaId, clienteId, AncorarPdfErroCodigos.TarefaAgendada);
            RegistrarEvento(repo, tarefaId2, clienteId, AncorarPdfErroCodigos.TarefaAgendada);

            var eventos = repo.ListarEventosOperacionais(new AncorarPdfEventoHistoricoFiltro
            {
                ClienteId = clienteId,
                TarefaId = tarefaId
            });

            eventos.Should().HaveCount(1, "apenas evento da tarefa específica deve retornar");
            eventos[0].TarefaId.Should().Be(tarefaId);
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    [Fact]
    public void T05_ListarEventosOperacionais_filtra_por_tipoEvento()
    {
        var dbPath = CriarDbPath("listar_tipo");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();
            var (tarefaId, clienteId) = CriarTarefaMinima(db);
            var repo = new SqliteAncorarPdfConfiguracaoRepository(db);

            RegistrarEvento(repo, tarefaId, clienteId, AncorarPdfErroCodigos.TarefaAgendada);
            RegistrarEvento(repo, tarefaId, clienteId, AncorarPdfErroCodigos.ExecucaoIniciada);
            RegistrarEvento(repo, tarefaId, clienteId, AncorarPdfErroCodigos.ExecucaoConcluida);

            var eventos = repo.ListarEventosOperacionais(new AncorarPdfEventoHistoricoFiltro
            {
                ClienteId = clienteId,
                TipoEvento = AncorarPdfErroCodigos.ExecucaoIniciada
            });

            eventos.Should().HaveCount(1);
            eventos[0].TipoEvento.Should().Be(AncorarPdfErroCodigos.ExecucaoIniciada);
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    [Fact]
    public void T06_ListarEventosOperacionais_filtra_por_janela_de_tempo()
    {
        var dbPath = CriarDbPath("listar_janela");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();
            var (tarefaId, clienteId) = CriarTarefaMinima(db);
            var repo = new SqliteAncorarPdfConfiguracaoRepository(db);

            var ontem = DateTime.UtcNow.AddDays(-1);
            var agora = DateTime.UtcNow;

            // Evento ontem (fora da janela).
            repo.RegistrarEventoScheduler(new AncorarPdfSchedulerEventoRegistro
            {
                TarefaId = tarefaId,
                ClienteId = clienteId,
                TipoEvento = AncorarPdfErroCodigos.TarefaAgendada,
                OcorreuEmUtc = ontem
            });

            // Evento agora (dentro da janela).
            repo.RegistrarEventoScheduler(new AncorarPdfSchedulerEventoRegistro
            {
                TarefaId = tarefaId,
                ClienteId = clienteId,
                TipoEvento = AncorarPdfErroCodigos.ExecucaoConcluida,
                OcorreuEmUtc = agora
            });

            var eventos = repo.ListarEventosOperacionais(new AncorarPdfEventoHistoricoFiltro
            {
                ClienteId = clienteId,
                DataInicioUtc = agora.AddHours(-1),
                DataFimUtc = agora.AddHours(1)
            });

            eventos.Should().HaveCount(1, "apenas evento dentro da janela deve retornar");
            eventos[0].TipoEvento.Should().Be(AncorarPdfErroCodigos.ExecucaoConcluida);
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    [Fact]
    public void T07_ListarEventosOperacionais_respeita_limite()
    {
        var dbPath = CriarDbPath("listar_limite");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();
            var (tarefaId, clienteId) = CriarTarefaMinima(db);
            var repo = new SqliteAncorarPdfConfiguracaoRepository(db);

            // Insere 10 eventos.
            for (var i = 0; i < 10; i++)
                RegistrarEvento(repo, tarefaId, clienteId, AncorarPdfErroCodigos.ExecucaoConcluida);

            var eventos = repo.ListarEventosOperacionais(new AncorarPdfEventoHistoricoFiltro
            {
                ClienteId = clienteId,
                Limite = 3
            });

            eventos.Should().HaveCount(3, "Limite=3 deve retornar no máximo 3 eventos");
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    [Fact]
    public void T08_ListarEventosOperacionais_retorna_ordenado_por_data_desc()
    {
        var dbPath = CriarDbPath("listar_ordem");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();
            var (tarefaId, clienteId) = CriarTarefaMinima(db);
            var repo = new SqliteAncorarPdfConfiguracaoRepository(db);

            var base_ = new DateTime(2026, 2, 26, 10, 0, 0, DateTimeKind.Utc);
            for (var i = 0; i < 3; i++)
            {
                repo.RegistrarEventoScheduler(new AncorarPdfSchedulerEventoRegistro
                {
                    TarefaId = tarefaId,
                    ClienteId = clienteId,
                    TipoEvento = AncorarPdfErroCodigos.ExecucaoConcluida,
                    OcorreuEmUtc = base_.AddMinutes(i)
                });
            }

            var eventos = repo.ListarEventosOperacionais(new AncorarPdfEventoHistoricoFiltro
            {
                ClienteId = clienteId
            });

            eventos.Should().HaveCount(3);
            // Mais recente primeiro.
            eventos[0].OcorreuEmUtc.Should().BeOnOrAfter(eventos[1].OcorreuEmUtc);
            eventos[1].OcorreuEmUtc.Should().BeOnOrAfter(eventos[2].OcorreuEmUtc);
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    [Fact]
    public void T09_ListarEventosOperacionais_clienteId_sem_eventos_retorna_lista_vazia()
    {
        var dbPath = CriarDbPath("listar_vazio");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();

            var repo = new SqliteAncorarPdfConfiguracaoRepository(db);

            var eventos = repo.ListarEventosOperacionais(new AncorarPdfEventoHistoricoFiltro
            {
                ClienteId = 99999
            });

            eventos.Should().BeEmpty("cliente sem eventos deve retornar lista vazia");
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    [Fact]
    public void T10_ListarEventosOperacionais_retorna_correlationId_e_campos_C7()
    {
        var dbPath = CriarDbPath("listar_campos_c7");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();
            var (tarefaId, clienteId) = CriarTarefaMinima(db);
            var repo = new SqliteAncorarPdfConfiguracaoRepository(db);

            var correlationId = Guid.NewGuid().ToString("N");
            repo.RegistrarEventoScheduler(new AncorarPdfSchedulerEventoRegistro
            {
                TarefaId = tarefaId,
                ClienteId = clienteId,
                TipoEvento = AncorarPdfErroCodigos.MisfireDetectado,
                OcorreuEmUtc = DateTime.UtcNow,
                CorrelationId = correlationId,
                StatusAnterior = AncorarPdfErroCodigos.StatusAgendada,
                StatusNovo = AncorarPdfErroCodigos.StatusAgendada,
                ErroCodigo = AncorarPdfErroCodigos.Nenhum,
                ExecutadaComAtraso = true
            });

            var eventos = repo.ListarEventosOperacionais(new AncorarPdfEventoHistoricoFiltro
            {
                ClienteId = clienteId,
                TipoEvento = AncorarPdfErroCodigos.MisfireDetectado
            });

            eventos.Should().HaveCount(1);
            var ev = eventos[0];
            ev.CorrelationId.Should().Be(correlationId);
            ev.StatusAnterior.Should().Be(AncorarPdfErroCodigos.StatusAgendada);
            ev.StatusNovo.Should().Be(AncorarPdfErroCodigos.StatusAgendada);
            ev.ErroCodigo.Should().Be(AncorarPdfErroCodigos.Nenhum);
            ev.ExecutadaComAtraso.Should().BeTrue();
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    // --- Helpers ---

    private static string CriarDbPath(string sufixo)
        => Path.Combine(Path.GetTempPath(), $"protons_c7_{sufixo}_{Guid.NewGuid():N}.db");

    private static void LimparDb(string dbPath)
    {
        if (File.Exists(dbPath)) File.Delete(dbPath);
    }

    private static (int tarefaId, int clienteId) CriarTarefaMinima(SqliteDb db, int? clienteIdExistente = null)
    {
        var userRepo = new UserRepository(db);
        var clienteRepo = new SqliteClienteRepository(db);
        var tarefaRepo = new SqliteTarefaRepository(db);

        var userId = userRepo.Create(new User
        {
            Empresa = "Protons",
            Nome = "Op C7",
            Cpf = "11144477735",
            Cargo = "Operador",
            Email = $"c7.{Guid.NewGuid():N}"[..25] + "@p.local",
            SenhaHash = "hash",
            SenhaSalt = "salt",
            IteracoesPbkdf2 = 100000,
            Status = UserStatus.Ativo,
            Role = UserRole.Admin,
            CriadoEmUtc = DateTime.UtcNow,
            AtualizadoEmUtc = DateTime.UtcNow
        });

        int clienteId;
        if (clienteIdExistente.HasValue)
        {
            clienteId = clienteIdExistente.Value;
        }
        else
        {
            var docBytes = Guid.NewGuid().ToByteArray();
            var docNum = BitConverter.ToUInt64(docBytes, 0) % 100_000_000_000_000UL;
            clienteId = clienteRepo.Create(new Cliente
            {
                CodigoCliente = $"C7-{Guid.NewGuid():N}"[..14],
                Nome = "Cliente C7",
                TipoDocumento = TipoDocumentoCliente.CNPJ,
                Documento = docNum.ToString("D14"),
                Ativo = true,
                CriadoPorUserId = userId,
                CriadoEmUtc = DateTime.UtcNow,
                AtualizadoEmUtc = DateTime.UtcNow
            });
        }

        var tarefaId = tarefaRepo.Create(new Tarefa
        {
            ClienteId = clienteId,
            FerramentaId = FerramentaTarefaIds.AncorarPdfCanonico,
            Titulo = "Tarefa C7",
            VencimentoUtc = DateTime.UtcNow.AddDays(1),
            ResponsavelUserId = userId,
            Status = TarefaStatus.Agendada,
            Recorrencia = TarefaRecorrencia.Nenhuma,
            Ativa = true,
            CriadoPorUserId = userId,
            CriadoEmUtc = DateTime.UtcNow,
            AtualizadoEmUtc = DateTime.UtcNow
        });

        return (tarefaId, clienteId);
    }

    private static void RegistrarEvento(
        SqliteAncorarPdfConfiguracaoRepository repo,
        int tarefaId,
        int clienteId,
        string tipoEvento)
    {
        repo.RegistrarEventoScheduler(new AncorarPdfSchedulerEventoRegistro
        {
            TarefaId = tarefaId,
            ClienteId = clienteId,
            TipoEvento = tipoEvento,
            OcorreuEmUtc = DateTime.UtcNow,
            CorrelationId = Guid.NewGuid().ToString("N")
        });
    }
}
