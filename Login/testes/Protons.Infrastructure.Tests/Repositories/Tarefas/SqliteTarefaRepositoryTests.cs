using FluentAssertions;
using Protons.Core.Clientes.Models;
using Protons.Core.Login.Models;
using Protons.Core.Tarefas.Models;
using Protons.Infrastructure.Clientes.Repositories;
using Protons.Infrastructure.Login.Database;
using Protons.Infrastructure.Login.Repositories;
using Protons.Infrastructure.Tarefas.Repositories;
using Xunit;

namespace Protons.Infrastructure.Tests.Repositories.Tarefas;

public sealed class SqliteTarefaRepositoryTests
{
    [Fact]
    public void CreateEBuscar_DevePersistirTarefaPorCliente()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"protons_tarefas_test_{Guid.NewGuid():N}.db");
        var db = new SqliteDb(dbPath);
        db.EnsureCreated();

        try
        {
            var userRepo = new UserRepository(db);
            var clienteRepo = new SqliteClienteRepository(db);
            var tarefaRepo = new SqliteTarefaRepository(db);

            var adminId = CriarUsuarioAtivo(userRepo, "admin@teste.local", UserRole.Admin);
            var operadorId = CriarUsuarioAtivo(userRepo, "operador@teste.local", UserRole.Usuario);
            var clienteId = clienteRepo.Create(new Cliente
            {
                CodigoCliente = "CLI-TAR-01",
                Nome = "Empresa Teste",
                TipoDocumento = TipoDocumentoCliente.CNPJ,
                Documento = "04252011000110",
                Email = "contato@empresa.teste",
                Telefone = "11999999999",
                Ativo = true,
                CriadoPorUserId = adminId,
                CriadoEmUtc = DateTime.UtcNow,
                AtualizadoEmUtc = DateTime.UtcNow
            });

            var id = tarefaRepo.Create(new Tarefa
            {
                ClienteId = clienteId,
                Titulo = "Fechar folha",
                VencimentoUtc = DateTime.UtcNow.AddDays(1),
                ResponsavelUserId = operadorId,
                Status = TarefaStatus.Agendada,
                Recorrencia = TarefaRecorrencia.Nenhuma,
                Ativa = true,
                CriadoPorUserId = adminId,
                CriadoEmUtc = DateTime.UtcNow,
                AtualizadoEmUtc = DateTime.UtcNow
            });

            var byId = tarefaRepo.GetById(id);
            var busca = tarefaRepo.Buscar(clienteId, "folha", TarefaStatus.Agendada, null, null, null);

            byId.Should().NotBeNull();
            byId!.Titulo.Should().Be("Fechar folha");
            busca.Should().HaveCount(1);
            busca[0].Id.Should().Be(id);
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Fact]
    public void ClientePermissaoRepository_DeveUpsertEListar()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"protons_tarefas_perm_test_{Guid.NewGuid():N}.db");
        var db = new SqliteDb(dbPath);
        db.EnsureCreated();

        try
        {
            var userRepo = new UserRepository(db);
            var clienteRepo = new SqliteClienteRepository(db);
            var permissaoRepo = new SqliteClientePermissaoRepository(db);

            var adminId = CriarUsuarioAtivo(userRepo, "admin2@teste.local", UserRole.Admin);
            var userA = CriarUsuarioAtivo(userRepo, "a@teste.local", UserRole.Usuario);
            var userB = CriarUsuarioAtivo(userRepo, "b@teste.local", UserRole.Usuario);

            var clienteId = clienteRepo.Create(new Cliente
            {
                CodigoCliente = "CLI-TAR-02",
                Nome = "Empresa Permissao",
                TipoDocumento = TipoDocumentoCliente.CNPJ,
                Documento = "11111111000199",
                Ativo = true,
                CriadoPorUserId = adminId,
                CriadoEmUtc = DateTime.UtcNow,
                AtualizadoEmUtc = DateTime.UtcNow
            });

            permissaoRepo.DefinirPermissoes(clienteId, adminId, new List<PermissaoClienteEntrada>
            {
                new() { UserId = userA, PodeEditar = true },
                new() { UserId = userB, PodeEditar = false }
            });

            var inicial = permissaoRepo.ListarPorCliente(clienteId);
            inicial.Should().HaveCount(2);
            inicial.Single(x => x.UserId == userA).PodeEditar.Should().BeTrue();

            permissaoRepo.DefinirPermissoes(clienteId, adminId, new List<PermissaoClienteEntrada>
            {
                new() { UserId = userA, PodeEditar = false }
            });

            var final = permissaoRepo.ListarPorCliente(clienteId);
            final.Should().HaveCount(1);
            final.Single().UserId.Should().Be(userA);
            final.Single().PodeEditar.Should().BeFalse();
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Fact]
    public void BuscarPorClienteIds_ComMaisDeUmLote_DeveRetornarResultadosOrdenados()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"protons_tarefas_lotes_{Guid.NewGuid():N}.db");
        var db = new SqliteDb(dbPath);
        db.EnsureCreated();

        try
        {
            var userRepo = new UserRepository(db);
            var clienteRepo = new SqliteClienteRepository(db);
            var tarefaRepo = new SqliteTarefaRepository(db);

            var adminId = CriarUsuarioAtivo(userRepo, "admin-lotes@teste.local", UserRole.Admin);
            var operadorId = CriarUsuarioAtivo(userRepo, "operador-lotes@teste.local", UserRole.Usuario);
            var clienteIds = new List<int>();

            for (var i = 1; i <= 1005; i++)
            {
                var clienteId = clienteRepo.Create(new Cliente
                {
                    CodigoCliente = $"CLI-LT-{i:D4}",
                    Nome = $"Cliente Lote {i:D4}",
                    TipoDocumento = TipoDocumentoCliente.CNPJ,
                    Documento = $"{i:D14}",
                    Email = $"cliente{i:D4}@teste.local",
                    Telefone = $"1199{i:D06}",
                    Ativo = true,
                    CriadoPorUserId = adminId,
                    CriadoEmUtc = DateTime.UtcNow,
                    AtualizadoEmUtc = DateTime.UtcNow
                });
                clienteIds.Add(clienteId);
            }

            var primeiroClienteId = clienteIds[0];
            var ultimoClienteId = clienteIds[^1];

            var primeiraTarefaId = tarefaRepo.Create(new Tarefa
            {
                ClienteId = primeiroClienteId,
                Titulo = "Tarefa Primeiro Cliente",
                VencimentoUtc = new DateTime(2026, 3, 8, 10, 0, 0, DateTimeKind.Utc),
                ResponsavelUserId = operadorId,
                Status = TarefaStatus.Agendada,
                Recorrencia = TarefaRecorrencia.Nenhuma,
                Ativa = true,
                CriadoPorUserId = adminId,
                CriadoEmUtc = DateTime.UtcNow,
                AtualizadoEmUtc = DateTime.UtcNow
            });

            var ultimaTarefaId = tarefaRepo.Create(new Tarefa
            {
                ClienteId = ultimoClienteId,
                Titulo = "Tarefa Ultimo Cliente",
                VencimentoUtc = new DateTime(2026, 3, 8, 15, 0, 0, DateTimeKind.Utc),
                ResponsavelUserId = operadorId,
                Status = TarefaStatus.Agendada,
                Recorrencia = TarefaRecorrencia.Nenhuma,
                Ativa = true,
                CriadoPorUserId = adminId,
                CriadoEmUtc = DateTime.UtcNow,
                AtualizadoEmUtc = DateTime.UtcNow
            });

            var resultado = tarefaRepo.BuscarPorClienteIds(clienteIds, null, null, null, null, null);

            resultado.Should().HaveCount(2);
            resultado.Select(x => x.Id).Should().Equal(primeiraTarefaId, ultimaTarefaId);
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
            Empresa = "Empresa",
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
