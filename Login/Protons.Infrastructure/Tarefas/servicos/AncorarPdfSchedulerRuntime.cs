using System.Diagnostics;
using System.Diagnostics.Metrics;
using Protons.Core.Tarefas.Repositories;
using Protons.Core.Tarefas.Services;

namespace Protons.Infrastructure.Tarefas.Services;

/// <summary>
/// Runtime do scheduler de ancorar_pdf.
/// Loop deterministico por polling (PeriodicTimer, 1s padrao) com:
///   - Ordenacao deterministica: PrioridadeExecucao ASC, CriadoEmUtc ASC.
///   - Deduplicacao de misfire: mesma chave (TarefaId:JanelaUtc) nao gera multiplos backlogs.
///   - Limpeza automatica de misfires antigos (> 7 dias) para evitar crescimento de memoria.
///   - Metricas OpenTelemetry via System.Diagnostics.Metrics (sem dependencia externa).
///   - Start/Stop/Dispose thread-safe com CancellationToken.
/// </summary>
public sealed class AncorarPdfSchedulerRuntime : IAncorarPdfScheduler
{
    // ------------------------------------------------------------------
    // Metricas OpenTelemetry (System.Diagnostics.Metrics — built-in .NET 8)
    // Exportadores externos (OTLP, Prometheus, etc.) podem ser ligados em
    // App.axaml.cs via MeterProvider sem alterar este codigo.
    // ------------------------------------------------------------------
    private static readonly Meter _meter = new("Protons.AncorarPdfScheduler", "1.0.0");

    /// <summary>Total de ticks executados pelo loop do scheduler.</summary>
    private static readonly Counter<long> _ticksCounter =
        _meter.CreateCounter<long>(
            "ancorar_pdf.scheduler.ticks_total",
            description: "Total de ticks executados pelo scheduler.");

    /// <summary>Total de tarefas disparadas com sucesso (TryMarkAsInProgress = true).</summary>
    private static readonly Counter<long> _disparosCounter =
        _meter.CreateCounter<long>(
            "ancorar_pdf.scheduler.disparos_total",
            description: "Total de tarefas disparadas com sucesso.");

    /// <summary>Total de misfires detectados (atraso >= limiar) e registrados como backlog.</summary>
    private static readonly Counter<long> _misfireCounter =
        _meter.CreateCounter<long>(
            "ancorar_pdf.scheduler.misfires_total",
            description: "Total de misfires detectados e enviados para backlog.");

    /// <summary>Total de erros nao-cancelamento capturados no loop do scheduler.</summary>
    private static readonly Counter<long> _errosCounter =
        _meter.CreateCounter<long>(
            "ancorar_pdf.scheduler.erros_total",
            description: "Total de erros nao-cancelamento no loop do scheduler.");

    /// <summary>
    /// Duracao de cada tick em milissegundos.
    /// SLO alvo: p95 &lt;= 200ms, p99 &lt;= 500ms por tick.
    /// </summary>
    private static readonly Histogram<double> _tickDurationMs =
        _meter.CreateHistogram<double>(
            "ancorar_pdf.scheduler.tick_duration_ms",
            unit: "ms",
            description: "Duracao de processamento de cada tick em ms. SLO: p95<=200ms p99<=500ms.");

    // ------------------------------------------------------------------
    // Estado de instancia
    // ------------------------------------------------------------------
    private readonly object _sync = new();
    private readonly IAncorarPdfConfiguracaoRepository _configuracoes;
    private readonly AncorarPdfJobHandler _jobHandler;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _intervaloPolling;
    private readonly int _limitePorTick;
    private readonly int _misfireAtrasoMinimoSegundos;

    // Evita duplicar backlog/evento de misfire em todo tick para a mesma janela.
    private readonly Dictionary<string, DateTime> _misfiresSinalizados = new(StringComparer.Ordinal);

    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public AncorarPdfSchedulerRuntime(
        IAncorarPdfConfiguracaoRepository configuracoes,
        ITarefaRepository tarefas,
        TimeProvider? timeProvider = null,
        TimeSpan? intervaloPolling = null,
        int limitePorTick = 200,
        int misfireAtrasoMinimoSegundos = 30,
        IAncorarPdfFilaExecucaoService? filaService = null)
    {
        _configuracoes = configuracoes;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _intervaloPolling = intervaloPolling ?? TimeSpan.FromSeconds(1);
        _limitePorTick = Math.Clamp(limitePorTick, 1, 2000);
        _misfireAtrasoMinimoSegundos = Math.Max(5, misfireAtrasoMinimoSegundos);
        _jobHandler = new AncorarPdfJobHandler(
            configuracoes,
            tarefas,
            _timeProvider,
            _misfireAtrasoMinimoSegundos,
            filaService);
    }

