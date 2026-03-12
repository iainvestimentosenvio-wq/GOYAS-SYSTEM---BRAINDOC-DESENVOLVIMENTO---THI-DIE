using FluentAssertions;
using Microsoft.Data.Sqlite;
using Protons.Core.Clientes.Models;
using Protons.Core.Login.Models;
using Protons.Core.Tarefas.Models;
using Protons.Infrastructure.Clientes.Repositories;
using Protons.Infrastructure.Login.Database;
using Protons.Infrastructure.Login.Repositories;
using Protons.Infrastructure.Tarefas.Repositories;
using Xunit;

namespace Protons.Infrastructure.Tests.Repositories.Tarefas;

public sealed class AncorarPdfChecklist01PersistenceTests
{
    [Fact]
    public void SqliteDb_EnsureCreated_DeveManterSchemaBaseDeTarefasParaChecklist01()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"protons_checklist01_schema_{Guid.NewGuid():N}.db");
        var db = new SqliteDb(dbPath);

        try
        {
            db.EnsureCreated();

            using var connection = new SqliteConnection(db.ConnectionString);
            connection.Open();

            using var versaoCmd = connection.CreateCommand();
            versaoCmd.CommandText = "SELECT Valor FROM SchemaMetadata WHERE Chave = 'SchemaVersion' LIMIT 1";
            var versao = versaoCmd.ExecuteScalar()?.ToString();
            versao.Should().NotBeNullOrWhiteSpace();

            using var tarefasCmd = connection.CreateCommand();
            tarefasCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='Tarefas'";
            tarefasCmd.ExecuteScalar()?.ToString().Should().Be("Tarefas");

            using var permissaoCmd = connection.CreateCommand();
            permissaoCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='ClientePermissoesUsuarios'";
            permissaoCmd.ExecuteScalar()?.ToString().Should().Be("ClientePermissoesUsuarios");
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Fact]
    public void SqliteTarefaRepository_DevePersistirFluxoBaseUsadoNoChecklist01()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"protons_checklist01_repo_{Guid.NewGuid():N}.db");
        var db = new SqliteDb(dbPath);
        db.EnsureCreated();

        try
        {
            var userRepo = new UserRepository(db);
            var clienteRepo = new SqliteClienteRepository(db);
            var tarefaRepo = new SqliteTarefaRepository(db);

            var adminId = CriarUsuarioAtivo(userRepo, "admin.checklist01@protons.local", UserRole.Admin);
            var operadorId = CriarUsuarioAtivo(userRepo, "operador.checklist01@protons.local", UserRole.Usuario);
            var clienteId = clienteRepo.Create(new Cliente
            {
                CodigoCliente = "CLI-CHK-01",
                Nome = "Cliente Checklist 01",
                TipoDocumento = TipoDocumentoCliente.CNPJ,
                Documento = "04252011000110",
                Ativo = true,
                CriadoPorUserId = adminId,
                CriadoEmUtc = DateTime.UtcNow,
                AtualizadoEmUtc = DateTime.UtcNow
            });

            var criadoId = tarefaRepo.Create(new Tarefa
            {
                ClienteId = clienteId,
                Titulo = "Ancorar PDF - fluxo base",
                VencimentoUtc = DateTime.UtcNow.AddMinutes(30),
                ResponsavelUserId = operadorId,
                Status = TarefaStatus.Agendada,
                Recorrencia = TarefaRecorrencia.Nenhuma,
                Ativa = true,
                CriadoPorUserId = adminId,
                CriadoEmUtc = DateTime.UtcNow,
                AtualizadoEmUtc = DateTime.UtcNow
            });

            var carregada = tarefaRepo.GetById(criadoId);

            carregada.Should().NotBeNull();
            carregada!.Titulo.Should().Be("Ancorar PDF - fluxo base");
            carregada.Status.Should().Be(TarefaStatus.Agendada);
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    private static int CriarUsuarioAtivo(UserRepository repo, string email, UserRole role)
    {
        return repo.Create(new User
        {
            Empresa = "Protons",
            Nome = email.Split('@')[0],
            Cpf = "111.444.777-35",
            Cargo = "Operador",
            Email = email,
            SenhaHash = "hash",
            SenhaSalt = "salt",
            IteracoesPbkdf2 = 100_000,
            Status = UserStatus.Ativo,
            Role = role,
            FalhasLogin = 0,
            CriadoEmUtc = DateTime.UtcNow,
            AtualizadoEmUtc = DateTime.UtcNow
        });
    }
}
