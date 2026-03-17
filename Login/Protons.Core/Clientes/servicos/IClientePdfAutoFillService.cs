namespace Protons.Core.Clientes.Services;

/// <summary>
/// Extrai dados de um PDF para preencher automaticamente o cadastro de cliente.
/// Reutiliza a infraestrutura do Ancorar PDF (extração de texto + Smart Detector).
/// </summary>
public interface IClientePdfAutoFillService
{
    /// <summary>
    /// Analisa um PDF e retorna os dados de cliente encontrados.
    /// </summary>
    Task<ClientePdfAutoFillResult> ExtrairDadosClienteAsync(string pdfPath, CancellationToken ct = default);
}

/// <summary>
/// Resultado da extração automática de dados de cliente a partir de PDF.
/// </summary>
public sealed class ClientePdfAutoFillResult
{
    public bool Sucesso { get; init; }
    public string? Mensagem { get; init; }
    public string? Cnpj { get; init; }
    public string? Cpf { get; init; }
    public string? RazaoSocial { get; init; }
    public string? NomeFantasia { get; init; }
    public string? Email { get; init; }
    public string? Telefone { get; init; }
    public string? Cep { get; init; }
    public string? Endereco { get; init; }
}
