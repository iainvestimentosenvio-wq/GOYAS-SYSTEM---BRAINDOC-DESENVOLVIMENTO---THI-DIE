namespace Protons.Core.Tarefas.Models;

public sealed class TarefaFiltroConsulta
{
    public int ClienteId { get; init; }
    public string? Termo { get; init; }
    public TarefaStatus? Status { get; init; }
    public DateTime? VencimentoInicioLocal { get; init; }
    public DateTime? VencimentoFimLocal { get; init; }
    public bool SomenteMinhasTarefas { get; init; }
}
