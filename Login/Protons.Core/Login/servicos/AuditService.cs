using System.Security.Cryptography;
using System.Text;
using Protons.Core.Login.Models;
using Protons.Core.Login.Repositories;

namespace Protons.Core.Login.Services;

public sealed class AuditService : IAuditService
{
    private readonly IAuditLogRepository _repository;

    public AuditService(IAuditLogRepository repository)
    {
        _repository = repository;
    }

    public void Registrar(AuditLogEntry entry, bool usarHashChain)
    {
        if (usarHashChain)
        {
            _repository.InsertWithHashChain(entry, CalcularHash);
        }
        else
        {
            _repository.Insert(entry);
        }
    }

    private static string CalcularHash(AuditLogEntry entry, string? prev)
    {
        var baseText = $"{entry.TimestampUtc:o}|{entry.UserId}|{entry.EmailSnapshot}|{entry.Acao}|{entry.Resultado}|{entry.Detalhes}|{entry.Maquina}|{entry.VersaoApp}|{prev}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(baseText));
        return Convert.ToBase64String(bytes);
    }
}
