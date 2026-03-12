using Protons.Core.Tarefas.Models;

namespace Protons.Core.Tarefas.Repositories;

public interface ITarefaRepository
{
    Tarefa? GetById(int id);
    IReadOnlyList<Tarefa> Buscar(
        int clienteId,
        string? termo,
        TarefaStatus? status,
        DateTime? vencimentoInicioUtc,
        DateTime? vencimentoFimUtc,
        int? responsavelUserId);

    /// <summary>
    /// Busca tarefas para múltiplos clientes em uma única consulta (evita N+1).
    /// </summary>
    IReadOnlyList<Tarefa> BuscarPorClienteIds(
        IReadOnlyList<int> clienteIds,
        string? termo,
        TarefaStatus? status,
        DateTime? vencimentoInicioUtc,
        DateTime? vencimentoFimUtc,
        int? responsavelUserId);

    int Create(Tarefa tarefa);
    void Update(Tarefa tarefa);
    bool TryMarkAsInProgress(int tarefaId, DateTime atualizadoEmUtc);
}
