using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Protons.UI.Common;

/// <summary>
/// Logger operacional assíncrono que grava registros em formato JSONL
/// (JSON Lines — um objeto JSON por linha) no arquivo <c>log_ops.jsonl</c>.
/// </summary>
/// <remarks>
/// <para><strong>Formato do registro:</strong></para>
/// <code>
/// {"ts":"2026-03-01T12:00:00.000Z","level":"INFO","message":"...","exception":null,"error":null}
/// </code>
/// Campos:
/// <list type="bullet">
///   <item><c>ts</c> — timestamp UTC em ISO 8601 com offset (<c>o</c> format).</item>
///   <item><c>level</c> — severidade: <c>INFO</c>, <c>WARN</c> ou <c>ERROR</c>.</item>
///   <item><c>message</c> — mensagem sanitizada (dados PII removidos).</item>
///   <item><c>exception</c> — nome simples do tipo de exceção, ou <c>null</c>.</item>
///   <item><c>error</c> — <c>Exception.Message</c> sanitizado, ou <c>null</c>.</item>
/// </list>
///
/// <para><strong>Política de sanitização de PII:</strong></para>
/// Aplicada em toda string antes da gravação (mensagem e erro):
/// <list type="bullet">
///   <item>E-mails → <c>[EMAIL]</c></item>
///   <item>CNPJs (com ou sem pontuação) → <c>[CNPJ]</c></item>
///   <item>CPFs (com ou sem pontuação) → <c>[CPF]</c></item>
///   <item>Telefones brasileiros (com ou sem código de país) → <c>[TELEFONE]</c></item>
///   <item>Sequências de 11 ou 14 dígitos isolados → <c>[DOCUMENTO]</c> (captura CPF/CNPJ sem pontuação)</item>
/// </list>
/// A ordem de substituição importa: e-mail antes de CNPJ/CPF para evitar regex overlap.
///
/// <para><strong>Comportamento em falha de I/O:</strong></para>
/// O logger nunca lança exceções para o chamador. Se o arquivo não puder ser gravado,
/// a linha é descartada silenciosamente. Em falha de path, o fallback é
/// <c>%TEMP%/protons_log_ops.jsonl</c>.
///
/// <para><strong>Thread-safety:</strong></para>
/// O enfileiramento é feito via <see cref="ConcurrentQueue{T}"/> (lock-free). A
/// drenagem ocorre em uma Task de background dedicada que acorda a cada 200 ms.
/// </remarks>
internal static class OpsLogger
{
    // Fila lock-free que desacopla a thread de UI do I/O de disco.
    private static readonly ConcurrentQueue<string> Queue = new();

