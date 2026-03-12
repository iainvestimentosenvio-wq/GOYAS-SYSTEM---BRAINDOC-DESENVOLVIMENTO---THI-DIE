using FluentAssertions;
using Moq;
using Protons.Core.Clientes.Exceptions;
using Protons.Core.Clientes.Models;
using Protons.Core.Clientes.Repositories;
using Protons.Core.Clientes.Security;
using Protons.Core.Clientes.Services;
using Protons.Core.Login.Models;
using Protons.Core.Login.Repositories;
using Protons.Core.Login.Services;
using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Repositories;

namespace Protons.Core.Tests.Services;

public sealed class ClienteServiceTests
{
    private readonly Mock<IClienteRepository> _repoMock;
    private readonly ClienteService _service;

    public ClienteServiceTests()
    {
        _repoMock = new Mock<IClienteRepository>();
        _service = new ClienteService(_repoMock.Object);
    }

    [Fact]
    public void Cadastrar_DeveInferirTipoDocumento_QuandoTipoEnviadoForDivergente()
    {
        var entrada = new ClienteCadastroEntrada
        {
            CodigoCliente = "CLI-0001",
            Nome = "Cliente CPF",
            TipoDocumento = TipoDocumentoCliente.CNPJ, // Ignorado no cadastro
            Documento = "123.456.789-09",
            Email = "cliente@exemplo.com",
            Telefone = "(11) 99999-9999",
            CriadoPorUserId = 10
        };

        _repoMock.Setup(x => x.GetByDocumento("12345678909")).Returns((Cliente?)null);
        _repoMock.Setup(x => x.GetByCodigoCliente("CLI-0001")).Returns((Cliente?)null);
        _repoMock.Setup(x => x.Create(It.IsAny<Cliente>())).Returns(42);

        var resultado = _service.Cadastrar(entrada);

        resultado.Sucesso.Should().BeTrue();
        resultado.Mensagem.Should().Be("Cliente cadastrado com sucesso.");
        resultado.Cliente.Should().NotBeNull();
        resultado.Cliente!.Id.Should().Be(42);
        resultado.Cliente.Documento.Should().Be("12345678909");
        resultado.Cliente.TipoDocumento.Should().Be(TipoDocumentoCliente.CPF);

        _repoMock.Verify(x => x.Create(It.Is<Cliente>(c =>
            c.CodigoCliente == "CLI-0001" &&
            c.Nome == "Cliente CPF" &&
            c.TipoDocumento == TipoDocumentoCliente.CPF &&
            c.Documento == "12345678909" &&
            c.CriadoPorUserId == 10
        )), Times.Once);
    }

    [Fact]
    public void Cadastrar_DeveFalhar_QuandoCodigoClienteNaoInformado()
    {
        var entrada = new ClienteCadastroEntrada
        {
            Nome = "Cliente Sem Codigo",
            Documento = "123.456.789-09",
            CriadoPorUserId = 10
        };

        var resultado = _service.Cadastrar(entrada);

        resultado.Sucesso.Should().BeFalse();
        resultado.Mensagem.Should().Contain("código");
        resultado.CodigoErro.Should().Be(ClienteCadastroErroCodigo.CodigoObrigatorio);
        resultado.CampoErro.Should().Be(ClienteCadastroCampoErro.CodigoCliente);
        _repoMock.Verify(x => x.Create(It.IsAny<Cliente>()), Times.Never);
    }

    [Fact]
    public void Cadastrar_DeveFalhar_QuandoCodigoClienteJaExiste()
    {
        var entrada = new ClienteCadastroEntrada
        {
            CodigoCliente = "CLI-7777",
            Nome = "Empresa Nova",
            Documento = "04.252.011/0001-10",
            CriadoPorUserId = 10
        };

        _repoMock.Setup(x => x.GetByDocumento("04252011000110")).Returns((Cliente?)null);
        _repoMock.Setup(x => x.GetByCodigoCliente("CLI-7777")).Returns(new Cliente
        {
            Id = 55,
            CodigoCliente = "CLI-7777",
            Nome = "Empresa Existente",
            TipoDocumento = TipoDocumentoCliente.CNPJ,
            Documento = "11111111000191",
            Ativo = true
        });

        var resultado = _service.Cadastrar(entrada);

        resultado.Sucesso.Should().BeFalse();
        resultado.Mensagem.Should().Contain("código");
        resultado.CodigoErro.Should().Be(ClienteCadastroErroCodigo.CodigoDuplicado);
        resultado.CampoErro.Should().Be(ClienteCadastroCampoErro.CodigoCliente);
        _repoMock.Verify(x => x.Create(It.IsAny<Cliente>()), Times.Never);
    }

