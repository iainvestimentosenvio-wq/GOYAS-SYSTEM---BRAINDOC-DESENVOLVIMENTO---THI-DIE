using FluentAssertions;
using Moq;
using Protons.Core.Clientes.Models;
using Protons.Core.Clientes.Repositories;
using Protons.Core.Login.Models;
using Protons.Core.Login.Repositories;
using Protons.Core.Login.Services;
using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Repositories;
using Protons.Core.Tarefas.Services;
using Xunit;

namespace Protons.Core.Tests.Services.Tarefas;

public sealed class TarefaServiceTests
{
    private readonly Mock<ITarefaRepository> _tarefas = new();
    private readonly Mock<IClientePermissaoRepository> _permissoes = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IClienteRepository> _clientes = new();
    private readonly Mock<IAuditService> _audit = new();

    private readonly TarefaService _service;

    public TarefaServiceTests()
    {
        _service = new TarefaService(
            _tarefas.Object,
            _permissoes.Object,
            _users.Object,
            _clientes.Object,
            _audit.Object,
            "TEST",
            "1.0.0",
            usarHashChain: false);
    }

    [Fact]
    public void Criar_DeveUsarTimeProviderParaCamposUtc()
    {
        var fixedNow = new DateTimeOffset(2026, 3, 1, 15, 0, 0, TimeSpan.Zero);
        var captured = default(Tarefa);

        var tarefas = new Mock<ITarefaRepository>();
        var permissoes = new Mock<IClientePermissaoRepository>();
        var users = new Mock<IUserRepository>();
        var clientes = new Mock<IClienteRepository>();
        var audit = new Mock<IAuditService>();
        var sut = new TarefaService(
            tarefas.Object,
            permissoes.Object,
            users.Object,
            clientes.Object,
            audit.Object,
            "TEST",
            "1.0.0",
            usarHashChain: false,
            timeProvider: new FixedTimeProvider(fixedNow));

        users.Setup(x => x.GetById(1)).Returns(NovoUsuario(1, UserRole.Admin));
        clientes.Setup(x => x.GetById(9)).Returns(new Cliente
        {
            Id = 9,
            Nome = "Empresa",
            Documento = "12345678909",
            TipoDocumento = TipoDocumentoCliente.CPF,
            Ativo = true
        });
        tarefas.Setup(x => x.Create(It.IsAny<Tarefa>()))
            .Callback<Tarefa>(t => captured = t)
            .Returns(123);

        _ = sut.Criar(new TarefaCadastroEntrada
        {
            ClienteId = 9,
            Titulo = "Fechar imposto",
            VencimentoLocal = new DateTime(2026, 3, 2, 9, 0, 0),
            ResponsavelUserId = 1,
            Recorrencia = TarefaRecorrencia.Nenhuma
        }, 1);

        captured.Should().NotBeNull();
        captured!.CriadoEmUtc.Should().Be(fixedNow.UtcDateTime);
        captured.AtualizadoEmUtc.Should().Be(fixedNow.UtcDateTime);
    }

    [Fact]
    public void Criar_DeveLancar_QuandoUsuarioComumTentaAtribuirParaOutroUsuario()
    {
        var solicitante = NovoUsuario(10, UserRole.Usuario);
        var outro = NovoUsuario(11, UserRole.Usuario);
        _users.Setup(x => x.GetById(10)).Returns(solicitante);
        _users.Setup(x => x.GetById(11)).Returns(outro);
        _clientes.Setup(x => x.GetById(9)).Returns(new Cliente
        {
            Id = 9,
            Nome = "Empresa",
            Documento = "12345678909",
            TipoDocumento = TipoDocumentoCliente.CPF,
            Ativo = true
        });
        _permissoes.Setup(x => x.Obter(9, 10)).Returns(new ClientePermissaoUsuario
        {
            ClienteId = 9,
            UserId = 10,
            PodeEditar = true
        });

        var act = () => _service.Criar(new TarefaCadastroEntrada
        {
            ClienteId = 9,
            Titulo = "Fechar imposto",
            VencimentoLocal = DateTime.Now.AddHours(2),
            ResponsavelUserId = 11,
            Recorrencia = TarefaRecorrencia.Nenhuma
        }, 10);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*so pode criar tarefa para si mesmo*");
    }

