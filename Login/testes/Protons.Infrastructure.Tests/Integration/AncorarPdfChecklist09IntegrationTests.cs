using System.Collections.Concurrent;
using System.Diagnostics;
using FluentAssertions;
using Moq;
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
/// C9 — Gate G4 (blocking): Testes de integração de release quality.
/// Cobre lacunas identificadas na matriz anti-duplicidade de C9:
/// segregação de clientes, atribuição multi-usuário, CorrelationId por enfileiramento,
/// Motivo persistido, exaustão de retry → Falhou Tecnica, dois ciclos independentes,
/// filtro de status na fila, e motor com CNPJ (C6 só usou CPF).
/// </summary>
[Trait("Checklist", "C9")]
[Trait("Category", "C9_G4_Integracao")]
public sealed class AncorarPdfChecklist09IntegrationTests
{
    // --- T01: Segregação de clientes via filtro TarefaId ---

    [Fact]
    public async Task T01_Segregacao_clientes_fila_retorna_apenas_itens_da_propria_tarefa()
    {
        var dbPath = DbPath("t01_segregacao");
        var db = new SqliteDb(dbPath);
        try
        {
            db.EnsureCreated();
            var tarefaId1 = CriarTarefa(db, out var clienteId1, out _);
            var tarefaId2 = CriarTarefa(db, out var clienteId2, out _);
            tarefaId1.Should().NotBe(tarefaId2);

            var filaRepo = new SqliteAncorarPdfExecucaoFilaRepository(db);
            var leaseRepo = new SqliteAncorarPdfExecucaoLeaseRepository(db);
            var motorMock = new Mock<IAncorarPdfMotorExecucao>();
            motorMock.Setup(m => m.ExecutarAsync(It.IsAny<AncorarPdfFilaItem>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AncorarPdfMotorResultado(true, AncorarPdfFilaFalhaCategoria.Nenhuma));

            var service = CriarService(filaRepo, leaseRepo, motorMock.Object);
            service.Start(numeroDeworkers: 1);

            // Enfileirar 2 itens para cliente1 e 1 para cliente2.
            await service.EnfileirarAsync(CriarEntrada(tarefaId1, clienteId1, "c9-t01-a", 3));
            await service.EnfileirarAsync(CriarEntrada(tarefaId1, clienteId1, "c9-t01-b", 3));
            await service.EnfileirarAsync(CriarEntrada(tarefaId2, clienteId2, "c9-t01-c", 3));

            await AguardarCondicaoAsync(() =>
                filaRepo.Listar(new AncorarPdfFilaFiltro(TarefaId: tarefaId1, Limite: 10))
                    .Count(x => x.Status == AncorarPdfFilaStatus.Concluido) == 2
                && filaRepo.Listar(new AncorarPdfFilaFiltro(TarefaId: tarefaId2, Limite: 10))
                    .Count(x => x.Status == AncorarPdfFilaStatus.Concluido) == 1,
                timeout: TimeSpan.FromSeconds(10));

            await service.StopAsync(TimeSpan.FromSeconds(3));

            // Filtro por TarefaId deve isolar completamente cada cliente.
            var itensTarefa1 = filaRepo.Listar(new AncorarPdfFilaFiltro(TarefaId: tarefaId1, Limite: 20));
            var itensTarefa2 = filaRepo.Listar(new AncorarPdfFilaFiltro(TarefaId: tarefaId2, Limite: 20));

            itensTarefa1.Should().HaveCount(2, "tarefa1 deve ter exatamente 2 itens");
            itensTarefa2.Should().HaveCount(1, "tarefa2 deve ter exatamente 1 item");
            itensTarefa1.Should().OnlyContain(i => i.TarefaId == tarefaId1);
            itensTarefa2.Should().OnlyContain(i => i.TarefaId == tarefaId2);
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    // --- T02: Atribuição multi-usuário persistida ---

    [Fact]
    public async Task T02_Atribuicao_multiusuario_nome_e_userId_persistidos_no_fila_item()
    {
        var dbPath = DbPath("t02_atribuicao");
        var db = new SqliteDb(dbPath);
        try
        {
            db.EnsureCreated();
            var tarefaId = CriarTarefa(db, out var clienteId, out var userId);

            var filaRepo = new SqliteAncorarPdfExecucaoFilaRepository(db);
            var leaseRepo = new SqliteAncorarPdfExecucaoLeaseRepository(db);
            var motorMock = new Mock<IAncorarPdfMotorExecucao>();
            motorMock.Setup(m => m.ExecutarAsync(It.IsAny<AncorarPdfFilaItem>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AncorarPdfMotorResultado(true, AncorarPdfFilaFalhaCategoria.Nenhuma));

            var service = CriarService(filaRepo, leaseRepo, motorMock.Object);
            service.Start(numeroDeworkers: 1);

            const string nomeEsperado = "OperadorEspecialC9";
            var entrada = new AncorarPdfFilaEnfileirarEntrada(
                TarefaId: tarefaId,
                ClienteId: clienteId,
                CicloId: "c9-t02-atrib",
                JanelaAlvoUtc: DateTime.UtcNow,
                PrioridadeExecucao: 3,
                Motivo: "scheduler_dispatch",
                EnfileiradoPorUserId: userId,
                EnfileiradoPorNome: nomeEsperado,
                CorrelationId: Guid.NewGuid().ToString("N"));

            await service.EnfileirarAsync(entrada);

            // Verificar imediatamente após enfileirar (antes ou depois de processar).
            var itens = filaRepo.Listar(new AncorarPdfFilaFiltro(TarefaId: tarefaId, Limite: 10));
            itens.Should().HaveCount(1);
            itens[0].EnfileiradoPorNome.Should().Be(nomeEsperado,
                "nome do operador deve ser persistido no item da fila");
            itens[0].EnfileiradoPorUserId.Should().Be(userId,
                "userId do operador deve ser persistido no item da fila");

            await service.StopAsync(TimeSpan.FromSeconds(3));
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    // --- T03: CorrelationId único por enfileiramento ---

    [Fact]
    public async Task T03_CorrelationId_unico_por_enfileiramento_persistido_corretamente()
    {
        var dbPath = DbPath("t03_correlation");
        var db = new SqliteDb(dbPath);
        try
        {
            db.EnsureCreated();
            var tarefaId = CriarTarefa(db, out var clienteId, out _);

            var filaRepo = new SqliteAncorarPdfExecucaoFilaRepository(db);
            var leaseRepo = new SqliteAncorarPdfExecucaoLeaseRepository(db);
            var motorMock = new Mock<IAncorarPdfMotorExecucao>();
            motorMock.Setup(m => m.ExecutarAsync(It.IsAny<AncorarPdfFilaItem>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AncorarPdfMotorResultado(true, AncorarPdfFilaFalhaCategoria.Nenhuma));

            var service = CriarService(filaRepo, leaseRepo, motorMock.Object);
            service.Start(numeroDeworkers: 1);

            // Enfileirar 3 itens com CorrelationIds únicos (como faz o JobHandler).
            var correlationIds = Enumerable.Range(0, 3)
                .Select(_ => Guid.NewGuid().ToString("N"))
                .ToArray();

            for (var i = 0; i < 3; i++)
            {
                await service.EnfileirarAsync(new AncorarPdfFilaEnfileirarEntrada(
                    TarefaId: tarefaId,
                    ClienteId: clienteId,
                    CicloId: $"c9-t03-{i:D3}",
                    JanelaAlvoUtc: DateTime.UtcNow,
                    PrioridadeExecucao: 3,
                    Motivo: "scheduler_dispatch",
                    EnfileiradoPorUserId: 0,
                    EnfileiradoPorNome: "c9_test",
                    CorrelationId: correlationIds[i]));
            }

            var itens = filaRepo.Listar(new AncorarPdfFilaFiltro(TarefaId: tarefaId, Limite: 20));

            itens.Should().HaveCount(3);
            var idsNoBanco = itens.Select(x => x.CorrelationId).ToArray();
            idsNoBanco.Distinct().Should().HaveCount(3, "cada item deve ter CorrelationId único");
            idsNoBanco.Should().BeEquivalentTo(correlationIds,
                "CorrelationIds fornecidos devem ser preservados exatamente");

            await service.StopAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    // --- T04: Motivo específico persistido no item da fila ---

    [Fact]
    public async Task T04_Motivo_especifico_persistido_e_recuperavel_da_fila()
    {
        var dbPath = DbPath("t04_motivo");
        var db = new SqliteDb(dbPath);
        try
        {
            db.EnsureCreated();
            var tarefaId = CriarTarefa(db, out var clienteId, out _);
            var filaRepo = new SqliteAncorarPdfExecucaoFilaRepository(db);
            var leaseRepo = new SqliteAncorarPdfExecucaoLeaseRepository(db);
            var motorMock = new Mock<IAncorarPdfMotorExecucao>();
            motorMock.Setup(m => m.ExecutarAsync(It.IsAny<AncorarPdfFilaItem>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AncorarPdfMotorResultado(true, AncorarPdfFilaFalhaCategoria.Nenhuma));

            var service = CriarService(filaRepo, leaseRepo, motorMock.Object);
            service.Start(numeroDeworkers: 1);

            const string motivoEsperado = "manual_operator_trigger_c9";
            await service.EnfileirarAsync(new AncorarPdfFilaEnfileirarEntrada(
                TarefaId: tarefaId,
                ClienteId: clienteId,
                CicloId: "c9-t04-motivo",
                JanelaAlvoUtc: DateTime.UtcNow,
                PrioridadeExecucao: 5,
                Motivo: motivoEsperado,
                EnfileiradoPorUserId: 0,
                EnfileiradoPorNome: "operador_c9",
                CorrelationId: Guid.NewGuid().ToString("N")));

            var itens = filaRepo.Listar(new AncorarPdfFilaFiltro(TarefaId: tarefaId, Limite: 10));

            itens.Should().HaveCount(1);
            itens[0].Motivo.Should().Be(motivoEsperado,
                "Motivo deve ser persistido exatamente como fornecido");

            await service.StopAsync(TimeSpan.FromSeconds(3));
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    // --- T05: Exaustão de retentativas → Falhou Tecnica permanente ---
    // Gap real: C8_P6 testa RECUPERAÇÃO (falha 2x, sucede na 3a).
    // Este teste verifica que quando TODAS as tentativas falham → status = Falhou (categoria Tecnica).

    [Fact]
    public async Task T05_Exaustao_retentativas_io_exception_resulta_em_falhou_tecnico_permanente()
    {
        var dbPath = DbPath("t05_exaustao");
        var db = new SqliteDb(dbPath);
        try
        {
            db.EnsureCreated();
            var tarefaId = CriarTarefa(db, out var clienteId, out _);
            var filaRepo = new SqliteAncorarPdfExecucaoFilaRepository(db);
            var leaseRepo = new SqliteAncorarPdfExecucaoLeaseRepository(db);

            // Motor sempre lança IOException — nunca recupera.
            var motorMock = new Mock<IAncorarPdfMotorExecucao>();
            motorMock.Setup(m => m.ExecutarAsync(It.IsAny<AncorarPdfFilaItem>(), It.IsAny<CancellationToken>()))
                .Throws(new IOException("falha permanente simulada c9"));

            var service = CriarService(filaRepo, leaseRepo, motorMock.Object,
                retryDelay: TimeSpan.FromMilliseconds(50));
            service.Start(numeroDeworkers: 1);

            await service.EnfileirarAsync(CriarEntrada(tarefaId, clienteId, "c9-t05-exaustao", 3));

            // Aguarda item entrar em Falhou (após 3 retentativas Polly).
            await AguardarCondicaoAsync(() =>
                filaRepo.Listar(new AncorarPdfFilaFiltro(
                    TarefaId: tarefaId,
                    Status: AncorarPdfFilaStatus.Falhou,
                    Limite: 10)).Count == 1,
                timeout: TimeSpan.FromSeconds(15));

            await service.StopAsync(TimeSpan.FromSeconds(3));

            var falhou = filaRepo.Listar(new AncorarPdfFilaFiltro(
                TarefaId: tarefaId, Status: AncorarPdfFilaStatus.Falhou, Limite: 10));

            falhou.Should().HaveCount(1, "exaustão de retentativas deve resultar em exatamente 1 Falhou");
            falhou[0].CategoriaFalha.Should().Be(AncorarPdfFilaFalhaCategoria.Tecnica,
                "IOException deve resultar em categoria Tecnica (não Negocio)");

            // Nenhum item deve estar Concluido.
            var concluidos = filaRepo.Listar(new AncorarPdfFilaFiltro(
                TarefaId: tarefaId, Status: AncorarPdfFilaStatus.Concluido, Limite: 10));
            concluidos.Should().BeEmpty("exaustão não deve resultar em Concluido");
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    // --- T06: Dois ciclos distintos da mesma tarefa completam independentemente ---
    // Gap real: C6_T04 testa idempotência (2x o MESMO cicloId). Este testa 2 cicloIds DISTINTOS.

    [Fact]
    public async Task T06_Dois_ciclos_distintos_mesma_tarefa_completam_independentemente()
    {
        var dbPath = DbPath("t06_dois_ciclos");
        var db = new SqliteDb(dbPath);
        try
        {
            db.EnsureCreated();
            var tarefaId = CriarTarefa(db, out var clienteId, out _);
            var filaRepo = new SqliteAncorarPdfExecucaoFilaRepository(db);
            var leaseRepo = new SqliteAncorarPdfExecucaoLeaseRepository(db);
            var motorMock = new Mock<IAncorarPdfMotorExecucao>();
            motorMock.Setup(m => m.ExecutarAsync(It.IsAny<AncorarPdfFilaItem>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AncorarPdfMotorResultado(true, AncorarPdfFilaFalhaCategoria.Nenhuma));

            var service = CriarService(filaRepo, leaseRepo, motorMock.Object);
            service.Start(numeroDeworkers: 2);

            // Enfileirar dois ciclos DISTINTOS da mesma tarefa.
            await service.EnfileirarAsync(CriarEntrada(tarefaId, clienteId, "ciclo-janeiro-2026", 3));
            await service.EnfileirarAsync(CriarEntrada(tarefaId, clienteId, "ciclo-fevereiro-2026", 3));

            await AguardarCondicaoAsync(() =>
                filaRepo.Listar(new AncorarPdfFilaFiltro(
                    TarefaId: tarefaId,
                    Status: AncorarPdfFilaStatus.Concluido,
                    Limite: 20)).Count == 2,
                timeout: TimeSpan.FromSeconds(10));

            await service.StopAsync(TimeSpan.FromSeconds(3));

            var concluidos = filaRepo.Listar(new AncorarPdfFilaFiltro(
                TarefaId: tarefaId, Status: AncorarPdfFilaStatus.Concluido, Limite: 20));

            concluidos.Should().HaveCount(2, "dois ciclos distintos devem resultar em 2 execuções completas");
            concluidos.Select(i => i.CicloId).Distinct()
                .Should().HaveCount(2, "cada ciclo deve ter um CicloId único");
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    // --- T07: Filtro de fila por Status funciona isoladamente ---

    [Fact]
    public void T07_Filtro_fila_por_status_retorna_subconjunto_correto()
    {
        var dbPath = DbPath("t07_filtro_status");
        var db = new SqliteDb(dbPath);
        try
        {
            db.EnsureCreated();
            var tarefaId = CriarTarefa(db, out var clienteId, out _);
            var filaRepo = new SqliteAncorarPdfExecucaoFilaRepository(db);

            // Inserir 3 itens diretamente (sem worker) e atualizar manualmente o status.
            var ids = Enumerable.Range(0, 3)
                .Select(_ => CriarFilaItemDireto(tarefaId, clienteId))
                .ToArray();

            foreach (var item in ids)
                filaRepo.Enfileirar(item);

            // Concluir o primeiro, falhar o segundo, deixar o terceiro aguardando.
            filaRepo.AtualizarStatus(ids[0].FilaItemId, AncorarPdfFilaStatus.Concluido,
                finalizadoEmUtc: DateTime.UtcNow);
            filaRepo.AtualizarStatus(ids[1].FilaItemId, AncorarPdfFilaStatus.Falhou,
                finalizadoEmUtc: DateTime.UtcNow,
                categoriaFalha: AncorarPdfFilaFalhaCategoria.Negocio,
                erroCodigo: AncorarPdfErroCodigos.NegPdfSemTexto);

            var concluidos = filaRepo.Listar(new AncorarPdfFilaFiltro(
                TarefaId: tarefaId, Status: AncorarPdfFilaStatus.Concluido, Limite: 10));
            var falhados = filaRepo.Listar(new AncorarPdfFilaFiltro(
                TarefaId: tarefaId, Status: AncorarPdfFilaStatus.Falhou, Limite: 10));
            var aguardando = filaRepo.Listar(new AncorarPdfFilaFiltro(
                TarefaId: tarefaId, Status: AncorarPdfFilaStatus.Aguardando, Limite: 10));

            concluidos.Should().HaveCount(1, "filtro Concluido deve retornar exatamente 1 item");
            falhados.Should().HaveCount(1, "filtro Falhou deve retornar exatamente 1 item");
            aguardando.Should().HaveCount(1, "filtro Aguardando deve retornar exatamente 1 item");

            falhados[0].CategoriaFalha.Should().Be(AncorarPdfFilaFalhaCategoria.Negocio);
            falhados[0].ErroCodigo.Should().Be(AncorarPdfErroCodigos.NegPdfSemTexto);
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    // --- T08: Motor com CNPJ (C6 só testou CPF no motor completo) ---

    [Fact]
    public async Task T08_Motor_com_cnpj_no_pdf_valida_cliente_e_produz_saida_concluida()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"c9_t08_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var dbPath = DbPath("t08_motor_cnpj");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();

            var (userId, clienteId, tarefaId) = SeedMotorDatabase(db, tempDir, "04252011000110");

            var confRepo = new SqliteAncorarPdfConfiguracaoRepository(db);
            var execRepo = new SqliteAncorarPdfExecucaoRepository(db);
            var saidaRepo = new SqliteAncorarPdfSaidaRepository(db);

            var motor = new AncorarPdfMotorExecucao(
                confRepo, execRepo, saidaRepo,
                new AncorarPdfSeletorArquivoPasta(),
                new AncorarPdfExtratorTextoPdfPig(),
                new AncorarPdfValidadorClienteRegex(new SqliteClienteRepository(db)),
                new AncorarPdfAncoradorEspacialBbox(),
                TimeProvider.System);

            // PDF com CNPJ (não CPF como em C6).
            var pdfPath = Path.Combine(tempDir, "relatorio_cnpj_2026.pdf");
            CriarPdfComTexto(pdfPath, "CNPJ 04.252.011/0001-10 Valor R$ 9.999,00");

            var item = new AncorarPdfFilaItem
            {
                FilaItemId = Guid.NewGuid().ToString("N"),
                TarefaId = tarefaId,
                ClienteId = clienteId,
                CicloId = "c9-t08-cnpj-motor",
                JanelaAlvoUtc = new DateTime(2026, 2, 26, 0, 0, 0, DateTimeKind.Utc),
                PrioridadeExecucao = 3,
                Motivo = "scheduler_dispatch",
                EnfileiradoPorUserId = userId,
                EnfileiradoPorNome = "c9_operador",
                EnfileiradoEmUtc = DateTime.UtcNow,
                CorrelationId = Guid.NewGuid().ToString("N"),
                CriadoEmUtc = DateTime.UtcNow
            };

            var resultado = await motor.ExecutarAsync(item, CancellationToken.None);

            resultado.Sucesso.Should().BeTrue(
                "PDF com CNPJ válido deve completar sem erro (match exato com documento do cliente)");
            resultado.Categoria.Should().Be(AncorarPdfFilaFalhaCategoria.Nenhuma);
            resultado.ErroCodigo.Should().BeNull();
        }
        finally
        {
            LimparDb(dbPath);
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    // --- Helpers ---

    private static string DbPath(string sufixo)
        => Path.Combine(Path.GetTempPath(), $"protons_c9_{sufixo}_{Guid.NewGuid():N}.db");

    private static void LimparDb(string dbPath)
    {
        if (File.Exists(dbPath)) File.Delete(dbPath);
    }

    private static AncorarPdfFilaExecucaoService CriarService(
        IAncorarPdfFilaExecucaoRepository filaRepo,
        IAncorarPdfExecucaoLeaseRepository leaseRepo,
        IAncorarPdfMotorExecucao motor,
        TimeSpan? retryDelay = null)
        => new(
            filaRepo, leaseRepo, motor,
            timeProvider: TimeProvider.System,
            channelCapacity: 50,
            leaseDuracao: TimeSpan.FromSeconds(60),
            processoTimeout: TimeSpan.FromSeconds(15),
            retryDelay: retryDelay ?? TimeSpan.FromMilliseconds(100));

    private static AncorarPdfFilaEnfileirarEntrada CriarEntrada(
        int tarefaId, int clienteId, string cicloId, int prioridade)
        => new(
            TarefaId: tarefaId,
            ClienteId: clienteId,
            CicloId: cicloId,
            JanelaAlvoUtc: DateTime.UtcNow,
            PrioridadeExecucao: prioridade,
            Motivo: "scheduler_dispatch",
            EnfileiradoPorUserId: 0,
            EnfileiradoPorNome: "c9_test",
            CorrelationId: Guid.NewGuid().ToString("N"));

    private static AncorarPdfFilaItem CriarFilaItemDireto(int tarefaId, int clienteId)
    {
        var now = DateTime.UtcNow;
        return new AncorarPdfFilaItem
        {
            FilaItemId = Guid.NewGuid().ToString("N"),
            TarefaId = tarefaId,
            ClienteId = clienteId,
            CicloId = Guid.NewGuid().ToString("N"),
            JanelaAlvoUtc = now,
            PrioridadeExecucao = 3,
            Status = AncorarPdfFilaStatus.Aguardando,
            Motivo = "c9_direto",
            EnfileiradoPorUserId = 0,
            EnfileiradoPorNome = "c9_test",
            EnfileiradoEmUtc = now,
            TentativaAtual = 0,
            TentativasMaximas = 3,
            CorrelationId = Guid.NewGuid().ToString("N"),
            CriadoEmUtc = now
        };
    }

    private static int CriarTarefa(SqliteDb db, out int clienteId, out int userId)
    {
        var userRepo = new UserRepository(db);
        var clienteRepo = new SqliteClienteRepository(db);
        var tarefaRepo = new SqliteTarefaRepository(db);

        userId = userRepo.Create(new User
        {
            Empresa = "Protons",
            Nome = $"Op C9 {Guid.NewGuid():N}"[..16],
            Cpf = "11144477735",
            Cargo = "Operador",
            Email = $"c9.{Guid.NewGuid():N}"[..28] + "@protons.local",
            SenhaHash = "hash",
            SenhaSalt = "salt",
            IteracoesPbkdf2 = 100000,
            Status = UserStatus.Ativo,
            Role = UserRole.Admin,
            CriadoEmUtc = DateTime.UtcNow,
            AtualizadoEmUtc = DateTime.UtcNow
        });

        // Documento único por registro (evita UNIQUE constraint em Clientes.DocumentoHash).
        var doc = (BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0)
            % (ulong)100_000_000_000_000).ToString("D14");

        clienteId = clienteRepo.Create(new Cliente
        {
            CodigoCliente = $"C9-{Guid.NewGuid():N}"[..14],
            Nome = $"Cliente C9 {Guid.NewGuid():N}"[..20],
            TipoDocumento = TipoDocumentoCliente.CNPJ,
            Documento = doc,
            Ativo = true,
            CriadoPorUserId = userId,
            CriadoEmUtc = DateTime.UtcNow,
            AtualizadoEmUtc = DateTime.UtcNow
        });

        return tarefaRepo.Create(new Tarefa
        {
            ClienteId = clienteId,
            FerramentaId = FerramentaTarefaIds.AncorarPdfCanonico,
            Titulo = $"Tarefa C9 {Guid.NewGuid():N}"[..20],
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

    private static (int userId, int clienteId, int tarefaId) SeedMotorDatabase(
        SqliteDb db, string tempDir, string documentoCliente)
    {
        db.EnsureCreated();

        var userRepo = new UserRepository(db);
        var clienteRepo = new SqliteClienteRepository(db);
        var tarefaRepo = new SqliteTarefaRepository(db);
        var confRepo = new SqliteAncorarPdfConfiguracaoRepository(db);

        var userId = userRepo.Create(new User
        {
            Empresa = "Protons",
            Nome = "Op C9 Motor CNPJ",
            Cpf = "11144477735",
            Cargo = "Operador",
            Email = $"c9.motor.{Guid.NewGuid():N}"[..30] + "@protons.local",
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
            CodigoCliente = $"C9M-{Guid.NewGuid():N}"[..14],
            Nome = "Cliente C9 Motor CNPJ",
            TipoDocumento = TipoDocumentoCliente.CNPJ,
            Documento = documentoCliente,
            Ativo = true,
            CriadoPorUserId = userId,
            CriadoEmUtc = DateTime.UtcNow,
            AtualizadoEmUtc = DateTime.UtcNow
        });

        var tarefaId = tarefaRepo.Create(new Tarefa
        {
            ClienteId = clienteId,
            FerramentaId = FerramentaTarefaIds.AncorarPdfCanonico,
            Titulo = "Tarefa C9 Motor CNPJ",
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
            NomeTarefaPersonalizado = "Tarefa C9 Motor CNPJ",
            PastaMonitoradaPath = tempDir,
            PdfModeloPath = string.Empty,
            NomeReferenciaArquivo = "relatorio_cnpj_2026",
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
                    Ordem = 1, CorHex = "#4A90D9", Pagina = 1,
                    XRel = 0.0, YRel = 0.0, LarguraRel = 1.0, AlturaRel = 1.0,
                    Metadado = new AncorarPdfTemplateMetadado
                    {
                        NomeExibido = "Texto Principal",
                        ChaveTecnica = "texto_principal",
                        TipoEsperado = "texto"
                    }
                }
            ],
            ProgramadoPorUserId = userId,
            ProgramadoPorNome = "c9_operador",
            ProgramadoEmUtc = DateTime.UtcNow,
            AtualizadoPorUserId = userId,
            AtualizadoEmUtc = DateTime.UtcNow,
            VersaoTemplate = 1
        }, null);

        return (userId, clienteId, tarefaId);
    }

    private static void CriarPdfComTexto(string path, string texto)
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        page.AddText(texto, 12, new UglyToad.PdfPig.Core.PdfPoint(50, 700), font);
        File.WriteAllBytes(path, builder.Build());
    }

    private static async Task AguardarCondicaoAsync(
        Func<bool> condicao, TimeSpan timeout, TimeSpan? intervalo = null)
    {
        var sw = Stopwatch.StartNew();
        var wait = intervalo ?? TimeSpan.FromMilliseconds(100);
        while (sw.Elapsed < timeout)
        {
            if (condicao()) return;
            await Task.Delay(wait);
        }
        throw new TimeoutException($"Condição não satisfeita em {timeout.TotalSeconds:F1}s.");
    }
}