    [Fact]
    public void Cadastrar_DeveSalvarGrupoEmpresarial_QuandoGrupoValidoInformado()
    {
        var entrada = new ClienteCadastroEntrada
        {
            CodigoCliente = "CLI-2000",
            Nome = "Cliente Com Grupo",
            Documento = "04.252.011/0001-10",
            GrupoEmpresarialId = 9,
            CriadoPorUserId = 10
        };

        _repoMock.Setup(x => x.GetByDocumento("04252011000110")).Returns((Cliente?)null);
        _repoMock.Setup(x => x.GetByCodigoCliente("CLI-2000")).Returns((Cliente?)null);
        _repoMock.Setup(x => x.GetGrupoById(9)).Returns(new GrupoEmpresarial
        {
            Id = 9,
            Nome = "Grupo X",
            NomeNormalizado = "GRUPO X",
            Ativo = true
        });
        _repoMock.Setup(x => x.Create(It.IsAny<Cliente>())).Returns(88);

        var resultado = _service.Cadastrar(entrada);

        resultado.Sucesso.Should().BeTrue();
        resultado.Cliente.Should().NotBeNull();
        resultado.Cliente!.GrupoEmpresarialId.Should().Be(9);
        resultado.Cliente.GrupoEmpresarialNome.Should().Be("Grupo X");
    }

    [Fact]
    public void CadastrarGrupo_DeveSerIdempotente_QuandoNomeJaExiste()
    {
        _repoMock.Setup(x => x.GetGrupoByNomeNormalizado("GRUPO ALFA")).Returns(new GrupoEmpresarial
        {
            Id = 11,
            Nome = "Grupo Alfa",
            NomeNormalizado = "GRUPO ALFA",
            Ativo = true
        });

        var resultado = _service.CadastrarGrupo(new GrupoEmpresarialCadastroEntrada
        {
            Nome = "  Grupo Alfa  ",
            CriadoPorUserId = 1
        });

        resultado.Sucesso.Should().BeTrue();
        resultado.Grupo.Should().NotBeNull();
        resultado.Grupo!.Id.Should().Be(11);
        _repoMock.Verify(x => x.CreateGrupo(It.IsAny<GrupoEmpresarial>()), Times.Never);
    }

    [Fact]
    public void Cadastrar_DeveFalhar_QuandoDocumentoDuplicadoNoPreCheck()
    {
        var clienteExistente = new Cliente
        {
            Id = 99,
            CodigoCliente = "CLI-0002",
            Nome = "Empresa Duplicada",
            TipoDocumento = TipoDocumentoCliente.CNPJ,
            Documento = "04252011000110",
            Ativo = true,
            CriadoPorUserId = 7,
            CriadoEmUtc = DateTime.UtcNow,
            AtualizadoEmUtc = DateTime.UtcNow
        };

        var entrada = new ClienteCadastroEntrada
        {
            CodigoCliente = "CLI-OUTRO",
            Nome = "Empresa Duplicada",
            Documento = "04.252.011/0001-10",
            CriadoPorUserId = 7
        };

        _repoMock.Setup(x => x.GetByDocumento("04252011000110")).Returns(clienteExistente);

        var resultado = _service.Cadastrar(entrada);

        resultado.Sucesso.Should().BeFalse();
        resultado.Mensagem.Should().Contain("documento");
        resultado.CodigoErro.Should().Be(ClienteCadastroErroCodigo.DocumentoDuplicado);
        resultado.CampoErro.Should().Be(ClienteCadastroCampoErro.Documento);
        _repoMock.Verify(x => x.Create(It.IsAny<Cliente>()), Times.Never);
    }

    [Fact]
    public void Cadastrar_DeveFalhar_QuandoRepositorioLancarDuplicidade()
    {
        var entrada = new ClienteCadastroEntrada
        {
            CodigoCliente = "CLI-0003",
            Nome = "Empresa Corrida",
            Documento = "04.252.011/0001-10",
            CriadoPorUserId = 7
        };

        var existente = new Cliente
        {
            Id = 123,
            CodigoCliente = "CLI-0100",
            Nome = "Empresa Corrida",
            TipoDocumento = TipoDocumentoCliente.CNPJ,
            Documento = "04252011000110",
            Ativo = true,
            CriadoPorUserId = 7,
            CriadoEmUtc = DateTime.UtcNow,
            AtualizadoEmUtc = DateTime.UtcNow
        };

        _repoMock.SetupSequence(x => x.GetByDocumento("04252011000110"))
            .Returns((Cliente?)null)
            .Returns(existente);
        _repoMock.Setup(x => x.GetByCodigoCliente("CLI-0003")).Returns((Cliente?)null);
        _repoMock
            .Setup(x => x.Create(It.IsAny<Cliente>()))
            .Throws(new DocumentoClienteDuplicadoException("duplicado"));

        var resultado = _service.Cadastrar(entrada);

        resultado.Sucesso.Should().BeFalse();
        resultado.Mensagem.Should().Contain("documento");
        resultado.CodigoErro.Should().Be(ClienteCadastroErroCodigo.DocumentoDuplicado);
        resultado.CampoErro.Should().Be(ClienteCadastroCampoErro.Documento);
    }

