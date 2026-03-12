using Protons.Core.Clientes.Models;

namespace Protons.Core.Clientes.Repositories;

public interface IClienteRepository
{
    Cliente? GetByDocumento(string? documentoSomenteDigitos);
    Cliente? GetByCodigoCliente(string? codigoCliente);
    Cliente? GetById(int id);
    IReadOnlyList<Cliente> ListarTodos();
    IReadOnlyList<Cliente> Buscar(string? termo, int pagina, int tamanhoPagina);
    int Contar(string? termo);
    int Create(Cliente cliente);
    void Update(Cliente cliente);
    void Inativar(int clienteId, int atualizadoPorUserId, DateTime atualizadoEmUtc);
    GrupoEmpresarial? GetGrupoById(int grupoId);
    GrupoEmpresarial? GetGrupoByNomeNormalizado(string? nomeNormalizado);
    IReadOnlyList<GrupoEmpresarial> ListarGrupos();
    int CreateGrupo(GrupoEmpresarial grupo);
}
