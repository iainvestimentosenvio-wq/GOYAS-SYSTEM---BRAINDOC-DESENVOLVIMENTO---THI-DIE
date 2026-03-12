namespace Protons.Core.Clientes.Models;

public sealed class ClienteCadastroEntrada
{
    public string CodigoCliente { get; init; } = string.Empty;
    public string Nome { get; init; } = string.Empty;
    public string? NomeFantasia { get; init; }
    public TipoDocumentoCliente TipoDocumento { get; init; } = TipoDocumentoCliente.CNPJ;
    public string Documento { get; init; } = string.Empty;
    public int? GrupoEmpresarialId { get; init; }
    public string? Email { get; init; }
    public string? Telefone { get; init; }
    public int CriadoPorUserId { get; init; }
}