    [Fact]
    public void Criar_DevePermitirAdminAtribuirParaUsuarioPermitido()
    {
        var admin = NovoUsuario(1, UserRole.Admin);
        var operador = NovoUsuario(2, UserRole.Usuario);
        _users.Setup(x => x.GetById(1)).Returns(admin);
        _users.Setup(x => x.GetById(2)).Returns(operador);
        _users.Setup(x => x.ListarPorAdmin(1)).Returns(new List<User> { operador });
        _clientes.Setup(x => x.GetById(99)).Returns(new Cliente
        {
            Id = 99,
            Nome = "Empresa",
            Documento = "04252011000110",
            TipoDocumento = TipoDocumentoCliente.CNPJ,
            Ativo = true
        });
        _permissoes.Setup(x => x.Obter(99, 2)).Returns(new ClientePermissaoUsuario
        {
            ClienteId = 99,
            UserId = 2,
            PodeEditar = true
        });
        _tarefas.Setup(x => x.Create(It.IsAny<Tarefa>())).Returns(300);

        var tarefa = _service.Criar(new TarefaCadastroEntrada
        {
            ClienteId = 99,
            Titulo = "Enviar fechamento",
            VencimentoLocal = new DateTime(2026, 2, 28, 9, 0, 0),
            ResponsavelUserId = 2,
            Recorrencia = TarefaRecorrencia.Mensal
        }, 1);

        tarefa.Id.Should().Be(300);
        tarefa.Recorrencia.Should().Be(TarefaRecorrencia.Mensal);
        tarefa.DiaRecorrenciaMensal.Should().Be(28);
    }

    [Fact]
    public void Criar_Admin_DeveNegarAtribuicaoParaUsuarioForaDaHierarquia()
    {
        var admin = NovoUsuario(1, UserRole.Admin);
        var operadorSemHierarquia = NovoUsuario(2, UserRole.Usuario);
        _users.Setup(x => x.GetById(1)).Returns(admin);
        _users.Setup(x => x.GetById(2)).Returns(operadorSemHierarquia);
        _users.Setup(x => x.ListarPorAdmin(1)).Returns(new List<User>());
        _clientes.Setup(x => x.GetById(9)).Returns(new Cliente
        {
            Id = 9,
            Nome = "Empresa",
            Documento = "12345678909",
            TipoDocumento = TipoDocumentoCliente.CPF,
            Ativo = true
        });

        var act = () => _service.Criar(new TarefaCadastroEntrada
        {
            ClienteId = 9,
            Titulo = "Tarefa bloqueada",
            VencimentoLocal = DateTime.Now.AddHours(2),
            ResponsavelUserId = 2,
            Recorrencia = TarefaRecorrencia.Nenhuma
        }, 1);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Admin só pode criar tarefa para si ou para subordinados*");
    }

