using Protons.Core.Login.Models;

namespace Protons.Core.Login.Repositories;

public interface ITarefaAlteracaoRepository
{
    void RegistrarAlteracao(TarefaAlteracao alteracao);
    IReadOnlyList<TarefaAlteracao> ListarPorTarefa(int tarefaId, int limit = 50);
}
