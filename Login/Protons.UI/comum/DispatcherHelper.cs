using System;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace Protons.UI.Common;

/// <summary>
/// Helper para execução segura de código assíncrono via <see cref="Dispatcher.Post"/>.
/// Evita fronteira async void onde exceções seriam perdidas.
/// </summary>
internal static class DispatcherHelper
{
    /// <summary>
    /// Agenda execução assíncrona na UI thread com captura de exceções.
    /// Exceções são registradas em <see cref="OpsLogger"/> e não propagam.
    /// </summary>
    /// <param name="action">Operação assíncrona a executar.</param>
    /// <param name="contextoLog">Contexto para log em caso de erro (ex.: "regua_tarefa_auto_falha").</param>
    /// <summary>
    /// Agenda execução assíncrona na UI thread com prioridade Normal.
    /// </summary>
    public static void PostAsyncSafe(Func<Task> action, string contextoLog)
        => PostAsyncSafe(action, contextoLog, DispatcherPriority.Normal);

    /// <param name="priority">Prioridade do dispatcher.</param>
    public static void PostAsyncSafe(
        Func<Task> action,
        string contextoLog,
        DispatcherPriority priority)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await action().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                OpsLogger.WriteError(contextoLog, ex);
            }
        }, priority);
    }
}