    [Fact]
    public void Atualizar_ConcluirMensal_DeveGerarProximaTarefa()
    {
        var admin = NovoUsuario(1, UserRole.Admin);
        _users.Setup(x => x.GetById(1)).Returns(admin);

        var atual = new Tarefa
        {
            Id = 44,
            ClienteId = 9,
            Titulo = "Apurar tributos",
            VencimentoUtc = new DateTime(2026, 1, 31, 12, 0, 0, DateTimeKind.Utc),
            ResponsavelUserId = 1,
            Status = TarefaStatus.EmAndamento,
            Recorrencia = TarefaRecorrencia.Mensal,
            DiaRecorrenciaMensal = 31,
            Ativa = true,
            CriadoPorUserId = 1,
            CriadoEmUtc = DateTime.UtcNow.AddDays(-10),
            AtualizadoEmUtc = DateTime.UtcNow.AddDays(-1)
        };

        _tarefas.Setup(x => x.GetById(44)).Returns(atual);
        _tarefas.Setup(x => x.Create(It.IsAny<Tarefa>())).Returns(55);
        _clientes.Setup(x => x.GetById(9)).Returns(new Cliente
        {
            Id = 9,
            Nome = "Empresa",
            Documento = "12345678909",
            TipoDocumento = TipoDocumentoCliente.CPF,
            Ativo = true
        });

        var retorno = _service.AlterarStatus(44, TarefaStatus.Concluida, 1);

        retorno.Status.Should().Be(TarefaStatus.Concluida);
        _tarefas.Verify(x => x.Update(It.Is<Tarefa>(t => t.Id == 44 && t.Status == TarefaStatus.Concluida)), Times.Once);
        _tarefas.Verify(x => x.Create(It.Is<Tarefa>(t =>
            t.ClienteId == 9 &&
            t.Recorrencia == TarefaRecorrencia.Mensal &&
            t.Status == TarefaStatus.Agendada &&
            t.DiaRecorrenciaMensal == 31)), Times.Once);
    }

    [Fact]
    public void Buscar_UsuarioComum_DeveRespeitarFiltroSomenteMinhas()
    {
        _users.Setup(x => x.GetById(10)).Returns(NovoUsuario(10, UserRole.Usuario));
        _permissoes.Setup(x => x.Obter(7, 10)).Returns(new ClientePermissaoUsuario
        {
            ClienteId = 7,
            UserId = 10,
            PodeEditar = true
        });
        _tarefas.Setup(x => x.Buscar(7, null, null, null, null, 10))
            .Returns(new List<Tarefa>
            {
                new()
                {
                    Id = 1,
                    ClienteId = 7,
                    Titulo = "Minha tarefa",
                    VencimentoUtc = DateTime.UtcNow,
                    ResponsavelUserId = 10,
                    Status = TarefaStatus.Agendada,
                    CriadoPorUserId = 10
                }
            });

        var tarefas = _service.Buscar(new TarefaFiltroConsulta
        {
            ClienteId = 7,
            SomenteMinhasTarefas = true
        }, 10);

        tarefas.Should().HaveCount(1);
        tarefas[0].ResponsavelUserId.Should().Be(10);
        _tarefas.Verify(x => x.Buscar(7, null, null, null, null, 10), Times.Once);
    }

    [Fact]
    public void Buscar_UsuarioComum_NaoDeveVerTarefaDeTerceiroMesmoComPermissaoNoCliente()
    {
        _users.Setup(x => x.GetById(10)).Returns(NovoUsuario(10, UserRole.Usuario));
        _permissoes.Setup(x => x.Obter(7, 10)).Returns(new ClientePermissaoUsuario
        {
            ClienteId = 7,
            UserId = 10,
            PodeEditar = true
        });
        _tarefas.Setup(x => x.Buscar(7, null, null, null, null, 10))
            .Returns(new List<Tarefa>
            {
                new()
                {
                    Id = 1,
                    ClienteId = 7,
                    Titulo = "Minha tarefa",
                    VencimentoUtc = DateTime.UtcNow,
                    ResponsavelUserId = 10,
                    Status = TarefaStatus.Agendada,
                    CriadoPorUserId = 10
                },
                new()
                {
                    Id = 2,
                    ClienteId = 7,
                    Titulo = "Tarefa de terceiro",
                    VencimentoUtc = DateTime.UtcNow,
                    ResponsavelUserId = 22,
                    Status = TarefaStatus.Agendada,
                    CriadoPorUserId = 22
                }
            });

        var tarefas = _service.Buscar(new TarefaFiltroConsulta
        {
            ClienteId = 7,
            SomenteMinhasTarefas = false
        }, 10);

        tarefas.Should().HaveCount(1);
        tarefas[0].Id.Should().Be(1);
    }