    // Regexes compiladas uma única vez (custo de compilação ~10 ms) para máxima performance
    // em chamadas repetidas. CultureInvariant garante comportamento consistente em qualquer locale.
    private static readonly Regex CpfRegex = new(@"\b\d{3}\.?\d{3}\.?\d{3}-?\d{2}\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CnpjRegex = new(@"\b\d{2}\.?\d{3}\.?\d{3}/?\d{4}-?\d{2}\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex EmailRegex = new(@"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex TelefoneRegex = new(@"\b(?:\+?55\s*)?(?:\(?\d{2}\)?\s*)?\d{4,5}[-\s]?\d{4}\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Captura CPF/CNPJ sem pontuação que escapariam dos padrões acima.
    private static readonly Regex DocumentoDigitsRegex = new(@"\b\d{11}\b|\b\d{14}\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Protege contra chamadas duplas a Initialize — Interlocked garante atomicidade.
    private static int _started;
    private static string? _logPath;
    private static CancellationTokenSource? _cts;
    private static Task? _drainTask;

    /// <summary>
    /// Inicializa o logger: cria o diretório de dados se necessário, define o caminho
    /// do arquivo JSONL e inicia a task de drenagem em background.
    /// </summary>
    /// <param name="appDataPath">
    /// Diretório de dados da aplicação (ex: <c>~/.local/share/Protons/</c>).
    /// O arquivo <c>log_ops.jsonl</c> será criado dentro deste diretório.
    /// </param>
    /// <remarks>
    /// Idempotente: chamadas subsequentes são ignoradas via <see cref="Interlocked.Exchange"/>.
    /// Deve ser chamado uma vez durante a inicialização da aplicação, antes de qualquer
    /// chamada a <see cref="WriteInfo"/>, <see cref="WriteWarning"/> ou <see cref="WriteError"/>.
    /// </remarks>
    public static void Initialize(string appDataPath)
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
            return;

        Directory.CreateDirectory(appDataPath);
        _logPath = Path.Combine(appDataPath, "log_ops.jsonl");
        _cts = new CancellationTokenSource();
        _drainTask = Task.Run(() => DrainLoop(_cts.Token));
        WriteInfo("ops_logger_initialized");
    }

    /// <summary>
    /// Cancela a task de drenagem, esvazia a fila de forma síncrona e aguarda
    /// o encerramento da task de background.
    /// </summary>
    /// <param name="timeoutMs">
    /// Tempo máximo de espera (em ms) pelo encerramento da task de background.
    /// Padrão: 1000 ms. Valores negativos são tratados como 0.
    /// </param>
    /// <remarks>
    /// Deve ser chamado no encerramento da aplicação (<c>ApplicationLifetime.Exit</c>)
    /// para garantir que registros enfileirados sejam gravados antes do processo terminar.
    /// Nunca lança exceções — erros no shutdown são suprimidos.
    /// </remarks>
    public static void FlushAndStop(int timeoutMs = 1000)
    {
        if (Interlocked.CompareExchange(ref _started, 1, 1) != 1)
            return;

        try
        {
            _cts?.Cancel();

            DrainQueueSync();

            if (_drainTask is not null)
            {
                _drainTask.Wait(Math.Max(0, timeoutMs));
            }
        }
        catch
        {
            // Nunca lançar exceções durante o shutdown
        }
    }

    /// <summary>Registra um evento informativo operacional.</summary>
    /// <param name="message">Mensagem descritiva do evento. Será sanitizada antes da gravação.</param>
    public static void WriteInfo(string message) => Enqueue("INFO", message, null);

    /// <summary>Registra um aviso operacional não-crítico.</summary>
    /// <param name="message">Descrição da condição anômala. Será sanitizada antes da gravação.</param>
    public static void WriteWarning(string message) => Enqueue("WARN", message, null);

    /// <summary>Registra um erro operacional, opcionalmente com detalhes de exceção.</summary>
    /// <param name="message">Contexto do erro. Será sanitizado antes da gravação.</param>
    /// <param name="ex">
    /// Exceção associada, ou <c>null</c>. Apenas o tipo (<see cref="Exception.GetType().Name"/>)
    /// e a mensagem são gravados — nunca o stack trace, para evitar exposição de caminhos de arquivo.
    /// </param>
    public static void WriteError(string message, Exception? ex) => Enqueue("ERROR", message, ex);

    /// <summary>
    /// Serializa o registro como JSON, aplica sanitização de PII e enfileira para gravação assíncrona.
    /// </summary>
    private static void Enqueue(string level, string message, Exception? ex)
    {
        var payload = new
        {
            ts = DateTime.UtcNow.ToString("o"),
            level,
            message = Sanitizar(message),
            exception = ex?.GetType().Name,
            error = Sanitizar(ex?.Message)
        };

        var json = JsonSerializer.Serialize(payload);
        Queue.Enqueue(json);
    }

    /// <summary>
    /// Loop de drenagem assíncrona: retira um item da fila por ciclo e grava no arquivo.
    /// Dorme 200 ms quando a fila está vazia para reduzir uso de CPU em repouso.
    /// </summary>
    /// <remarks>
    /// Não agrupa múltiplas linhas por ciclo intencionalmente: simplifica o tratamento
    /// de erros de I/O e mantém a latência de gravação previsível (~200 ms máximo).
    /// Em cenários de alto volume, o <see cref="ConcurrentQueue{T}"/> absorve o burst.
    /// </remarks>
    private static async Task DrainLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            if (Queue.TryDequeue(out var line))
            {
                var path = _logPath ?? Path.Combine(Path.GetTempPath(), "protons_log_ops.jsonl");
                await File.AppendAllTextAsync(path, line + Environment.NewLine).ConfigureAwait(false);
            }
            else
            {
                await Task.Delay(200).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Drenagem síncrona da fila para uso no shutdown. Erros de I/O por item são
    /// suprimidos individualmente para garantir que os demais itens sejam tentados.
    /// </summary>
    private static void DrainQueueSync()
    {
        var path = _logPath ?? Path.Combine(Path.GetTempPath(), "protons_log_ops.jsonl");
        while (Queue.TryDequeue(out var line))
        {
            try
            {
                File.AppendAllText(path, line + Environment.NewLine);
            }
            catch
            {
                // Item descartado em caso de falha de I/O — shutdown não pode bloquear
            }
        }
    }

    /// <summary>
    /// Aplica substituições de PII em sequência sobre a string de entrada.
    /// Retorna <c>null</c> ou string vazia inalterada se a entrada for vazia/nula.
    /// </summary>
    /// <remarks>
    /// A ordem de aplicação é: Email → CNPJ → CPF → Telefone → Dígitos isolados.
    /// Email é aplicado primeiro porque endereços podem conter sequências numéricas
    /// que disparariam os padrões de CPF/CNPJ em seguida.
    /// </remarks>
    private static string? Sanitizar(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return valor;

        var sanitizado = valor;
        sanitizado = EmailRegex.Replace(sanitizado, "[EMAIL]");
        sanitizado = CnpjRegex.Replace(sanitizado, "[CNPJ]");
        sanitizado = CpfRegex.Replace(sanitizado, "[CPF]");
        sanitizado = TelefoneRegex.Replace(sanitizado, "[TELEFONE]");
        sanitizado = DocumentoDigitsRegex.Replace(sanitizado, "[DOCUMENTO]");
        return sanitizado;
    }
}
