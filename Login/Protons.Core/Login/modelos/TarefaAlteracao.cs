namespace Protons.Core.Login.Models;

/// <summary>Registro imutável de uma alteração feita em uma tarefa (audit trail).</summary>
public sealed class TarefaAlteracao
{
    public int Id { get; set; }
    public int TarefaId { get; set; }
    public int AlteradoPorUserId { get; set; }
    public string AlteradoPorNome { get; set; } = string.Empty;
    public string CampoAlterado { get; set; } = string.Empty;
    public string? ValorAnterior { get; set; }
    public string? ValorNovo { get; set; }
    public DateTime AlteradoEmUtc { get; set; } = DateTime.UtcNow;
}
