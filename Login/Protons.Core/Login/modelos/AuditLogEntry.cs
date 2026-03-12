namespace Protons.Core.Login.Models;

public sealed class AuditLogEntry
{
    public int Id { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public int? UserId { get; set; }
    public string? EmailSnapshot { get; set; }
    public string Acao { get; set; } = string.Empty;
    public string Resultado { get; set; } = string.Empty;
    public string? Detalhes { get; set; }
    public string Maquina { get; set; } = string.Empty;
    public string VersaoApp { get; set; } = string.Empty;
    public string? PrevHash { get; set; }
    public string? Hash { get; set; }
}
