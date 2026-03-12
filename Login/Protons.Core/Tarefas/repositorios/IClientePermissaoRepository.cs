using Protons.Core.Tarefas.Models;

namespace Protons.Core.Tarefas.Repositories;

public interface IClientePermissaoRepository
{
    IReadOnlyList<ClientePermissaoUsuario> ListarPorCliente(int clienteId);
    ClientePermissaoUsuario? Obter(int clienteId, int userId);
    void DefinirPermissoes(int clienteId, int concedidoPorUserId, IReadOnlyList<PermissaoClienteEntrada> permissoes);
}
