using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Protons.Core.Login.Models;
using Protons.Core.Login.Services;
using Protons.Infrastructure.Login.Database;
using Protons.Infrastructure.Login.Repositories;
using Xunit;

namespace Protons.Infrastructure.Tests.Integration;

public sealed class AuthFlowSqliteTests
{
    [Fact]
    public void FullFlow_ShouldCreateApproveLoginLogout_AndWriteAuditLogs()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"protons_flow_test_{Guid.NewGuid():N}.db");
        var db = new SqliteDb(dbPath);
        db.EnsureCreated();

        try
        {
            var userRepo = new UserRepository(db);
            var auditRepo = new AuditLogRepository(db);
            var auditService = new AuditService(auditRepo);
            var passwordHasher = new PasswordHasher();
            var authService = new AuthService(
                userRepo,
                passwordHasher,
                auditService,
                "TEST-MACHINE",
                "0.0.0",
                usarHashChain: false);

            var adminId = CreateAdminUser(userRepo, passwordHasher);

            var email = $"user_{Guid.NewGuid():N}@example.com";
            var password = "SenhaForte#123";
            var novoUsuario = new User
            {
                Empresa = "Empresa Teste",
                Nome = "Usuario Teste",
                Cargo = "Dev",
                Email = email,
                Cpf = "123.456.789-09"
            };

            var createResult = authService.CriarConta(novoUsuario, password);
            createResult.Sucesso.Should().BeTrue();

            var pendentes = authService.ListarPendentes();
            pendentes.Should().ContainSingle(u => u.Email == email);
            var userId = pendentes.Single(u => u.Email == email).Id;

            authService.AprovarUsuario(userId, adminId, "OK");

            var loginResult = authService.Autenticar(email, password, false);
            loginResult.Sucesso.Should().BeTrue();
            loginResult.UserId.Should().Be(userId);

            authService.RegistrarLogout(userId, email);

            var logs = auditRepo.GetPage(1, 50);
            logs.Select(l => l.Acao).Should().Contain(new[]
            {
                "CRIAR_CONTA",
                "APROVAR_USUARIO",
                "LOGIN_SUCESSO",
                "LOGOUT"
            });
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    private static int CreateAdminUser(UserRepository repo, PasswordHasher hasher)
    {
        var (hash, salt, iterations) = hasher.HashPassword("AdminForte#123");
        var admin = new User
        {
            Empresa = "Admin",
            Nome = "Administrador",
            Cargo = "Admin",
            Email = $"admin_{Guid.NewGuid():N}@example.com",
            Cpf = "111.444.777-35",
            SenhaHash = hash,
            SenhaSalt = salt,
            IteracoesPbkdf2 = iterations,
            Status = UserStatus.Ativo,
            Role = UserRole.Admin,
            FalhasLogin = 0,
            CriadoEmUtc = DateTime.UtcNow,
            AtualizadoEmUtc = DateTime.UtcNow
        };

        return repo.Create(admin);
    }
}
