using System;
using Protons.Core.Login.Models;

namespace Protons.Core.Login.Repositories;

public interface IAuditLogRepository
{
    void Insert(AuditLogEntry entry);
    string? GetLastHash();
    void InsertWithHashChain(AuditLogEntry entry, Func<AuditLogEntry, string?, string> computeHash);
    IReadOnlyList<AuditLogEntry> GetPage(int page, int pageSize);
}
