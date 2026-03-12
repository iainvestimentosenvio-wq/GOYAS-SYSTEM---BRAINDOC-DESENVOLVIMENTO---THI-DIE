using Protons.Core.Clientes.Models;

namespace Protons.Core.Clientes.Services;

public interface IClienteService
{
    ResultadoCadastroCliente Cadastrar(ClienteCadastroEntrada entrada);
    ResultadoCadastroCliente Editar(ClienteEdicaoEntrada entrada);
    ResultadoCadastroGrupo CadastrarGrupo(GrupoEmpresarialCadastroEntrada entrada);
    bool Inativar(int clienteId, int atualizadoPorUserId);
    IReadOnlyList<GrupoEmpresarial> ListarGrupos();

    [Obsolete("Use ListarTodosPorUsuario para aplicar controle de acesso por usuário.")]
    IReadOnlyList<Cliente> ListarTodos();

    [Obsolete("Use ObterPorIdPorUsuario para aplicar controle de acesso por usuário.")]
    Cliente? ObterPorId(int id);

    [Obsolete("Use BuscarPorUsuario para aplicar controle de acesso por usuário.")]
    PaginacaoResultado<Cliente> Buscar(string? termo, int pagina, int tamanhoPagina);

    IReadOnlyList<Cliente> ListarTodosPorUsuario(int solicitanteUserId);
    Cliente? ObterPorIdPorUsuario(int solicitanteUserId, int clienteId);
    PaginacaoResultado<Cliente> BuscarPorUsuario(int solicitanteUserId, string? termo, int pagina, int tamanhoPagina);
}