    // ------------------------------------------------------------------
    // Ciclo de vida: Start / Stop / Dispose
    // ------------------------------------------------------------------

    public void Start()
    {
        lock (_sync)
        {
            if (_cts is not null)
                return;

            _cts = new CancellationTokenSource();
            _loopTask = Task.Run(() => LoopAsync(_cts.Token));
        }
    }

    public void Stop(TimeSpan? timeout = null)
    {
        CancellationTokenSource? cts;
        Task? loop;

        lock (_sync)
        {
            cts = _cts;
            loop = _loopTask;
            _cts = null;
            _loopTask = null;
        }

        if (cts is null)
            return;

        try
        {
            cts.Cancel();
        }
        finally
        {
            cts.Dispose();
        }

        if (loop is null)
            return;

        try
        {
            loop.Wait(timeout ?? TimeSpan.FromSeconds(5));
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(inner => inner is OperationCanceledException))
        {
            // Encerramento normal do loop.
        }
        catch (OperationCanceledException)
        {
            // Encerramento normal do loop.
        }
    }

    public void Dispose()
    {
        Stop();
        _meter.Dispose();
        GC.SuppressFinalize(this);
    }

    // ------------------------------------------------------------------
    // Loop interno
    // ------------------------------------------------------------------

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        // Primeiro tick imediato ao iniciar (reidratacao de backlog de startup).
        await ProcessarTickSeguroAsync(cancellationToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(_intervaloPolling);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            await ProcessarTickSeguroAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ProcessarTickSeguroAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.GetTimestamp();
        try
        {
            await ProcessarTickAsync(cancellationToken).ConfigureAwait(false);
            _ticksCounter.Add(1);
        }
        catch (OperationCanceledException)
        {
            // Encerramento normal — nao contar como erro.
        }
        catch (Exception ex)
        {
            _errosCounter.Add(1);
            Debug.WriteLine($"[AncorarPdfSchedulerRuntime] Falha no tick: {ex.GetType().Name} - {ex.Message}");
        }
        finally
        {
            _tickDurationMs.Record(Stopwatch.GetElapsedTime(sw).TotalMilliseconds);
        }
    }

    private async Task ProcessarTickAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var referenciaUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var agendamentosVencidos = _configuracoes.ListarAgendamentosAtivosAte(referenciaUtc, _limitePorTick);
        if (agendamentosVencidos.Count == 0)
        {
            LimparMisfiresAntigos(referenciaUtc);
            return;
        }

        // Ordenacao deterministica: menor prioridade numerica = maior urgencia.
        // Em caso de empate: tarefa criada ha mais tempo tem precedencia (FIFO por criacao).
        foreach (var agendamento in agendamentosVencidos
                     .OrderBy(x => x.PrioridadeExecucao)
                     .ThenBy(x => x.CriadoEmUtc))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var atrasoSegundos = Math.Max(0, (int)(referenciaUtc - agendamento.VencimentoUtc).TotalSeconds);
            var chaveMisfire = $"{agendamento.TarefaId}:{agendamento.VencimentoUtc:O}";

            // Ja foi sinalizado como misfire neste ciclo: nao duplicar backlog.
            if (atrasoSegundos >= _misfireAtrasoMinimoSegundos && _misfiresSinalizados.ContainsKey(chaveMisfire))
                continue;

            var resultado = await _jobHandler.ProcessarAsync(agendamento, referenciaUtc).ConfigureAwait(false);

            switch (resultado.Decisao)
            {
                case AncorarPdfJobDecision.Disparado:
                    _disparosCounter.Add(1);
                    _misfiresSinalizados.Remove(chaveMisfire);
                    break;

                case AncorarPdfJobDecision.BacklogRegistrado:
                    _misfireCounter.Add(1);
                    _misfiresSinalizados[chaveMisfire] = referenciaUtc;
                    break;

                // Ignorado: claim perdido para outro worker (concorrencia normal em multi-instancia).
                case AncorarPdfJobDecision.Ignorado:
                default:
                    break;
            }
        }

        LimparMisfiresAntigos(referenciaUtc);
    }

    private void LimparMisfiresAntigos(DateTime referenciaUtc)
    {
        if (_misfiresSinalizados.Count == 0)
            return;

        var limite = referenciaUtc.AddDays(-7);
        var chavesExpiradas = _misfiresSinalizados
            .Where(x => x.Value < limite)
            .Select(x => x.Key)
            .ToArray();

        foreach (var key in chavesExpiradas)
            _misfiresSinalizados.Remove(key);
    }
}
