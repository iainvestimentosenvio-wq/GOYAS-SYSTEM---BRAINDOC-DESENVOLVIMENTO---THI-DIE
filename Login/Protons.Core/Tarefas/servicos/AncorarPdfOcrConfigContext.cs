using Protons.Core.Tarefas.Models;

namespace Protons.Core.Tarefas.Services;

/// <summary>
/// Contexto de config para fallback OCR (usado pelo decorator durante ExecutarAsync).
/// O motor define antes de chamar o extrator; o decorator lê.
/// </summary>
public static class AncorarPdfOcrConfigContext
{
    private static readonly AsyncLocal<AncorarPdfConfiguracaoTarefa?> _current = new();

    public static AncorarPdfConfiguracaoTarefa? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }
}
