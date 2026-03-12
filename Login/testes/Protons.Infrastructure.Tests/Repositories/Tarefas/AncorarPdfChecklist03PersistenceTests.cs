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

[Trait("Checklist", "C3")]
public sealed class AncorarPdfChecklist03PersistenceTests
{
    // --- C3_G1: Schema v8 cria tabelas de execucao ---

    [Fact]
    [Trait("ChecklistGate", "C3_G1_Schema")]
    [Trait("Category", "C3_G1_Schema")]
    public void C3_G1_SchemaV8_DeveCriarTabelasDeExecucao()
    {
        var dbPath = CriarDbPath("schema");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();

            using var connection = new SqliteConnection(db.ConnectionString);
            connection.Open();

            // Verifica que as 3 novas tabelas de execucao existem.
            foreach (var tabela in new[] { "AncorarPdfExecucoes", "AncorarPdfResultadosVariavel", "AncorarPdfSaidaVariavel" })
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = $"SELECT name FROM sqlite_master WHERE type='table' AND name='{tabela}'";
                cmd.ExecuteScalar()?.ToString().Should().Be(tabela, $"tabela {tabela} deve existir no schema v8");
            }

            // Verifica versao do schema atualizada (ao menos 8).
            using var versaoCmd = connection.CreateCommand();
            versaoCmd.CommandText = "SELECT CAST(Valor AS INTEGER) FROM SchemaMetadata WHERE Chave = 'SchemaVersion' LIMIT 1";
            var versao = Convert.ToInt32(versaoCmd.ExecuteScalar());
            versao.Should().BeGreaterThanOrEqualTo(8, "schema deve ser ao menos v8 apos C3");
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    [Fact]
    [Trait("ChecklistGate", "C3_G1_Schema")]
    [Trait("Category", "C3_G1_Schema")]
    public void C3_G1_IndicesDeExecucao_DevemExistir()
    {
        var dbPath = CriarDbPath("indices");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();

            using var connection = new SqliteConnection(db.ConnectionString);
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
SELECT name FROM sqlite_master
WHERE type='index'
		  AND name IN (
		    'IX_AncorarPdfExecucoes_Cliente_Status_Janela',
		    'IX_AncorarPdfExecucoes_Tarefa_IniciadaEm',
		    'UX_AncorarPdfExecucoes_Tarefa_Ciclo',
		    'IX_AncorarPdfSaida_Cliente_DataExecucao',
		    'IX_AncorarPdfSaida_Tarefa_DataExecucao',
		    'UX_AncorarPdfSaida_Tarefa_PayloadHash_V1'
		  )
		ORDER BY name;";

            var indicesEncontrados = new List<string>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                indicesEncontrados.Add(reader.GetString(0));

            indicesEncontrados.Should().Contain("UX_AncorarPdfSaida_Tarefa_PayloadHash_V1",
                "deduplicacao por hash precisa de unicidade condicional por schema ativo para consistencia enterprise");
            indicesEncontrados.Should().HaveCountGreaterThanOrEqualTo(5, "indices criticos de execucao/saida devem existir");
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    // --- C3_G2: Idempotencia de processamento por nome/ciclo ---

    [Fact]
    [Trait("ChecklistGate", "C3_G2_Idempotencia")]
    [Trait("Category", "C3_G2_Idempotencia")]
    public void C3_G2_TentarReservar_PrimeiraVez_DeveReservar()
    {
        var dbPath = CriarDbPath("reserva_ok");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();
            var tarefaId = CriarTarefaAncorarPdf(db, out _, out _);
            var repo = new SqliteAncorarPdfExecucaoRepository(db);

            var reserva = repo.TentarReservarProcessamento(
                tarefaId, "ciclo-2026-02-25", "nota_fiscal",
                "hash_abc123", "/pasta/nota.pdf", 12345, DateTime.UtcNow);

            reserva.Reservado.Should().BeTrue("primeira reserva deve ter sucesso");
            reserva.NomeEsperadoLogico.Should().Be("nota_fiscal");
            reserva.ArquivoHash.Should().Be("hash_abc123");
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    [Fact]
    [Trait("ChecklistGate", "C3_G2_Idempotencia")]
    [Trait("Category", "C3_G2_Idempotencia")]
    public void C3_G2_TentarReservar_SegundaVez_NaoDeveReservar()
    {
        var dbPath = CriarDbPath("reserva_dupla");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();
            var tarefaId = CriarTarefaAncorarPdf(db, out _, out _);
            var repo = new SqliteAncorarPdfExecucaoRepository(db);

            // Primeira reserva bem-sucedida.
            repo.TentarReservarProcessamento(
                tarefaId, "ciclo-2026-02-25", "nota_fiscal",
                "hash_abc123", "/pasta/nota.pdf", 12345, DateTime.UtcNow);

            // Segunda tentativa com mesmo nome/ciclo: nao deve reservar.
            var reservaDupla = repo.TentarReservarProcessamento(
                tarefaId, "ciclo-2026-02-25", "nota_fiscal",
                "hash_abc123", "/pasta/nota.pdf", 12345, DateTime.UtcNow);

            reservaDupla.Reservado.Should().BeFalse("segunda reserva para mesmo nome/ciclo deve ser bloqueada");
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    [Fact]
    [Trait("ChecklistGate", "C3_G2_Idempotencia")]
    [Trait("Category", "C3_G2_Idempotencia")]
    public void C3_G2_TentarReservar_NovoCiclo_DevePermitir()
    {
        var dbPath = CriarDbPath("reserva_novo_ciclo");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();
            var tarefaId = CriarTarefaAncorarPdf(db, out _, out _);
            var repo = new SqliteAncorarPdfExecucaoRepository(db);

            // Ciclo de ontem.
            repo.TentarReservarProcessamento(
                tarefaId, "ciclo-2026-02-24", "nota_fiscal",
                "hash_abc123", "/pasta/nota.pdf", 12345, DateTime.UtcNow.AddDays(-1));

            // Mesmo arquivo mas em novo ciclo: deve permitir.
            var reservaNovoCiclo = repo.TentarReservarProcessamento(
                tarefaId, "ciclo-2026-02-25", "nota_fiscal",
                "hash_abc123", "/pasta/nota.pdf", 12345, DateTime.UtcNow);

            reservaNovoCiclo.Reservado.Should().BeTrue("novo ciclo deve permitir reprocessamento do mesmo arquivo");
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    // --- C3_G3: Execucao completa (ciclo de vida) ---

    [Fact]
    [Trait("ChecklistGate", "C3_G3_Execucao")]
    [Trait("Category", "C3_G3_Execucao")]
    public void C3_G3_CriarExecucao_ERecuperar_ComDadosIntegros()
    {
        var dbPath = CriarDbPath("execucao_cria");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();
            var tarefaId = CriarTarefaAncorarPdf(db, out var clienteId, out var userId);
            var repo = new SqliteAncorarPdfExecucaoRepository(db);

            var execucaoId = $"exec-{Guid.NewGuid():N}";
            var janela = new DateTime(2026, 2, 25, 9, 0, 0, DateTimeKind.Utc);
            var correlationId = Guid.NewGuid().ToString("N");

            var execucao = new TarefaAncorarPdfExecucao
            {
                ExecucaoId = execucaoId,
                TarefaId = tarefaId,
                ClienteId = clienteId,
                EsteiraId = 1,
                CicloId = "ciclo-2026-02-25T09:00:00Z",
                JanelaAlvoUtc = janela,
                Status = "Agendada",
                ProgramadoPorUserId = userId,
                ProgramadoPorNome = "Operador C3",
                ProgramadoEmUtc = DateTime.UtcNow,
                CorrelationId = correlationId,
                CriadoEmUtc = DateTime.UtcNow
            };

            var criado = repo.CriarExecucao(execucao);
            criado.Should().BeTrue("primeira criacao deve ter sucesso");

            var recuperada = repo.ObterPorExecucaoId(execucaoId);
            recuperada.Should().NotBeNull();
            recuperada!.TarefaId.Should().Be(tarefaId);
            recuperada.ClienteId.Should().Be(clienteId);
            recuperada.Status.Should().Be("Agendada");
            recuperada.CorrelationId.Should().Be(correlationId);
            recuperada.CicloId.Should().Be("ciclo-2026-02-25T09:00:00Z");
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    [Fact]
    [Trait("ChecklistGate", "C3_G3_Execucao")]
    [Trait("Category", "C3_G3_Execucao")]
    public void C3_G3_CriarExecucao_Duplicado_NaoDeveErrar()
    {
        var dbPath = CriarDbPath("execucao_dupla");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();
            var tarefaId = CriarTarefaAncorarPdf(db, out var clienteId, out var userId);
            var repo = new SqliteAncorarPdfExecucaoRepository(db);

            var execucaoId = $"exec-{Guid.NewGuid():N}";
            var execucao = CriarExecucaoBase(execucaoId, tarefaId, clienteId, userId);

            repo.CriarExecucao(execucao);
            var dupla = repo.CriarExecucao(execucao); // nao deve lancar excecao

            dupla.Should().BeFalse("segunda criacao com mesmo ID deve retornar false (idempotente)");
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    [Fact]
    [Trait("ChecklistGate", "C3_G3_Execucao")]
    [Trait("Category", "C3_G3_Execucao")]
    public void C3_G3_AtualizarStatus_DeveAlterarStatusDaExecucao()
    {
        var dbPath = CriarDbPath("execucao_status");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();
            var tarefaId = CriarTarefaAncorarPdf(db, out var clienteId, out var userId);
            var repo = new SqliteAncorarPdfExecucaoRepository(db);

            var execucaoId = $"exec-{Guid.NewGuid():N}";
            repo.CriarExecucao(CriarExecucaoBase(execucaoId, tarefaId, clienteId, userId));

            var iniciada = DateTime.UtcNow;
            repo.AtualizarStatus(execucaoId, "Executando", iniciadaEmUtc: iniciada);

            var exec = repo.ObterPorExecucaoId(execucaoId);
            exec!.Status.Should().Be("Executando");
            exec.IniciadaEmUtc.Should().NotBeNull();

            repo.AtualizarStatus(execucaoId, "Concluida",
                finalizadaEmUtc: DateTime.UtcNow);

            var concluida = repo.ObterPorExecucaoId(execucaoId);
            concluida!.Status.Should().Be("Concluida");
            concluida.FinalizadaEmUtc.Should().NotBeNull();
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    // --- C3_G4: Salvar execucao completa (transacao atomica) ---

    [Fact]
    [Trait("ChecklistGate", "C3_G4_TransacaoAtomica")]
    [Trait("Category", "C3_G4_TransacaoAtomica")]
    public void C3_G4_SalvarExecucaoCompleta_DevePersistirExecucaoResultadosSaida()
    {
        var dbPath = CriarDbPath("execucao_completa");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();
            var tarefaId = CriarTarefaAncorarPdf(db, out var clienteId, out var userId);
            var repoExec = new SqliteAncorarPdfExecucaoRepository(db);
            var repoSaida = new SqliteAncorarPdfSaidaRepository(db);

            var execucaoId = $"exec-{Guid.NewGuid():N}";
            var saidaId = $"saida-{Guid.NewGuid():N}";

            var saida = CriarSaidaCompleta(saidaId, execucaoId, tarefaId, clienteId);
            var hashCalculado = AncorarPdfPayloadHash.Computar(saida);
            saida = saida with { PayloadHashSha256 = hashCalculado };

            var entrada = new AncorarPdfSalvarExecucaoEntrada
            {
                Execucao = CriarExecucaoBase(execucaoId, tarefaId, clienteId, userId) with
                {
                    Status = "Concluida",
                    IniciadaEmUtc = DateTime.UtcNow.AddMinutes(-1),
                    FinalizadaEmUtc = DateTime.UtcNow
                },
                Resultados =
                [
                    new ResultadoAncoraVariavel
                    {
                        ResultadoId = $"res-{Guid.NewGuid():N}",
                        ExecucaoId = execucaoId,
                        TarefaId = tarefaId,
                        ClienteId = clienteId,
                        ArquivoPath = "/pasta/nota.pdf",
                        ArquivoHash = "sha256:abc",
                        Chave = "nome_cliente",
                        ValorBruto = "EMPRESA LTDA",
                        ValorNormalizado = "empresa ltda",
                        Tipo = "texto",
                        CorTemplate = "#4A90D9",
                        Confianca = 0.97,
                        Pagina = 1
                    }
                ],
                Saida = saida
            };

            repoExec.SalvarExecucaoCompleta(entrada);

            // Verificar execucao persistida.
            var execPersistida = repoExec.ObterPorExecucaoId(execucaoId);
            execPersistida.Should().NotBeNull();
            execPersistida!.Status.Should().Be("Concluida");

            // Verificar saida persistida.
            var saidaPersistida = repoSaida.ObterPorSaidaId(saidaId);
            saidaPersistida.Should().NotBeNull();
            saidaPersistida!.SchemaVersion.Should().Be(1);
            saidaPersistida.PayloadHashSha256.Should().Be(hashCalculado);
            saidaPersistida.Variaveis.Should().HaveCount(2);
            saidaPersistida.ClienteId.Should().Be(clienteId);
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    // --- C3_G5: Saida consolidada — nunca sobrescrever ---

    [Fact]
    [Trait("ChecklistGate", "C3_G5_SaidaImutavel")]
    [Trait("Category", "C3_G5_SaidaImutavel")]
    public void C3_G5_SalvarSaida_NaoDeveSobrescreverPayloadAntigo()
    {
        var dbPath = CriarDbPath("saida_imutavel");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();
            var tarefaId = CriarTarefaAncorarPdf(db, out var clienteId, out var userId);
            var repoExec = new SqliteAncorarPdfExecucaoRepository(db);
            var repoSaida = new SqliteAncorarPdfSaidaRepository(db);

            var execucaoId = $"exec-{Guid.NewGuid():N}";
            var saidaId = $"saida-{Guid.NewGuid():N}";

            repoExec.CriarExecucao(CriarExecucaoBase(execucaoId, tarefaId, clienteId, userId));

            var saida1 = CriarSaidaCompleta(saidaId, execucaoId, tarefaId, clienteId);
            var hash1 = AncorarPdfPayloadHash.Computar(saida1);
            saida1 = saida1 with { PayloadHashSha256 = hash1 };

            var inserida = repoSaida.Salvar(saida1);
            inserida.Should().BeTrue("primeira insercao deve ter sucesso");

            // Tenta sobrescrever com payload diferente usando mesmo SaidaId.
            var saida2 = saida1 with
            {
                Variaveis = [new SaidaVariavelItem { Chave = "alterado", ValorBruto = "ALTERADO", ValorNormalizado = "alterado", Tipo = "texto" }]
            };
            var naoSobrescrita = repoSaida.Salvar(saida2);
            naoSobrescrita.Should().BeFalse("saida existente nao pode ser sobrescrita");

            // Verificar que o payload original permanece intacto.
            var recuperada = repoSaida.ObterPorSaidaId(saidaId);
            recuperada!.Variaveis.Should().HaveCount(2, "payload original com 2 variaveis deve ser mantido");
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    // --- C3_G6: Consulta por cliente/tarefa/faixa de data ---

    [Fact]
    [Trait("ChecklistGate", "C3_G6_Consulta")]
    [Trait("Category", "C3_G6_Consulta")]
    public void C3_G6_ListarSaidas_PorClienteEFaixaData_DeveRetornarApenasDoCliente()
    {
        var dbPath = CriarDbPath("consulta_saidas");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();
            var tarefaId = CriarTarefaAncorarPdf(db, out var clienteId, out var userId);
            var repoExec = new SqliteAncorarPdfExecucaoRepository(db);
            var repoSaida = new SqliteAncorarPdfSaidaRepository(db);

            // Insere 2 saidas para o cliente.
            for (var i = 0; i < 2; i++)
            {
                var execId = $"exec-{Guid.NewGuid():N}";
                repoExec.CriarExecucao(CriarExecucaoBase(execId, tarefaId, clienteId, userId));

                var saidaId = $"saida-{Guid.NewGuid():N}";
                var saida = CriarSaidaCompleta(saidaId, execId, tarefaId, clienteId) with
                {
                    DataExecucaoUtc = DateTime.UtcNow.AddHours(-i)
                };
                var hash = AncorarPdfPayloadHash.Computar(saida);
                repoSaida.Salvar(saida with { PayloadHashSha256 = hash });
            }

            var filtro = new AncorarPdfSaidasFiltro
            {
                ClienteId = clienteId,
                DataInicioUtc = DateTime.UtcNow.AddDays(-1),
                DataFimUtc = DateTime.UtcNow.AddDays(1),
                Limite = 10
            };

            var saidas = repoSaida.Listar(filtro);
            saidas.Should().HaveCount(2);
            saidas.Should().AllSatisfy(s => s.ClienteId.Should().Be(clienteId));
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    [Fact]
    [Trait("ChecklistGate", "C3_G6_Consulta")]
    [Trait("Category", "C3_G6_Consulta")]
    public void C3_G6_ListarExecucoes_PorCliente_DeveRetornarOrdenadoPorJanela()
    {
        var dbPath = CriarDbPath("consulta_execucoes");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();
            var tarefaId = CriarTarefaAncorarPdf(db, out var clienteId, out var userId);
            var repo = new SqliteAncorarPdfExecucaoRepository(db);

            // Insere execucoes em ordem inversa.
            var execIds = new List<string>();
            for (var i = 3; i >= 1; i--)
            {
                var execId = $"exec-{Guid.NewGuid():N}";
                execIds.Add(execId);
                repo.CriarExecucao(CriarExecucaoBase(execId, tarefaId, clienteId, userId) with
                {
                    JanelaAlvoUtc = new DateTime(2026, 2, 25, i, 0, 0, DateTimeKind.Utc),
                    CicloId = $"ciclo-{i}"
                });
            }

            var filtro = new AncorarPdfExecucoesFiltro
            {
                ClienteId = clienteId,
                Limite = 10
            };

            var execucoes = repo.Listar(filtro);
            execucoes.Should().HaveCount(3);

            // Deve estar ordenado por JanelaAlvoUtc DESC.
            execucoes[0].JanelaAlvoUtc.Should().BeAfter(execucoes[1].JanelaAlvoUtc);
            execucoes[1].JanelaAlvoUtc.Should().BeAfter(execucoes[2].JanelaAlvoUtc);
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    // --- C3_G7: Isolamento por cliente ---

    [Fact]
    [Trait("ChecklistGate", "C3_G7_IsolamentoCliente")]
    [Trait("Category", "C3_G7_IsolamentoCliente")]
    public void C3_G7_Consulta_NaoDeveRetornarDadosDeOutroCliente()
    {
        var dbPath = CriarDbPath("isolamento");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();

            // Cria dois clientes independentes.
            var tarefaId1 = CriarTarefaAncorarPdf(db, out var clienteId1, out var userId1);
            var tarefaId2 = CriarTarefaAncorarPdf(db, out var clienteId2, out var userId2);

            var repoExec = new SqliteAncorarPdfExecucaoRepository(db);
            var repoSaida = new SqliteAncorarPdfSaidaRepository(db);

            // Insere saida para cliente 1.
            var execId1 = $"exec-{Guid.NewGuid():N}";
            repoExec.CriarExecucao(CriarExecucaoBase(execId1, tarefaId1, clienteId1, userId1));
            var saidaId1 = $"saida-{Guid.NewGuid():N}";
            var saida1 = CriarSaidaCompleta(saidaId1, execId1, tarefaId1, clienteId1);
            repoSaida.Salvar(saida1 with { PayloadHashSha256 = AncorarPdfPayloadHash.Computar(saida1) });

            // Consulta com clienteId2 nao deve retornar dados do cliente 1.
            var filtroCliente2 = new AncorarPdfSaidasFiltro { ClienteId = clienteId2 };
            var saidasCliente2 = repoSaida.Listar(filtroCliente2);

            saidasCliente2.Should().BeEmpty("dados do cliente1 nao devem vazar para cliente2");
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    // --- C3_G8: Deduplicacao por hash de payload ---

    [Fact]
    [Trait("ChecklistGate", "C3_G8_DeduplicacaoHash")]
    [Trait("Category", "C3_G8_DeduplicacaoHash")]
    public void C3_G8_ExisteSaidaComHash_DeveDetectarDuplicidadePorConteudo()
    {
        var dbPath = CriarDbPath("hash_dedup");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();
            var tarefaId = CriarTarefaAncorarPdf(db, out var clienteId, out var userId);
            var repoExec = new SqliteAncorarPdfExecucaoRepository(db);
            var repoSaida = new SqliteAncorarPdfSaidaRepository(db);

            var execId = $"exec-{Guid.NewGuid():N}";
            repoExec.CriarExecucao(CriarExecucaoBase(execId, tarefaId, clienteId, userId));

            var saida = CriarSaidaCompleta($"saida-{Guid.NewGuid():N}", execId, tarefaId, clienteId);
            var hash = AncorarPdfPayloadHash.Computar(saida);
            repoSaida.Salvar(saida with { PayloadHashSha256 = hash });

            // Verifica deduplicacao por hash.
            var existe = repoSaida.ExisteSaidaComHash(tarefaId, hash);
            existe.Should().BeTrue("hash do payload deve ser detectado como existente");

            var naoExiste = repoSaida.ExisteSaidaComHash(tarefaId, "hash_que_nao_existe");
            naoExiste.Should().BeFalse("hash inexistente deve retornar false");
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    [Fact]
    [Trait("ChecklistGate", "C3_G8_DeduplicacaoHash")]
    [Trait("Category", "C3_G8_DeduplicacaoHash")]
    public void C3_G8_SalvarDuplicadoMesmoHashV1_DeveSerIdempotente()
    {
        var dbPath = CriarDbPath("hash_idempotente");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();
            var tarefaId = CriarTarefaAncorarPdf(db, out var clienteId, out var userId);
            var repoExec = new SqliteAncorarPdfExecucaoRepository(db);
            var repoSaida = new SqliteAncorarPdfSaidaRepository(db);

            var execId = $"exec-{Guid.NewGuid():N}";
            repoExec.CriarExecucao(CriarExecucaoBase(execId, tarefaId, clienteId, userId));

            var baseSaida = CriarSaidaCompleta($"saida-{Guid.NewGuid():N}", execId, tarefaId, clienteId);
            var hash = AncorarPdfPayloadHash.Computar(baseSaida);

            var primeira = repoSaida.Salvar(baseSaida with { PayloadHashSha256 = hash, SchemaVersion = 1 });
            var segunda = repoSaida.Salvar(baseSaida with
            {
                SaidaId = $"saida-{Guid.NewGuid():N}",
                PayloadHashSha256 = hash,
                SchemaVersion = 1
            });

            primeira.Should().BeTrue("primeira gravacao de hash inedito deve persistir");
            segunda.Should().BeFalse("segunda gravacao com mesmo hash V1 deve ser absorvida por unicidade condicional");

            var saidas = repoSaida.Listar(new AncorarPdfSaidasFiltro { ClienteId = clienteId, TarefaId = tarefaId });
            saidas.Should().HaveCount(1, "deduplicacao por indice unico condicional deve impedir registros redundantes");
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    [Fact]
    [Trait("ChecklistGate", "C3_G8_DeduplicacaoHash")]
    [Trait("Category", "C3_G8_DeduplicacaoHash")]
    public void C3_G8_LeituraComDataInvalida_DeveFalharRapidoComFormatException()
    {
        var dbPath = CriarDbPath("hash_data_invalida");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();
            var tarefaId = CriarTarefaAncorarPdf(db, out var clienteId, out var userId);
            var repoExec = new SqliteAncorarPdfExecucaoRepository(db);
            var repoSaida = new SqliteAncorarPdfSaidaRepository(db);

            var execucaoId = $"exec-{Guid.NewGuid():N}";
            repoExec.CriarExecucao(CriarExecucaoBase(execucaoId, tarefaId, clienteId, userId));

            using (var connection = new SqliteConnection(db.ConnectionString))
            {
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
INSERT INTO AncorarPdfSaidaVariavel
    (SaidaId, ExecucaoId, TarefaId, ClienteId, ArquivoPath, ArquivoHash, ArquivoNomeLogico, DataExecucaoUtc, SchemaVersion, VariaveisJson, PayloadHashSha256, CriadoEmUtc)
VALUES
    ($saidaId, $execucaoId, $tarefaId, $clienteId, $arquivoPath, $arquivoHash, $arquivoNomeLogico, $dataExecucaoUtc, 1, $variaveisJson, $payloadHash, $criadoEmUtc);";
                cmd.Parameters.AddWithValue("$saidaId", $"saida-{Guid.NewGuid():N}");
                cmd.Parameters.AddWithValue("$execucaoId", execucaoId);
                cmd.Parameters.AddWithValue("$tarefaId", tarefaId);
                cmd.Parameters.AddWithValue("$clienteId", clienteId);
                cmd.Parameters.AddWithValue("$arquivoPath", "/tmp/arquivo.pdf");
                cmd.Parameters.AddWithValue("$arquivoHash", $"sha256:{Guid.NewGuid():N}");
                cmd.Parameters.AddWithValue("$arquivoNomeLogico", "arquivo");
                cmd.Parameters.AddWithValue("$dataExecucaoUtc", "data_invalida");
                cmd.Parameters.AddWithValue("$variaveisJson", "[]");
                cmd.Parameters.AddWithValue("$payloadHash", "hash-teste");
                cmd.Parameters.AddWithValue("$criadoEmUtc", DateTime.UtcNow.ToString("o"));
                cmd.ExecuteNonQuery();
            }

            var acao = () => repoSaida.Listar(new AncorarPdfSaidasFiltro { ClienteId = clienteId, Limite = 10 });
            acao.Should().Throw<FormatException>()
                .WithMessage("*SqliteAncorarPdfSaidaRepository*DataExecucaoUtc*");
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    // --- Helpers ---

    private static string CriarDbPath(string sufixo)
        => Path.Combine(Path.GetTempPath(), $"protons_c3_{sufixo}_{Guid.NewGuid():N}.db");

    private static void LimparDb(string dbPath)
    {
        if (File.Exists(dbPath))
            File.Delete(dbPath);
    }

    private static int CriarTarefaAncorarPdf(SqliteDb db, out int clienteId, out int userId)
    {
        var userRepo = new UserRepository(db);
        var clienteRepo = new SqliteClienteRepository(db);
        var tarefaRepo = new SqliteTarefaRepository(db);

        userId = userRepo.Create(new User
        {
            Empresa = "Protons",
            Nome = "Operador C3",
            Cpf = "11144477735",
            Cargo = "Operador",
            Email = $"operador.c3.{Guid.NewGuid():N}@protons.local",
            SenhaHash = "hash",
            SenhaSalt = "salt",
            IteracoesPbkdf2 = 100000,
            Status = UserStatus.Ativo,
            Role = UserRole.Admin,
            CriadoEmUtc = DateTime.UtcNow,
            AtualizadoEmUtc = DateTime.UtcNow
        });

        // Documento único por chamada para evitar UNIQUE constraint em testes que criam 2 clientes.
        var docBytes = Guid.NewGuid().ToByteArray();
        var docNum = BitConverter.ToUInt64(docBytes, 0) % 100_000_000_000_000UL;
        clienteId = clienteRepo.Create(new Cliente
        {
            CodigoCliente = $"CLI-C3-{Guid.NewGuid():N}"[..14],
            Nome = "Cliente C3",
            TipoDocumento = TipoDocumentoCliente.CNPJ,
            Documento = docNum.ToString("D14"),
            Ativo = true,
            CriadoPorUserId = userId,
            CriadoEmUtc = DateTime.UtcNow,
            AtualizadoEmUtc = DateTime.UtcNow
        });

        return tarefaRepo.Create(new Tarefa
        {
            ClienteId = clienteId,
            FerramentaId = FerramentaTarefaIds.AncorarPdfCanonico,
            Titulo = "Ancorar PDF C3",
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

    private static TarefaAncorarPdfExecucao CriarExecucaoBase(
        string execucaoId,
        int tarefaId,
        int clienteId,
        int userId)
    {
        return new TarefaAncorarPdfExecucao
        {
            ExecucaoId = execucaoId,
            TarefaId = tarefaId,
            ClienteId = clienteId,
            EsteiraId = 1,
            CicloId = $"ciclo-{Guid.NewGuid():N}",
            JanelaAlvoUtc = DateTime.UtcNow,
            Status = "Agendada",
            ProgramadoPorUserId = userId,
            ProgramadoPorNome = "Operador C3",
            ProgramadoEmUtc = DateTime.UtcNow,
            CorrelationId = Guid.NewGuid().ToString("N"),
            CriadoEmUtc = DateTime.UtcNow
        };
    }

    private static SaidaVariavelAncorada CriarSaidaCompleta(
        string saidaId,
        string execucaoId,
        int tarefaId,
        int clienteId)
    {
        return new SaidaVariavelAncorada
        {
            SaidaId = saidaId,
            ExecucaoId = execucaoId,
            TarefaId = tarefaId,
            ClienteId = clienteId,
            ArquivoPath = "/pasta/nota_fiscal.pdf",
            ArquivoHash = $"sha256:{Guid.NewGuid():N}",
            ArquivoNomeLogico = "nota_fiscal",
            DataExecucaoUtc = DateTime.UtcNow,
            SchemaVersion = 1,
            Variaveis =
            [
                new SaidaVariavelItem
                {
                    Chave = "nome_cliente",
                    Tipo = "texto",
                    ValorBruto = "EMPRESA LTDA",
                    ValorNormalizado = "empresa ltda",
                    Confianca = 0.98,
                    CorTemplate = "#4A90D9",
                    Pagina = 1,
                    BboxRelativo = new BboxRelativo(0.1, 0.05, 0.5, 0.03)
                },
                new SaidaVariavelItem
                {
                    Chave = "valor_total",
                    Tipo = "moeda",
                    ValorBruto = "R$ 1.500,00",
                    ValorNormalizado = "1500.00",
                    Confianca = 0.95,
                    CorTemplate = "#F5D547",
                    Pagina = 1
                }
            ],
            CriadoEmUtc = DateTime.UtcNow
        };
    }
}
