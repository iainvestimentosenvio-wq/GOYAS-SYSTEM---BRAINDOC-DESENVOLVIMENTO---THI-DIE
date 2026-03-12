using Protons.Core.Tarefas.Models;

namespace Protons.Core.Tarefas.Repositories;

public interface IAncorarPdfConfiguracaoRepository
{
    AncorarPdfConfiguracaoTarefa? ObterPorTarefaId(int tarefaId);

    void Salvar(
        AncorarPdfConfiguracaoTarefa configuracao,
        AncorarPdfTemplateHistoricoItem? historicoAlteracao);

    IReadOnlyList<AncorarPdfTemplateHistoricoItem> ListarHistoricoTemplate(int tarefaId, int limite);

    bool ExisteNomeAtivoNoEscopo(int clienteId, int esteiraId, string nomeTarefaPersonalizado, int? tarefaIdIgnorar);

    IReadOnlyList<AncorarPdfSchedulerAgendamentoAtivo> ListarAgendamentosAtivosAte(DateTime referenciaUtc, int limite);

    long RegistrarBacklogPendente(AncorarPdfSchedulerBacklogRegistro registro);

    AncorarPdfBacklogPendenteItem? ObterBacklogPendentePorId(long backlogId);

    IReadOnlyList<AncorarPdfBacklogPendenteItem> ListarBacklogPendente(int clienteId, int limite);

    bool ResolverBacklogPendente(
        long backlogId,
        AncorarPdfBacklogStatus statusFinal,
        int decididoPorUserId,
        string decididoPorNome,
        DateTime decididoEmUtc,
        string? observacao);

    void RegistrarEventoScheduler(AncorarPdfSchedulerEventoRegistro eventoRegistro);

    /// <summary>
    /// Consulta histórico de eventos operacionais filtrado por cliente, tarefa e/ou janela de tempo.
    /// Retorna em ordem decrescente de OcorreuEmUtc (mais recentes primeiro).
    /// </summary>
    IReadOnlyList<AncorarPdfEventoHistoricoItem> ListarEventosOperacionais(
        AncorarPdfEventoHistoricoFiltro filtro);
}
