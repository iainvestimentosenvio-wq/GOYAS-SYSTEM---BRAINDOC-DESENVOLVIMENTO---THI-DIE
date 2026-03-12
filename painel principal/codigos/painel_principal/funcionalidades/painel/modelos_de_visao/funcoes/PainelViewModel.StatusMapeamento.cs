using Protons.Core.Tarefas.Models;

namespace Protons.UI.Painel.ViewModels;

public sealed partial class PainelViewModel
{
    private static string ObterStatusTextoPadrao(TarefaStatus status)
    {
        return status switch
        {
            TarefaStatus.Agendada => "AGENDADA",
            TarefaStatus.EmAndamento => "EM ANDAMENTO",
            TarefaStatus.Bloqueada => "ERRO",
            TarefaStatus.Concluida => "SUCESSO",
            _ => status.ToString().ToUpperInvariant()
        };
    }

    private static string ObterStatusTextoPadraoRegua(TarefaStatus status)
    {
        return status switch
        {
            TarefaStatus.EmAndamento => "ANDAMENTO",
            TarefaStatus.Concluida => "CONCLUIDA",
            _ => ObterStatusTextoPadrao(status)
        };
    }

    private static string ObterCorStatusPadrao(TarefaStatus status)
    {
        return status switch
        {
            TarefaStatus.Agendada => CorAmarela,
            TarefaStatus.EmAndamento => CorAzul,
            TarefaStatus.Bloqueada => CorVermelha,
            TarefaStatus.Concluida => CorVerde,
            _ => CorAmarela
        };
    }
}