    [Fact]
    public void Cadastrar_DeveFalharComCodigoDuplicado_QuandoRepositorioLancarDuplicidadeDeCodigo()
    {
        var entrada = new ClienteCadastroEntrada
        {
            CodigoCliente = "CLI-RACE-01",
            Nome = "Empresa Corrida Codigo",
            Documento = "04.252.011/0001-10",
            CriadoPorUserId = 7
        };

        _repoMock.Setup(x => x.GetByDocumento("04252011000110")).Returns((Cliente?)null);
        _repoMock.Setup(x => x.GetByCodigoCliente("CLI-RACE-01")).Returns((Cliente?)null);
        _repoMock
            .Setup(x => x.Create(It.IsAny<Cliente>()))
            .Throws(new CodigoClienteDuplicadoException("duplicado"));

        var resultado = _service.Cadastrar(entrada);

        resultado.Sucesso.Should().BeFalse();
        resultado.CodigoErro.Should().Be(ClienteCadastroErroCodigo.CodigoDuplicado);
        resultado.CampoErro.Should().Be(ClienteCadastroCampoErro.CodigoCliente);
        resultado.Mensagem.Should().Contain("código");
    }

    [Fact]
    public void Cadastrar_DeveFalhar_QuandoCnpjInvalido()
    {
        var entrada = new ClienteCadastroEntrada
        {
            CodigoCliente = "CLI-0004",
            Nome = "Empresa CNPJ Inválido",
            Documento = "11.111.111/1111-11",
            CriadoPorUserId = 2
        };

        var resultado = _service.Cadastrar(entrada);

        resultado.Sucesso.Should().BeFalse();
        resultado.Mensagem.Should().Be("CNPJ inválido.");
        resultado.CodigoErro.Should().Be(ClienteCadastroErroCodigo.CnpjInvalido);
        resultado.CampoErro.Should().Be(ClienteCadastroCampoErro.Documento);
        _repoMock.Verify(x => x.Create(It.IsAny<Cliente>()), Times.Never);
    }

    [Fact]
    public void Cadastrar_DeveFalhar_QuandoDocumentoTemCaracteresNaoPermitidos()
    {
        var entrada = new ClienteCadastroEntrada
        {
            CodigoCliente = "CLI-0005",
            Nome = "Empresa Documento Inválido",
            Documento = "04.252.011/0001-10ABC",
            CriadoPorUserId = 2
        };

        var resultado = _service.Cadastrar(entrada);

        resultado.Sucesso.Should().BeFalse();
        resultado.Mensagem.Should().Contain("separadores");
        resultado.CodigoErro.Should().Be(ClienteCadastroErroCodigo.DocumentoCaracteresInvalidos);
        resultado.CampoErro.Should().Be(ClienteCadastroCampoErro.Documento);
        _repoMock.Verify(x => x.Create(It.IsAny<Cliente>()), Times.Never);
    }

    [Fact]
    public void Cadastrar_DeveFalhar_QuandoDocumentoNaoTemOnzeOuQuatorzeDigitos()
    {
        var entrada = new ClienteCadastroEntrada
        {
            CodigoCliente = "CLI-0006",
            Nome = "Empresa Documento Curto",
            Documento = "123456789012",
            CriadoPorUserId = 2
        };

        var resultado = _service.Cadastrar(entrada);

        resultado.Sucesso.Should().BeFalse();
        resultado.Mensagem.Should().Contain("11 dígitos").And.Contain("14");
        resultado.CodigoErro.Should().Be(ClienteCadastroErroCodigo.DocumentoTamanhoInvalido);
        resultado.CampoErro.Should().Be(ClienteCadastroCampoErro.Documento);
        _repoMock.Verify(x => x.Create(It.IsAny<Cliente>()), Times.Never);
    }