    [Fact]
    public void Buscar_Admin_DeveVerPropriasEMaisSubordinados()
    {
        _users.Setup(x => x.GetById(1)).Returns(NovoUsuario(1, UserRole.Admin));
        _users.Setup(x => x.ListarPorAdmin(1)).Returns(new List<User>
        {
            NovoUsuario(2, UserRole.Usuario, responsavelAdminId: 1)
        });
        _tarefas.Setup(x => x.Buscar(9, null, null, null, null, null))
            .Returns(new List<Tarefa>
            {
                new()
                {
                    Id = 11,
                    ClienteId = 9,
                    Titulo = "Minha tarefa admin",
                    VencimentoUtc = DateTime.UtcNow,
                    ResponsavelUserId = 1,
                    CriadoPorUserId = 1,
                    Status = TarefaStatus.Agendada
                },
                new()
                {
                    Id = 12,
                    ClienteId = 9,
                    Titulo = "Tarefa subordinado",
                    VencimentoUtc = DateTime.UtcNow,
                    ResponsavelUserId = 2,
                    CriadoPorUserId = 2,
                    Status = TarefaStatus.Agendada
                },
                new()
                {
                    Id = 13,
                    ClienteId = 9,
                    Titulo = "Tarefa externo",
                    VencimentoUtc = DateTime.UtcNow,
                    ResponsavelUserId = 99,
                    CriadoPorUserId = 99,
                    Status = TarefaStatus.Agendada
                }
            });

        var tarefas = _service.Buscar(new TarefaFiltroConsulta
        {
            ClienteId = 9,
            SomenteMinhasTarefas = false
        }, 1);

        tarefas.Select(t => t.Id).Should().BeEquivalentTo([11, 12]);
    }

    [Fact]
    public void Buscar_Supremo_DeveVerTodasAsTarefasDoCliente()
    {
        _users.Setup(x => x.GetById(50)).Returns(NovoUsuario(50, UserRole.Supremo));
        _tarefas.Setup(x => x.Buscar(9, null, null, null, null, null))
            .Returns(new List<Tarefa>
            {
                new()
                {
                    Id = 21,
                    ClienteId = 9,
                    Titulo = "Tarefa A",
                    VencimentoUtc = DateTime.UtcNow,
                    ResponsavelUserId = 2,
                    CriadoPorUserId = 2,
                    Status = TarefaStatus.Agendada
                },
                new()
                {
                    Id = 22,
                    ClienteId = 9,
                    Titulo = "Tarefa B",
                    VencimentoUtc = DateTime.UtcNow,
                    ResponsavelUserId = 99,
                    CriadoPorUserId = 99,
                    Status = TarefaStatus.Agendada
                }
            });

        var tarefas = _service.Buscar(new TarefaFiltroConsulta
        {
            ClienteId = 9,
            SomenteMinhasTarefas = false
        }, 50);

        tarefas.Should().HaveCount(2);
        tarefas.Select(t => t.Id).Should().BeEquivalentTo([21, 22]);
    }

