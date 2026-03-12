namespace Protons.Core.Tarefas.Services;

public readonly record struct AncorarPdfRuntimeResolved(
    int Workers,
    int ChannelCapacity,
    int SchedulerPollingMs,
    int SchedulerLimitPerTick,
    int MisfireMinSeconds,
    int WorkerTimeoutSeconds,
    int LeaseDurationSeconds,
    int RetryDelaySeconds);

public static class AncorarPdfRuntimeTuningPolicy
{
    public const int DefaultWorkers = 0;
    public const int DefaultChannelCapacity = 1000;
    public const int DefaultSchedulerPollingMs = 1000;
    public const int DefaultSchedulerLimitPerTick = 200;
    public const int DefaultMisfireMinSeconds = 30;
    public const int DefaultWorkerTimeoutSeconds = 30;
    public const int DefaultLeaseDurationSeconds = 300;
    public const int DefaultRetryDelaySeconds = 5;

    public static AncorarPdfRuntimeResolved Resolve(
        int configuredWorkers,
        int processorCount,
        int channelCapacity,
        int schedulerPollingMs,
        int schedulerLimitPerTick,
        int misfireMinSeconds,
        int workerTimeoutSeconds,
        int leaseDurationSeconds,
        int retryDelaySeconds)
    {
        return new AncorarPdfRuntimeResolved(
            Workers: ResolveWorkers(configuredWorkers, processorCount),
            ChannelCapacity: ResolveChannelCapacity(channelCapacity),
            SchedulerPollingMs: ResolveSchedulerPollingMs(schedulerPollingMs),
            SchedulerLimitPerTick: ResolveSchedulerLimitPerTick(schedulerLimitPerTick),
            MisfireMinSeconds: ResolveMisfireMinSeconds(misfireMinSeconds),
            WorkerTimeoutSeconds: ResolveWorkerTimeoutSeconds(workerTimeoutSeconds),
            LeaseDurationSeconds: ResolveLeaseDurationSeconds(leaseDurationSeconds),
            RetryDelaySeconds: ResolveRetryDelaySeconds(retryDelaySeconds));
    }

    public static int ResolveWorkers(int configuredWorkers, int processorCount)
    {
        if (configuredWorkers == 0)
        {
            var cores = processorCount <= 0 ? 1 : processorCount;
            return Math.Clamp(cores, 1, 16);
        }

        return Math.Clamp(configuredWorkers, 1, 16);
    }

    public static int ResolveChannelCapacity(int value) => Math.Clamp(value, 100, 20000);

    public static int ResolveSchedulerPollingMs(int value) => Math.Clamp(value, 250, 5000);

    public static int ResolveSchedulerLimitPerTick(int value) => Math.Clamp(value, 10, 2000);

    public static int ResolveMisfireMinSeconds(int value) => Math.Clamp(value, 5, 3600);

    public static int ResolveWorkerTimeoutSeconds(int value) => Math.Clamp(value, 5, 300);

    public static int ResolveLeaseDurationSeconds(int value) => Math.Clamp(value, 30, 3600);

    public static int ResolveRetryDelaySeconds(int value) => Math.Clamp(value, 1, 120);
}
