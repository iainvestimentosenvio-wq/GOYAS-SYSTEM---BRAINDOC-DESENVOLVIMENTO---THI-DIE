using System.Diagnostics;
using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Repositories;
using Protons.Core.Tarefas.Services;
using Protons.Infrastructure.Tarefas.Telemetry;

namespace Protons.Infrastructure.Tarefas.Services;

public enum AncorarPdfJobDecision
{
    Ignorado = 0,
    Disparado = 1,
    BacklogRegistrado = 2
}

public readonly record struct AncorarPdfJobHandlerResult(
    AncorarPdfJobDecision Decisao,
    int TarefaId,
    long? BacklogId,
    int AtrasoSegundos);

public sealed class AncorarPdfJobHandler
{
    private const string MisfireMotivoPadrao = "misfire_offline";

    private readonly IAncorarPdfConfiguracaoRepository _configuracoes;
    private readonly ITarefaRepository _tarefas;
    private readonly TimeProvider _timeProvider;
    private readonly int _misfireAtrasoMinimoSegundos;
    private readonly IAncorarPdfFilaExecucaoService? _filaService;

    public AncorarPdfJobHandler(
        IAncorarPdfConfiguracaoRepository configuracoes,
        ITarefaRepository tarefas,
        TimeProvider? timeProvider = null,
        int misfireAtrasoMinimoSegundos = 30,
        IAncorarPdfFilaExecucaoService? filaService = null)
    {
        _configuracoes = configuracoes;
        _tarefas = tarefas;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _misfireAtrasoMinimoSegundos = Math.Max(5, misfireAtrasoMinimoSegundos);
        _filaService = filaService;
    }

    public AncorarPdfJobHandlerResult Processar(AncorarPdfSchedulerAgendamentoAtivo agendamento, DateTime referenciaUtc)
        => ProcessarAsync(agendamento, referenciaUtc).GetAwaiter().GetResult();

    public async Task<AncorarPdfJobHandlerResult> ProcessarAsync(AncorarPdfSchedulerAgendamentoAtivo agendamento, DateTime referenciaUtc)
    {
        if (agendamento.TarefaId <= 0 || agendamento.ClienteId <= 0)
            return new AncorarPdfJobHandlerResult(AncorarPdfJobDecision.Ignorado, agendamento.TarefaId, null, 0);

        var referenciaNormalizadaUtc = referenciaUtc.Kind == DateTimeKind.Utc
            ? referenciaUtc
            : referenciaUtc.ToUniversalTime();
        var atrasoSegundos = Math.Max(0, (int)(referenciaNormalizadaUtc - agendamento.VencimentoUtc).TotalSeconds);

        if (atrasoSegundos >= _misfireAtrasoMinimoSegundos)
        {
            return RegistrarBacklogMisfire(agendamento, referenciaNormalizadaUtc, atrasoSegundos);
        }

        var disparou = _tarefas.TryMarkAsInProgress(agendamento.TarefaId, referenciaNormalizadaUtc);
        if (!disparou)
        {
            return new AncorarPdfJobHandlerResult(AncorarPdfJobDecision.Ignorado, agendamento.TarefaId, null, atrasoSegundos);
        }

        using var activity = AncorarPdfActivitySource.Source.StartActivity("ancorar_pdf.scheduler.dispatch", ActivityKind.Internal);
        var correlationId = Guid.NewGuid().ToString("N");
        if (activity != null && activity.IsAllDataRequested)
        {
            activity.SetTag("ancorar_pdf.correlation_id", correlationId);
            activity.SetTag("ancorar_pdf.tarefa_id", agendamento.TarefaId);
            activity.SetTag("ancorar_pdf.cliente_id", agendamento.ClienteId);
        }
        var agora = _timeProvider.GetUtcNow().UtcDateTime;

        _configuracoes.RegistrarEventoScheduler(new AncorarPdfSchedulerEventoRegistro
        {
            TarefaId = agendamento.TarefaId,
            ClienteId = agendamento.ClienteId,
            TipoEvento = AncorarPdfErroCodigos.TarefaAgendada,
            Detalhes = $"timezone={agendamento.TimezoneId} prioridade={agendamento.PrioridadeExecucao}",
            OcorreuEmUtc = agora,
            CorrelationId = correlationId,
            StatusAnterior = AncorarPdfErroCodigos.StatusAgendada,
            StatusNovo = AncorarPdfErroCodigos.StatusEmAndamento,
            ErroCodigo = AncorarPdfErroCodigos.Nenhum,
            ExecutadaComAtraso = atrasoSegundos > 0
        });

        if (_filaService is not null)
        {
            await _filaService.EnfileirarAsync(new AncorarPdfFilaEnfileirarEntrada(
                TarefaId: agendamento.TarefaId,
                ClienteId: agendamento.ClienteId,
                CicloId: $"{agendamento.TarefaId}:{agendamento.VencimentoUtc:O}",
                JanelaAlvoUtc: agendamento.VencimentoUtc,
                PrioridadeExecucao: agendamento.PrioridadeExecucao,
                Motivo: "scheduler_dispatch",
                EnfileiradoPorUserId: 0,
                EnfileiradoPorNome: "scheduler",
                CorrelationId: correlationId)).ConfigureAwait(false);
        }

        return new AncorarPdfJobHandlerResult(AncorarPdfJobDecision.Disparado, agendamento.TarefaId, null, atrasoSegundos);
    }

    private AncorarPdfJobHandlerResult RegistrarBacklogMisfire(
        AncorarPdfSchedulerAgendamentoAtivo agendamento,
        DateTime referenciaUtc,
        int atrasoSegundos)
    {
        var backlogId = _configuracoes.RegistrarBacklogPendente(new AncorarPdfSchedulerBacklogRegistro
        {
            TarefaId = agendamento.TarefaId,
            ClienteId = agendamento.ClienteId,
            JanelaAlvoUtc = agendamento.VencimentoUtc,
            AtrasoSegundos = atrasoSegundos,
            Motivo = MisfireMotivoPadrao,
            DetectadoEmUtc = referenciaUtc
        });

        _configuracoes.RegistrarEventoScheduler(new AncorarPdfSchedulerEventoRegistro
        {
            TarefaId = agendamento.TarefaId,
            ClienteId = agendamento.ClienteId,
            TipoEvento = AncorarPdfErroCodigos.MisfireDetectado,
            Detalhes = $"backlog_id={backlogId} atraso_segundos={atrasoSegundos}",
            OcorreuEmUtc = referenciaUtc,
            CorrelationId = Guid.NewGuid().ToString("N"),
            StatusAnterior = AncorarPdfErroCodigos.StatusAgendada,
            StatusNovo = AncorarPdfErroCodigos.StatusAgendada,
            ErroCodigo = AncorarPdfErroCodigos.Nenhum,
            ExecutadaComAtraso = true
        });

        return new AncorarPdfJobHandlerResult(
            AncorarPdfJobDecision.BacklogRegistrado,
            agendamento.TarefaId,
            backlogId <= 0 ? null : backlogId,
            atrasoSegundos);
    }
}
