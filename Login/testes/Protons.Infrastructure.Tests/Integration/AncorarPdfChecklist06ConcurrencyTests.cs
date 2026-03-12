using FluentAssertions;
using Moq;
using Protons.Core.Clientes.Models;
using Protons.Core.Login.Models;
using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Services;
using Protons.Infrastructure.Clientes.Repositories;
using Protons.Infrastructure.Login.Database;
using Protons.Infrastructure.Login.Repositories;
using Protons.Infrastructure.Tarefas.Repositories;
using Protons.Infrastructure.Tarefas.Services;
using UglyToad.PdfPig.Writer;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using Xunit;

namespace Protons.Infrastructure.Tests.Integration;

/// <summary>
/// C6 — Gate G4 (blocking): Testes de concorrência do motor de ancoragem.
/// Verifica que múltiplos workers respeitam idempotência de lease e que
/// o Polly retenta IOExceptions corretamente.
/// </summary>
[Trait("Checklist", "C6")]
[Trait("Category", "C6_G4_Concorrencia")]
public sealed class AncorarPdfChecklist06ConcurrencyTests
{
    private static string DbPath(string tag)
        => Path.Combine(Path.GetTempPath(), $"c6_conc_{tag}_{Guid.NewGuid():N}.db");

    private static void LimparDb(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static (int userId, int clienteId, int tarefaId) CriarTarefa(SqliteDb db)
    {
        db.EnsureCreated();

        var userRepo = new UserRepository(db);
        var clienteRepo = new SqliteClienteRepository(db);
        var tarefaRepo = new SqliteTarefaRepository(db);

        var userId = userRepo.Create(new User
        {
            Empresa = "Protons",
            Nome = $"Op C6 Conc {Guid.NewGuid():N}"[..20],
            Cpf = "11144477735",
            Cargo = "Op",
            Email = $"c6.conc.{Guid.NewGuid():N}"[..25] + "@p.local",
            SenhaHash = "h",
            SenhaSalt = "s",
            IteracoesPbkdf2 = 100000,
            Status = UserStatus.Ativo,
            Role = UserRole.Admin,
            CriadoEmUtc = DateTime.UtcNow,
            AtualizadoEmUtc = DateTime.UtcNow
        });

        var clienteId = clienteRepo.Create(new Cliente
        {
            CodigoCliente = $"C6C-{Guid.NewGuid():N}"[..14],
            Nome = "Cliente C6 Conc",
            TipoDocumento = TipoDocumentoCliente.CNPJ,
            Documento = "04252011000110",
            Ativo = true,
            CriadoPorUserId = userId,
            CriadoEmUtc = DateTime.UtcNow,
            AtualizadoEmUtc = DateTime.UtcNow
        });

        var tarefaId = tarefaRepo.Create(new Tarefa
        {
            ClienteId = clienteId,
            FerramentaId = FerramentaTarefaIds.AncorarPdfCanonico,
            Titulo = "Tarefa C6 Conc",
            VencimentoUtc = DateTime.UtcNow.AddHours(1),
            ResponsavelUserId = userId,
            Status = TarefaStatus.Agendada,
            Recorrencia = TarefaRecorrencia.Nenhuma,
            Ativa = true,
            CriadoPorUserId = userId,
            CriadoEmUtc = DateTime.UtcNow,
            AtualizadoEmUtc = DateTime.UtcNow
        });

        return (userId, clienteId, tarefaId);
    }

    private static AncorarPdfFilaItem CriarEntrada(int tarefaId, int clienteId, int userId, string cicloId)
        => new()
        {
            FilaItemId = Guid.NewGuid().ToString("N"),
            TarefaId = tarefaId,
            ClienteId = clienteId,
            CicloId = cicloId,
            JanelaAlvoUtc = new DateTime(2026, 2, 26, 0, 0, 0, DateTimeKind.Utc),
            PrioridadeExecucao = 3,
            Motivo = "scheduler_dispatch",
            EnfileiradoPorUserId = userId,
            EnfileiradoPorNome = "testuser",
            EnfileiradoEmUtc = DateTime.UtcNow,
            CorrelationId = Guid.NewGuid().ToString("N"),
            CriadoEmUtc = DateTime.UtcNow
        };

    [Fact]
    public async Task T01_Dois_workers_mesmo_item_processa_exatamente_1x()
    {
        var dbPath = DbPath("dual_worker");
        var db = new SqliteDb(dbPath);
        try
        {
            var (userId, clienteId, tarefaId) = CriarTarefa(db);
            var filaRepo = new SqliteAncorarPdfExecucaoFilaRepository(db);
            var leaseRepo = new SqliteAncorarPdfExecucaoLeaseRepository(db);

            var processados = new System.Collections.Concurrent.ConcurrentBag<string>();
            var motorMock = new Mock<IAncorarPdfMotorExecucao>();
            motorMock.Setup(m => m.ExecutarAsync(
                    It.IsAny<AncorarPdfFilaItem>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((AncorarPdfFilaItem item, CancellationToken _) =>
                {
                    processados.Add(item.FilaItemId);
                    return new AncorarPdfMotorResultado(true, AncorarPdfFilaFalhaCategoria.Nenhuma);
                });

            filaRepo.Enfileirar(CriarEntrada(tarefaId, clienteId, userId, "ciclo-t01-conc"));

            var service = new AncorarPdfFilaExecucaoService(
                filaRepo, leaseRepo, motorMock.Object,
                timeProvider: TimeProvider.System,
                channelCapacity: 100,
                leaseDuracao: TimeSpan.FromMinutes(5),
                processoTimeout: TimeSpan.FromSeconds(5));
            service.Start(numeroDeworkers: 2);

            await Task.Delay(TimeSpan.FromSeconds(3));
            await service.StopAsync(TimeSpan.FromSeconds(3));

            processados.Should().HaveCount(1, "um único item deve ser processado exatamente uma vez");
        }
        finally { LimparDb(dbPath); }
    }

    [Fact]
    public async Task T02_Cinco_itens_distintos_cinco_saidas_distintas()
    {
        var dbPath = DbPath("cinco_itens");
        var db = new SqliteDb(dbPath);
        try
        {
            var (userId, clienteId, tarefaId) = CriarTarefa(db);
            var filaRepo = new SqliteAncorarPdfExecucaoFilaRepository(db);
            var leaseRepo = new SqliteAncorarPdfExecucaoLeaseRepository(db);

            var processados = new System.Collections.Concurrent.ConcurrentBag<string>();
            var motorMock = new Mock<IAncorarPdfMotorExecucao>();
            motorMock.Setup(m => m.ExecutarAsync(
                    It.IsAny<AncorarPdfFilaItem>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((AncorarPdfFilaItem item, CancellationToken _) =>
                {
                    processados.Add(item.CicloId);
                    return new AncorarPdfMotorResultado(true, AncorarPdfFilaFalhaCategoria.Nenhuma);
                });

            for (int i = 0; i < 5; i++)
                filaRepo.Enfileirar(CriarEntrada(tarefaId, clienteId, userId, $"ciclo-t02-{i:D2}"));

            var service = new AncorarPdfFilaExecucaoService(
                filaRepo, leaseRepo, motorMock.Object,
                timeProvider: TimeProvider.System,
                channelCapacity: 100,
                leaseDuracao: TimeSpan.FromMinutes(5),
                processoTimeout: TimeSpan.FromSeconds(5));
            service.Start(numeroDeworkers: 2);

            await Task.Delay(TimeSpan.FromSeconds(5));
            await service.StopAsync(TimeSpan.FromSeconds(3));

            processados.Should().HaveCount(5);
            processados.Distinct().Should().HaveCount(5, "sem duplicatas entre ciclos distintos");
        }
        finally { LimparDb(dbPath); }
    }

    [Fact]
    public async Task T03_Retry_io_exception_3x_sucede_na_4a()
    {
        var dbPath = DbPath("retry_io");
        var db = new SqliteDb(dbPath);
        try
        {
            var (userId, clienteId, tarefaId) = CriarTarefa(db);
            var filaRepo = new SqliteAncorarPdfExecucaoFilaRepository(db);
            var leaseRepo = new SqliteAncorarPdfExecucaoLeaseRepository(db);

            var chamadas = 0;
            var motorMock = new Mock<IAncorarPdfMotorExecucao>();
            motorMock.Setup(m => m.ExecutarAsync(
                    It.IsAny<AncorarPdfFilaItem>(), It.IsAny<CancellationToken>()))
                .Returns((AncorarPdfFilaItem _, CancellationToken _) =>
                {
                    chamadas++;
                    if (chamadas < 4)
                        throw new IOException("arquivo bloqueado simulado");
                    return Task.FromResult(
                        new AncorarPdfMotorResultado(true, AncorarPdfFilaFalhaCategoria.Nenhuma));
                });

            filaRepo.Enfileirar(CriarEntrada(tarefaId, clienteId, userId, "ciclo-t03-retry"));

            var service = new AncorarPdfFilaExecucaoService(
                filaRepo, leaseRepo, motorMock.Object,
                timeProvider: TimeProvider.System,
                channelCapacity: 100,
                leaseDuracao: TimeSpan.FromMinutes(5),
                processoTimeout: TimeSpan.FromSeconds(15),
                retryDelay: TimeSpan.FromMilliseconds(200));
            service.Start(numeroDeworkers: 1);

            // 3 retries x ~200ms base = ~600ms total + overhead → 5s é suficiente.
            await Task.Delay(TimeSpan.FromSeconds(5));
            await service.StopAsync(TimeSpan.FromSeconds(3));

            chamadas.Should().BeGreaterThanOrEqualTo(4,
                "Polly deve ter retentado >=3x antes de suceder na 4ª chamada");
        }
        finally { LimparDb(dbPath); }
    }

    [Fact]
    public async Task T04_Timeout_polly_retorna_falhou_tecnico()
    {
        var dbPath = DbPath("timeout");
        var db = new SqliteDb(dbPath);
        try
        {
            var (userId, clienteId, tarefaId) = CriarTarefa(db);
            var filaRepo = new SqliteAncorarPdfExecucaoFilaRepository(db);
            var leaseRepo = new SqliteAncorarPdfExecucaoLeaseRepository(db);

            // Motor que nunca termina (simula operação travada).
            var motorMock = new Mock<IAncorarPdfMotorExecucao>();
            motorMock.Setup(m => m.ExecutarAsync(
                    It.IsAny<AncorarPdfFilaItem>(), It.IsAny<CancellationToken>()))
                .Returns(async (AncorarPdfFilaItem _, CancellationToken ct) =>
                {
                    await Task.Delay(TimeSpan.FromMinutes(10), ct);
                    return new AncorarPdfMotorResultado(true, AncorarPdfFilaFalhaCategoria.Nenhuma);
                });

            filaRepo.Enfileirar(CriarEntrada(tarefaId, clienteId, userId, "ciclo-t04-timeout"));

            // processoTimeout de 2s provocará timeout do worker.
            var service = new AncorarPdfFilaExecucaoService(
                filaRepo, leaseRepo, motorMock.Object,
                timeProvider: TimeProvider.System,
                channelCapacity: 100,
                leaseDuracao: TimeSpan.FromMinutes(5),
                processoTimeout: TimeSpan.FromSeconds(2));
            service.Start(numeroDeworkers: 1);

            await Task.Delay(TimeSpan.FromSeconds(8));
            await service.StopAsync(TimeSpan.FromSeconds(3));

            // Item deve ter status Falhou no banco.
            var itens = filaRepo.Listar(new AncorarPdfFilaFiltro(
                TarefaId: tarefaId,
                Status: AncorarPdfFilaStatus.Falhou));

            itens.Should().NotBeEmpty("timeout deve resultar em item com status Falhou");
        }
        finally { LimparDb(dbPath); }
    }
}
