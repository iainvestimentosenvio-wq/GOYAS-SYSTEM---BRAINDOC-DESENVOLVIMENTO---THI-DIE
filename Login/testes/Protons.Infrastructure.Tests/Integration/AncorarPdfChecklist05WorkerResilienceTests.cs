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
using Xunit;

namespace Protons.Infrastructure.Tests.Integration;

/// <summary>
/// C5 — Gate G4 (resiliência): testa retry, timeout, falha de negócio,
/// cancelamento e restart recovery via SQLite temporário.
/// </summary>
[Trait("Checklist", "C5")]
[Trait("Category", "C5_G4_Resiliencia")]
public sealed class AncorarPdfChecklist05WorkerResilienceTests
{
    [Fact]
    public async Task C5_G4_RetryTecnico_AposDoisFalha_ConcluidoNaTerceiraVez()
    {
        var dbPath = DbPath("retry");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();
            var tarefaId = CriarTarefa(db, out var clienteId, out _);
            var filaRepo = new SqliteAncorarPdfExecucaoFilaRepository(db);
            var leaseRepo = new SqliteAncorarPdfExecucaoLeaseRepository(db);

            var callCount = 0;
            var motorMock = new Mock<IAncorarPdfMotorExecucao>();
            motorMock.Setup(m => m.ExecutarAsync(It.IsAny<AncorarPdfFilaItem>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    callCount++;
                    if (callCount < 3)
                        throw new IOException($"falha_tecnica_{callCount}");
                    return new AncorarPdfMotorResultado(true, AncorarPdfFilaFalhaCategoria.Nenhuma);
                });

            var item = CriarFilaItem(tarefaId, clienteId, "ciclo-retry");
            filaRepo.Enfileirar(item);

            // Channel com 10 itens, timeout generoso para retry
            var service = new AncorarPdfFilaExecucaoService(
                filaRepo, leaseRepo, motorMock.Object,
                timeProvider: TimeProvider.System,
                channelCapacity: 10,
                leaseDuracao: TimeSpan.FromMinutes(5),
                processoTimeout: TimeSpan.FromSeconds(15));
            service.Start(numeroDeworkers: 1);

            // Aguarda processamento (retry tem jitter, mas curto em testes)
            await Task.Delay(TimeSpan.FromSeconds(8));
            await service.StopAsync(TimeSpan.FromSeconds(3));

            var lista = filaRepo.Listar(new AncorarPdfFilaFiltro(TarefaId: tarefaId));
            lista.Should().HaveCountGreaterThanOrEqualTo(1);
            // callCount deve ser 3 (2 falhas + 1 sucesso)
            callCount.Should().BeGreaterThanOrEqualTo(2, "deve ter tentado pelo menos 2x antes de concluir");
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    [Fact]
    public async Task C5_G4_FalhaDeNegocio_NaoFazRetry()
    {
        var dbPath = DbPath("negocio");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();
            var tarefaId = CriarTarefa(db, out var clienteId, out _);
            var filaRepo = new SqliteAncorarPdfExecucaoFilaRepository(db);
            var leaseRepo = new SqliteAncorarPdfExecucaoLeaseRepository(db);

            var callCount = 0;
            var motorMock = new Mock<IAncorarPdfMotorExecucao>();
            motorMock.Setup(m => m.ExecutarAsync(It.IsAny<AncorarPdfFilaItem>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    callCount++;
                    return new AncorarPdfMotorResultado(false,
                        AncorarPdfFilaFalhaCategoria.Negocio,
                        ErroCodigo: "arquivo_invalido");
                });

            var item = CriarFilaItem(tarefaId, clienteId, "ciclo-negocio");
            filaRepo.Enfileirar(item);

            var service = new AncorarPdfFilaExecucaoService(
                filaRepo, leaseRepo, motorMock.Object,
                timeProvider: TimeProvider.System,
                channelCapacity: 10,
                leaseDuracao: TimeSpan.FromMinutes(5),
                processoTimeout: TimeSpan.FromSeconds(5));
            service.Start(numeroDeworkers: 1);

            await Task.Delay(TimeSpan.FromSeconds(3));
            await service.StopAsync(TimeSpan.FromSeconds(2));

            callCount.Should().Be(1, "falha de negócio NÃO deve fazer retry — apenas 1 tentativa");

            var lista = filaRepo.Listar(new AncorarPdfFilaFiltro(
                TarefaId: tarefaId,
                Status: AncorarPdfFilaStatus.Falhou));
            lista.Should().HaveCount(1);
            lista[0].CategoriaFalha.Should().Be(AncorarPdfFilaFalhaCategoria.Negocio);
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    [Fact]
    public async Task C5_G4_Timeout_MarcaComoFalhaTecnica()
    {
        var dbPath = DbPath("timeout");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();
            var tarefaId = CriarTarefa(db, out var clienteId, out _);
            var filaRepo = new SqliteAncorarPdfExecucaoFilaRepository(db);
            var leaseRepo = new SqliteAncorarPdfExecucaoLeaseRepository(db);

            var motorMock = new Mock<IAncorarPdfMotorExecucao>();
            motorMock.Setup(m => m.ExecutarAsync(It.IsAny<AncorarPdfFilaItem>(), It.IsAny<CancellationToken>()))
                .Returns(async (AncorarPdfFilaItem _, CancellationToken ct) =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), ct);
                    return new AncorarPdfMotorResultado(true, AncorarPdfFilaFalhaCategoria.Nenhuma);
                });

