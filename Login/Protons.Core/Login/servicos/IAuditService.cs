using Protons.Core.Login.Models;

namespace Protons.Core.Login.Services;

public interface IAuditService
{
    void Registrar(AuditLogEntry entry, bool usarHashChain);
}
