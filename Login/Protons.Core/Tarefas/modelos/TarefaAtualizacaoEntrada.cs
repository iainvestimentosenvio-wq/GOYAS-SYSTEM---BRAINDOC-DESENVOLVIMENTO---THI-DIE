namespace Protons.Core.Tarefas.Models;

public sealed class TarefaAtualizacaoEntrada
{
    public int TarefaId { get; init; }
    public string? FerramentaId { get; init; }
    public string? Titulo { get; init; }
    public DateTime? VencimentoLocal { get; init; }
    public int? ResponsavelUserId { get; init; }
    public TarefaStatus? Status { get; init; }
    public TarefaRecorrencia? Recorrencia { get; init; }
    public int? EsteiraId { get; init; }
}
