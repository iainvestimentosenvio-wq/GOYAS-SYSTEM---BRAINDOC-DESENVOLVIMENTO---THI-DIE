using Protons.Core.Tarefas.Models;

namespace Protons.Core.Tarefas.Repositories;

public interface IAncorarPdfExecucaoLeaseRepository
{
    AncorarPdfExecucaoLease? TentarAcquirir(
        string filaItemId,
        int tarefaId,
        int clienteId,
        string workerId,
        TimeSpan duracao);
    void Liberar(string leaseId, string motivo);
    IReadOnlyList<AncorarPdfExecucaoLease> ListarExpirados(DateTime referenciaUtc, int limite = 50);
    bool TentarRenovar(string leaseId, TimeSpan novaDuracao, DateTime referenciaUtc);
}
