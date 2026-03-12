namespace Protons.Core.Tarefas.Models;

/// <summary>
/// Códigos de erro estruturados para fluxos de configuração/salvamento do Ancorar PDF.
/// Mantém estabilidade para suporte, automação e observabilidade.
/// </summary>
public static class AncorarPdfErrorCode
{
    // -----------------------------------------------------------------------
    // Negócio (sem retry)
    // -----------------------------------------------------------------------

    public const string Validation = "ANCORA-NEG-VALIDATION";
    public const string DuplicateTaskName = "ANCORA-NEG-DUPLICATE_TASK_NAME";
    public const string RequesterInvalid = "ANCORA-NEG-REQUESTER_INVALID";
    public const string ConcurrencyConflict = "ANCORA-NEG-CONCURRENCY_CONFLICT";
    public const string TaskNotFound = "ANCORA-NEG-TASK_NOT_FOUND";
    public const string InvalidPathPolicy = "ANCORA-NEG-INVALID_PATH_POLICY";

    // -----------------------------------------------------------------------
    // Técnico
    // -----------------------------------------------------------------------

    public const string SaveTimeout = "ANCORA-TEC-SAVE_TIMEOUT";
    public const string SaveUnexpected = "ANCORA-TEC-SAVE_UNEXPECTED";
}