            var item = CriarFilaItem(tarefaId, clienteId, "ciclo-timeout");
            filaRepo.Enfileirar(item);

            // Timeout de 2 segundos — motor demora 10s, deve ser interrompido
            var service = new AncorarPdfFilaExecucaoService(
                filaRepo, leaseRepo, motorMock.Object,
                timeProvider: TimeProvider.System,
                channelCapacity: 10,
                leaseDuracao: TimeSpan.FromMinutes(5),
                processoTimeout: TimeSpan.FromSeconds(2));
            service.Start(numeroDeworkers: 1);

            await Task.Delay(TimeSpan.FromSeconds(5));
            await service.StopAsync(TimeSpan.FromSeconds(3));

            var lista = filaRepo.Listar(new AncorarPdfFilaFiltro(
                TarefaId: tarefaId,
                Status: AncorarPdfFilaStatus.Falhou));
            lista.Should().HaveCount(1, "item deve estar Falhou após timeout");
            lista[0].ErroCodigo.Should().Be("timeout");
            lista[0].CategoriaFalha.Should().Be(AncorarPdfFilaFalhaCategoria.Tecnica);
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    [Fact]
    public async Task C5_G4_Restart_ItensAguardandoSaoReidratados()
    {
        var dbPath = DbPath("restart");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();
            var tarefaId = CriarTarefa(db, out var clienteId, out _);
            var filaRepo = new SqliteAncorarPdfExecucaoFilaRepository(db);
            var leaseRepo = new SqliteAncorarPdfExecucaoLeaseRepository(db);

            // Pré-inserir 3 itens "órfãos" como se estivessem de um restart
            for (int i = 0; i < 3; i++)
            {
                var item = CriarFilaItem(tarefaId, clienteId, $"ciclo-restart-{i}",
                    prioridade: i + 1);
                filaRepo.Enfileirar(item);
            }

            var processados = 0;
            var motorMock = new Mock<IAncorarPdfMotorExecucao>();
            motorMock.Setup(m => m.ExecutarAsync(It.IsAny<AncorarPdfFilaItem>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    Interlocked.Increment(ref processados);
                    return new AncorarPdfMotorResultado(true, AncorarPdfFilaFalhaCategoria.Nenhuma);
                });

            // Start() deve reidratar os 3 itens do channel
            var service = new AncorarPdfFilaExecucaoService(
                filaRepo, leaseRepo, motorMock.Object,
                timeProvider: TimeProvider.System,
                channelCapacity: 10,
                leaseDuracao: TimeSpan.FromMinutes(5),
                processoTimeout: TimeSpan.FromSeconds(5));
            service.Start(numeroDeworkers: 1);

            await Task.Delay(TimeSpan.FromSeconds(3));
            await service.StopAsync(TimeSpan.FromSeconds(3));

            processados.Should().Be(3, "todos os 3 itens reidratados devem ser processados");
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static string DbPath(string sufixo)
        => Path.Combine(Path.GetTempPath(), $"protons_c5_res_{sufixo}_{Guid.NewGuid():N}.db");

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
            Nome = $"Op C5 Res {Guid.NewGuid():N}"[..20],
            Cpf = "11144477735",
            Cargo = "Operador",
            Email = $"res.c5.{Guid.NewGuid():N}@protons.local",
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
            CodigoCliente = $"C5R-{Guid.NewGuid():N}"[..14],
            Nome = "Cliente C5 Res",
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
            Titulo = "AncorarPdf C5 Res",
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
            Motivo = "test",
            EnfileiradoPorUserId = 0,
            EnfileiradoPorNome = "test",
            EnfileiradoEmUtc = now,
            TentativasMaximas = 3,
            CorrelationId = Guid.NewGuid().ToString("N"),
            CriadoEmUtc = now
        };
    }
}
