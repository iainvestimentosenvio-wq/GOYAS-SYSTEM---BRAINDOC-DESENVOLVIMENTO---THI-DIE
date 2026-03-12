using FluentAssertions;
using Protons.Core.Tarefas.Services;

namespace Protons.Core.Tests.Services.Tarefas;

[Trait("Checklist", "C8")]
[Trait("Category", "C8_G1_Contratos")]
public sealed class AncorarPdfChecklist08ContractTests
{
    [Fact]
    public void T01_Defaults_enterprise_devem_permanecer_estaveis()
    {
        AncorarPdfRuntimeTuningPolicy.DefaultWorkers.Should().Be(0);
        AncorarPdfRuntimeTuningPolicy.DefaultChannelCapacity.Should().Be(1000);
        AncorarPdfRuntimeTuningPolicy.DefaultSchedulerPollingMs.Should().Be(1000);
        AncorarPdfRuntimeTuningPolicy.DefaultSchedulerLimitPerTick.Should().Be(200);
        AncorarPdfRuntimeTuningPolicy.DefaultMisfireMinSeconds.Should().Be(30);
        AncorarPdfRuntimeTuningPolicy.DefaultWorkerTimeoutSeconds.Should().Be(30);
        AncorarPdfRuntimeTuningPolicy.DefaultLeaseDurationSeconds.Should().Be(300);
        AncorarPdfRuntimeTuningPolicy.DefaultRetryDelaySeconds.Should().Be(5);
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(0, 8, 8)]
    [InlineData(0, 128, 16)]
    [InlineData(0, -4, 1)]
    public void T02_Workers_zero_deve_operar_em_modo_auto(int configuredWorkers, int processorCount, int esperado)
    {
        var resolved = AncorarPdfRuntimeTuningPolicy.ResolveWorkers(configuredWorkers, processorCount);
        resolved.Should().Be(esperado);
    }

    [Theory]
    [InlineData(-10, 1)]
    [InlineData(1, 1)]
    [InlineData(16, 16)]
    [InlineData(99, 16)]
    public void T03_Workers_configurado_deve_ser_clampado_entre_1_e_16(int configuredWorkers, int esperado)
    {
        var resolved = AncorarPdfRuntimeTuningPolicy.ResolveWorkers(configuredWorkers, processorCount: 8);
        resolved.Should().Be(esperado);
    }

    [Theory]
    [InlineData(-1, 100)]
    [InlineData(100, 100)]
    [InlineData(1000, 1000)]
    [InlineData(50000, 20000)]
    public void T04_Channel_capacity_deve_respeitar_faixa_enterprise(int value, int esperado)
    {
        AncorarPdfRuntimeTuningPolicy.ResolveChannelCapacity(value).Should().Be(esperado);
    }

    [Theory]
    [InlineData(1, 250)]
    [InlineData(250, 250)]
    [InlineData(1000, 1000)]
    [InlineData(9999, 5000)]
    public void T05_Scheduler_polling_ms_deve_ser_clampado(int value, int esperado)
    {
        AncorarPdfRuntimeTuningPolicy.ResolveSchedulerPollingMs(value).Should().Be(esperado);
    }

    [Theory]
    [InlineData(1, 10)]
    [InlineData(10, 10)]
    [InlineData(200, 200)]
    [InlineData(9999, 2000)]
    public void T06_Scheduler_limit_por_tick_deve_ser_clampado(int value, int esperado)
    {
        AncorarPdfRuntimeTuningPolicy.ResolveSchedulerLimitPerTick(value).Should().Be(esperado);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(5, 5)]
    [InlineData(30, 30)]
    [InlineData(9999, 3600)]
    public void T07_Misfire_min_seconds_deve_ser_clampado(int value, int esperado)
    {
        AncorarPdfRuntimeTuningPolicy.ResolveMisfireMinSeconds(value).Should().Be(esperado);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(5, 5)]
    [InlineData(30, 30)]
    [InlineData(9999, 300)]
    public void T08_Timeout_worker_deve_ser_clampado(int value, int esperado)
    {
        AncorarPdfRuntimeTuningPolicy.ResolveWorkerTimeoutSeconds(value).Should().Be(esperado);
    }

    [Theory]
    [InlineData(0, 30)]
    [InlineData(30, 30)]
    [InlineData(300, 300)]
    [InlineData(9999, 3600)]
    public void T09_Lease_e_retry_devem_ser_clampados(int leaseValue, int leaseEsperado)
    {
        AncorarPdfRuntimeTuningPolicy.ResolveLeaseDurationSeconds(leaseValue).Should().Be(leaseEsperado);
        AncorarPdfRuntimeTuningPolicy.ResolveRetryDelaySeconds(leaseValue).Should().BeInRange(1, 120);
    }

    [Fact]
    public void T10_Resolve_agregado_deve_retorna_contrato_resolvido_completo()
    {
        var resolved = AncorarPdfRuntimeTuningPolicy.Resolve(
            configuredWorkers: 0,
            processorCount: 6,
            channelCapacity: 1000,
            schedulerPollingMs: 1000,
            schedulerLimitPerTick: 200,
            misfireMinSeconds: 30,
            workerTimeoutSeconds: 30,
            leaseDurationSeconds: 300,
            retryDelaySeconds: 5);

        resolved.Workers.Should().Be(6);
        resolved.ChannelCapacity.Should().Be(1000);
        resolved.SchedulerPollingMs.Should().Be(1000);
        resolved.SchedulerLimitPerTick.Should().Be(200);
        resolved.MisfireMinSeconds.Should().Be(30);
        resolved.WorkerTimeoutSeconds.Should().Be(30);
        resolved.LeaseDurationSeconds.Should().Be(300);
        resolved.RetryDelaySeconds.Should().Be(5);
    }
}
