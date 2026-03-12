namespace Protons.Core.Clientes.Models;

public sealed class Cliente
{
    public int Id { get; set; }
    public string CodigoCliente { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;
    public string? NomeFantasia { get; set; }
    public TipoDocumentoCliente TipoDocumento { get; set; } = TipoDocumentoCliente.CNPJ;
    public string Documento { get; set; } = string.Empty; // Somente digitos
    public int? GrupoEmpresarialId { get; set; }
    public string? GrupoEmpresarialNome { get; set; }
    public string? Email { get; set; }
    public string? Telefone { get; set; }
    public bool Ativo { get; set; } = true;
    public int CriadoPorUserId { get; set; }
    public DateTime CriadoEmUtc { get; set; } = DateTime.UtcNow;
    public DateTime AtualizadoEmUtc { get; set; } = DateTime.UtcNow;
}
