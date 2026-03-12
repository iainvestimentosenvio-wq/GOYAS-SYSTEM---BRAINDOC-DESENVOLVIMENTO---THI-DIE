namespace Protons.Core.Clientes.Models;

public sealed class ResultadoCadastroGrupo
{
    public bool Sucesso { get; init; }
    public string Mensagem { get; init; } = string.Empty;
    public GrupoEmpresarial? Grupo { get; init; }
}
