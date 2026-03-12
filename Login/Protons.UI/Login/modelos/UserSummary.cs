namespace Protons.UI.Login.Models;

public sealed class UserSummary
{
    public int Id { get; init; }
    public string Empresa { get; init; } = string.Empty;
    public string Nome { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Cargo { get; init; } = string.Empty;
}
