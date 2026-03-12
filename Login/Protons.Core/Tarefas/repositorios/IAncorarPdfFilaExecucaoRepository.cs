using Protons.Core.Tarefas.Models;

namespace Protons.Core.Tarefas.Repositories;

public interface IAncorarPdfFilaExecucaoRepository
{
    AncorarPdfFilaEnfileirarResultado Enfileirar(AncorarPdfFilaItem item);
    AncorarPdfFilaItem? TentarReclamar(string workerId);
    void AtualizarStatus(
        string filaItemId,
        AncorarPdfFilaStatus status,
        DateTime? iniciadoEmUtc = null,
        DateTime? finalizadoEmUtc = null,
        AncorarPdfFilaFalhaCategoria? categoriaFalha = null,
        string? erroCodigo = null,
        string? erroDetalhe = null,
        int? tentativaAtual = null);
    bool TentarCancelar(string filaItemId, string canceladoPorNome, DateTime canceladoEmUtc);
    IReadOnlyList<AncorarPdfFilaItem> ListarParaReidratar(int limite = 200);
    IReadOnlyList<AncorarPdfFilaItem> Listar(AncorarPdfFilaFiltro filtro);
    int ContarAguardando();
}