    [Fact]
    public void BuscarHistoricoGlobal_UsuarioComum_DeveRespeitarPermissoes()
    {
        _users.Setup(x => x.GetById(10)).Returns(NovoUsuario(10, UserRole.Usuario));
        _clientes.Setup(x => x.ListarTodos()).Returns(new List<Cliente>
        {
            new() { Id = 7, CodigoCliente = "CLI-0007", Nome = "Cliente 7", Documento = "11111111000191", TipoDocumento = TipoDocumentoCliente.CNPJ, Ativo = true },
            new() { Id = 8, CodigoCliente = "CLI-0008", Nome = "Cliente 8", Documento = "22222222000191", TipoDocumento = TipoDocumentoCliente.CNPJ, Ativo = true }
        });
        _permissoes.Setup(x => x.Obter(7, 10)).Returns(new ClientePermissaoUsuario
        {
            ClienteId = 7,
            UserId = 10,
            PodeEditar = true
        });
        _permissoes.Setup(x => x.Obter(8, 10)).Returns((ClientePermissaoUsuario?)null);
        _tarefas.Setup(x => x.BuscarPorClienteIds(
            It.IsAny<IReadOnlyList<int>>(),
            null, null, null, null, null)).Returns(new List<Tarefa>
        {
            new()
            {
                Id = 1,
                ClienteId = 7,
                Titulo = "Tarefa cliente 7",
                ResponsavelUserId = 10,
                CriadoPorUserId = 10,
                Status = TarefaStatus.Concluida,
                AtualizadoEmUtc = DateTime.UtcNow
            }
        });

        var resultado = _service.BuscarHistoricoGlobal(10, 20);

        resultado.Should().HaveCount(1);
        resultado[0].ClienteId.Should().Be(7);
        _tarefas.Verify(x => x.BuscarPorClienteIds(It.Is<IReadOnlyList<int>>(ids => ids.Count == 1 && ids[0] == 7), null, null, null, null, null), Times.Once);
    }

    [Fact]
    public void BuscarHistoricoGlobal_Admin_DeveRetornarHistoricoAgregado()
    {
        _users.Setup(x => x.GetById(1)).Returns(NovoUsuario(1, UserRole.Admin));
        _clientes.Setup(x => x.ListarTodos()).Returns(new List<Cliente>
        {
            new() { Id = 7, CodigoCliente = "CLI-0007", Nome = "Cliente 7", Documento = "11111111000191", TipoDocumento = TipoDocumentoCliente.CNPJ, Ativo = true },
            new() { Id = 8, CodigoCliente = "CLI-0008", Nome = "Cliente 8", Documento = "22222222000191", TipoDocumento = TipoDocumentoCliente.CNPJ, Ativo = true }
        });
        _tarefas.Setup(x => x.BuscarPorClienteIds(
            It.IsAny<IReadOnlyList<int>>(),
            null, null, null, null, null)).Returns(new List<Tarefa>
        {
            new()
            {
                Id = 10,
                ClienteId = 7,
                Titulo = "Tarefa cliente 7",
                ResponsavelUserId = 2,
                CriadoPorUserId = 1,
                Status = TarefaStatus.Concluida,
                AtualizadoEmUtc = DateTime.UtcNow.AddMinutes(-2)
            },
            new()
            {
                Id = 11,
                ClienteId = 8,
                Titulo = "Tarefa cliente 8",
                ResponsavelUserId = 3,
                CriadoPorUserId = 1,
                Status = TarefaStatus.Agendada,
                AtualizadoEmUtc = DateTime.UtcNow.AddMinutes(-1)
            }
        });

        var resultado = _service.BuscarHistoricoGlobal(1, 20);

        resultado.Should().HaveCount(2);
        resultado.Select(x => x.ClienteId).Should().Contain(7);
        resultado.Select(x => x.ClienteId).Should().Contain(8);
        _tarefas.Verify(x => x.BuscarPorClienteIds(It.Is<IReadOnlyList<int>>(ids => ids.Count == 2 && ids.Contains(7) && ids.Contains(8)), null, null, null, null, null), Times.Once);
    }

    private static User NovoUsuario(int id, UserRole role, int? responsavelAdminId = null)
    {
        return new User
        {
            Id = id,
            Empresa = "Empresa",
            Nome = $"User {id}",
            Cpf = "123.456.789-09",
            Cargo = "Operador",
            Email = $"u{id}@protons.local",
            Status = UserStatus.Ativo,
            Role = role,
            ResponsavelAdminId = responsavelAdminId,
            CriadoEmUtc = DateTime.UtcNow,
            AtualizadoEmUtc = DateTime.UtcNow
        };
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
