using System;
using FluentAssertions;
using Npgsql;
using Protons.Core.Login.Models;
using Protons.Infrastructure.Login.Database;
using Protons.Infrastructure.Login.Repositories;
using Xunit;
using Xunit.Abstractions;

namespace Protons.Infrastructure.Tests.Repositories;

public sealed class PostgresUserRepositoryTests
{
    private const string ConnectionStringEnvVar = "PROTONS_POSTGRES_TEST_CONNECTION_STRING";
    private readonly ITestOutputHelper _output;

    public PostgresUserRepositoryTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Map_ShouldFallback_WhenEnumsInvalid()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvVar);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            _output.WriteLine($"Skipped: {ConnectionStringEnvVar} not set.");
            return;
        }

        var db = new PostgresDb(connectionString);
        db.EnsureCreated();
        var repo = new PostgresUserRepository(db);
        var email = $"test_{Guid.NewGuid():N}@example.com";

        using var connection = new NpgsqlConnection(connectionString);
        connection.Open();
        try
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = @"
INSERT INTO Users (Empresa, Nome, CPF, Cargo, Email, SenhaHash, SenhaSalt, IteracoesPBKDF2, Status, Role, FalhasLogin, LockoutAteUtc, CriadoEmUtc, AtualizadoEmUtc, UltimoLoginUtc)
VALUES (@empresa, @nome, @cpf, @cargo, @email, @hash, @salt, @iter, @status, @role, @falhas, @lockout, @criado, @atualizado, @ultimo);
";
                cmd.Parameters.AddWithValue("@empresa", "Empresa Teste");
                cmd.Parameters.AddWithValue("@nome", "Usuario Teste");
                cmd.Parameters.AddWithValue("@cpf", "123.456.789-09");
                cmd.Parameters.AddWithValue("@cargo", "Dev");
                cmd.Parameters.AddWithValue("@email", email);
                cmd.Parameters.AddWithValue("@hash", "hash");
                cmd.Parameters.AddWithValue("@salt", "salt");
                cmd.Parameters.AddWithValue("@iter", 100000);
                cmd.Parameters.AddWithValue("@status", "invalido_status");
                cmd.Parameters.AddWithValue("@role", "invalido_role");
                cmd.Parameters.AddWithValue("@falhas", 0);
                cmd.Parameters.AddWithValue("@lockout", DBNull.Value);
                cmd.Parameters.AddWithValue("@criado", DateTime.UtcNow.ToString("o"));
                cmd.Parameters.AddWithValue("@atualizado", DateTime.UtcNow.ToString("o"));
                cmd.Parameters.AddWithValue("@ultimo", DBNull.Value);
                cmd.ExecuteNonQuery();
            }

            var user = repo.GetByEmail(email);
            user.Should().NotBeNull();
            user!.Status.Should().Be(UserStatus.Pendente);
            user.Role.Should().Be(UserRole.Usuario);
        }
        finally
        {
            using var cleanup = connection.CreateCommand();
            cleanup.CommandText = "DELETE FROM Users WHERE Email = @email";
            cleanup.Parameters.AddWithValue("@email", email);
            cleanup.ExecuteNonQuery();
        }
    }

    [Fact]
    public void GetByEmail_ShouldReturnNull_WhenEmailIsNullOrWhitespace()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvVar);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            _output.WriteLine($"Skipped: {ConnectionStringEnvVar} not set.");
            return;
        }

        var db = new PostgresDb(connectionString);
        db.EnsureCreated();
        var repo = new PostgresUserRepository(db);

        repo.GetByEmail(null).Should().BeNull();
        repo.GetByEmail(string.Empty).Should().BeNull();
        repo.GetByEmail("   ").Should().BeNull();
    }
}
