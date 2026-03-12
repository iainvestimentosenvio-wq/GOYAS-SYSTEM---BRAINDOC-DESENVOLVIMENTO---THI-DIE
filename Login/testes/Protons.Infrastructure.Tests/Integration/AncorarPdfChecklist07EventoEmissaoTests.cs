using FluentAssertions;
using Protons.Core.Clientes.Models;
using Protons.Core.Login.Models;
using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Repositories;
using Protons.Core.Tarefas.Services;
using Protons.Infrastructure.Clientes.Repositories;
using Protons.Infrastructure.Login.Database;
using Protons.Infrastructure.Login.Repositories;
using Protons.Infrastructure.Tarefas.Repositories;
using Protons.Infrastructure.Tarefas.Services;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Writer;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using Xunit;

namespace Protons.Infrastructure.Tests.Integration;

/// <summary>
/// C7 — Gate G3 (blocking): Testes de emissão de eventos operacionais canônicos.
/// Verifica que o motor e o job handler emitem os 6 eventos C7 com campos corretos.
/// </summary>
[Trait("Checklist", "C7")]
[Trait("Category", "C7_G3_EventoEmissao")]
public sealed class AncorarPdfChecklist07EventoEmissaoTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly SqliteDb _db;
    private readonly int _userId;
    private readonly int _clienteId;
    private readonly int _tarefaId;
    private readonly AncorarPdfMotorExecucao _motor;
    private readonly IAncorarPdfConfiguracaoRepository _confRepo;
    private readonly IAncorarPdfExecucaoRepository _execRepo;
    private readonly IAncorarPdfSaidaRepository _saidaRepo;

    public AncorarPdfChecklist07EventoEmissaoTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"c7_eventos_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _dbPath = Path.Combine(_tempDir, "c7_eventos.db");
        _db = new SqliteDb($"Data Source={_dbPath}");

        (_userId, _clienteId, _tarefaId) = SeedDatabase(_db, _tempDir);

        _confRepo = new SqliteAncorarPdfConfiguracaoRepository(_db);
        _execRepo = new SqliteAncorarPdfExecucaoRepository(_db);
        _saidaRepo = new SqliteAncorarPdfSaidaRepository(_db);

        _motor = new AncorarPdfMotorExecucao(
            _confRepo,
            _execRepo,
            _saidaRepo,
            new AncorarPdfSeletorArquivoPasta(),
            new AncorarPdfExtratorTextoPdfPig(),
            new AncorarPdfValidadorClienteRegex(new SqliteClienteRepository(_db)),
            new AncorarPdfAncoradorEspacialBbox(),
            TimeProvider.System);
    }

    // --- T01: Motor emite ExecucaoIniciada quando inicia o processamento ---

    [Fact]
    public async Task T01_Motor_emite_ExecucaoIniciada_ao_iniciar_processamento()
    {
        // Arrange
        var pdfPath = Path.Combine(_tempDir, "relatorio_2026.pdf");
        CriarPdfComTexto(pdfPath, "CPF 123.456.789-09 Cliente Teste");
        var item = CriarFilaItem(_tarefaId, _clienteId, "ciclo-c7t01");

        // Act
        await _motor.ExecutarAsync(item, CancellationToken.None);

        // Assert — verificar evento ExecucaoIniciada
        var eventos = _confRepo.ListarEventosOperacionais(new AncorarPdfEventoHistoricoFiltro
        {
            ClienteId = _clienteId,
            TipoEvento = AncorarPdfErroCodigos.ExecucaoIniciada
        });

        eventos.Should().HaveCountGreaterThanOrEqualTo(1, "motor deve emitir ExecucaoIniciada");
        var ev = eventos[0];
        ev.StatusAnterior.Should().Be(AncorarPdfErroCodigos.StatusAgendada);
        ev.StatusNovo.Should().Be(AncorarPdfErroCodigos.StatusEmAndamento);
        ev.ErroCodigo.Should().Be(AncorarPdfErroCodigos.Nenhum);
    }

    // --- T02: Motor emite ExecucaoConcluida quando sucesso ---

    [Fact]
    public async Task T02_Motor_emite_ExecucaoConcluida_quando_sucesso()
    {
        // Arrange
        var pdfPath = Path.Combine(_tempDir, "relatorio_2026.pdf");
        CriarPdfComTexto(pdfPath, "CPF 123.456.789-09 Valor R$ 9.999,00");
        var item = CriarFilaItem(_tarefaId, _clienteId, "ciclo-c7t02");

        // Act
        var resultado = await _motor.ExecutarAsync(item, CancellationToken.None);

        // Assert
        resultado.Sucesso.Should().BeTrue();

        var eventos = _confRepo.ListarEventosOperacionais(new AncorarPdfEventoHistoricoFiltro
        {
            ClienteId = _clienteId,
            TipoEvento = AncorarPdfErroCodigos.ExecucaoConcluida
        });

        eventos.Should().HaveCountGreaterThanOrEqualTo(1, "motor deve emitir ExecucaoConcluida");
        var ev = eventos[0];
        ev.StatusAnterior.Should().Be(AncorarPdfErroCodigos.StatusEmAndamento);
        ev.StatusNovo.Should().Be(AncorarPdfErroCodigos.StatusConcluida);
        ev.ErroCodigo.Should().Be(AncorarPdfErroCodigos.Nenhum);
    }

    // --- T03: Motor emite ExecucaoFalhou quando config não encontrada ---

    [Fact]
    public async Task T03_Motor_emite_ExecucaoFalhou_quando_config_nao_encontrada()
    {
        // Arrange: tarefa existe no banco mas SEM configuração ancorar_pdf.
        var tarefaSemConfigId = CriarTarefaSemConfig(_db, _userId, _clienteId);
        var item = CriarFilaItem(tarefaSemConfigId, _clienteId, "ciclo-c7t03");

        // Act
        var resultado = await _motor.ExecutarAsync(item, CancellationToken.None);

        // Assert
        resultado.Sucesso.Should().BeFalse();
        resultado.ErroCodigo.Should().Be(AncorarPdfErroCodigos.NegConfigNaoEncontrada);

        var eventos = _confRepo.ListarEventosOperacionais(new AncorarPdfEventoHistoricoFiltro
        {
            ClienteId = _clienteId,
            TipoEvento = AncorarPdfErroCodigos.ExecucaoFalhou
        });

        eventos.Should().HaveCountGreaterThanOrEqualTo(1, "motor deve emitir ExecucaoFalhou");
        eventos.Should().Contain(e => e.ErroCodigo == AncorarPdfErroCodigos.NegConfigNaoEncontrada,
            "deve existir ExecucaoFalhou com ErroCodigo NegConfigNaoEncontrada");
    }

    // --- T04: Motor emite ExecucaoFalhou quando PDF sem texto ---

    [Fact]
    public async Task T04_Motor_emite_ExecucaoFalhou_quando_pdf_sem_texto()
    {
        // Arrange
        var pdfPath = Path.Combine(_tempDir, "relatorio_2026.pdf");
        CriarPdfSemTexto(pdfPath);
        var item = CriarFilaItem(_tarefaId, _clienteId, "ciclo-c7t04");

        // Act
        var resultado = await _motor.ExecutarAsync(item, CancellationToken.None);

        // Assert
        resultado.Sucesso.Should().BeFalse();
        resultado.ErroCodigo.Should().Be(AncorarPdfErroCodigos.NegPdfSemTexto);

        var eventos = _confRepo.ListarEventosOperacionais(new AncorarPdfEventoHistoricoFiltro
        {
            ClienteId = _clienteId,
            TipoEvento = AncorarPdfErroCodigos.ExecucaoFalhou
        });

        eventos.Should().HaveCountGreaterThanOrEqualTo(1);
        eventos.Should().Contain(e => e.ErroCodigo == AncorarPdfErroCodigos.NegPdfSemTexto,
            "deve existir evento ExecucaoFalhou com ErroCodigo NegPdfSemTexto");
    }

    [Fact]
    public async Task T04B_Motor_emite_ExecucaoFalhou_quando_ocr_indisponivel()
    {
        // Arrange
        var cfg = _confRepo.ObterPorTarefaId(_tarefaId)!;
        _confRepo.Salvar(cfg with
        {
            OcrFallbackAtivo = true,
            OcrDpi = 300,
            OcrLang = "por+eng"
        }, null);

        var pdfPath = Path.Combine(_tempDir, "relatorio_2026.pdf");
        CriarPdfSemTexto(pdfPath);
        var item = CriarFilaItem(_tarefaId, _clienteId, "ciclo-c7t04b");

        var extratorComOcr = new AncorarPdfExtratorTextoComOcrFallback(
            new AncorarPdfExtratorTextoPdfPig(),
            (dpi, lang) => new AncorarPdfExtratorOcrTesseract(
                Path.Combine(_tempDir, "tessdata_inexistente"),
                dpi,
                lang));
        var motor = CriarMotor(extratorComOcr);

        // Act
        var resultado = await motor.ExecutarAsync(item, CancellationToken.None);

        // Assert
        resultado.Sucesso.Should().BeFalse();
        resultado.ErroCodigo.Should().Be(AncorarPdfErroCodigos.NegOcrIndisponivel);

        var eventos = _confRepo.ListarEventosOperacionais(new AncorarPdfEventoHistoricoFiltro
        {
            ClienteId = _clienteId,
            TipoEvento = AncorarPdfErroCodigos.ExecucaoFalhou
        });

        eventos.Should().Contain(e => e.ErroCodigo == AncorarPdfErroCodigos.NegOcrIndisponivel,
            "deve existir evento ExecucaoFalhou com ErroCodigo NegOcrIndisponivel");
    }

    // --- T05: Motor emite ExecucaoFalhou quando arquivo não encontrado ---

    [Fact]
    public async Task T05_Motor_emite_ExecucaoFalhou_quando_arquivo_nao_encontrado()
    {
        // Arrange: pasta vazia sem PDFs.
        var pastaVazia = Path.Combine(_tempDir, "vazia_c7t05");
        Directory.CreateDirectory(pastaVazia);
        var tarefaVaziaId = CriarTarefaComPasta(_db, _userId, _clienteId, pastaVazia);
        var item = CriarFilaItem(tarefaVaziaId, _clienteId, "ciclo-c7t05");

        // Act
        var resultado = await _motor.ExecutarAsync(item, CancellationToken.None);

        // Assert
        resultado.Sucesso.Should().BeFalse();
        resultado.ErroCodigo.Should().Be(AncorarPdfErroCodigos.NegArquivoNaoEncontrado);

        var eventos = _confRepo.ListarEventosOperacionais(new AncorarPdfEventoHistoricoFiltro
        {
            ClienteId = _clienteId,
            TipoEvento = AncorarPdfErroCodigos.ExecucaoFalhou
        });

        eventos.Should().Contain(e => e.ErroCodigo == AncorarPdfErroCodigos.NegArquivoNaoEncontrado,
            "deve existir evento ExecucaoFalhou com ErroCodigo NegArquivoNaoEncontrado");
    }

    // --- T06: CorrelationId dos eventos de sucesso corresponde ao CorrelationId do item ---

    [Fact]
    public async Task T06_Motor_propagates_CorrelationId_nos_eventos()
    {
        // Arrange
        var pdfPath = Path.Combine(_tempDir, "relatorio_2026.pdf");
        CriarPdfComTexto(pdfPath, "CPF 123.456.789-09");
        var correlationId = Guid.NewGuid().ToString("N");
        var item = new AncorarPdfFilaItem
        {
            FilaItemId = Guid.NewGuid().ToString("N"),
            TarefaId = _tarefaId,
            ClienteId = _clienteId,
            CicloId = "ciclo-c7t06-corr",
            JanelaAlvoUtc = DateTime.UtcNow,
            PrioridadeExecucao = 3,
            Motivo = "scheduler_dispatch",
            EnfileiradoPorUserId = _userId,
            EnfileiradoPorNome = "testuser",
            EnfileiradoEmUtc = DateTime.UtcNow,
            CorrelationId = correlationId,
            CriadoEmUtc = DateTime.UtcNow
        };

        // Act
        await _motor.ExecutarAsync(item, CancellationToken.None);

        // Assert — todos os eventos devem ter o mesmo correlationId
        var eventos = _confRepo.ListarEventosOperacionais(new AncorarPdfEventoHistoricoFiltro
        {
            ClienteId = _clienteId,
            TarefaId = _tarefaId
        });

        eventos.Should().NotBeEmpty();
        eventos.Should().AllSatisfy(e =>
            e.CorrelationId.Should().Be(correlationId,
                "todos os eventos do motor devem compartilhar o correlationId do item de fila"));
    }

    // --- T07: JobHandler emite TarefaAgendada ao despachar tarefa ---

    [Fact]
    public void T07_JobHandler_emite_TarefaAgendada_ao_despachar()
    {
        // Arrange
        var agendamento = new AncorarPdfSchedulerAgendamentoAtivo
        {
            TarefaId = _tarefaId,
            ClienteId = _clienteId,
            VencimentoUtc = DateTime.UtcNow.AddSeconds(-5),
            TimezoneId = "UTC",
            PrioridadeExecucao = 3
        };

        var tarefaRepo = new SqliteTarefaRepository(_db);
        var handler = new AncorarPdfJobHandler(
            _confRepo,
            tarefaRepo,
            TimeProvider.System,
            misfireAtrasoMinimoSegundos: 30);

        // Act
        var result = handler.Processar(agendamento, DateTime.UtcNow);

        // Assert — disparo ou backlog registrado
        result.Decisao.Should().BeOneOf(
            AncorarPdfJobDecision.Disparado,
            AncorarPdfJobDecision.BacklogRegistrado);

        // Se disparado, deve existir evento TarefaAgendada no banco.
        if (result.Decisao == AncorarPdfJobDecision.Disparado)
        {
            var eventos = _confRepo.ListarEventosOperacionais(new AncorarPdfEventoHistoricoFiltro
            {
                ClienteId = _clienteId,
                TarefaId = _tarefaId,
                TipoEvento = AncorarPdfErroCodigos.TarefaAgendada
            });

            eventos.Should().HaveCountGreaterThanOrEqualTo(1, "JobHandler deve emitir TarefaAgendada");
            eventos[0].StatusAnterior.Should().Be(AncorarPdfErroCodigos.StatusAgendada);
            eventos[0].StatusNovo.Should().Be(AncorarPdfErroCodigos.StatusEmAndamento);
            eventos[0].ErroCodigo.Should().Be(AncorarPdfErroCodigos.Nenhum);
            eventos[0].CorrelationId.Should().NotBeNullOrEmpty("JobHandler deve gerar CorrelationId");
        }
    }

    // --- T08: JobHandler emite MisfireDetectado ao registrar backlog ---

    [Fact]
    public void T08_JobHandler_emite_MisfireDetectado_ao_registrar_backlog()
    {
        // Arrange: atraso de 2 horas → definitivamente é misfire.
        var agendamento = new AncorarPdfSchedulerAgendamentoAtivo
        {
            TarefaId = _tarefaId,
            ClienteId = _clienteId,
            VencimentoUtc = DateTime.UtcNow.AddHours(-2),
            TimezoneId = "UTC",
            PrioridadeExecucao = 3
        };

        var tarefaRepo = new SqliteTarefaRepository(_db);
        var handler = new AncorarPdfJobHandler(
            _confRepo,
            tarefaRepo,
            TimeProvider.System,
            misfireAtrasoMinimoSegundos: 30);

        // Act
        var result = handler.Processar(agendamento, DateTime.UtcNow);

        // Assert
        result.Decisao.Should().Be(AncorarPdfJobDecision.BacklogRegistrado,
            "atraso de 2h deve ser detectado como misfire e registrado em backlog");

        var eventos = _confRepo.ListarEventosOperacionais(new AncorarPdfEventoHistoricoFiltro
        {
            ClienteId = _clienteId,
            TarefaId = _tarefaId,
            TipoEvento = AncorarPdfErroCodigos.MisfireDetectado
        });

        eventos.Should().HaveCountGreaterThanOrEqualTo(1, "JobHandler deve emitir MisfireDetectado");
        eventos[0].ExecutadaComAtraso.Should().BeTrue();
    }

    // --- Helpers ---

    private static (int userId, int clienteId, int tarefaId) SeedDatabase(SqliteDb db, string tempDir)
    {
        db.EnsureCreated();

        var userRepo = new UserRepository(db);
        var clienteRepo = new SqliteClienteRepository(db);
        var tarefaRepo = new SqliteTarefaRepository(db);
        var confRepo = new SqliteAncorarPdfConfiguracaoRepository(db);

        var userId = userRepo.Create(new User
        {
            Empresa = "Protons",
            Nome = "Op C7 Eventos",
            Cpf = "11144477735",
            Cargo = "Operador",
            Email = $"c7.ev.{Guid.NewGuid():N}"[..25] + "@protons.local",
            SenhaHash = "hash",
            SenhaSalt = "salt",
            IteracoesPbkdf2 = 100000,
            Status = UserStatus.Ativo,
            Role = UserRole.Admin,
            CriadoEmUtc = DateTime.UtcNow,
            AtualizadoEmUtc = DateTime.UtcNow
        });

        var clienteId = clienteRepo.Create(new Cliente
        {
            CodigoCliente = $"C7EV-{Guid.NewGuid():N}"[..14],
            Nome = "Cliente C7 Eventos",
            TipoDocumento = TipoDocumentoCliente.CPF,
            Documento = "12345678909",
            Ativo = true,
            CriadoPorUserId = userId,
            CriadoEmUtc = DateTime.UtcNow,
            AtualizadoEmUtc = DateTime.UtcNow
        });

        var tarefaId = tarefaRepo.Create(new Tarefa
        {
            ClienteId = clienteId,
            FerramentaId = FerramentaTarefaIds.AncorarPdfCanonico,
            Titulo = "Tarefa C7 Eventos",
            VencimentoUtc = DateTime.UtcNow.AddDays(1),
            ResponsavelUserId = userId,
            Status = TarefaStatus.Agendada,
            Recorrencia = TarefaRecorrencia.Nenhuma,
            Ativa = true,
            CriadoPorUserId = userId,
            CriadoEmUtc = DateTime.UtcNow,
            AtualizadoEmUtc = DateTime.UtcNow
        });

        confRepo.Salvar(new AncorarPdfConfiguracaoTarefa
        {
            TarefaId = tarefaId,
            ClienteId = clienteId,
            EsteiraId = 1,
            NomeTarefaPersonalizado = "Tarefa C7 Eventos",
            PastaMonitoradaPath = tempDir,
            PdfModeloPath = string.Empty,
            NomeReferenciaArquivo = "relatorio_2026",
            MonitorarSubpastas = false,
            ValidacaoClienteAtiva = true,
            LimiarSimilaridadeNome = 0.7,
            HighlightOpacity = 0.4,
            ModoSelecao = AncorarPdfModoSelecao.RetanguloLivre,
            PdfModeloCrossCliente = false,
            Recorrencia = TarefaRecorrencia.Nenhuma,
            AgendamentoSegundo = 0,
            TimezoneId = "UTC",
            PrioridadeExecucao = 3,
            TemplateAncoras =
            [
                new AncorarPdfTemplateAncora
                {
                    Ordem = 1,
                    CorHex = "#4A90D9",
                    Pagina = 1,
                    XRel = 0.0,
                    YRel = 0.0,
                    LarguraRel = 1.0,
                    AlturaRel = 1.0,
                    Metadado = new AncorarPdfTemplateMetadado
                    {
                        NomeExibido = "Texto",
                        ChaveTecnica = "texto",
                        TipoEsperado = "texto"
                    }
                }
            ],
            ProgramadoPorUserId = userId,
            ProgramadoPorNome = "testuser",
            ProgramadoEmUtc = DateTime.UtcNow,
            AtualizadoPorUserId = userId,
            AtualizadoEmUtc = DateTime.UtcNow,
            VersaoTemplate = 1
        }, null);

        return (userId, clienteId, tarefaId);
    }

    private static int CriarTarefaSemConfig(SqliteDb db, int userId, int clienteId)
    {
        var tarefaRepo = new SqliteTarefaRepository(db);
        return tarefaRepo.Create(new Tarefa
        {
            ClienteId = clienteId,
            FerramentaId = FerramentaTarefaIds.AncorarPdfCanonico,
            Titulo = "Tarefa C7 Sem Config",
            VencimentoUtc = DateTime.UtcNow.AddDays(1),
            ResponsavelUserId = userId,
            Status = TarefaStatus.Agendada,
            Recorrencia = TarefaRecorrencia.Nenhuma,
            Ativa = true,
            CriadoPorUserId = userId,
            CriadoEmUtc = DateTime.UtcNow,
            AtualizadoEmUtc = DateTime.UtcNow
        });
    }

    private static int CriarTarefaComPasta(SqliteDb db, int userId, int clienteId, string pasta)
    {
        var tarefaRepo = new SqliteTarefaRepository(db);
        var confRepo = new SqliteAncorarPdfConfiguracaoRepository(db);

        var tarefaId = tarefaRepo.Create(new Tarefa
        {
            ClienteId = clienteId,
            FerramentaId = FerramentaTarefaIds.AncorarPdfCanonico,
            Titulo = "Tarefa C7 Vazia",
            VencimentoUtc = DateTime.UtcNow.AddDays(1),
            ResponsavelUserId = userId,
            Status = TarefaStatus.Agendada,
            Recorrencia = TarefaRecorrencia.Nenhuma,
            Ativa = true,
            CriadoPorUserId = userId,
            CriadoEmUtc = DateTime.UtcNow,
            AtualizadoEmUtc = DateTime.UtcNow
        });

        confRepo.Salvar(new AncorarPdfConfiguracaoTarefa
        {
            TarefaId = tarefaId,
            ClienteId = clienteId,
            EsteiraId = 1,
            NomeTarefaPersonalizado = "Tarefa C7 Vazia",
            PastaMonitoradaPath = pasta,
            PdfModeloPath = string.Empty,
            NomeReferenciaArquivo = "relatorio_2026",
            MonitorarSubpastas = false,
            ValidacaoClienteAtiva = false,
            LimiarSimilaridadeNome = 0.7,
            HighlightOpacity = 0.4,
            ModoSelecao = AncorarPdfModoSelecao.RetanguloLivre,
            Recorrencia = TarefaRecorrencia.Nenhuma,
            AgendamentoSegundo = 0,
            TimezoneId = "UTC",
            PrioridadeExecucao = 3,
            TemplateAncoras = [],
            ProgramadoPorUserId = userId,
            ProgramadoPorNome = "testuser",
            ProgramadoEmUtc = DateTime.UtcNow,
            AtualizadoPorUserId = userId,
            AtualizadoEmUtc = DateTime.UtcNow,
            VersaoTemplate = 1
        }, null);

        return tarefaId;
    }

    private static void CriarPdfComTexto(string path, string texto)
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        page.AddText(texto, 12, new UglyToad.PdfPig.Core.PdfPoint(50, 700), font);
        File.WriteAllBytes(path, builder.Build());
    }

    private static void CriarPdfSemTexto(string path)
    {
        var builder = new PdfDocumentBuilder();
        builder.AddPage(PageSize.A4);
        File.WriteAllBytes(path, builder.Build());
    }

    private AncorarPdfMotorExecucao CriarMotor(IAncorarPdfExtratorTexto extratorTexto)
    {
        return new AncorarPdfMotorExecucao(
            _confRepo,
            _execRepo,
            _saidaRepo,
            new AncorarPdfSeletorArquivoPasta(),
            extratorTexto,
            new AncorarPdfValidadorClienteRegex(new SqliteClienteRepository(_db)),
            new AncorarPdfAncoradorEspacialBbox(),
            TimeProvider.System);
    }

    private AncorarPdfFilaItem CriarFilaItem(int tarefaId, int clienteId, string cicloId)
        => new()
        {
            FilaItemId = Guid.NewGuid().ToString("N"),
            TarefaId = tarefaId,
            ClienteId = clienteId,
            CicloId = cicloId,
            JanelaAlvoUtc = new DateTime(2026, 2, 26, 0, 0, 0, DateTimeKind.Utc),
            PrioridadeExecucao = 3,
            Motivo = "scheduler_dispatch",
            EnfileiradoPorUserId = _userId,
            EnfileiradoPorNome = "testuser",
            EnfileiradoEmUtc = DateTime.UtcNow,
            CorrelationId = Guid.NewGuid().ToString("N"),
            CriadoEmUtc = DateTime.UtcNow
        };

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}
