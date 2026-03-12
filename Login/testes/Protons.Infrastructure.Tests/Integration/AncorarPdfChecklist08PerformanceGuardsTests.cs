using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
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

namespace Protons.Infrastructure.Tests.Integration;

[Trait("Checklist", "C8")]
public sealed class AncorarPdfChecklist08PerformanceGuardsTests
{
    [Fact]
    [Trait("Category", "C8_G2_Guards")]
    [Trait("Category", "C8_P1")]
    public async Task C8_P1_Backpressure_deve_esperar_sem_descartar_itens()
    {
        var dbPath = DbPath("p1_backpressure");
        var db = new SqliteDb(dbPath);
        var bloqueio = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

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
                    await bloqueio.Task.WaitAsync(ct);
                    return new AncorarPdfMotorResultado(true, AncorarPdfFilaFalhaCategoria.Nenhuma);
                });

            var service = new AncorarPdfFilaExecucaoService(
                filaRepo,
                leaseRepo,
                motorMock.Object,
                timeProvider: TimeProvider.System,
                channelCapacity: 3,
                leaseDuracao: TimeSpan.FromMinutes(5),
                processoTimeout: TimeSpan.FromSeconds(30),
                retryDelay: TimeSpan.FromMilliseconds(100));
            service.Start(numeroDeworkers: 1);

            var enfileiramentos = new List<Task<AncorarPdfFilaEnfileirarResultado>>();
            for (int i = 0; i < 8; i++)
            {
                enfileiramentos.Add(service.EnfileirarAsync(CriarEntrada(
                    tarefaId,
                    clienteId,
                    cicloId: $"c8-p1-{i:D3}",
                    prioridade: 3)));
            }

            await Task.Delay(400);
            bloqueio.TrySetResult(true);
            await Task.WhenAll(enfileiramentos.Select(t => t.WaitAsync(TimeSpan.FromSeconds(20))));
            await service.StopAsync(TimeSpan.FromSeconds(5));

            var total = filaRepo.Listar(new AncorarPdfFilaFiltro(TarefaId: tarefaId, Limite: 200)).Count;
            total.Should().Be(8, "back-pressure deve aplicar espera sem perda de itens");
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    [Fact]
    [Trait("Category", "C8_G2_Guards")]
    [Trait("Category", "C8_P2")]
    public async Task C8_P2_Lease_expirado_deve_ser_recuperado_sem_duplicidade()
    {
        var dbPath = DbPath("p2_lease_recovery");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();
            var tarefaId = CriarTarefa(db, out var clienteId, out _);
            var filaRepo = new SqliteAncorarPdfExecucaoFilaRepository(db);
            var leaseRepo = new SqliteAncorarPdfExecucaoLeaseRepository(db);

            var item = CriarFilaItem(tarefaId, clienteId, "c8-p2-ciclo");
            filaRepo.Enfileirar(item);

            var lease = leaseRepo.TentarAcquirir(
                item.FilaItemId,
                tarefaId,
                clienteId,
                "worker-expirado",
                TimeSpan.FromMilliseconds(-1));
            lease.Should().NotBeNull();

            var expirados = leaseRepo.ListarExpirados(DateTime.UtcNow.AddSeconds(1), limite: 10);
            expirados.Should().Contain(x => x.LeaseId == lease!.LeaseId);

            leaseRepo.Liberar(lease.LeaseId, "expirado_recovery_teste");
            var reidratados = filaRepo.ListarParaReidratar(limite: 10);
            reidratados.Should().Contain(x => x.FilaItemId == item.FilaItemId);

            var processados = new ConcurrentBag<string>();
            var motorMock = new Mock<IAncorarPdfMotorExecucao>();
            motorMock.Setup(m => m.ExecutarAsync(It.IsAny<AncorarPdfFilaItem>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((AncorarPdfFilaItem filaItem, CancellationToken _) =>
                {
                    processados.Add(filaItem.FilaItemId);
                    return new AncorarPdfMotorResultado(true, AncorarPdfFilaFalhaCategoria.Nenhuma);
                });

            var service = new AncorarPdfFilaExecucaoService(
                filaRepo,
                leaseRepo,
                motorMock.Object,
                timeProvider: TimeProvider.System,
                channelCapacity: 10,
                leaseDuracao: TimeSpan.FromSeconds(30),
                processoTimeout: TimeSpan.FromSeconds(10),
                retryDelay: TimeSpan.FromMilliseconds(100));
            service.Start(numeroDeworkers: 1);

            await AguardarCondicaoAsync(() =>
                    filaRepo.Listar(new AncorarPdfFilaFiltro(TarefaId: tarefaId, Status: AncorarPdfFilaStatus.Concluido, Limite: 10)).Count == 1,
                timeout: TimeSpan.FromSeconds(8));

            await service.StopAsync(TimeSpan.FromSeconds(5));
            processados.Distinct().Should().HaveCount(1, "item recuperado deve ser processado uma unica vez");
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    [Fact]
    [Trait("Category", "C8_G2_Guards")]
    [Trait("Category", "C8_P3")]
    public async Task C8_P3_Burst_deve_terminar_em_janela_deterministica_ampla()
    {
        var dbPath = DbPath("p3_burst");
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

            var service = new AncorarPdfFilaExecucaoService(
                filaRepo,
                leaseRepo,
                motorMock.Object,
                timeProvider: TimeProvider.System,
                channelCapacity: 100,
                leaseDuracao: TimeSpan.FromMinutes(5),
                processoTimeout: TimeSpan.FromSeconds(15),
                retryDelay: TimeSpan.FromMilliseconds(100));
            service.Start(numeroDeworkers: 2);

            const int totalItens = 30;
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < totalItens; i++)
            {
                await service.EnfileirarAsync(CriarEntrada(
                    tarefaId,
                    clienteId,
                    cicloId: $"c8-p3-{i:D3}",
                    prioridade: (i % 5) + 1));
            }

            await AguardarCondicaoAsync(() =>
                    filaRepo.Listar(new AncorarPdfFilaFiltro(
                        TarefaId: tarefaId,
                        Status: AncorarPdfFilaStatus.Concluido,
                        Limite: 200)).Count == totalItens,
                timeout: TimeSpan.FromSeconds(20));
            sw.Stop();

            await service.StopAsync(TimeSpan.FromSeconds(5));

            sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(20),
                "burst deve permanecer em janela ampla e deterministica");

            var falhas = filaRepo.Listar(new AncorarPdfFilaFiltro(
                TarefaId: tarefaId,
                Status: AncorarPdfFilaStatus.Falhou,
                Limite: 20));
            falhas.Should().BeEmpty();
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    [Fact]
    [Trait("Category", "C8_G2_Guards")]
    [Trait("Category", "C8_P4")]
    public async Task C8_P4_Restart_com_rehydrate_deve_manter_consistencia()
    {
        var dbPath = DbPath("p4_restart");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();
            var tarefaId = CriarTarefa(db, out var clienteId, out _);
            var filaRepo = new SqliteAncorarPdfExecucaoFilaRepository(db);
            var leaseRepo = new SqliteAncorarPdfExecucaoLeaseRepository(db);

            const int totalItens = 8;
            for (int i = 0; i < totalItens; i++)
                filaRepo.Enfileirar(CriarFilaItem(tarefaId, clienteId, $"c8-p4-{i:D3}"));

            var processadosPrimeira = new ConcurrentBag<string>();
            var motorPrimeira = new Mock<IAncorarPdfMotorExecucao>();
            motorPrimeira.Setup(m => m.ExecutarAsync(It.IsAny<AncorarPdfFilaItem>(), It.IsAny<CancellationToken>()))
                .Returns(async (AncorarPdfFilaItem item, CancellationToken ct) =>
                {
                    await Task.Delay(50, ct);
                    processadosPrimeira.Add(item.FilaItemId);
                    return new AncorarPdfMotorResultado(true, AncorarPdfFilaFalhaCategoria.Nenhuma);
                });

            var servicePrimeira = new AncorarPdfFilaExecucaoService(
                filaRepo,
                leaseRepo,
                motorPrimeira.Object,
                timeProvider: TimeProvider.System,
                channelCapacity: 20,
                leaseDuracao: TimeSpan.FromSeconds(60),
                processoTimeout: TimeSpan.FromSeconds(30),
                retryDelay: TimeSpan.FromMilliseconds(100));

            servicePrimeira.Start(numeroDeworkers: 2);
            await Task.Delay(180);
            await servicePrimeira.StopAsync(TimeSpan.FromSeconds(5));

            var processadosSegunda = new ConcurrentBag<string>();
            var motorSegunda = new Mock<IAncorarPdfMotorExecucao>();
            motorSegunda.Setup(m => m.ExecutarAsync(It.IsAny<AncorarPdfFilaItem>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((AncorarPdfFilaItem item, CancellationToken _) =>
                {
                    processadosSegunda.Add(item.FilaItemId);
                    return new AncorarPdfMotorResultado(true, AncorarPdfFilaFalhaCategoria.Nenhuma);
                });

            var serviceSegunda = new AncorarPdfFilaExecucaoService(
                filaRepo,
                leaseRepo,
                motorSegunda.Object,
                timeProvider: TimeProvider.System,
                channelCapacity: 20,
                leaseDuracao: TimeSpan.FromSeconds(60),
                processoTimeout: TimeSpan.FromSeconds(30),
                retryDelay: TimeSpan.FromMilliseconds(100));

            serviceSegunda.Start(numeroDeworkers: 2);
            await AguardarCondicaoAsync(() =>
                    filaRepo.Listar(new AncorarPdfFilaFiltro(TarefaId: tarefaId, Limite: 200))
                        .All(x => x.Status is AncorarPdfFilaStatus.Concluido
                            or AncorarPdfFilaStatus.Cancelado
                            or AncorarPdfFilaStatus.Falhou),
                timeout: TimeSpan.FromSeconds(20));
            await serviceSegunda.StopAsync(TimeSpan.FromSeconds(5));

            var totalConcluidos = filaRepo.Listar(new AncorarPdfFilaFiltro(
                TarefaId: tarefaId,
                Status: AncorarPdfFilaStatus.Concluido,
                Limite: 200)).Count;
            var totalCancelados = filaRepo.Listar(new AncorarPdfFilaFiltro(
                TarefaId: tarefaId,
                Status: AncorarPdfFilaStatus.Cancelado,
                Limite: 200)).Count;
            var totalFalhas = filaRepo.Listar(new AncorarPdfFilaFiltro(
                TarefaId: tarefaId,
                Status: AncorarPdfFilaStatus.Falhou,
                Limite: 200)).Count;

            totalFalhas.Should().Be(0, "restart/rehydrate nao pode introduzir falhas espurias");
            (totalConcluidos + totalCancelados).Should().Be(totalItens,
                "todos os itens devem chegar a estado terminal consistente apos restart");

            var idsUnicos = processadosPrimeira.Concat(processadosSegunda).Distinct().Count();
            idsUnicos.Should().Be(totalConcluidos, "restart nao deve introduzir duplicidade de processamento");
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    [Fact]
    [Trait("Category", "C8_G2_Guards")]
    [Trait("Category", "C8_P5")]
    public async Task C8_P5_Timeout_deve_resultar_em_falha_tecnica()
    {
        var dbPath = DbPath("p5_timeout");
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

            var service = new AncorarPdfFilaExecucaoService(
                filaRepo,
                leaseRepo,
                motorMock.Object,
                timeProvider: TimeProvider.System,
                channelCapacity: 10,
                leaseDuracao: TimeSpan.FromSeconds(30),
                processoTimeout: TimeSpan.FromSeconds(1),
                retryDelay: TimeSpan.FromMilliseconds(100));
            service.Start(numeroDeworkers: 1);

            await service.EnfileirarAsync(CriarEntrada(tarefaId, clienteId, "c8-p5-timeout", 3));
            await AguardarCondicaoAsync(() =>
                    filaRepo.Listar(new AncorarPdfFilaFiltro(
                        TarefaId: tarefaId,
                        Status: AncorarPdfFilaStatus.Falhou,
                        Limite: 10)).Count == 1,
                timeout: TimeSpan.FromSeconds(12));
            await service.StopAsync(TimeSpan.FromSeconds(5));

            var falha = filaRepo.Listar(new AncorarPdfFilaFiltro(
                TarefaId: tarefaId,
                Status: AncorarPdfFilaStatus.Falhou,
                Limite: 10)).Single();
            falha.CategoriaFalha.Should().Be(AncorarPdfFilaFalhaCategoria.Tecnica);
            falha.ErroCodigo.Should().Be("timeout");
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    [Fact]
    [Trait("Category", "C8_G2_Guards")]
    [Trait("Category", "C8_P6")]
    public async Task C8_P6_Retry_tecnico_deve_recuperar_sem_falha_final()
    {
        var dbPath = DbPath("p6_retry");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();
            var tarefaId = CriarTarefa(db, out var clienteId, out _);
            var filaRepo = new SqliteAncorarPdfExecucaoFilaRepository(db);
            var leaseRepo = new SqliteAncorarPdfExecucaoLeaseRepository(db);

            var chamadas = 0;
            var motorMock = new Mock<IAncorarPdfMotorExecucao>();
            motorMock.Setup(m => m.ExecutarAsync(It.IsAny<AncorarPdfFilaItem>(), It.IsAny<CancellationToken>()))
                .Returns((AncorarPdfFilaItem _, CancellationToken _) =>
                {
                    chamadas++;
                    if (chamadas < 3)
                        throw new IOException("falha tecnica simulada");

                    return Task.FromResult(new AncorarPdfMotorResultado(true, AncorarPdfFilaFalhaCategoria.Nenhuma));
                });

            var service = new AncorarPdfFilaExecucaoService(
                filaRepo,
                leaseRepo,
                motorMock.Object,
                timeProvider: TimeProvider.System,
                channelCapacity: 10,
                leaseDuracao: TimeSpan.FromSeconds(60),
                processoTimeout: TimeSpan.FromSeconds(10),
                retryDelay: TimeSpan.FromMilliseconds(100));
            service.Start(numeroDeworkers: 1);

            await service.EnfileirarAsync(CriarEntrada(tarefaId, clienteId, "c8-p6-retry", 3));
            await AguardarCondicaoAsync(() =>
                    filaRepo.Listar(new AncorarPdfFilaFiltro(
                        TarefaId: tarefaId,
                        Status: AncorarPdfFilaStatus.Concluido,
                        Limite: 10)).Count == 1,
                timeout: TimeSpan.FromSeconds(10));
            await service.StopAsync(TimeSpan.FromSeconds(5));

            chamadas.Should().BeGreaterThanOrEqualTo(3);
            var falhas = filaRepo.Listar(new AncorarPdfFilaFiltro(
                TarefaId: tarefaId,
                Status: AncorarPdfFilaStatus.Falhou,
                Limite: 10));
            falhas.Should().BeEmpty();
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    [Fact]
    [Trait("Category", "C8_G2_Guards")]
    [Trait("Category", "C8_P7")]
    public async Task C8_P7_MetricaProcessLatency_deve_ser_emitida_em_sucesso()
    {
        var dbPath = DbPath("p7_metric_success");
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

            var service = new AncorarPdfFilaExecucaoService(
                filaRepo,
                leaseRepo,
                motorMock.Object,
                timeProvider: TimeProvider.System,
                channelCapacity: 10,
                leaseDuracao: TimeSpan.FromSeconds(60),
                processoTimeout: TimeSpan.FromSeconds(10),
                retryDelay: TimeSpan.FromMilliseconds(100));

            var medicoes = await CapturarMetricasLatenciaProcessamentoAsync(async () =>
            {
                service.Start(numeroDeworkers: 1);
                await service.EnfileirarAsync(CriarEntrada(tarefaId, clienteId, "c8-p7-success", 3));
                await AguardarCondicaoAsync(() =>
                        filaRepo.Listar(new AncorarPdfFilaFiltro(
                            TarefaId: tarefaId,
                            Status: AncorarPdfFilaStatus.Concluido,
                            Limite: 10)).Count == 1,
                    timeout: TimeSpan.FromSeconds(8));
                await service.StopAsync(TimeSpan.FromSeconds(5));
            });

            medicoes.Should().Contain(m =>
                    string.Equals(m.Resultado, "sucesso", StringComparison.Ordinal)
                    && string.Equals(m.Categoria, "nenhuma", StringComparison.Ordinal)
                    && m.ValorMs >= 0,
                "process_file_latency deve registrar sucesso com tags de baixa cardinalidade");
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    [Fact]
    [Trait("Category", "C8_G2_Guards")]
    [Trait("Category", "C8_P8")]
    public async Task C8_P8_MetricaProcessLatency_deve_ser_emitida_em_falha_tecnica()
    {
        var dbPath = DbPath("p8_metric_tech_fail");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();
            var tarefaId = CriarTarefa(db, out var clienteId, out _);
            var filaRepo = new SqliteAncorarPdfExecucaoFilaRepository(db);
            var leaseRepo = new SqliteAncorarPdfExecucaoLeaseRepository(db);

            var motorMock = new Mock<IAncorarPdfMotorExecucao>();
            motorMock.Setup(m => m.ExecutarAsync(It.IsAny<AncorarPdfFilaItem>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new IOException("falha técnica simulada"));

            var service = new AncorarPdfFilaExecucaoService(
                filaRepo,
                leaseRepo,
                motorMock.Object,
                timeProvider: TimeProvider.System,
                channelCapacity: 10,
                leaseDuracao: TimeSpan.FromSeconds(60),
                processoTimeout: TimeSpan.FromSeconds(10),
                retryDelay: TimeSpan.FromMilliseconds(50));

            var medicoes = await CapturarMetricasLatenciaProcessamentoAsync(async () =>
            {
                service.Start(numeroDeworkers: 1);
                await service.EnfileirarAsync(CriarEntrada(tarefaId, clienteId, "c8-p8-tech-fail", 3));
                await AguardarCondicaoAsync(() =>
                        filaRepo.Listar(new AncorarPdfFilaFiltro(
                            TarefaId: tarefaId,
                            Status: AncorarPdfFilaStatus.Falhou,
                            Limite: 10)).Count == 1,
                    timeout: TimeSpan.FromSeconds(12));
                await service.StopAsync(TimeSpan.FromSeconds(5));
            });

            medicoes.Should().Contain(m =>
                    string.Equals(m.Resultado, "falha_tecnica", StringComparison.Ordinal)
                    && string.Equals(m.Categoria, "tecnica", StringComparison.Ordinal)
                    && m.ValorMs >= 0,
                "process_file_latency deve registrar falha técnica com categoria técnica");
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    [Fact]
    [Trait("Category", "C8_G2_Guards")]
    [Trait("Category", "C8_P9")]
    public async Task C8_P9_MetricaProcessLatency_deve_ser_emitida_em_falha_negocio()
    {
        var dbPath = DbPath("p9_metric_business_fail");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();
            var tarefaId = CriarTarefa(db, out var clienteId, out _);
            var filaRepo = new SqliteAncorarPdfExecucaoFilaRepository(db);
            var leaseRepo = new SqliteAncorarPdfExecucaoLeaseRepository(db);

            var motorMock = new Mock<IAncorarPdfMotorExecucao>();
            motorMock.Setup(m => m.ExecutarAsync(It.IsAny<AncorarPdfFilaItem>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AncorarPdfMotorResultado(
                    false,
                    AncorarPdfFilaFalhaCategoria.Negocio,
                    ErroCodigo: "negocio_teste",
                    ErroDetalhe: "falha de negócio simulada"));

            var service = new AncorarPdfFilaExecucaoService(
                filaRepo,
                leaseRepo,
                motorMock.Object,
                timeProvider: TimeProvider.System,
                channelCapacity: 10,
                leaseDuracao: TimeSpan.FromSeconds(60),
                processoTimeout: TimeSpan.FromSeconds(10),
                retryDelay: TimeSpan.FromMilliseconds(50));

            var medicoes = await CapturarMetricasLatenciaProcessamentoAsync(async () =>
            {
                service.Start(numeroDeworkers: 1);
                await service.EnfileirarAsync(CriarEntrada(tarefaId, clienteId, "c8-p9-business-fail", 3));
                await AguardarCondicaoAsync(() =>
                        filaRepo.Listar(new AncorarPdfFilaFiltro(
                            TarefaId: tarefaId,
                            Status: AncorarPdfFilaStatus.Falhou,
                            Limite: 10)).Count == 1,
                    timeout: TimeSpan.FromSeconds(10));
                await service.StopAsync(TimeSpan.FromSeconds(5));
            });

            medicoes.Should().Contain(m =>
                    string.Equals(m.Resultado, "falha_negocio", StringComparison.Ordinal)
                    && string.Equals(m.Categoria, "negocio", StringComparison.Ordinal)
                    && m.ValorMs >= 0,
                "process_file_latency deve registrar falha de negócio com categoria negócio");
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    [Fact]
    [Trait("Category", "C8_G2_Guards")]
    [Trait("Category", "C8_P10")]
    public async Task C8_P10_MetricaProcessLatency_deve_ser_emitida_em_cancelamento()
    {
        var dbPath = DbPath("p10_metric_cancel");
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
                    await Task.Delay(TimeSpan.FromSeconds(30), ct);
                    return new AncorarPdfMotorResultado(true, AncorarPdfFilaFalhaCategoria.Nenhuma);
                });

            var service = new AncorarPdfFilaExecucaoService(
                filaRepo,
                leaseRepo,
                motorMock.Object,
                timeProvider: TimeProvider.System,
                channelCapacity: 10,
                leaseDuracao: TimeSpan.FromSeconds(60),
                processoTimeout: TimeSpan.FromSeconds(60),
                retryDelay: TimeSpan.FromMilliseconds(50));

            var medicoes = await CapturarMetricasLatenciaProcessamentoAsync(async () =>
            {
                service.Start(numeroDeworkers: 1);
                await service.EnfileirarAsync(CriarEntrada(tarefaId, clienteId, "c8-p10-cancel", 3));
                await AguardarCondicaoAsync(() =>
                        filaRepo.Listar(new AncorarPdfFilaFiltro(
                            TarefaId: tarefaId,
                            Status: AncorarPdfFilaStatus.EmProcessamento,
                            Limite: 10)).Count == 1,
                    timeout: TimeSpan.FromSeconds(8));
                await service.StopAsync(TimeSpan.FromSeconds(5));
            });

            var cancelados = filaRepo.Listar(new AncorarPdfFilaFiltro(
                TarefaId: tarefaId,
                Status: AncorarPdfFilaStatus.Cancelado,
                Limite: 10));
            cancelados.Should().HaveCount(1, "cancelamento de host deve marcar item como cancelado");

            medicoes.Should().Contain(m =>
                    string.Equals(m.Resultado, "cancelado", StringComparison.Ordinal)
                    && string.Equals(m.Categoria, "nenhuma", StringComparison.Ordinal)
                    && m.ValorMs >= 0,
                "process_file_latency deve registrar cancelamento sem categoria de falha");
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    [Fact]
    [Trait("Category", "C8_F1")]
    public async Task C8_F1_Load_burst_deve_concluir_lote_maior_sem_erros()
    {
        var dbPath = DbPath("f1_load_burst");
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

            var service = new AncorarPdfFilaExecucaoService(
                filaRepo,
                leaseRepo,
                motorMock.Object,
                timeProvider: TimeProvider.System,
                channelCapacity: 200,
                leaseDuracao: TimeSpan.FromSeconds(120),
                processoTimeout: TimeSpan.FromSeconds(20),
                retryDelay: TimeSpan.FromMilliseconds(100));
            service.Start(numeroDeworkers: 4);

            const int totalItens = 80;
            var sw = Stopwatch.StartNew();
            var enfileiramentos = Enumerable.Range(0, totalItens)
                .Select(i => service.EnfileirarAsync(CriarEntrada(
                    tarefaId,
                    clienteId,
                    cicloId: $"c8-f1-{i:D4}",
                    prioridade: (i % 5) + 1)))
                .ToArray();
            await Task.WhenAll(enfileiramentos.Select(t => t.WaitAsync(TimeSpan.FromSeconds(20))));

            await AguardarCondicaoAsync(() =>
                    filaRepo.Listar(new AncorarPdfFilaFiltro(
                        TarefaId: tarefaId,
                        Status: AncorarPdfFilaStatus.Concluido,
                        Limite: 500)).Count == totalItens,
                timeout: TimeSpan.FromSeconds(30));
            sw.Stop();
            await service.StopAsync(TimeSpan.FromSeconds(5));

            var throughput = totalItens / Math.Max(sw.Elapsed.TotalSeconds, 1);
            throughput.Should().BeGreaterThan(2, "burst full deve manter throughput minimo operacional");
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    [Fact]
    [Trait("Category", "C8_F2")]
    public async Task C8_F2_Load_sustained_deve_manter_estabilidade()
    {
        var dbPath = DbPath("f2_load_sustained");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();
            var tarefaId = CriarTarefa(db, out var clienteId, out _);
            var filaRepo = new SqliteAncorarPdfExecucaoFilaRepository(db);
            var leaseRepo = new SqliteAncorarPdfExecucaoLeaseRepository(db);

            var processados = new ConcurrentBag<string>();
            var motorMock = new Mock<IAncorarPdfMotorExecucao>();
            motorMock.Setup(m => m.ExecutarAsync(It.IsAny<AncorarPdfFilaItem>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((AncorarPdfFilaItem item, CancellationToken _) =>
                {
                    processados.Add(item.CicloId);
                    return new AncorarPdfMotorResultado(true, AncorarPdfFilaFalhaCategoria.Nenhuma);
                });

            var service = new AncorarPdfFilaExecucaoService(
                filaRepo,
                leaseRepo,
                motorMock.Object,
                timeProvider: TimeProvider.System,
                channelCapacity: 300,
                leaseDuracao: TimeSpan.FromSeconds(120),
                processoTimeout: TimeSpan.FromSeconds(20),
                retryDelay: TimeSpan.FromMilliseconds(100));
            service.Start(numeroDeworkers: 3);

            const int batches = 4;
            const int batchSize = 20;
            for (int batch = 0; batch < batches; batch++)
            {
                for (int i = 0; i < batchSize; i++)
                {
                    await service.EnfileirarAsync(CriarEntrada(
                        tarefaId,
                        clienteId,
                        cicloId: $"c8-f2-b{batch:D2}-{i:D3}",
                        prioridade: 3));
                }

                await Task.Delay(100);
            }

            var totalEsperado = batches * batchSize;
            await AguardarCondicaoAsync(() =>
                    filaRepo.Listar(new AncorarPdfFilaFiltro(
                        TarefaId: tarefaId,
                        Status: AncorarPdfFilaStatus.Concluido,
                        Limite: 600)).Count == totalEsperado,
                timeout: TimeSpan.FromSeconds(35));
            await service.StopAsync(TimeSpan.FromSeconds(5));

            processados.Distinct().Should().HaveCount(totalEsperado, "load sustentado nao deve duplicar ciclos");
            filaRepo.Listar(new AncorarPdfFilaFiltro(
                TarefaId: tarefaId,
                Status: AncorarPdfFilaStatus.Falhou,
                Limite: 50)).Should().BeEmpty();
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    [Fact]
    [Trait("Category", "C8_F3")]
    public async Task C8_F3_Recovery_restart_deve_fechar_todos_os_itens_sem_duplicidade()
    {
        var dbPath = DbPath("f3_restart_recovery");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();
            var tarefaId = CriarTarefa(db, out var clienteId, out _);
            var filaRepo = new SqliteAncorarPdfExecucaoFilaRepository(db);
            var leaseRepo = new SqliteAncorarPdfExecucaoLeaseRepository(db);

            const int totalItens = 25;
            for (int i = 0; i < totalItens; i++)
                filaRepo.Enfileirar(CriarFilaItem(tarefaId, clienteId, $"c8-f3-{i:D4}"));

            var processados = new ConcurrentBag<string>();
            var motorLento = new Mock<IAncorarPdfMotorExecucao>();
            motorLento.Setup(m => m.ExecutarAsync(It.IsAny<AncorarPdfFilaItem>(), It.IsAny<CancellationToken>()))
                .Returns(async (AncorarPdfFilaItem item, CancellationToken ct) =>
                {
                    await Task.Delay(40, ct);
                    processados.Add(item.FilaItemId);
                    return new AncorarPdfMotorResultado(true, AncorarPdfFilaFalhaCategoria.Nenhuma);
                });

            var service1 = new AncorarPdfFilaExecucaoService(
                filaRepo,
                leaseRepo,
                motorLento.Object,
                timeProvider: TimeProvider.System,
                channelCapacity: 100,
                leaseDuracao: TimeSpan.FromSeconds(120),
                processoTimeout: TimeSpan.FromSeconds(20),
                retryDelay: TimeSpan.FromMilliseconds(100));
            service1.Start(numeroDeworkers: 2);
            await Task.Delay(250);
            await service1.StopAsync(TimeSpan.FromSeconds(5));

            var motorRapido = new Mock<IAncorarPdfMotorExecucao>();
            motorRapido.Setup(m => m.ExecutarAsync(It.IsAny<AncorarPdfFilaItem>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((AncorarPdfFilaItem item, CancellationToken _) =>
                {
                    processados.Add(item.FilaItemId);
                    return new AncorarPdfMotorResultado(true, AncorarPdfFilaFalhaCategoria.Nenhuma);
                });

            var service2 = new AncorarPdfFilaExecucaoService(
                filaRepo,
                leaseRepo,
                motorRapido.Object,
                timeProvider: TimeProvider.System,
                channelCapacity: 100,
                leaseDuracao: TimeSpan.FromSeconds(120),
                processoTimeout: TimeSpan.FromSeconds(20),
                retryDelay: TimeSpan.FromMilliseconds(100));
            service2.Start(numeroDeworkers: 3);

            await AguardarCondicaoAsync(() =>
                    filaRepo.Listar(new AncorarPdfFilaFiltro(TarefaId: tarefaId, Limite: 600))
                        .All(x => x.Status is AncorarPdfFilaStatus.Concluido
                            or AncorarPdfFilaStatus.Cancelado
                            or AncorarPdfFilaStatus.Falhou),
                timeout: TimeSpan.FromSeconds(40));
            await service2.StopAsync(TimeSpan.FromSeconds(5));

            var concluidos = filaRepo.Listar(new AncorarPdfFilaFiltro(
                TarefaId: tarefaId,
                Status: AncorarPdfFilaStatus.Concluido,
                Limite: 600)).Count;
            var cancelados = filaRepo.Listar(new AncorarPdfFilaFiltro(
                TarefaId: tarefaId,
                Status: AncorarPdfFilaStatus.Cancelado,
                Limite: 600)).Count;
            var falhas = filaRepo.Listar(new AncorarPdfFilaFiltro(
                TarefaId: tarefaId,
                Status: AncorarPdfFilaStatus.Falhou,
                Limite: 600)).Count;

            falhas.Should().Be(0, "recovery de restart nao deve terminar com falha");
            (concluidos + cancelados).Should().Be(totalItens,
                "todos os itens devem chegar em estado terminal apos restart");
            processados.Distinct().Should().HaveCount(concluidos,
                "nao pode haver duplicidade de processamento entre ciclos de restart");
        }
        finally
        {
            LimparDb(dbPath);
        }
    }

    private static async Task AguardarCondicaoAsync(
        Func<bool> condicao,
        TimeSpan timeout,
        TimeSpan? intervalo = null)
    {
        var sw = Stopwatch.StartNew();
        var wait = intervalo ?? TimeSpan.FromMilliseconds(100);
        while (sw.Elapsed < timeout)
        {
            if (condicao())
                return;

            await Task.Delay(wait);
        }

        throw new TimeoutException($"Condicao nao satisfeita em {timeout.TotalSeconds:F1}s.");
    }

    private static string DbPath(string sufixo)
        => Path.Combine(Path.GetTempPath(), $"protons_c8_{sufixo}_{Guid.NewGuid():N}.db");

    private static void LimparDb(string dbPath)
    {
        if (File.Exists(dbPath))
            File.Delete(dbPath);
    }

    private static int CriarTarefa(SqliteDb db, out int clienteId, out int userId)
    {
        var userRepo = new UserRepository(db);
        var clienteRepo = new SqliteClienteRepository(db);
        var tarefaRepo = new SqliteTarefaRepository(db);

        userId = userRepo.Create(new User
        {
            Empresa = "Protons",
            Nome = $"Op C8 Perf {Guid.NewGuid():N}"[..20],
            Cpf = "11144477735",
            Cargo = "Operador",
            Email = $"perf.c8.{Guid.NewGuid():N}@protons.local",
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
            CodigoCliente = $"C8-{Guid.NewGuid():N}"[..14],
            Nome = "Cliente C8 Performance",
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
            Titulo = "AncorarPdf C8 Performance",
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

    private static AncorarPdfFilaEnfileirarEntrada CriarEntrada(
        int tarefaId,
        int clienteId,
        string cicloId,
        int prioridade)
        => new(
            TarefaId: tarefaId,
            ClienteId: clienteId,
            CicloId: cicloId,
            JanelaAlvoUtc: DateTime.UtcNow,
            PrioridadeExecucao: prioridade,
            Motivo: "scheduler_dispatch",
            EnfileiradoPorUserId: 0,
            EnfileiradoPorNome: "c8_test",
            CorrelationId: Guid.NewGuid().ToString("N"));

    private static AncorarPdfFilaItem CriarFilaItem(int tarefaId, int clienteId, string cicloId)
    {
        var now = DateTime.UtcNow;
        return new AncorarPdfFilaItem
        {
            FilaItemId = Guid.NewGuid().ToString("N"),
            TarefaId = tarefaId,
            ClienteId = clienteId,
            CicloId = cicloId,
            JanelaAlvoUtc = now,
            PrioridadeExecucao = 3,
            Status = AncorarPdfFilaStatus.Aguardando,
            Motivo = "restart_rehydrate",
            EnfileiradoPorUserId = 0,
            EnfileiradoPorNome = "c8_test",
            EnfileiradoEmUtc = now,
            TentativaAtual = 0,
            TentativasMaximas = 3,
            CorrelationId = Guid.NewGuid().ToString("N"),
            CriadoEmUtc = now
        };
    }

    private static async Task<IReadOnlyList<ProcessLatencyMeasurement>> CapturarMetricasLatenciaProcessamentoAsync(
        Func<Task> acao)
    {
        var medicoes = new ConcurrentBag<ProcessLatencyMeasurement>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, metricListener) =>
        {
            if (string.Equals(instrument.Meter.Name, "Protons.AncorarPdfFila", StringComparison.Ordinal)
                && string.Equals(instrument.Name, "ancorar_pdf.fila.process_file_latency_ms", StringComparison.Ordinal))
            {
                metricListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((_, valor, tags, _) =>
        {
            var resultado = ObterTag(tags, "resultado") ?? "indefinido";
            var categoria = ObterTag(tags, "categoria") ?? "indefinida";
            medicoes.Add(new ProcessLatencyMeasurement(valor, resultado, categoria));
        });
        listener.Start();

        await acao().ConfigureAwait(false);
        return medicoes.ToArray();
    }

    private static string? ObterTag(ReadOnlySpan<KeyValuePair<string, object?>> tags, string chave)
    {
        for (var i = 0; i < tags.Length; i++)
        {
            if (string.Equals(tags[i].Key, chave, StringComparison.Ordinal))
            {
                return tags[i].Value?.ToString();
            }
        }

        return null;
    }

    private readonly record struct ProcessLatencyMeasurement(double ValorMs, string Resultado, string Categoria);
}
