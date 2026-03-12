namespace Protons.Core.Login.Models;

public sealed class UserDirectoryItem
{
    public int Id { get; init; }
    public string Nome { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public UserRole Role { get; init; } = UserRole.Usuario;
}