    [Fact]
    public void Cadastrar_DeveFalharComFalhaTecnicaEReferencia_QuandoOcorreErroInesperado()
    {
        var entrada = new ClienteCadastroEntrada
        {
            CodigoCliente = "CLI-TECH-01",
            Nome = "Cliente Tecnico",
            Documento = "123.456.789-09",
            CriadoPorUserId = 10
        };

        _repoMock.Setup(x => x.GetByDocumento("12345678909"))
            .Throws(new InvalidOperationException("falha inesperada"));

        var resultado = _service.Cadastrar(entrada);

        resultado.Sucesso.Should().BeFalse();
        resultado.CodigoErro.Should().Be(ClienteCadastroErroCodigo.FalhaTecnica);
        resultado.CampoErro.Should().Be(ClienteCadastroCampoErro.Nenhum);
        resultado.Mensagem.Should().Be("Não foi possível concluir o cadastro agora. Tente novamente.");
        resultado.ReferenciaErro.Should().NotBeNullOrWhiteSpace();
        resultado.ReferenciaErro.Should().StartWith("CLI-CAD-");
    }

    [Fact]
    public void ListarTodos_DeveDelegarParaRepositorio()
    {
        var clientes = new List<Cliente>
        {
            new() { Id = 1, CodigoCliente = "CLI-0101", Nome = "A", Documento = "12345678909", TipoDocumento = TipoDocumentoCliente.CPF },
            new() { Id = 2, CodigoCliente = "CLI-0102", Nome = "B", Documento = "04252011000110", TipoDocumento = TipoDocumentoCliente.CNPJ }
        };

        _repoMock.Setup(x => x.ListarTodos()).Returns(clientes);

        var resultado = _service.ListarTodos();

        resultado.Should().HaveCount(2);
        _repoMock.Verify(x => x.ListarTodos(), Times.Once);
    }

    [Fact]
    public void ObterPorId_DeveDelegarQuandoIdValido()
    {
        var cliente = new Cliente
        {
            Id = 77,
            CodigoCliente = "CLI-0077",
            Nome = "Cliente 77",
            Documento = "12345678909",
            TipoDocumento = TipoDocumentoCliente.CPF
        };
        _repoMock.Setup(x => x.GetById(77)).Returns(cliente);

        var resultado = _service.ObterPorId(77);

        resultado.Should().NotBeNull();
        resultado!.Id.Should().Be(77);
        _repoMock.Verify(x => x.GetById(77), Times.Once);
    }

    [Fact]
    public void Buscar_DeveRetornarPaginacaoComTotalCorreto()
    {
        var itens = new List<Cliente>
        {
            new() { Id = 1, CodigoCliente = "CLI-0201", Nome = "Empresa Alpha", Documento = "12345678909", TipoDocumento = TipoDocumentoCliente.CPF },
            new() { Id = 2, CodigoCliente = "CLI-0202", Nome = "Empresa Beta", Documento = "04252011000110", TipoDocumento = TipoDocumentoCliente.CNPJ }
        };

        _repoMock.Setup(x => x.Contar("empresa")).Returns(41);
        _repoMock.Setup(x => x.Buscar("empresa", 2, 20)).Returns(itens);

        var resultado = _service.Buscar(" empresa ", 2, 20);

        resultado.TotalItens.Should().Be(41);
        resultado.PaginaAtual.Should().Be(2);
        resultado.TamanhoPagina.Should().Be(20);
        resultado.TotalPaginas.Should().Be(3);
        resultado.Itens.Should().HaveCount(2);

        _repoMock.Verify(x => x.Contar("empresa"), Times.Once);
        _repoMock.Verify(x => x.Buscar("empresa", 2, 20), Times.Once);
    }

    [Fact]
    public async Task Cadastrar_EmConcorrencia_DevePermitirApenasUmCadastro()
    {
        var repo = new RepositorioConcorrenteFake();
        var service = new ClienteService(repo);

        var tasks = Enumerable.Range(0, 20).Select(i => Task.Run(() => service.Cadastrar(new ClienteCadastroEntrada
        {
            CodigoCliente = $"CLI-{9000 + i}",
            Nome = "Empresa Concorrente",
            Documento = "04.252.011/0001-10",
            CriadoPorUserId = 7
        })));

        var resultados = await Task.WhenAll(tasks);

        resultados.Count(r => r.Sucesso).Should().Be(1);
        resultados.Count(r => !r.Sucesso).Should().Be(19);
        resultados.Where(r => !r.Sucesso)
            .Should()
            .OnlyContain(r => r.Mensagem.Contains("documento", StringComparison.OrdinalIgnoreCase));
        repo.TotalClientes.Should().Be(1);
    }

