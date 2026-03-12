namespace Protons.Core.Clientes.Models;

public sealed class GrupoEmpresarial
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string NomeNormalizado { get; set; } = string.Empty;
    public bool Ativo { get; set; } = true;
    public int CriadoPorUserId { get; set; }
    public DateTime CriadoEmUtc { get; set; } = DateTime.UtcNow;
    public DateTime AtualizadoEmUtc { get; set; } = DateTime.UtcNow;
}
