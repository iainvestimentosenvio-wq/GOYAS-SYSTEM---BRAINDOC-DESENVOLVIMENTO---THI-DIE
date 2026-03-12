namespace Protons.Core.Tarefas.Models;

public sealed class TarefaCadastroEntrada
{
    public int ClienteId { get; init; }
    public string FerramentaId { get; init; } = "generica";
    public string Titulo { get; init; } = string.Empty;
    public DateTime VencimentoLocal { get; init; }
    public int ResponsavelUserId { get; init; }
    public TarefaRecorrencia Recorrencia { get; init; } = TarefaRecorrencia.Nenhuma;
    public int? EsteiraId { get; init; }
}
