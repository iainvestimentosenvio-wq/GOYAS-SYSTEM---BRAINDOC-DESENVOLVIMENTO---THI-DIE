namespace Protons.Core.Login.Models;

public sealed class AuthResult
{
    public bool Sucesso { get; init; }
    public string Mensagem { get; init; } = string.Empty;
    public bool Bloqueado { get; init; }
    public bool Pendente { get; init; }
    public bool SemPermissao { get; init; }
    public int? UserId { get; init; }
    public string? Nome { get; init; }
    public UserRole? Role { get; init; }
}
