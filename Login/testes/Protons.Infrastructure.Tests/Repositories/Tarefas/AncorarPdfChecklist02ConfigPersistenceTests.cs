using FluentAssertions;
using Microsoft.Data.Sqlite;
using Protons.Core.Clientes.Models;
using Protons.Core.Login.Models;
using Protons.Core.Tarefas.Models;
using Protons.Infrastructure.Clientes.Repositories;
using Protons.Infrastructure.Login.Database;
using Protons.Infrastructure.Login.Repositories;
using Protons.Infrastructure.Tarefas.Repositories;

namespace Protons.Infrastructure.Tests.Repositories.Tarefas;

public sealed class AncorarPdfChecklist02ConfigPersistenceTests
{
    [Fact]
    [Trait("ChecklistGate", "C2_G3_Infra_Persistence")]
    [Trait("ChecklistGate", "C2_F13")]
    [Trait("Category", "C2_F13")]
    public void C2_F13_DevePersistirConfiguracaoEPermitirReaberturaComDadosIntegros()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"protons_checklist02_cfg_{Guid.NewGuid():N}.db");
        var db = new SqliteDb(dbPath);
        db.EnsureCreated();

        try
        {
            var tarefaId = CriarTarefaAncorarPdf(db, out var clienteId, out var userId);
            var repo = new SqliteAncorarPdfConfiguracaoRepository(db);

            var config = CriarConfiguracao(tarefaId, clienteId, userId, "Ancorar C2");
            repo.Salvar(config, historicoAlteracao: null);

            var carregada = repo.ObterPorTarefaId(tarefaId);

            carregada.Should().NotBeNull();
            carregada!.NomeTarefaPersonalizado.Should().Be("Ancorar C2");
            carregada.TemplateAncoras.Should().HaveCount(2);
            carregada.ProgramadoPorUserId.Should().Be(userId);
            carregada.VersaoTemplate.Should().Be(1);
            carregada.HighlightOpacity.Should().Be(0.40);
            carregada.ModoSelecao.Should().Be(AncorarPdfModoSelecao.TextoExpandido);
            carregada.PdfModeloCrossCliente.Should().BeTrue();
            carregada.PdfModeloCrossClienteJustificativa.Should().Be("Treinamento de template com base homologada.");
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Fact]
    [Trait("ChecklistGate", "C2_G3_Infra_Persistence")]
    [Trait("ChecklistGate", "C2_F14")]
    [Trait("Category", "C2_F14")]
    public void C2_F14_DevePersistirHistoricoTemplateComAntesEDepois()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"protons_checklist02_hist_{Guid.NewGuid():N}.db");
        var db = new SqliteDb(dbPath);
        db.EnsureCreated();

        try
        {
            var tarefaId = CriarTarefaAncorarPdf(db, out var clienteId, out var userId);
            var repo = new SqliteAncorarPdfConfiguracaoRepository(db);

            var v1 = CriarConfiguracao(tarefaId, clienteId, userId, "Template V1");
            repo.Salvar(v1, historicoAlteracao: null);

            var v2 = v1 with
            {
                NomeTarefaPersonalizado = "Template V2",
                VersaoTemplate = 2
            };

            var historico = new AncorarPdfTemplateHistoricoItem
            {
                TarefaId = tarefaId,
                Versao = 2,
                AntesJson = "{\"nome\":\"Template V1\"}",
                DepoisJson = "{\"nome\":\"Template V2\"}",
                AlteradoPorUserId = userId,
                AlteradoPorNome = "Operador C2",
                AlteradoEmUtc = DateTime.UtcNow
            };

            repo.Salvar(v2, historico);

            var itens = repo.ListarHistoricoTemplate(tarefaId, limite: 10);

            itens.Should().NotBeEmpty();
            itens[0].Versao.Should().Be(2);
            itens[0].AntesJson.Should().Contain("Template V1");
            itens[0].DepoisJson.Should().Contain("Template V2");
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Fact]
    [Trait("ChecklistGate", "C2_G3_Infra_Persistence")]
    public void C2_G3_SchemaV9_DeveCriarTabelasEColunaFerramentaId()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"protons_checklist02_schema_{Guid.NewGuid():N}.db");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();

            using var connection = new SqliteConnection(db.ConnectionString);
            connection.Open();

            using var versaoCmd = connection.CreateCommand();
            versaoCmd.CommandText = "SELECT Valor FROM SchemaMetadata WHERE Chave = 'SchemaVersion' LIMIT 1";
            versaoCmd.ExecuteScalar()?.ToString().Should().Be("16");

            using var tabelaCmd = connection.CreateCommand();
            tabelaCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='AncorarPdfConfiguracoesTarefa'";
            tabelaCmd.ExecuteScalar()?.ToString().Should().Be("AncorarPdfConfiguracoesTarefa");

            using var colunaCmd = connection.CreateCommand();
            colunaCmd.CommandText = "PRAGMA table_info('Tarefas')";
            using var reader = colunaCmd.ExecuteReader();
            var temFerramentaId = false;
            while (reader.Read())
            {
                if (reader.IsDBNull(1))
                    continue;

                if (string.Equals(reader.GetString(1), "FerramentaId", StringComparison.OrdinalIgnoreCase))
                {
                    temFerramentaId = true;
                    break;
                }
            }

            temFerramentaId.Should().BeTrue();

            using var colunasConfigCmd = connection.CreateCommand();
            colunasConfigCmd.CommandText = "PRAGMA table_info('AncorarPdfConfiguracoesTarefa')";
            using var readerConfig = colunasConfigCmd.ExecuteReader();
            var temHighlightOpacity = false;
            var temModoSelecao = false;
            var temCrossCliente = false;
            var temCrossClienteJustificativa = false;
            var temTimezoneId = false;
            var temPrioridadeExecucao = false;
            var temDstHorarioInvalidoPolicy = false;
            var temDstHorarioAmbiguoPolicy = false;
            while (readerConfig.Read())
            {
                if (readerConfig.IsDBNull(1))
                    continue;

                var nome = readerConfig.GetString(1);
                if (string.Equals(nome, "HighlightOpacity", StringComparison.OrdinalIgnoreCase))
                    temHighlightOpacity = true;
                if (string.Equals(nome, "ModoSelecao", StringComparison.OrdinalIgnoreCase))
                    temModoSelecao = true;
                if (string.Equals(nome, "PdfModeloCrossCliente", StringComparison.OrdinalIgnoreCase))
                    temCrossCliente = true;
                if (string.Equals(nome, "PdfModeloCrossClienteJustificativa", StringComparison.OrdinalIgnoreCase))
                    temCrossClienteJustificativa = true;
                if (string.Equals(nome, "TimezoneId", StringComparison.OrdinalIgnoreCase))
                    temTimezoneId = true;
                if (string.Equals(nome, "PrioridadeExecucao", StringComparison.OrdinalIgnoreCase))
                    temPrioridadeExecucao = true;
                if (string.Equals(nome, "DstHorarioInvalidoPolicy", StringComparison.OrdinalIgnoreCase))
                    temDstHorarioInvalidoPolicy = true;
                if (string.Equals(nome, "DstHorarioAmbiguoPolicy", StringComparison.OrdinalIgnoreCase))
                    temDstHorarioAmbiguoPolicy = true;
            }

            temHighlightOpacity.Should().BeTrue();
            temModoSelecao.Should().BeTrue();
            temCrossCliente.Should().BeTrue();
            temCrossClienteJustificativa.Should().BeTrue();
            temTimezoneId.Should().BeTrue();
            temPrioridadeExecucao.Should().BeTrue();
            temDstHorarioInvalidoPolicy.Should().BeTrue();
            temDstHorarioAmbiguoPolicy.Should().BeTrue();
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    private static int CriarTarefaAncorarPdf(SqliteDb db, out int clienteId, out int userId)
    {
        var userRepo = new UserRepository(db);
        var clienteRepo = new SqliteClienteRepository(db);
        var tarefaRepo = new SqliteTarefaRepository(db);

        userId = userRepo.Create(new User
        {
            Empresa = "Protons",
            Nome = "Operador C2",
            Cpf = "11144477735",
            Cargo = "Operador",
            Email = $"operador.c2.{Guid.NewGuid():N}@protons.local",
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
            CodigoCliente = $"CLI-C2-{Guid.NewGuid():N}"[..14],
            Nome = "Cliente C2",
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
            Titulo = "Ancorar PDF C2",
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

    private static AncorarPdfConfiguracaoTarefa CriarConfiguracao(int tarefaId, int clienteId, int userId, string nome)
    {
        return new AncorarPdfConfiguracaoTarefa
        {
            TarefaId = tarefaId,
            ClienteId = clienteId,
            EsteiraId = 1,
            NomeTarefaPersonalizado = nome,
            PastaMonitoradaPath = "/tmp/entrada",
            PdfModeloPath = "/tmp/modelo.pdf",
            NomeReferenciaArquivo = "holerite",
            MonitorarSubpastas = false,
            ValidacaoClienteAtiva = true,
            LimiarSimilaridadeNome = 0.75,
            HighlightOpacity = 0.40,
            ModoSelecao = AncorarPdfModoSelecao.TextoExpandido,
            PdfModeloCrossCliente = true,
            PdfModeloCrossClienteJustificativa = "Treinamento de template com base homologada.",
            Recorrencia = TarefaRecorrencia.Nenhuma,
            AgendamentoSegundo = 15,
            TemplateAncoras =
            [
                new AncorarPdfTemplateAncora
                {
                    Ordem = 0,
                    CorHex = "#4A90D9",
                    Pagina = 1,
                    XRel = 0.1,
                    YRel = 0.1,
                    LarguraRel = 0.2,
                    AlturaRel = 0.08,
                    Metadado = new AncorarPdfTemplateMetadado
                    {
                        NomeExibido = "CPF",
                        ChaveTecnica = "cpf",
                        TipoEsperado = "texto"
                    }
                },
                new AncorarPdfTemplateAncora
                {
                    Ordem = 1,
                    CorHex = "#5CB85C",
                    Pagina = 1,
                    XRel = 0.35,
                    YRel = 0.1,
                    LarguraRel = 0.2,
                    AlturaRel = 0.08,
                    Metadado = new AncorarPdfTemplateMetadado
                    {
                        NomeExibido = "Nome",
                        ChaveTecnica = "nome",
                        TipoEsperado = "texto"
                    }
                }
            ],
            ProgramadoPorUserId = userId,
            ProgramadoPorNome = "Operador C2",
            ProgramadoEmUtc = DateTime.UtcNow,
            AtualizadoPorUserId = userId,
            AtualizadoEmUtc = DateTime.UtcNow,
            VersaoTemplate = 1
        };
    }
}