    [Fact]
    public void ListarTodosPorUsuario_DeveRetornarSomenteClientesPermitidos_ParaUsuarioComum()
    {
        var repo = new Mock<IClienteRepository>();
        var audit = new Mock<IAuditService>();
        var users = new Mock<IUserRepository>();
        var permissoes = new Mock<IClientePermissaoRepository>();
        var service = new ClienteService(
            repo.Object,
            audit.Object,
            "maq",
            "1.0.0",
            usarHashChain: true,
            users.Object,
            permissoes.Object);

        users.Setup(x => x.GetById(10)).Returns(new User
        {
            Id = 10,
            Status = UserStatus.Ativo,
            Role = UserRole.Usuario
        });

        repo.Setup(x => x.ListarTodos()).Returns(new List<Cliente>
        {
            new() { Id = 1, CodigoCliente = "CLI-1", Nome = "Cliente 1", Documento = "12345678909", TipoDocumento = TipoDocumentoCliente.CPF, Ativo = true },
            new() { Id = 2, CodigoCliente = "CLI-2", Nome = "Cliente 2", Documento = "04252011000110", TipoDocumento = TipoDocumentoCliente.CNPJ, Ativo = true }
        });

        permissoes.Setup(x => x.Obter(1, 10)).Returns(new ClientePermissaoUsuario { ClienteId = 1, UserId = 10, PodeEditar = true });
        permissoes.Setup(x => x.Obter(2, 10)).Returns((ClientePermissaoUsuario?)null);

        var resultado = service.ListarTodosPorUsuario(10);

        resultado.Should().HaveCount(1);
        resultado[0].Id.Should().Be(1);
    }

    [Fact]
    public void ListarTodosPorUsuario_DevePermitirClienteCriadoPeloUsuario_MesmoSemPermissaoExplicita()
    {
        var repo = new Mock<IClienteRepository>();
        var audit = new Mock<IAuditService>();
        var users = new Mock<IUserRepository>();
        var permissoes = new Mock<IClientePermissaoRepository>();
        var service = new ClienteService(
            repo.Object,
            audit.Object,
            "maq",
            "1.0.0",
            usarHashChain: true,
            users.Object,
            permissoes.Object);

        users.Setup(x => x.GetById(10)).Returns(new User
        {
            Id = 10,
            Status = UserStatus.Ativo,
            Role = UserRole.Usuario
        });

        repo.Setup(x => x.ListarTodos()).Returns(new List<Cliente>
        {
            new() { Id = 1, CodigoCliente = "CLI-1", Nome = "Meu Cliente", Documento = "12345678909", TipoDocumento = TipoDocumentoCliente.CPF, Ativo = true, CriadoPorUserId = 10 },
            new() { Id = 2, CodigoCliente = "CLI-2", Nome = "Outro Cliente", Documento = "04252011000110", TipoDocumento = TipoDocumentoCliente.CNPJ, Ativo = true, CriadoPorUserId = 99 }
        });

        permissoes.Setup(x => x.Obter(1, 10)).Returns((ClientePermissaoUsuario?)null);
        permissoes.Setup(x => x.Obter(2, 10)).Returns((ClientePermissaoUsuario?)null);

        var resultado = service.ListarTodosPorUsuario(10);

        resultado.Should().HaveCount(1);
        resultado[0].Id.Should().Be(1);
    }

    [Fact]
    public void ListarTodosPorUsuario_Supremo_DeveRetornarTodosOsClientes()
    {
        var repo = new Mock<IClienteRepository>();
        var audit = new Mock<IAuditService>();
        var users = new Mock<IUserRepository>();
        var permissoes = new Mock<IClientePermissaoRepository>();
        var service = new ClienteService(
            repo.Object,
            audit.Object,
            "maq",
            "1.0.0",
            usarHashChain: true,
            users.Object,
            permissoes.Object);

        users.Setup(x => x.GetById(77)).Returns(new User
        {
            Id = 77,
            Status = UserStatus.Ativo,
            Role = UserRole.Supremo
        });
        repo.Setup(x => x.ListarTodos()).Returns(new List<Cliente>
        {
            new() { Id = 1, CodigoCliente = "CLI-1", Nome = "Cliente 1", Documento = "12345678909", TipoDocumento = TipoDocumentoCliente.CPF, Ativo = true },
            new() { Id = 2, CodigoCliente = "CLI-2", Nome = "Cliente 2", Documento = "04252011000110", TipoDocumento = TipoDocumentoCliente.CNPJ, Ativo = true }
        });

        var resultado = service.ListarTodosPorUsuario(77);

        resultado.Should().HaveCount(2);
    }

