namespace Protons.Core.Clientes.Models;

public enum ClienteCadastroErroCodigo
{
    ValidacaoNegocio = 0,
    UsuarioResponsavelInvalido = 1,
    NomeObrigatorio = 2,
    NomeInvalido = 3,
    CodigoObrigatorio = 4,
    CodigoInvalido = 5,
    CodigoDuplicado = 6,
    DocumentoCaracteresInvalidos = 7,
    DocumentoObrigatorio = 8,
    DocumentoTamanhoInvalido = 9,
    CpfInvalido = 10,
    CnpjInvalido = 11,
    DocumentoDuplicado = 12,
    GrupoInvalido = 13,
    EmailInvalido = 14,
    TelefoneInvalido = 15,
    FalhaTecnica = 16
}
