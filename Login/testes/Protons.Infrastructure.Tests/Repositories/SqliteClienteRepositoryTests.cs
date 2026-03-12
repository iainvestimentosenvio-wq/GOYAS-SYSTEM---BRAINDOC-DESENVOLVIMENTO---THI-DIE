using System;
using System.IO;
using FluentAssertions;
using Protons.Core.Clientes.Exceptions;
using Protons.Core.Clientes.Models;
using Protons.Infrastructure.Clientes.Repositories;
using Protons.Infrastructure.Clientes.Security;
using Protons.Infrastructure.Login.Database;
using Xunit;

namespace Protons.Infrastructure.Tests.Repositories;

public sealed class SqliteClienteRepositoryTests
{
    [Fact]
    public void CreateEGetByDocumento_DevePersistirCliente()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"protons_clientes_test_{Guid.NewGuid():N}.db");
        var db = new SqliteDb(dbPath, CriarProtector(), allowLegacyPlaintext: false);
        db.EnsureCreated();

        try
        {
            var repo = new SqliteClienteRepository(db, CriarProtector(), allowLegacyPlaintext: false);
            var cliente = new Cliente
            {
                CodigoCliente = "CLI-0001",
                Nome = "Empresa Teste",
                TipoDocumento = TipoDocumentoCliente.CNPJ,
                Documento = "04252011000110",
                Email = "contato@empresa.teste",
                Telefone = "11999999999",
                Ativo = true,
                CriadoPorUserId = 1,
                CriadoEmUtc = DateTime.UtcNow,
                AtualizadoEmUtc = DateTime.UtcNow
            };

            var id = repo.Create(cliente);
            var persisted = repo.GetByDocumento("04252011000110");
            var byId = repo.GetById(id);

            id.Should().BeGreaterThan(0);
            persisted.Should().NotBeNull();
            persisted!.Id.Should().Be(id);
            persisted.Nome.Should().Be("Empresa Teste");
            persisted.TipoDocumento.Should().Be(TipoDocumentoCliente.CNPJ);
            persisted.Documento.Should().Be("04252011000110");

            byId.Should().NotBeNull();
            byId!.Id.Should().Be(id);
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Fact]
    public void Create_DevePersistirDocumentoEmailTelefoneCifrados()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"protons_clientes_test_{Guid.NewGuid():N}.db");
        var protector = CriarProtector();
        var db = new SqliteDb(dbPath, protector, allowLegacyPlaintext: false);
        db.EnsureCreated();

        try
        {
            var repo = new SqliteClienteRepository(db, protector, allowLegacyPlaintext: false);
            var id = repo.Create(new Cliente
            {
                CodigoCliente = "CLI-CIPHER",
                Nome = "Empresa Cipher",
                TipoDocumento = TipoDocumentoCliente.CNPJ,
                Documento = "04252011000110",
                Email = "seguranca@empresa.teste",
                Telefone = "11999998888",
                Ativo = true,
                CriadoPorUserId = 1,
                CriadoEmUtc = DateTime.UtcNow,
                AtualizadoEmUtc = DateTime.UtcNow
            });

            using var connection = db.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
SELECT DocumentoHash, DocumentoCipher, EmailCipher, TelefoneCipher
FROM Clientes
WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", id);

            using var reader = cmd.ExecuteReader();
            reader.Read().Should().BeTrue();
            var documentoHash = reader.GetString(0);
            var documentoCipher = reader.GetString(1);
            var emailCipher = reader.GetString(2);
            var telefoneCipher = reader.GetString(3);

            documentoHash.Should().NotBeNullOrWhiteSpace();
            documentoCipher.Should().StartWith("v1:");
            emailCipher.Should().StartWith("v1:");
            telefoneCipher.Should().StartWith("v1:");
            documentoCipher.Should().NotContain("04252011000110");
            emailCipher.Should().NotContain("seguranca@empresa.teste");
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Fact]
    public void ListarTodos_DeveRetornarApenasClientesAtivos()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"protons_clientes_test_{Guid.NewGuid():N}.db");
        var db = new SqliteDb(dbPath, CriarProtector(), allowLegacyPlaintext: false);
        db.EnsureCreated();

        try
        {
            var repo = new SqliteClienteRepository(db, CriarProtector(), allowLegacyPlaintext: false);
            var now = DateTime.UtcNow;

            repo.Create(new Cliente
            {
                CodigoCliente = "CLI-0002",
                Nome = "Ativo A",
                TipoDocumento = TipoDocumentoCliente.CPF,
                Documento = "12345678909",
                Ativo = true,
                CriadoPorUserId = 1,
                CriadoEmUtc = now,
                AtualizadoEmUtc = now
            });

            repo.Create(new Cliente
            {
                CodigoCliente = "CLI-0003",
                Nome = "Ativo B",
                TipoDocumento = TipoDocumentoCliente.CNPJ,
                Documento = "04252011000110",
                Ativo = true,
                CriadoPorUserId = 1,
                CriadoEmUtc = now,
                AtualizadoEmUtc = now
            });

            using (var connection = db.Open())
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "UPDATE Clientes SET Ativo = 0 WHERE CodigoCliente = $codigo";
                cmd.Parameters.AddWithValue("$codigo", "CLI-0002");
                cmd.ExecuteNonQuery();
            }

            var list = repo.ListarTodos();
            list.Should().HaveCount(1);
            list[0].Nome.Should().Be("Ativo B");
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Fact]
    public void BuscarEContar_DeveFiltrarPorNomeEDocumento_ComPaginacao()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"protons_clientes_test_{Guid.NewGuid():N}.db");
        var db = new SqliteDb(dbPath, CriarProtector(), allowLegacyPlaintext: false);
        db.EnsureCreated();

        try
        {
            var repo = new SqliteClienteRepository(db, CriarProtector(), allowLegacyPlaintext: false);
            var now = DateTime.UtcNow;

            for (var i = 1; i <= 35; i++)
            {
                repo.Create(new Cliente
                {
                    CodigoCliente = $"CLI-{i:0000}",
                    Nome = $"Empresa {i:00}",
                    TipoDocumento = TipoDocumentoCliente.CNPJ,
                    Documento = $"0425201100{i:0000}",
                    Ativo = true,
                    CriadoPorUserId = 1,
                    CriadoEmUtc = now,
                    AtualizadoEmUtc = now
                });
            }

            var totalEmpresa = repo.Contar("Empresa");
            var pagina2 = repo.Buscar("Empresa", 2, 10);
            var buscaPorDocumento = repo.Buscar("04252011000010", 1, 10);

            totalEmpresa.Should().Be(35);
            pagina2.Should().HaveCount(10);
            pagina2[0].Nome.Should().Be("Empresa 11");
            buscaPorDocumento.Should().HaveCount(1);
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Fact]
    public void BuscarEContar_DeveRespeitarFiltroQuandoTermoNaoTemDigitos()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"protons_clientes_test_{Guid.NewGuid():N}.db");
        var db = new SqliteDb(dbPath, CriarProtector(), allowLegacyPlaintext: false);
        db.EnsureCreated();

        try
        {
            var repo = new SqliteClienteRepository(db, CriarProtector(), allowLegacyPlaintext: false);
            var now = DateTime.UtcNow;

            repo.Create(new Cliente
            {
                CodigoCliente = "CLI-0100",
                Nome = "Alpha Contabil",
                TipoDocumento = TipoDocumentoCliente.CNPJ,
                Documento = "04252011000110",
                Ativo = true,
                CriadoPorUserId = 1,
                CriadoEmUtc = now,
                AtualizadoEmUtc = now
            });

            repo.Create(new Cliente
            {
                CodigoCliente = "CLI-0101",
                Nome = "Beta Fiscal",
                TipoDocumento = TipoDocumentoCliente.CNPJ,
                Documento = "22222222000100",
                Ativo = true,
                CriadoPorUserId = 1,
                CriadoEmUtc = now,
                AtualizadoEmUtc = now
            });

            repo.Create(new Cliente
            {
                CodigoCliente = "CLI-0102",
                Nome = "Gamma Tributario",
                TipoDocumento = TipoDocumentoCliente.CNPJ,
                Documento = "33333333000100",
                Ativo = true,
                CriadoPorUserId = 1,
                CriadoEmUtc = now,
                AtualizadoEmUtc = now
            });

            var filtroNomeUnico = repo.Buscar("Alpha", 1, 20);
            var filtroInexistente = repo.Buscar("NaoExiste", 1, 20);
            var totalInexistente = repo.Contar("NaoExiste");

            filtroNomeUnico.Should().HaveCount(1);
            filtroNomeUnico[0].Nome.Should().Be("Alpha Contabil");
            filtroInexistente.Should().BeEmpty();
            totalInexistente.Should().Be(0);
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Fact]
    public void BuscarEContar_DeveFiltrarPorCodigoFantasiaEGrupo()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"protons_clientes_test_{Guid.NewGuid():N}.db");
        var db = new SqliteDb(dbPath, CriarProtector(), allowLegacyPlaintext: false);
        db.EnsureCreated();

        try
        {
            var repo = new SqliteClienteRepository(db, CriarProtector(), allowLegacyPlaintext: false);
            var now = DateTime.UtcNow;

            var grupoId = repo.CreateGrupo(new GrupoEmpresarial
            {
                Nome = "Grupo Alfa",
                NomeNormalizado = "GRUPO ALFA",
                Ativo = true,
                CriadoPorUserId = 1,
                CriadoEmUtc = now,
                AtualizadoEmUtc = now
            });

            repo.Create(new Cliente
            {
                CodigoCliente = "CLI-GRP-01",
                Nome = "Empresa Grupo Um",
                NomeFantasia = "Fantasia Um",
                GrupoEmpresarialId = grupoId,
                TipoDocumento = TipoDocumentoCliente.CNPJ,
                Documento = "44444444000100",
                Ativo = true,
                CriadoPorUserId = 1,
                CriadoEmUtc = now,
                AtualizadoEmUtc = now
            });

            repo.Create(new Cliente
            {
                CodigoCliente = "CLI-GRP-02",
                Nome = "Empresa Grupo Dois",
                NomeFantasia = "Fantasia Dois",
                GrupoEmpresarialId = grupoId,
                TipoDocumento = TipoDocumentoCliente.CNPJ,
                Documento = "55555555000100",
                Ativo = true,
                CriadoPorUserId = 1,
                CriadoEmUtc = now,
                AtualizadoEmUtc = now
            });

            var porCodigo = repo.Buscar("CLI-GRP-01", 1, 20);
            var porFantasia = repo.Buscar("Fantasia Dois", 1, 20);
            var porGrupo = repo.Buscar("Grupo Alfa", 1, 20);

            porCodigo.Should().NotBeEmpty();
            porCodigo[0].CodigoCliente.Should().Be("CLI-GRP-01");
            porFantasia.Should().HaveCount(1);
            porFantasia[0].NomeFantasia.Should().Be("Fantasia Dois");
            porGrupo.Should().HaveCount(2);
            repo.Contar("Grupo Alfa").Should().Be(2);
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Fact]
    public void Buscar_DeveOrdenarPorRelevanciaComCodigoExatoPrimeiro()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"protons_clientes_test_{Guid.NewGuid():N}.db");
        var db = new SqliteDb(dbPath, CriarProtector(), allowLegacyPlaintext: false);
        db.EnsureCreated();

        try
        {
            var repo = new SqliteClienteRepository(db, CriarProtector(), allowLegacyPlaintext: false);
            var now = DateTime.UtcNow;

            var idExato = repo.Create(new Cliente
            {
                CodigoCliente = "ALVO-100",
                Nome = "Empresa Alvo",
                TipoDocumento = TipoDocumentoCliente.CNPJ,
                Documento = "66666666000100",
                Ativo = true,
                CriadoPorUserId = 1,
                CriadoEmUtc = now,
                AtualizadoEmUtc = now
            });

            repo.Create(new Cliente
            {
                CodigoCliente = "CLI-1000",
                Nome = "Cliente ALVO-100 Secundario",
                TipoDocumento = TipoDocumentoCliente.CNPJ,
                Documento = "77777777000100",
                Ativo = true,
                CriadoPorUserId = 1,
                CriadoEmUtc = now,
                AtualizadoEmUtc = now
            });

            var busca = repo.Buscar("ALVO-100", 1, 20);

            busca.Should().NotBeEmpty();
            busca[0].Id.Should().Be(idExato);
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Fact]
    public void Create_DeveLancarExcecaoDuplicidade_QuandoDocumentoJaExiste()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"protons_clientes_test_{Guid.NewGuid():N}.db");
        var db = new SqliteDb(dbPath, CriarProtector(), allowLegacyPlaintext: false);
        db.EnsureCreated();

        try
        {
            var repo = new SqliteClienteRepository(db, CriarProtector(), allowLegacyPlaintext: false);
            var now = DateTime.UtcNow;

            repo.Create(new Cliente
            {
                CodigoCliente = "CLI-0200",
                Nome = "Empresa A",
                TipoDocumento = TipoDocumentoCliente.CNPJ,
                Documento = "04252011000110",
                Ativo = true,
                CriadoPorUserId = 1,
                CriadoEmUtc = now,
                AtualizadoEmUtc = now
            });

            var act = () => repo.Create(new Cliente
            {
                CodigoCliente = "CLI-0201",
                Nome = "Empresa B",
                TipoDocumento = TipoDocumentoCliente.CNPJ,
                Documento = "04252011000110",
                Ativo = true,
                CriadoPorUserId = 1,
                CriadoEmUtc = now,
                AtualizadoEmUtc = now
            });

            act.Should().Throw<DocumentoClienteDuplicadoException>();
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Fact]
    public void Create_DeveLancarExcecaoDuplicidade_QuandoCodigoClienteJaExiste()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"protons_clientes_test_{Guid.NewGuid():N}.db");
        var db = new SqliteDb(dbPath, CriarProtector(), allowLegacyPlaintext: false);
        db.EnsureCreated();

        try
        {
            var repo = new SqliteClienteRepository(db, CriarProtector(), allowLegacyPlaintext: false);
            var now = DateTime.UtcNow;

            repo.Create(new Cliente
            {
                CodigoCliente = "CLI-COD-01",
                Nome = "Empresa A",
                TipoDocumento = TipoDocumentoCliente.CNPJ,
                Documento = "04252011000110",
                Ativo = true,
                CriadoPorUserId = 1,
                CriadoEmUtc = now,
                AtualizadoEmUtc = now
            });

            var act = () => repo.Create(new Cliente
            {
                CodigoCliente = "CLI-COD-01",
                Nome = "Empresa B",
                TipoDocumento = TipoDocumentoCliente.CNPJ,
                Documento = "11111111000191",
                Ativo = true,
                CriadoPorUserId = 1,
                CriadoEmUtc = now,
                AtualizadoEmUtc = now
            });

            act.Should().Throw<CodigoClienteDuplicadoException>();
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Fact]
    public void Create_DevePermitirDocumentoDuplicado_QuandoAnteriorInativo()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"protons_clientes_test_{Guid.NewGuid():N}.db");
        var db = new SqliteDb(dbPath, CriarProtector(), allowLegacyPlaintext: false);
        db.EnsureCreated();

        try
        {
            var repo = new SqliteClienteRepository(db, CriarProtector(), allowLegacyPlaintext: false);
            var now = DateTime.UtcNow;

            var idOriginal = repo.Create(new Cliente
            {
                CodigoCliente = "CLI-0300",
                Nome = "Empresa A",
                TipoDocumento = TipoDocumentoCliente.CNPJ,
                Documento = "04252011000110",
                Ativo = true,
                CriadoPorUserId = 1,
                CriadoEmUtc = now,
                AtualizadoEmUtc = now
            });

            repo.Inativar(idOriginal, 1, DateTime.UtcNow);

            var idNovo = repo.Create(new Cliente
            {
                CodigoCliente = "CLI-0301",
                Nome = "Empresa B",
                TipoDocumento = TipoDocumentoCliente.CNPJ,
                Documento = "04252011000110",
                Ativo = true,
                CriadoPorUserId = 2,
                CriadoEmUtc = now,
                AtualizadoEmUtc = now
            });

            idNovo.Should().BeGreaterThan(idOriginal);
            var ativo = repo.GetByDocumento("04252011000110");
            ativo.Should().NotBeNull();
            ativo!.Id.Should().Be(idNovo);
            ativo.Nome.Should().Be("Empresa B");
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    private static AesGcmClienteDataProtector CriarProtector()
    {
        var key = new byte[32];
        for (var i = 0; i < key.Length; i++)
            key[i] = (byte)(i + 1);

        return new AesGcmClienteDataProtector(key);
    }
}
