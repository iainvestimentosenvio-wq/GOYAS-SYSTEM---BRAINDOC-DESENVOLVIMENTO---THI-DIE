namespace Protons.Core.Login.Models;

public sealed class User
{
    public int Id { get; set; }
    public string Empresa { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;
    public string Cpf { get; set; } = string.Empty;
    public string Cargo { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    public string SenhaHash { get; set; } = string.Empty;
    public string SenhaSalt { get; set; } = string.Empty;
    public int IteracoesPbkdf2 { get; set; }

    public UserStatus Status { get; set; } = UserStatus.Pendente;
    public UserRole Role { get; set; } = UserRole.Usuario;

    public int FalhasLogin { get; set; }
    public int LockoutsConsecutivos { get; set; }
    public DateTime? LockoutAteUtc { get; set; }

    /// <summary>Admin ou Supremo responsável por este usuário (null para Supremo).</summary>
    public int? ResponsavelAdminId { get; set; }

    public DateTime CriadoEmUtc { get; set; } = DateTime.UtcNow;
    public DateTime AtualizadoEmUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UltimoLoginUtc { get; set; }
    public DateTime? ExcluidoEmUtc { get; set; }
}