    [Fact]
    public void ObterPorIdPorUsuario_DeveRetornarNulo_QuandoUsuarioNaoTemPermissao()
    {
        var repo = new Mock<IClienteRepository>();
        var audit = new Mock<IAuditService>();
        var users = new Mock<IUserRepository>();
        var permissoes = new Mock<IClientePermissaoRepository>();
        var service = new ClienteService(
            repo.Object,
            audit.Object,
            "maq",
            "1.0.0",
            usarHashChain: true,
            users.Object,
            permissoes.Object);

        users.Setup(x => x.GetById(11)).Returns(new User
        {
            Id = 11,
            Status = UserStatus.Ativo,
            Role = UserRole.Usuario
        });
        repo.Setup(x => x.GetById(77)).Returns(new Cliente
        {
            Id = 77,
            CodigoCliente = "CLI-77",
            Nome = "Cliente 77",
            Documento = "12345678909",
            TipoDocumento = TipoDocumentoCliente.CPF,
            Ativo = true
        });
        permissoes.Setup(x => x.Obter(77, 11)).Returns((ClientePermissaoUsuario?)null);

        var resultado = service.ObterPorIdPorUsuario(11, 77);

        resultado.Should().BeNull();
    }

    [Fact]
    public void ObterPorIdPorUsuario_DeveRetornarClienteCriadoPeloUsuario_MesmoSemPermissaoExplicita()
    {
        var repo = new Mock<IClienteRepository>();
        var audit = new Mock<IAuditService>();
        var users = new Mock<IUserRepository>();
        var permissoes = new Mock<IClientePermissaoRepository>();
        var service = new ClienteService(
            repo.Object,
            audit.Object,
            "maq",
            "1.0.0",
            usarHashChain: true,
            users.Object,
            permissoes.Object);

        users.Setup(x => x.GetById(11)).Returns(new User
        {
            Id = 11,
            Status = UserStatus.Ativo,
            Role = UserRole.Usuario
        });
        repo.Setup(x => x.GetById(77)).Returns(new Cliente
        {
            Id = 77,
            CodigoCliente = "CLI-77",
            Nome = "Cliente 77",
            Documento = "12345678909",
            TipoDocumento = TipoDocumentoCliente.CPF,
            Ativo = true,
            CriadoPorUserId = 11
        });
        permissoes.Setup(x => x.Obter(77, 11)).Returns((ClientePermissaoUsuario?)null);

        var resultado = service.ObterPorIdPorUsuario(11, 77);

        resultado.Should().NotBeNull();
        resultado!.Id.Should().Be(77);
    }

    [Fact]
    public void ObterPorIdPorUsuario_Supremo_DeveRetornarClienteMesmoSemPermissaoExplicita()
    {
        var repo = new Mock<IClienteRepository>();
        var audit = new Mock<IAuditService>();
        var users = new Mock<IUserRepository>();
        var permissoes = new Mock<IClientePermissaoRepository>();
        var service = new ClienteService(
            repo.Object,
            audit.Object,
            "maq",
            "1.0.0",
            usarHashChain: true,
            users.Object,
            permissoes.Object);

        users.Setup(x => x.GetById(88)).Returns(new User
        {
            Id = 88,
            Status = UserStatus.Ativo,
            Role = UserRole.Supremo
        });
        repo.Setup(x => x.GetById(77)).Returns(new Cliente
        {
            Id = 77,
            CodigoCliente = "CLI-77",
            Nome = "Cliente 77",
            Documento = "12345678909",
            TipoDocumento = TipoDocumentoCliente.CPF,
            Ativo = true
        });
        permissoes.Setup(x => x.Obter(77, 88)).Returns((ClientePermissaoUsuario?)null);

        var resultado = service.ObterPorIdPorUsuario(88, 77);

        resultado.Should().NotBeNull();
        resultado!.Id.Should().Be(77);
    }

    [Fact]
    public void Cadastrar_DeveConcederPermissaoInicialAoCriador_QuandoRbacDisponivel()
    {
        var repo = new Mock<IClienteRepository>();
        var audit = new Mock<IAuditService>();
        var users = new Mock<IUserRepository>();
        var permissoes = new Mock<IClientePermissaoRepository>();
        var service = new ClienteService(
            repo.Object,
            audit.Object,
            "maq",
            "1.0.0",
            usarHashChain: true,
            users.Object,
            permissoes.Object);

        repo.Setup(x => x.GetByDocumento("12345678909")).Returns((Cliente?)null);
        repo.Setup(x => x.GetByCodigoCliente("CLI-PERM-01")).Returns((Cliente?)null);
        repo.Setup(x => x.Create(It.IsAny<Cliente>())).Returns(501);

        var resultado = service.Cadastrar(new ClienteCadastroEntrada
        {
            CodigoCliente = "CLI-PERM-01",
            Nome = "Cliente Permissao",
            Documento = "123.456.789-09",
            CriadoPorUserId = 33
        });

        resultado.Sucesso.Should().BeTrue();
        permissoes.Verify(x => x.DefinirPermissoes(
            501,
            33,
            It.Is<IReadOnlyList<PermissaoClienteEntrada>>(lista =>
                lista.Count == 1 &&
                lista[0].UserId == 33 &&
                lista[0].PodeEditar)),
            Times.Once);
    }

