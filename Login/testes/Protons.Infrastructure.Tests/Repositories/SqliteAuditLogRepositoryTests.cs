using System;
using System.IO;
using FluentAssertions;
using Protons.Core.Login.Models;
using Protons.Infrastructure.Login.Database;
using Protons.Infrastructure.Login.Repositories;
using Xunit;

namespace Protons.Infrastructure.Tests.Repositories;

public sealed class SqliteAuditLogRepositoryTests
{
    [Fact]
    public void GetPage_ShouldReturnLogsInDescendingOrder()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"protons_audit_test_{Guid.NewGuid():N}.db");
        var db = new SqliteDb(dbPath);
        db.EnsureCreated();

        try
        {
            var repo = new AuditLogRepository(db);
            repo.Insert(CreateEntry("A1"));
            repo.Insert(CreateEntry("A2"));
            repo.Insert(CreateEntry("A3"));

            var page1 = repo.GetPage(1, 2);
            page1.Should().HaveCount(2);
            page1[0].Acao.Should().Be("A3");
            page1[1].Acao.Should().Be("A2");
            page1[0].Id.Should().BeGreaterThan(page1[1].Id);

            var page2 = repo.GetPage(2, 2);
            page2.Should().HaveCount(1);
            page2[0].Acao.Should().Be("A1");
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    private static AuditLogEntry CreateEntry(string acao)
    {
        return new AuditLogEntry
        {
            TimestampUtc = DateTime.UtcNow,
            Acao = acao,
            Resultado = "OK",
            Maquina = "TEST",
            VersaoApp = "0.0.0"
        };
    }
}
