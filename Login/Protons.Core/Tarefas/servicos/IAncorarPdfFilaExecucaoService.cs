using Protons.Core.Tarefas.Models;

namespace Protons.Core.Tarefas.Services;

public interface IAncorarPdfFilaExecucaoService : IDisposable
{
    void Start(int numeroDeworkers = 1);
    Task StopAsync(TimeSpan? timeout = null);
    Task<AncorarPdfFilaEnfileirarResultado> EnfileirarAsync(
        AncorarPdfFilaEnfileirarEntrada entrada, CancellationToken ct = default);
    Task<AncorarPdfFilaCancelarResultado> CancelarAsync(
        string filaItemId, int userId, string nome, CancellationToken ct = default);
    Task<IReadOnlyList<AncorarPdfFilaItem>> ListarAsync(
        AncorarPdfFilaFiltro filtro, CancellationToken ct = default);
    int ObterProfundidadeAtual();
}