    [Fact]
    public void Cadastrar_AuditoriaNaoDeveConterPiiNosDetalhes()
    {
        var repo = new Mock<IClienteRepository>();
        var audit = new Mock<IAuditService>();
        var users = new Mock<IUserRepository>();
        var permissoes = new Mock<IClientePermissaoRepository>();
        var protector = new Mock<IClienteDataProtector>();
        protector.Setup(x => x.ComputeDocumentoHash("12345678909")).Returns("HASH-CLIENTE-123456");

        var service = new ClienteService(
            repo.Object,
            audit.Object,
            "maq",
            "1.0.0",
            usarHashChain: true,
            users.Object,
            permissoes.Object,
            protector.Object);

        repo.Setup(x => x.GetByDocumento("12345678909")).Returns((Cliente?)null);
        repo.Setup(x => x.GetByCodigoCliente("CLI-AUDIT")).Returns((Cliente?)null);
        repo.Setup(x => x.Create(It.IsAny<Cliente>())).Returns(700);

        var resultado = service.Cadastrar(new ClienteCadastroEntrada
        {
            CodigoCliente = "CLI-AUDIT",
            Nome = "Cliente Auditoria",
            Documento = "123.456.789-09",
            Email = "cliente@empresa.teste",
            Telefone = "11999999999",
            CriadoPorUserId = 1
        });

        resultado.Sucesso.Should().BeTrue();

        audit.Verify(x => x.Registrar(
            It.Is<AuditLogEntry>(a =>
                a.Acao == "CLIENTE_CRIADO" &&
                !string.IsNullOrWhiteSpace(a.Detalhes) &&
                !a.Detalhes!.Contains("12345678909", StringComparison.Ordinal) &&
                !a.Detalhes.Contains("cliente@empresa.teste", StringComparison.OrdinalIgnoreCase) &&
                !a.Detalhes.Contains("11999999999", StringComparison.Ordinal)),
            true), Times.Once);
    }

