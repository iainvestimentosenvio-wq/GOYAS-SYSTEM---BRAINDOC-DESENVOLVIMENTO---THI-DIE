namespace Protons.Core.Tarefas.Models;

public sealed class AncorarPdfFalhaDeNegocioException : Exception
{
    public string Codigo { get; }
    public AncorarPdfError? Error { get; }

    public AncorarPdfFalhaDeNegocioException(string codigo, string mensagem)
        : base(mensagem)
    {
        Codigo = codigo;
        Error = AncorarPdfErrorCatalog.Create(codigo, mensagem);
    }

    public AncorarPdfFalhaDeNegocioException(string codigo, string mensagem, Exception inner)
        : base(mensagem, inner)
    {
        Codigo = codigo;
        Error = AncorarPdfErrorCatalog.Create(codigo, mensagem);
    }

    public AncorarPdfFalhaDeNegocioException(AncorarPdfError error)
        : base(error.Message)
    {
        Codigo = error.Code;
        Error = error;
    }
}
