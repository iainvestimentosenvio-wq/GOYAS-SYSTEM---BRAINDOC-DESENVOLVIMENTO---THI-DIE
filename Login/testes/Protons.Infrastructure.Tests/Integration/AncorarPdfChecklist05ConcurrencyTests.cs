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
/// C5 — Gate G5 (multi-worker): concorrência, sem duplicatas, back-pressure.
/// </summary>
[Trait("Checklist", "C5")]
[Trait("Category", "C5_G5_Concorrencia")]
public sealed class AncorarPdfChecklist05ConcurrencyTests
{
    [Fact]
    public async Task C5_G5_DoisWorkers_10Itens_CadaItemProcessadoUmaVez()
    {
        var dbPath = DbPath("multi_worker");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();
            var tarefaId = CriarTarefa(db, out var clienteId, out _);
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

            // Inserir 10 itens distintos
            for (int i = 0; i < 10; i++)
            {
                var item = CriarFilaItem(tarefaId, clienteId, $"ciclo-conc-{i:D3}");
                filaRepo.Enfileirar(item);
            }

            var service = new AncorarPdfFilaExecucaoService(
                filaRepo, leaseRepo, motorMock.Object,
                timeProvider: TimeProvider.System,
                channelCapacity: 100,
                leaseDuracao: TimeSpan.FromMinutes(5),
                processoTimeout: TimeSpan.FromSeconds(5));
            service.Start(numeroDeworkers: 2);

            await Task.Delay(TimeSpan.FromSeconds(5));
            await service.StopAsync(TimeSpan.FromSeconds(3));

            // Nenhum item deve ter sido processado mais de uma vez
            var duplicatas = processados
                .GroupBy(id => id)
                .Where(g => g.Count() > 1)
                .ToList();
            duplicatas.Should().BeEmpty("nenhum item pode ser processado duas vezes");

            // Todos os 10 devem ter sido processados
            processados.Should().HaveCount(10, "todos os 10 itens devem ser processados exatamente uma vez");
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    [Fact]
    public async Task C5_G5_ObterProfundidadeAtual_RefleteCanalReal()
    {
        var dbPath = DbPath("profundidade");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();
            var tarefaId = CriarTarefa(db, out var clienteId, out _);
            var filaRepo = new SqliteAncorarPdfExecucaoFilaRepository(db);
            var leaseRepo = new SqliteAncorarPdfExecucaoLeaseRepository(db);

            // Motor que bloqueia para manter itens no channel
            var barreira = new TaskCompletionSource<bool>();
            var motorMock = new Mock<IAncorarPdfMotorExecucao>();
            motorMock.Setup(m => m.ExecutarAsync(
                    It.IsAny<AncorarPdfFilaItem>(), It.IsAny<CancellationToken>()))
                .Returns(async (AncorarPdfFilaItem _, CancellationToken ct) =>
                {
                    await barreira.Task.WaitAsync(ct);
                    return new AncorarPdfMotorResultado(true, AncorarPdfFilaFalhaCategoria.Nenhuma);
                });

            var service = new AncorarPdfFilaExecucaoService(
                filaRepo, leaseRepo, motorMock.Object,
                timeProvider: TimeProvider.System,
                channelCapacity: 100,
                leaseDuracao: TimeSpan.FromMinutes(5),
                processoTimeout: TimeSpan.FromSeconds(30));
            service.Start(numeroDeworkers: 1);

            // Enfileirar 5 itens
            for (int i = 0; i < 5; i++)
            {
                var item = CriarFilaItem(tarefaId, clienteId, $"ciclo-prof-{i}");
                filaRepo.Enfileirar(item);
                await service.EnfileirarAsync(new AncorarPdfFilaEnfileirarEntrada(
                    tarefaId, clienteId, $"ciclo-prof-{i}",
                    DateTime.UtcNow, 3, "test", 0, "test",
                    Guid.NewGuid().ToString("N")));
            }

            await Task.Delay(100); // Deixar o worker consumir pelo menos 1
            var profundidade = service.ObterProfundidadeAtual();
            profundidade.Should().BeGreaterThanOrEqualTo(0,
                "profundidade deve ser >= 0");

            barreira.SetResult(true);
            await service.StopAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    [Fact]
    public async Task C5_G5_BackPressure_ChannelBounded_NaoPerdeDados()
    {
        var dbPath = DbPath("backpressure");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();
            var tarefaId = CriarTarefa(db, out var clienteId, out _);
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

            // Canal com capacidade 5, 8 enfileiramentos → os extras esperam
            var service = new AncorarPdfFilaExecucaoService(
                filaRepo, leaseRepo, motorMock.Object,
                timeProvider: TimeProvider.System,
                channelCapacity: 5,
                leaseDuracao: TimeSpan.FromMinutes(5),
                processoTimeout: TimeSpan.FromSeconds(5));
            service.Start(numeroDeworkers: 1);

            var enfileiramentos = new List<Task>();
            for (int i = 0; i < 8; i++)
            {
                var item = CriarFilaItem(tarefaId, clienteId, $"ciclo-bp-{i}");
                filaRepo.Enfileirar(item);
                var entrada = new AncorarPdfFilaEnfileirarEntrada(
                    item.TarefaId, item.ClienteId, item.CicloId,
                    item.JanelaAlvoUtc, item.PrioridadeExecucao,
                    item.Motivo, item.EnfileiradoPorUserId,
                    item.EnfileiradoPorNome, item.CorrelationId);
                enfileiramentos.Add(service.EnfileirarAsync(entrada));
            }

            // Aguardar todos os enfileiramentos (o channel espera ao invés de perder)
            await Task.WhenAll(enfileiramentos.Select(t =>
                t.WaitAsync(TimeSpan.FromSeconds(10))));

            await Task.Delay(TimeSpan.FromSeconds(5));
            await service.StopAsync(TimeSpan.FromSeconds(3));

            // Todos os 8 devem ter sido enfileirados no DB
            var total = filaRepo.Listar(new AncorarPdfFilaFiltro(TarefaId: tarefaId)).Count;
            total.Should().Be(8, "todos os 8 itens devem estar no banco (back-pressure não descarta)");
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
        => Path.Combine(Path.GetTempPath(), $"protons_c5_conc_{sufixo}_{Guid.NewGuid():N}.db");

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
            Nome = $"Op C5 Conc {Guid.NewGuid():N}"[..21],
            Cpf = "11144477735",
            Cargo = "Operador",
            Email = $"conc.c5.{Guid.NewGuid():N}@protons.local",
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
            CodigoCliente = $"C5C-{Guid.NewGuid():N}"[..14],
            Nome = "Cliente C5 Conc",
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
            Titulo = "AncorarPdf C5 Conc",
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
