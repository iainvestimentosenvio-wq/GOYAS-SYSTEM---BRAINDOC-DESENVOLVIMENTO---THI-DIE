using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Protons.UI.Common;

namespace Protons.UI.Painel.ViewModels;

public sealed partial class PainelViewModel
{
    private static string GerarCorrelationId()
    {
        return Guid.NewGuid().ToString("N")[..12];
    }

    private MedicaoOperacaoPainel IniciarMetricaPainel(string operacao, string correlationId)
    {
        return new MedicaoOperacaoPainel(operacao, correlationId, Stopwatch.StartNew());
    }

    private void RegistrarMetricaPainel(string metricaId, long valor, string unidade, string? contexto = null)
    {
        var mensagem = $"painel_metrica id={metricaId} valor={valor} unidade={unidade} user_id={_userId}";
        if (!string.IsNullOrWhiteSpace(contexto))
            mensagem += $" {contexto}";

        OpsLogger.WriteInfo(mensagem);
    }

    private void RegistrarEventoPainel(string eventoId, string? contexto = null)
    {
        var mensagem = $"painel_evento id={eventoId} user_id={_userId}";
        if (!string.IsNullOrWhiteSpace(contexto))
            mensagem += $" {contexto}";

        OpsLogger.WriteInfo(mensagem);
    }

    private void RegistrarErroPainel(string eventoId, Exception ex, string? contexto = null)
    {
        var mensagem = $"painel_evento id={eventoId} user_id={_userId}";
        if (!string.IsNullOrWhiteSpace(contexto))
            mensagem += $" {contexto}";

        OpsLogger.WriteError(mensagem, ex);
    }

    private void RegistrarInteracaoPainel()
    {
        _ultimaInteracaoPainelUtc = DateTime.UtcNow;
    }

    private void DispararComSeguranca(Task tarefa, string eventoErroId)
    {
        _ = tarefa.ContinueWith(t =>
        {
            if (t.IsFaulted && t.Exception is not null)
                RegistrarErroPainel(eventoErroId, t.Exception.InnerException ?? t.Exception);
        }, TaskScheduler.Default);
    }

    private readonly record struct MedicaoOperacaoPainel(string Operacao, string CorrelationId, Stopwatch Stopwatch)
    {
        public long ElapsedMs()
        {
            return Stopwatch.ElapsedMilliseconds;
        }
    }
}
