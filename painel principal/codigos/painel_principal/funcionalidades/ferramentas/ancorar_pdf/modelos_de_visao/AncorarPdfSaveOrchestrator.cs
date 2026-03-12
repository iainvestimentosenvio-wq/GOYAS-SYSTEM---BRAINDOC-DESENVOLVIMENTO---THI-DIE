using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Protons.Core.Tarefas.Models;

namespace Protons.UI.Painel.Funcionalidades.Ferramentas.AncorarPdf.ModelosDeVisao;

internal sealed class AncorarPdfSaveOrchestrator : IDisposable
{
    private readonly SemaphoreSlim _saveSemaphore = new(1, 1);
    private bool _disposed;

    public async Task<bool> TentarIniciarAsync()
    {
        return await _saveSemaphore.WaitAsync(0);
    }

    public void FinalizarComSeguranca()
    {
        try
        {
            _saveSemaphore.Release();
        }
        catch (ObjectDisposedException)
        {
            // A VM pode ser descartada enquanto um save ainda finaliza em background.
        }
        catch (SemaphoreFullException)
        {
            // Evita falha em reentrada defensiva quando o semáforo já foi liberado.
        }
    }

    public bool Validar(AncorarPdfSaveValidationInput input, Func<string?, string> normalizarTimezoneId, out string erro)
    {
        if (string.IsNullOrWhiteSpace(input.NomeTarefaPersonalizado))
        {
            erro = "Nome da tarefa é obrigatório.";
            return false;
        }

        if (input.NomeTarefaPersonalizado.Trim().Length > 200)
        {
            erro = "Nome da tarefa não pode ter mais de 200 caracteres.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(input.PastaMonitoradaPath))
        {
            erro = "Pasta monitorada é obrigatória.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(input.PdfModeloPath))
        {
            erro = "PDF modelo é obrigatório.";
            return false;
        }

        var pdfModeloPathNormalizado = input.PdfModeloPath.Trim();
        if (Directory.Exists(pdfModeloPathNormalizado))
        {
            erro = "No modal avançado, PDF modelo deve ser arquivo .pdf. Pasta é permitida apenas no wizard básico.";
            return false;
        }

        if (!pdfModeloPathNormalizado.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            erro = "PDF modelo deve ter extensão .pdf.";
            return false;
        }

        if (input.HighlightOpacity is < 0.30 or > 0.45)
        {
            erro = "Opacidade de destaque deve estar entre 30% e 45%.";
            return false;
        }

        if (input.PdfModeloCrossCliente && !input.SolicitanteEhAdmin)
        {
            erro = "Somente Admin pode usar PDF modelo cross-cliente.";
            return false;
        }

        if (input.OcrFallbackAtivo)
        {
            if (input.OcrDpi is < 150 or > 600)
            {
                erro = "OCR DPI deve estar entre 150 e 600.";
                return false;
            }

            if (!EhOcrLangValido(input.OcrLang))
            {
                erro = "OCR idiomas deve usar formato Tesseract, por exemplo: por+eng.";
                return false;
            }
        }

        if (input.HoraSelecionada is < 0 or > 23)
        {
            erro = "Hora inválida. Informe um valor entre 0 e 23.";
            return false;
        }

        if (input.MinutoSelecionado is < 0 or > 59)
        {
            erro = "Minuto inválido. Informe um valor entre 0 e 59.";
            return false;
        }

        if (input.SegundoSelecionado is < 0 or > 59)
        {
            erro = "Segundo inválido. Informe um valor entre 0 e 59.";
            return false;
        }

        if (input.LimiarSimilaridadeNome is < 0 or > 1)
        {
            erro = "Limiar de similaridade deve estar entre 0 e 1.";
            return false;
        }

        if (input.PrioridadeExecucaoSelecionada is < 1 or > 5)
        {
            erro = "Prioridade de execução deve estar entre 1 e 5.";
            return false;
        }

        if (!input.DstHorarioInvalidoPolicies.Contains(input.DstHorarioInvalidoPolicySelecionada, StringComparer.Ordinal))
        {
            erro = "Política DST para horário inválido é obrigatória.";
            return false;
        }

        if (!input.DstHorarioAmbiguoPolicies.Contains(input.DstHorarioAmbiguoPolicySelecionada, StringComparer.Ordinal))
        {
            erro = "Política DST para horário ambíguo é obrigatória.";
            return false;
        }

        try
        {
            _ = normalizarTimezoneId(input.TimezoneIdSelecionado);
        }
        catch (InvalidOperationException ex)
        {
            erro = ex.Message;
            return false;
        }

        if (input.PdfModeloCrossCliente && (input.PdfModeloCrossClienteJustificativa?.Trim().Length ?? 0) < 15)
        {
            erro = "Informe justificativa cross-cliente com no mínimo 15 caracteres.";
            return false;
        }

        if (input.PdfModeloCrossCliente && (input.PdfModeloCrossClienteJustificativa?.Trim().Length ?? 0) > 500)
        {
            erro = "Justificativa cross-cliente não pode exceder 500 caracteres.";
            return false;
        }

        erro = string.Empty;
        return true;
    }

    public static string GerarCorrelationId()
    {
        if (Activity.Current is { TraceId: var traceId } && traceId != default)
            return traceId.ToString();

        return Guid.NewGuid().ToString("N");
    }

    public AncorarPdfError CriarErroValidacao(string detalhes, string? correlationId = null)
    {
        return CriarErro(
            AncorarPdfErrorCode.Validation,
            detalhes,
            correlationId,
            exceptionType: "ValidationError");
    }

    public AncorarPdfError CriarErroTimeout(TimeSpan timeout, string? correlationId = null)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["origin"] = "ui.modal.save",
            ["timeout_seconds"] = Math.Round(timeout.TotalSeconds).ToString(),
            ["exception_type"] = nameof(OperationCanceledException)
        };

        return AncorarPdfErrorCatalog.Create(
            AncorarPdfErrorCode.SaveTimeout,
            $"Tempo limite de salvamento excedido ({Math.Round(timeout.TotalSeconds)}s).",
            correlationId,
            metadata);
    }

