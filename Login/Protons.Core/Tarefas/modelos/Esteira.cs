namespace Protons.Core.Tarefas.Models;

public sealed class Esteira
{
    public int Id { get; set; }
    public int ClienteId { get; set; }
    public string Nome { get; set; } = string.Empty;
    public int Ordem { get; set; }
    public bool Ativa { get; set; } = true;
    public DateTime CriadoEmUtc { get; set; } = DateTime.UtcNow;
}
