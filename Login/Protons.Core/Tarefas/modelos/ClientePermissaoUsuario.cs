namespace Protons.Core.Tarefas.Models;

public sealed class ClientePermissaoUsuario
{
    public int ClienteId { get; init; }
    public int UserId { get; init; }
    public bool PodeEditar { get; init; }
    public int ConcedidoPorUserId { get; init; }
    public DateTime CriadoEmUtc { get; init; } = DateTime.UtcNow;
    public DateTime AtualizadoEmUtc { get; init; } = DateTime.UtcNow;
}
