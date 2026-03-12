namespace Protons.Core.Tarefas.Models;

/// <summary>
/// Contrato de erro estruturado para Ancorar PDF (enterprise).
/// Alinhado a RFC 7807/9457, Google AIP-193 e OpenTelemetry.
/// </summary>
/// <param name="Code">Código estável (ex: ANCORA-NEG-PDF_SEM_TEXTO).</param>
/// <param name="Category">Categoria para classificação e retry.</param>
/// <param name="Message">Mensagem para log/observabilidade.</param>
/// <param name="UserMessage">Mensagem segura para UI (opcional).</param>
/// <param name="Retryable">Polly deve retentar? NEG=never, TEC=condicional.</param>
/// <param name="CorrelationId">Rastreio transacional.</param>
/// <param name="Metadata">Metadata adicional (ErrorInfo.metadata).</param>
public sealed record AncorarPdfError(
    string Code,
    AncorarPdfErrorCategory Category,
    string Message,
    string? UserMessage,
    bool Retryable,
    string? CorrelationId,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    /// <summary>
    /// Alias explícito para manter semântica "details" no contrato enterprise.
    /// </summary>
    public string Details => Message;
}

/// <summary>Categoria do erro para classificação e retry.</summary>
public enum AncorarPdfErrorCategory
{
    /// <summary>Falha de negócio — cliente deve corrigir; nunca retry.</summary>
    Negocio,

    /// <summary>Falha técnica — timeout, IO, rede; candidato a retry.</summary>
    Tecnico,

    /// <summary>Falha de sistema — bug, corrupção; não retry.</summary>
    Sistema
}
