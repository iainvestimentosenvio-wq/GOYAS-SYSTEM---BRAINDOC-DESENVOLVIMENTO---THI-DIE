using Protons.Core.Login.Models;

namespace Protons.Core.Login.Services;

public interface IAuditLogQueryService
{
    IReadOnlyList<AuditLogEntry> GetPage(int page, int pageSize);
}
