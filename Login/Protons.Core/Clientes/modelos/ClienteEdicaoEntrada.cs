namespace Protons.Core.Clientes.Models;

public sealed class ClienteEdicaoEntrada
{
    public int ClienteId { get; init; }
    public string CodigoCliente { get; init; } = string.Empty;
    public string Nome { get; init; } = string.Empty;
    public string? NomeFantasia { get; init; }
    public int? GrupoEmpresarialId { get; init; }
    public string? Email { get; init; }
    public string? Telefone { get; init; }
    public int AtualizadoPorUserId { get; init; }
}