    public AncorarPdfError MapearErro(Exception ex, string? correlationId = null)
    {
        ArgumentNullException.ThrowIfNull(ex);

        var details = string.IsNullOrWhiteSpace(ex.Message)
            ? "Erro sem mensagem detalhada."
            : ex.Message.Trim();

        var code = MapearCodigo(ex, details);
        return CriarErro(code, details, correlationId, ex.GetType().Name);
    }

    public string ResolverMensagemUsuario(AncorarPdfError erroTipado)
    {
        if (string.Equals(erroTipado.Code, AncorarPdfErrorCode.Validation, StringComparison.Ordinal))
            return erroTipado.Details;

        return string.IsNullOrWhiteSpace(erroTipado.UserMessage)
            ? "Erro ao salvar. Tente novamente ou entre em contato com o suporte."
            : erroTipado.UserMessage!;
    }

    public string MapearMensagemErro(Exception ex)
    {
        return ResolverMensagemUsuario(MapearErro(ex));
    }

    private static AncorarPdfError CriarErro(
        string code,
        string details,
        string? correlationId,
        string exceptionType)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["origin"] = "ui.modal.save",
            ["exception_type"] = exceptionType
        };

        return AncorarPdfErrorCatalog.Create(
            code,
            details,
            correlationId,
            metadata);
    }

    private static string MapearCodigo(Exception ex, string details)
    {
        if (ex is AncorarPdfFalhaDeNegocioException negocio && !string.IsNullOrWhiteSpace(negocio.Codigo))
            return negocio.Codigo;

        if (ContemQualquer(details, "mesmo nome", "já existe"))
            return AncorarPdfErrorCode.DuplicateTaskName;

        if (ContemQualquer(details, "Solicitante inválido", "inativo"))
            return AncorarPdfErrorCode.RequesterInvalid;

        if (ContemQualquer(details, "simultane"))
            return AncorarPdfErrorCode.ConcurrencyConflict;

        if (ContemQualquer(details, "não encontrada"))
            return AncorarPdfErrorCode.TaskNotFound;

        if (ContemQualquer(details, "não autorizado", "allowlist", "caminho"))
            return AncorarPdfErrorCode.InvalidPathPolicy;

        if (ContemQualquer(details, "Admin", "cross-cliente", "obrigatório", "inválid"))
            return AncorarPdfErrorCode.Validation;

        return AncorarPdfErrorCode.SaveUnexpected;
    }

    private static bool ContemQualquer(string texto, params string[] trechos)
    {
        foreach (var trecho in trechos)
        {
            if (texto.Contains(trecho, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool EhOcrLangValido(string? lang)
    {
        if (string.IsNullOrWhiteSpace(lang))
            return false;

        var texto = lang.Trim();
        if (texto.StartsWith('+') || texto.EndsWith('+') || texto.Contains("++", StringComparison.Ordinal))
            return false;

        var tokens = texto.Split('+', StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
            return false;

        foreach (var token in tokens)
        {
            if (string.IsNullOrWhiteSpace(token))
                return false;

            foreach (var ch in token)
            {
                if (!(char.IsLetterOrDigit(ch) || ch == '_'))
                    return false;
            }
        }

        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _saveSemaphore.Dispose();
    }
}

internal sealed record AncorarPdfSaveValidationInput(
    string NomeTarefaPersonalizado,
    string PastaMonitoradaPath,
    string PdfModeloPath,
    double HighlightOpacity,
    bool PdfModeloCrossCliente,
    bool SolicitanteEhAdmin,
    int HoraSelecionada,
    int MinutoSelecionado,
    int SegundoSelecionado,
    double LimiarSimilaridadeNome,
    int PrioridadeExecucaoSelecionada,
    string DstHorarioInvalidoPolicySelecionada,
    string DstHorarioAmbiguoPolicySelecionada,
    IReadOnlyList<string> DstHorarioInvalidoPolicies,
    IReadOnlyList<string> DstHorarioAmbiguoPolicies,
    string? TimezoneIdSelecionado,
    string? PdfModeloCrossClienteJustificativa,
    bool OcrFallbackAtivo,
    int OcrDpi,
    string? OcrLang);
