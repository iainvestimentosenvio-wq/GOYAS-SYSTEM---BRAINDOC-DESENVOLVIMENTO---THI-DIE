using System;
using System.IO;
using FluentAssertions;
using Protons.Infrastructure.Login.Database;
using Protons.Infrastructure.Login.Repositories;
using Xunit;

namespace Protons.Infrastructure.Tests.Repositories;

public sealed class SqliteUserRepositoryTests
{
    [Fact]
    public void GetByEmail_ShouldReturnNull_WhenEmailIsNullOrWhitespace()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"protons_test_{Guid.NewGuid():N}.db");
        var db = new SqliteDb(dbPath);
        db.EnsureCreated();

        try
        {
            var repo = new UserRepository(db);

            repo.GetByEmail(null).Should().BeNull();
            repo.GetByEmail(string.Empty).Should().BeNull();
            repo.GetByEmail("   ").Should().BeNull();
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }
}
