using Protons.Core.Tarefas.Models;

namespace Protons.Core.Tarefas.Services;

public interface ITarefaService
{
    Tarefa? ObterPorId(int tarefaId, int solicitanteUserId);
    IReadOnlyList<Tarefa> Buscar(TarefaFiltroConsulta filtro, int solicitanteUserId);

    /// <summary>
    /// Busca tarefas para múltiplos clientes em uma única consulta (evita N+1).
    /// Valida acesso por PodeAcessarCliente e aplica FiltrarVisibilidade.
    /// </summary>
    IReadOnlyList<Tarefa> BuscarPorClienteIds(
        IReadOnlyList<int> clienteIds,
        string? termo,
        TarefaStatus? status,
        DateTime? vencimentoInicioUtc,
        DateTime? vencimentoFimUtc,
        bool somenteMinhasTarefas,
        int solicitanteUserId);

    IReadOnlyList<Tarefa> BuscarHistoricoGlobal(int solicitanteUserId, int limite);
    Tarefa Criar(TarefaCadastroEntrada entrada, int solicitanteUserId);
    Tarefa Atualizar(TarefaAtualizacaoEntrada entrada, int solicitanteUserId);
    Tarefa AlterarStatus(int tarefaId, TarefaStatus status, int solicitanteUserId);
    IReadOnlyList<ClientePermissaoUsuario> ListarPermissoesCliente(int clienteId, int solicitanteUserId);
    void DefinirPermissoesCliente(int clienteId, IReadOnlyList<PermissaoClienteEntrada> permissoes, int adminUserId);
}
