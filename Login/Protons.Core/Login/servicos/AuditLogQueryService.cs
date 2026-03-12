using Protons.Core.Login.Models;
using Protons.Core.Login.Repositories;

namespace Protons.Core.Login.Services;

public sealed class AuditLogQueryService : IAuditLogQueryService
{
    private readonly IAuditLogRepository _repository;

    public AuditLogQueryService(IAuditLogRepository repository)
    {
        _repository = repository;
    }

    public IReadOnlyList<AuditLogEntry> GetPage(int page, int pageSize)
    {
        return _repository.GetPage(page, pageSize);
    }
}
