namespace Protons.Core.Clientes.Models;

public sealed class ResultadoCadastroCliente
{
    public bool Sucesso { get; init; }
    public string Mensagem { get; init; } = string.Empty;
    public Cliente? Cliente { get; init; }
    public ClienteCadastroErroCodigo? CodigoErro { get; init; }
    public ClienteCadastroCampoErro? CampoErro { get; init; }
    public ClienteCadastroSeveridade Severidade { get; init; } = ClienteCadastroSeveridade.Info;
    public string? ReferenciaErro { get; init; }
}
