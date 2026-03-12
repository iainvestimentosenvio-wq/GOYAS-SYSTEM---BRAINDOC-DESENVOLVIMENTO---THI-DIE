using System.Threading.Tasks;
using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Services;

namespace Protons.UI.Painel.ViewModels;

internal static class ITarefaServiceAsyncExtensions
{
    public static Task<Tarefa?> ObterPorIdAsync(this ITarefaService tarefaService, int tarefaId, int userId)
    {
        return Task.Run(() => tarefaService.ObterPorId(tarefaId, userId));
    }

    public static Task<Tarefa> AlterarStatusAsync(this ITarefaService tarefaService, int tarefaId, TarefaStatus status, int userId)
    {
        return Task.Run(() => tarefaService.AlterarStatus(tarefaId, status, userId));
    }
}
