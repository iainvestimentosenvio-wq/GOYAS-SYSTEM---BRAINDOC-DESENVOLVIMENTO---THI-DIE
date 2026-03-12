namespace Protons.Core.Clientes.Exceptions;

public sealed class CodigoClienteDuplicadoException : Exception
{
    public CodigoClienteDuplicadoException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
