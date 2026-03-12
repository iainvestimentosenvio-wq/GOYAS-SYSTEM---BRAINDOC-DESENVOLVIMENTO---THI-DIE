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
/// C6 — Gate G2 (blocking): Testes de integração do motor de ancoragem PDF.
/// Usa SQLite em arquivo temp + PDFs gerados programaticamente com PdfPig Writer.
/// </summary>
[Trait("Checklist", "C6")]
[Trait("Category", "C6_G2_Motor")]
public sealed class AncorarPdfChecklist06MotorIntegrationTests : IDisposable
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

    public AncorarPdfChecklist06MotorIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"c6_motor_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _dbPath = Path.Combine(_tempDir, "c6_motor.db");
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

    // --- Testes ---

    [Fact]
    public async Task T01_Motor_pdf_texto_completo_produz_saida_concluida()
    {
        // Arrange: cria PDF com CPF exato do cliente.
        var pdfPath = Path.Combine(_tempDir, "relatorio_2026.pdf");
        CriarPdfComTexto(pdfPath, "Contrato CPF 123.456.789-09 Valor R$ 1.234,56");

        var item = CriarFilaItem(_tarefaId, _clienteId, "ciclo-t01");

        // Act
        var resultado = await _motor.ExecutarAsync(item, CancellationToken.None);

        // Assert
        resultado.Sucesso.Should().BeTrue();
        resultado.Categoria.Should().Be(AncorarPdfFilaFalhaCategoria.Nenhuma);
    }

    [Fact]
    public async Task T02_Motor_pdf_sem_texto_retorna_negocio_pdf_sem_texto()
    {
        // Arrange: PDF sem conteúdo de texto.
        var pdfPath = Path.Combine(_tempDir, "relatorio_2026.pdf");
        CriarPdfSemTexto(pdfPath);

        var item = CriarFilaItem(_tarefaId, _clienteId, "ciclo-t02");

        // Act
        var resultado = await _motor.ExecutarAsync(item, CancellationToken.None);

        // Assert
        resultado.Sucesso.Should().BeFalse();
        resultado.Categoria.Should().Be(AncorarPdfFilaFalhaCategoria.Negocio);
        resultado.ErroCodigo.Should().Be(AncorarPdfErroCodigos.NegPdfSemTexto);
    }

    [Fact]
    public async Task T03_Motor_arquivo_inexistente_retorna_negocio_nao_encontrado()
    {
        // Arrange: tarefa apontando para pasta vazia.
        var pastaVazia = Path.Combine(_tempDir, "pasta_vazia");
        Directory.CreateDirectory(pastaVazia);

        var tarefaVaziaId = CriarTarefaComPasta(_db, _userId, _clienteId, pastaVazia);
        var item = CriarFilaItem(tarefaVaziaId, _clienteId, "ciclo-t03");

        // Act
        var resultado = await _motor.ExecutarAsync(item, CancellationToken.None);

        // Assert
        resultado.Sucesso.Should().BeFalse();
        resultado.Categoria.Should().Be(AncorarPdfFilaFalhaCategoria.Negocio);
        resultado.ErroCodigo.Should().Be(AncorarPdfErroCodigos.NegArquivoNaoEncontrado);
    }

    [Fact]
    public async Task T04_Motor_segundo_run_mesmo_ciclo_retorna_sucesso_silencioso()
    {
        // Arrange
        var pdfPath = Path.Combine(_tempDir, "relatorio_2026.pdf");
        CriarPdfComTexto(pdfPath, "Texto CPF 123.456.789-09");

        var item = CriarFilaItem(_tarefaId, _clienteId, "ciclo-t04-idem");

        // Act — primeira execução.
        var r1 = await _motor.ExecutarAsync(item, CancellationToken.None);

        // Act — segunda execução com mesmo cicloId (idempotência).
        var r2 = await _motor.ExecutarAsync(item, CancellationToken.None);

        // Assert — ambas sucedem; a segunda não reprocesa.
        r1.Sucesso.Should().BeTrue();
        r2.Sucesso.Should().BeTrue();
        r2.ErroCodigo.Should().BeNull();
    }

    [Fact]
    public async Task T05_Motor_config_inexistente_retorna_negocio_config_nao_encontrada()
    {
        // Arrange: TarefaId sem configuração.
        var item = CriarFilaItem(tarefaId: 99998, _clienteId, "ciclo-t05");

        // Act
        var resultado = await _motor.ExecutarAsync(item, CancellationToken.None);

        // Assert
        resultado.Sucesso.Should().BeFalse();
        resultado.ErroCodigo.Should().Be(AncorarPdfErroCodigos.NegConfigNaoEncontrada);
    }

    [Fact]
    public async Task T06_Motor_cliente_invalido_retorna_negocio_cliente_nao_validado()
    {
        // Arrange: PDF com CPF válido diferente do documento oficial do cliente.
        var pdfPath = Path.Combine(_tempDir, "relatorio_2026.pdf");
        CriarPdfComTexto(pdfPath, "Contrato CPF 529.982.247-25");

        // Tarefa com validação ativa para o mesmo cliente cadastrado.
        var item = CriarFilaItem(_tarefaId, _clienteId, "ciclo-t06");

        // Act
        var resultado = await _motor.ExecutarAsync(item, CancellationToken.None);

        // Assert
        resultado.Sucesso.Should().BeFalse();
        resultado.Categoria.Should().Be(AncorarPdfFilaFalhaCategoria.Negocio);
        resultado.ErroCodigo.Should().Be("cliente_nao_validado");
    }

    [Fact]
    public async Task T07_Motor_ocr_ativo_sem_tessdata_retorna_negocio_ocr_indisponivel()
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
        var item = CriarFilaItem(_tarefaId, _clienteId, "ciclo-t07");

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
        resultado.Categoria.Should().Be(AncorarPdfFilaFalhaCategoria.Negocio);
        resultado.ErroCodigo.Should().Be(AncorarPdfErroCodigos.NegOcrIndisponivel);
    }

    [Fact]
    public async Task T08_Motor_ocr_ativo_sem_palavras_permanece_neg_pdf_sem_texto()
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
        var item = CriarFilaItem(_tarefaId, _clienteId, "ciclo-t08");

        var extratorComOcr = new AncorarPdfExtratorTextoComOcrFallback(
            new AncorarPdfExtratorTextoPdfPig(),
            (_, _) => new ExtratorTextoFixo(
            [
                new PdfPaginaTexto(1, 595, 842, [])
            ]));
        var motor = CriarMotor(extratorComOcr);

        // Act
        var resultado = await motor.ExecutarAsync(item, CancellationToken.None);

        // Assert
        resultado.Sucesso.Should().BeFalse();
        resultado.Categoria.Should().Be(AncorarPdfFilaFalhaCategoria.Negocio);
        resultado.ErroCodigo.Should().Be(AncorarPdfErroCodigos.NegPdfSemTexto);
        resultado.ErroDetalhe.Should().Contain("OCR foi tentado sem sucesso");
    }

    [Fact]
    public async Task T09_Motor_ocr_ativo_com_palavras_retorna_sucesso()
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
        var item = CriarFilaItem(_tarefaId, _clienteId, "ciclo-t09");

        var extratorComOcr = new AncorarPdfExtratorTextoComOcrFallback(
            new AncorarPdfExtratorTextoPdfPig(),
            (_, _) => new ExtratorTextoFixo(
            [
                new PdfPaginaTexto(
                    1,
                    595,
                    842,
                    [
                        new PdfPalavra("123.456.789-09", new BboxRelativo(0.1, 0.1, 0.2, 0.05)),
                        new PdfPalavra("R$ 1.234,56", new BboxRelativo(0.2, 0.2, 0.2, 0.05))
                    ])
            ]));
        var motor = CriarMotor(extratorComOcr);

        // Act
        var resultado = await motor.ExecutarAsync(item, CancellationToken.None);

        // Assert
        resultado.Sucesso.Should().BeTrue();
        resultado.Categoria.Should().Be(AncorarPdfFilaFalhaCategoria.Nenhuma);
    }

    [Fact]
    public async Task T10_Motor_ancora_por_texto_extrai_a_direita()
    {
        // Arrange: ExtratorTextoFixo com palavras conhecidas para teste determinístico.
        var pasta = Path.Combine(_tempDir, "ancora_texto");
        Directory.CreateDirectory(pasta);
        var pdfPath = Path.Combine(pasta, "relatorio_2026.pdf");
        CriarPdfComValorLabel(pdfPath);

        var tarefaId = CriarTarefaComAncoraPorTexto(_db, _userId, _clienteId, pasta);
        var item = CriarFilaItem(tarefaId, _clienteId, "ciclo-t10");

        var paginas = new[]
        {
            new PdfPaginaTexto(1, 595, 842,
            [
                new PdfPalavra("Valor:", new BboxRelativo(0.08, 0.82, 0.08, 0.03)),
                new PdfPalavra("R$", new BboxRelativo(0.17, 0.82, 0.04, 0.03)),
                new PdfPalavra("1.234,56", new BboxRelativo(0.22, 0.82, 0.10, 0.03))
            ])
        };
        var extrator = new ExtratorTextoFixo(paginas);
        var motor = CriarMotor(extrator);

        // Act
        var resultado = await motor.ExecutarAsync(item, CancellationToken.None);

        // Assert
        resultado.Sucesso.Should().BeTrue();
        var saidas = _saidaRepo.Listar(new AncorarPdfSaidasFiltro { ClienteId = _clienteId, TarefaId = tarefaId });
        saidas.Should().NotBeEmpty();
        saidas[0].Variaveis.Should().Contain(v => v.Chave == "valor" && v.ValorBruto.Contains("1.234"));
    }

    // --- Helpers ---

    private static int CriarTarefaComAncoraPorTexto(SqliteDb db, int userId, int clienteId, string pasta)
    {
        var tarefaRepo = new SqliteTarefaRepository(db);
        var confRepo = new SqliteAncorarPdfConfiguracaoRepository(db);

        var tarefaId = tarefaRepo.Create(new Tarefa
        {
            ClienteId = clienteId,
            FerramentaId = FerramentaTarefaIds.AncorarPdfCanonico,
            Titulo = "Tarefa C6 Ancora Texto",
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
            NomeTarefaPersonalizado = "Tarefa C6 Ancora Texto",
            PastaMonitoradaPath = pasta,
            PdfModeloPath = Path.Combine(pasta, "relatorio_2026.pdf"),
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
            TemplateAncoras =
            [
                new AncorarPdfTemplateAncora
                {
                    Ordem = 1,
                    CorHex = "#4A90D9",
                    Pagina = 1,
                    ModoAncora = AncorarPdfModoAncora.TextoADireita,
                    TextoAncora = "Valor:",
                    LarguraExtracaoRel = 0.3,
                    AlturaExtracaoRel = 0.05,
                    Metadado = new AncorarPdfTemplateMetadado
                    {
                        NomeExibido = "Valor",
                        ChaveTecnica = "valor",
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

        return tarefaId;
    }

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
            Nome = "Op C6 Motor",
            Cpf = "11144477735",
            Cargo = "Operador",
            Email = $"c6.motor.{Guid.NewGuid():N}"[..30] + "@protons.local",
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
            CodigoCliente = $"C6M-{Guid.NewGuid():N}"[..14],
            Nome = "Cliente C6 Motor",
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
            Titulo = "Tarefa C6 Motor",
            VencimentoUtc = DateTime.UtcNow.AddDays(1),
            ResponsavelUserId = userId,
            Status = TarefaStatus.Agendada,
            Recorrencia = TarefaRecorrencia.Nenhuma,
            Ativa = true,
            CriadoPorUserId = userId,
            CriadoEmUtc = DateTime.UtcNow,
            AtualizadoEmUtc = DateTime.UtcNow
        });

        // Configuração com uma âncora de template cobrindo toda a página.
        confRepo.Salvar(new AncorarPdfConfiguracaoTarefa
        {
            TarefaId = tarefaId,
            ClienteId = clienteId,
            EsteiraId = 1,
            NomeTarefaPersonalizado = "Tarefa C6 Motor",
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
                        NomeExibido = "Texto Principal",
                        ChaveTecnica = "texto_principal",
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

    private static int CriarTarefaComPasta(SqliteDb db, int userId, int clienteId, string pasta)
    {
        var tarefaRepo = new SqliteTarefaRepository(db);
        var confRepo = new SqliteAncorarPdfConfiguracaoRepository(db);

        var tarefaId = tarefaRepo.Create(new Tarefa
        {
            ClienteId = clienteId,
            FerramentaId = FerramentaTarefaIds.AncorarPdfCanonico,
            Titulo = "Tarefa C6 Vazia",
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
            NomeTarefaPersonalizado = "Tarefa C6 Vazia",
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

    private static void CriarPdfComValorLabel(string path)
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        page.AddText("Valor:", 12, new UglyToad.PdfPig.Core.PdfPoint(50, 700), font);
        page.AddText("R$ 1.234,56", 12, new UglyToad.PdfPig.Core.PdfPoint(120, 700), font);
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

    private sealed class ExtratorTextoFixo : IAncorarPdfExtratorTexto
    {
        private readonly IReadOnlyList<PdfPaginaTexto> _paginas;

        public ExtratorTextoFixo(IReadOnlyList<PdfPaginaTexto> paginas)
        {
            _paginas = paginas;
        }

        public Task<IReadOnlyList<PdfPaginaTexto>> ExtrairAsync(string arquivoPath, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(_paginas);
        }
    }
}
