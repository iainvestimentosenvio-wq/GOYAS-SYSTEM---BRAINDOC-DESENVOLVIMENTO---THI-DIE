namespace Protons.Core.Clientes.Exceptions;

public sealed class DocumentoClienteDuplicadoException : Exception
{
    public DocumentoClienteDuplicadoException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
