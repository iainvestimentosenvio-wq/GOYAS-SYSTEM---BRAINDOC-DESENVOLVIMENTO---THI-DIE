using Protons.Core.Tarefas.Models;

namespace Protons.Core.Tarefas.Services;

public interface IAncorarPdfConfiguracaoService
{
    AncorarPdfConfiguracaoTarefa? ObterPorTarefaId(int tarefaId, int solicitanteUserId);
    AncorarPdfConfiguracaoTarefa CriarOuAtualizar(AncorarPdfSalvarEntrada entrada, int solicitanteUserId);
    IReadOnlyList<AncorarPdfTemplateHistoricoItem> ListarHistoricoTemplate(int tarefaId, int solicitanteUserId, int limite);
    IReadOnlyList<AncorarPdfBacklogPendenteItem> ListarBacklogPendente(int clienteId, int solicitanteUserId, int limite);
    bool RegistrarDecisaoBacklog(AncorarPdfBacklogDecisaoEntrada entrada, int solicitanteUserId);
}
