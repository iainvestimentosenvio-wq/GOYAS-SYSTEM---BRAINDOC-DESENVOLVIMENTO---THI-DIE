using Protons.Core.Tarefas.Models;

namespace Protons.Core.Tarefas.Repositories;

public interface IEsteiraRepository
{
    Esteira? GetById(int id);
    IReadOnlyList<Esteira> ListarPorCliente(int clienteId);
    int Create(Esteira esteira);
    void Update(Esteira esteira);
    void Delete(int id);
}
