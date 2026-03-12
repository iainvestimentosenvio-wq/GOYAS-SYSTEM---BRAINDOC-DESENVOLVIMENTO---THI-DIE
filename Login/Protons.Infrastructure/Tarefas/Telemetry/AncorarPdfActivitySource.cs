using System.Diagnostics;

namespace Protons.Infrastructure.Tarefas.Telemetry;

/// <summary>
/// ActivitySource compartilhado para tracing distribuído do fluxo AncorarPdf.
/// Nome alinhado a convenção OTel (dot-separated UpperCamelCase).
/// Uso: spans por etapa (scheduler, enqueue, worker, motor) com correlationId.
/// </summary>
public static class AncorarPdfActivitySource
{
    /// <summary>Source para spans do fluxo AncorarPdf. Reutilizar (não criar por chamada).</summary>
    public static readonly ActivitySource Source = new("Protons.AncorarPdf", "1.0.0");

    /// <summary>Nome do source para AddSource no TracerProvider.</summary>
    public const string SourceName = "Protons.AncorarPdf";

    /// <summary>Cria contexto pai a partir de traceId/spanId para propagação cross-thread (ex: channel).</summary>
    public static ActivityContext? CreateParentContext(string? traceId, string? spanId)
    {
        if (string.IsNullOrEmpty(traceId) || string.IsNullOrEmpty(spanId))
            return null;

        try
        {
            var tid = ActivityTraceId.CreateFromString(traceId.AsSpan());
            var sid = ActivitySpanId.CreateFromString(spanId.AsSpan());
            return new ActivityContext(tid, sid, ActivityTraceFlags.None);
        }
        catch
        {
            return null;
        }
    }
}