    [Fact]
    public void Cadastrar_DeveUsarTimeProviderParaTimestamps()
    {
        var fixedNow = new DateTimeOffset(2026, 3, 1, 18, 45, 0, TimeSpan.Zero);
        var repo = new Mock<IClienteRepository>();
        var audit = new Mock<IAuditService>();
        var service = new ClienteService(
            repo.Object,
            audit.Object,
            "maq",
            "1.0.0",
            usarHashChain: true,
            timeProvider: new FixedTimeProvider(fixedNow));

        repo.Setup(x => x.GetByDocumento("12345678909")).Returns((Cliente?)null);
        repo.Setup(x => x.GetByCodigoCliente("CLI-TIME")).Returns((Cliente?)null);
        repo.Setup(x => x.Create(It.IsAny<Cliente>())).Returns(77);

        var resultado = service.Cadastrar(new ClienteCadastroEntrada
        {
            CodigoCliente = "CLI-TIME",
            Nome = "Cliente Time",
            Documento = "123.456.789-09",
            CriadoPorUserId = 5
        });

        resultado.Sucesso.Should().BeTrue();
        repo.Verify(x => x.Create(It.Is<Cliente>(c =>
            c.CriadoEmUtc == fixedNow.UtcDateTime &&
            c.AtualizadoEmUtc == fixedNow.UtcDateTime)), Times.Once);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class RepositorioConcorrenteFake : IClienteRepository
    {
        private readonly object _sync = new();
        private readonly Dictionary<string, Cliente> _clientes = new(StringComparer.Ordinal);
        private int _sequenciaId;

        public int TotalClientes
        {
            get
            {
                lock (_sync)
                {
                    return _clientes.Count;
                }
            }
        }

        public Cliente? GetByDocumento(string? documentoSomenteDigitos)
        {
            if (string.IsNullOrWhiteSpace(documentoSomenteDigitos))
                return null;

            lock (_sync)
            {
                return _clientes.TryGetValue(documentoSomenteDigitos, out var cliente)
                    ? Clonar(cliente)
                    : null;
            }
        }

        public Cliente? GetByCodigoCliente(string? codigoCliente)
        {
            if (string.IsNullOrWhiteSpace(codigoCliente))
                return null;

            lock (_sync)
            {
                var cliente = _clientes.Values.FirstOrDefault(c =>
                    string.Equals(c.CodigoCliente, codigoCliente, StringComparison.OrdinalIgnoreCase));
                return cliente is null ? null : Clonar(cliente);
            }
        }

        public Cliente? GetById(int id)
        {
            lock (_sync)
            {
                var cliente = _clientes.Values.FirstOrDefault(c => c.Id == id);
                return cliente is null ? null : Clonar(cliente);
            }
        }

        public IReadOnlyList<Cliente> ListarTodos()
        {
            lock (_sync)
            {
                return _clientes.Values.Select(Clonar).ToList();
            }
        }

        public IReadOnlyList<Cliente> Buscar(string? termo, int pagina, int tamanhoPagina)
        {
            lock (_sync)
            {
                IEnumerable<Cliente> query = _clientes.Values;
                if (!string.IsNullOrWhiteSpace(termo))
                {
                    query = query.Where(c => c.Nome.Contains(termo, StringComparison.OrdinalIgnoreCase) ||
                                             c.Documento.Contains(termo, StringComparison.OrdinalIgnoreCase));
                }

                return query
                    .OrderBy(c => c.Nome, StringComparer.OrdinalIgnoreCase)
                    .Skip((Math.Max(1, pagina) - 1) * Math.Max(1, tamanhoPagina))
                    .Take(Math.Max(1, tamanhoPagina))
                    .Select(Clonar)
                    .ToList();
            }
        }

        public int Contar(string? termo)
        {
            lock (_sync)
            {
                if (string.IsNullOrWhiteSpace(termo))
                    return _clientes.Count;

                return _clientes.Values.Count(c => c.Nome.Contains(termo, StringComparison.OrdinalIgnoreCase) ||
                                                   c.Documento.Contains(termo, StringComparison.OrdinalIgnoreCase));
            }
        }

        public int Create(Cliente cliente)
        {
            lock (_sync)
            {
                if (_clientes.ContainsKey(cliente.Documento))
                    throw new DocumentoClienteDuplicadoException("Documento duplicado");
                if (_clientes.Values.Any(c => string.Equals(c.CodigoCliente, cliente.CodigoCliente, StringComparison.OrdinalIgnoreCase)))
                    throw new CodigoClienteDuplicadoException("Codigo duplicado");

                var clone = Clonar(cliente);
                clone.Id = ++_sequenciaId;
                _clientes[clone.Documento] = clone;
                return clone.Id;
            }
        }

        public void Update(Cliente cliente)
        {
            lock (_sync)
            {
                _clientes[cliente.Documento] = Clonar(cliente);
            }
        }

        public void Inativar(int clienteId, int atualizadoPorUserId, DateTime atualizadoEmUtc)
        {
            lock (_sync)
            {
                var atual = _clientes.Values.FirstOrDefault(c => c.Id == clienteId);
                if (atual is null)
                    return;

                atual.Ativo = false;
                atual.AtualizadoEmUtc = atualizadoEmUtc;
                _clientes[atual.Documento] = Clonar(atual);
            }
        }

        public GrupoEmpresarial? GetGrupoById(int grupoId) => null;

        public GrupoEmpresarial? GetGrupoByNomeNormalizado(string? nomeNormalizado) => null;

        public IReadOnlyList<GrupoEmpresarial> ListarGrupos() => [];

        public int CreateGrupo(GrupoEmpresarial grupo) => 0;

        private static Cliente Clonar(Cliente cliente)
        {
            return new Cliente
            {
                Id = cliente.Id,
                CodigoCliente = cliente.CodigoCliente,
                Nome = cliente.Nome,
                NomeFantasia = cliente.NomeFantasia,
                TipoDocumento = cliente.TipoDocumento,
                Documento = cliente.Documento,
                GrupoEmpresarialId = cliente.GrupoEmpresarialId,
                GrupoEmpresarialNome = cliente.GrupoEmpresarialNome,
                Email = cliente.Email,
                Telefone = cliente.Telefone,
                Ativo = cliente.Ativo,
                CriadoPorUserId = cliente.CriadoPorUserId,
                CriadoEmUtc = cliente.CriadoEmUtc,
                AtualizadoEmUtc = cliente.AtualizadoEmUtc
            };
        }
    }
}
