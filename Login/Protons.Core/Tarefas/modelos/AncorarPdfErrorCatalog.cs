using System.Collections.Generic;

namespace Protons.Core.Tarefas.Models;

/// <summary>
/// Catálogo de erros Ancorar PDF com mapeamento Code → AncorarPdfError.
/// Retryable: ANCORA-NEG-* = false, ANCORA-TEC-* = true (exceto FalhaGeral condicional).
/// </summary>
public static class AncorarPdfErrorCatalog
{
    private static readonly IReadOnlyDictionary<string, (AncorarPdfErrorCategory Cat, bool Retry, string UserMsg)> _map = new Dictionary<string, (AncorarPdfErrorCategory, bool, string)>(StringComparer.Ordinal)
    {
        [AncorarPdfErroCodigos.NegPdfSemTexto] = (AncorarPdfErrorCategory.Negocio, false, "PDF sem texto nativo (escaneado ou vazio)."),
        [AncorarPdfErroCodigos.NegClienteNaoValidado] = (AncorarPdfErrorCategory.Negocio, false, "Validação de cliente falhou."),
        [AncorarPdfErroCodigos.NegArquivoNaoEncontrado] = (AncorarPdfErrorCategory.Negocio, false, "Nenhum PDF encontrado na pasta com similaridade suficiente."),
        [AncorarPdfErroCodigos.NegConfigNaoEncontrada] = (AncorarPdfErrorCategory.Negocio, false, "Configuração da tarefa não encontrada."),
        [AncorarPdfErroCodigos.NegOcrIndisponivel] = (AncorarPdfErrorCategory.Negocio, false, "OCR indisponível no ambiente. Verifique tessdata e idiomas configurados."),
        [AncorarPdfErrorCode.Validation] = (AncorarPdfErrorCategory.Negocio, false, "Falha de validação na configuração."),
        [AncorarPdfErrorCode.DuplicateTaskName] = (AncorarPdfErrorCategory.Negocio, false, "Já existe uma tarefa com este nome neste cliente/esteira. Escolha um nome diferente."),
        [AncorarPdfErrorCode.RequesterInvalid] = (AncorarPdfErrorCategory.Negocio, false, "Sessão expirada ou usuário inativo. Faça login novamente."),
        [AncorarPdfErrorCode.ConcurrencyConflict] = (AncorarPdfErrorCategory.Negocio, false, "Esta configuração foi alterada por outra sessão. Reabra a tarefa e tente novamente."),
        [AncorarPdfErrorCode.TaskNotFound] = (AncorarPdfErrorCategory.Negocio, false, "Tarefa não encontrada. Ela pode ter sido removida por outro usuário."),
        [AncorarPdfErrorCode.InvalidPathPolicy] = (AncorarPdfErrorCategory.Negocio, false, "Caminho não autorizado pela política de segurança."),
        [AncorarPdfErroCodigos.TecFalhaIo] = (AncorarPdfErrorCategory.Tecnico, true, "Falha de acesso a arquivo ou pasta."),
        [AncorarPdfErroCodigos.TecFalhaTimeout] = (AncorarPdfErrorCategory.Tecnico, true, "Tempo limite de processamento excedido."),
        [AncorarPdfErroCodigos.TecFalhaGeral] = (AncorarPdfErrorCategory.Tecnico, false, "Falha técnica não categorizada."),
        [AncorarPdfErrorCode.SaveTimeout] = (AncorarPdfErrorCategory.Tecnico, true, "O servidor demorou mais de 30 segundos para responder. Verifique a conexão e tente novamente."),
        [AncorarPdfErrorCode.SaveUnexpected] = (AncorarPdfErrorCategory.Tecnico, false, "Erro ao salvar. Tente novamente ou entre em contato com o suporte."),
    };

    /// <summary>Cria AncorarPdfError a partir do código canônico.</summary>
    public static AncorarPdfError Create(
        string code,
        string message,
        string? correlationId = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        if (_map.TryGetValue(code, out var entry))
            return new AncorarPdfError(code, entry.Cat, message, entry.UserMsg, entry.Retry, correlationId, metadata);

        var (cat, retry) = code.StartsWith("ANCORA-NEG-", StringComparison.Ordinal)
            ? (AncorarPdfErrorCategory.Negocio, false)
            : (AncorarPdfErrorCategory.Tecnico, code.StartsWith("ANCORA-TEC-", StringComparison.Ordinal));
        return new AncorarPdfError(code, cat, message, null, retry, correlationId, metadata);
    }

    /// <summary>Indica se o código é retryable (para Polly/observabilidade).</summary>
    public static bool IsRetryable(string code)
    {
        if (string.IsNullOrEmpty(code)) return false;
        if (_map.TryGetValue(code, out var entry)) return entry.Retry;
        return code.StartsWith("ANCORA-TEC-", StringComparison.Ordinal);
    }
}
