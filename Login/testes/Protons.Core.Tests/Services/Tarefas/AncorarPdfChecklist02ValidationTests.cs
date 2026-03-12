using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Protons.Core.Login.Models;
using Protons.Core.Login.Repositories;
using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Repositories;
using Protons.Core.Tarefas.Services;

namespace Protons.Core.Tests.Services.Tarefas;

public sealed class AncorarPdfChecklist02ValidationTests
{
    private readonly Mock<ITarefaService> _tarefas = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IAncorarPdfConfiguracaoRepository> _repo = new();

    [Fact]
    [Trait("ChecklistGate", "C2_P1")]
    [Trait("Category", "C2_P1")]
    public void C2_P1_DeveFalharQuandoHighlightOpacityForaDaFaixa()
    {
        var sut = CriarSut();
        var entrada = CriarEntradaBase() with { HighlightOpacity = 0.20 };

        var act = () => sut.CriarOuAtualizar(entrada, 77);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*HighlightOpacity*");
    }

    [Fact]
    [Trait("ChecklistGate", "C2_P2")]
    [Trait("Category", "C2_P2")]
    public void C2_P2_CrossClienteDeveFalharQuandoSolicitanteNaoEhAdmin()
    {
        var sut = CriarSut(role: UserRole.Usuario);
        var entrada = CriarEntradaBase() with
        {
            PdfModeloCrossCliente = true,
            PdfModeloCrossClienteJustificativa = "Treinamento com base compartilhada aprovada."
        };

        var act = () => sut.CriarOuAtualizar(entrada, 77);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*somente para Admin*");
    }

    [Fact]
    [Trait("ChecklistGate", "C2_P3")]
    [Trait("Category", "C2_P3")]
    public void C2_P3_CrossClienteSemJustificativaDeveFalhar()
    {
        var sut = CriarSut(role: UserRole.Admin);
        var entrada = CriarEntradaBase() with
        {
            PdfModeloCrossCliente = true,
            PdfModeloCrossClienteJustificativa = "curta"
        };

        var act = () => sut.CriarOuAtualizar(entrada, 77);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*mínimo 15 caracteres*");
    }

    [Fact]
    [Trait("ChecklistGate", "C2_P4")]
    [Trait("Category", "C2_P4")]
    public void C2_P4_PathForaDaAllowlistDeveFalhar()
    {
        var sut = CriarSut(pathPolicy: new DenyAllPathPolicy());
        var entrada = CriarEntradaBase();

        var act = () => sut.CriarOuAtualizar(entrada, 77);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*allowlist*");
    }

    [Fact]
    [Trait("ChecklistGate", "C2_P1")]
    [Trait("Category", "C2_P1")]
    public void C2_P1_DeveFalharQuandoOcrDpiForaDaFaixa()
    {
        var sut = CriarSut();
        var entrada = CriarEntradaBase() with
        {
            OcrFallbackAtivo = true,
            OcrDpi = 700,
            OcrLang = "por+eng"
        };

        var act = () => sut.CriarOuAtualizar(entrada, 77);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*OcrDpi*");
    }

    [Fact]
    [Trait("ChecklistGate", "C2_P1")]
    [Trait("Category", "C2_P1")]
    public void C2_P1_DeveFalharQuandoOcrLangInvalido()
    {
        var sut = CriarSut();
        var entrada = CriarEntradaBase() with
        {
            OcrFallbackAtivo = true,
            OcrDpi = 300,
            OcrLang = "por++eng"
        };

        var act = () => sut.CriarOuAtualizar(entrada, 77);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*OcrLang inválido*");
    }

    [Fact]
    [Trait("ChecklistGate", "C2_P1")]
    [Trait("Category", "C2_P1")]
    public void C2_P1_DeveNormalizarOcrLangAoSalvar()
    {
        var sut = CriarSut();
        var entrada = CriarEntradaBase() with
        {
            OcrFallbackAtivo = true,
            OcrDpi = 300,
            OcrLang = " POR + ENG + eng "
        };

        sut.CriarOuAtualizar(entrada, 77);

        _repo.Verify(x => x.Salvar(
                It.Is<AncorarPdfConfiguracaoTarefa>(cfg =>
                    cfg.OcrFallbackAtivo &&
                    cfg.OcrDpi == 300 &&
                    cfg.OcrLang == "por+eng"),
                It.IsAny<AncorarPdfTemplateHistoricoItem?>()),
            Times.Once);
    }

    [Fact]
    [Trait("ChecklistGate", "C2_P5")]
    [Trait("Category", "C2_P5")]
    public void C2_P5_ModoSelecaoInvalidoDeveFalhar()
    {
        var sut = CriarSut();
        var entrada = CriarEntradaBase() with
        {
            ModoSelecao = (AncorarPdfModoSelecao)999
        };

        var act = () => sut.CriarOuAtualizar(entrada, 77);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ModoSelecao inválido*");
    }

    [Fact]
    [Trait("ChecklistGate", "C2_G2_Core_Validation")]
    [Trait("ChecklistGate", "C2_F4")]
    [Trait("Category", "C2_F4")]
    public void C2_F4_DeveRejeitarRecorrenciaHorariaNoAncorarPdf()
    {
        var sut = CriarSut();
        var entrada = CriarEntradaBase() with { Recorrencia = TarefaRecorrencia.Horaria };

        var act = () => sut.CriarOuAtualizar(entrada, 77);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Recorrência inválida*");
    }

    [Fact]
    [Trait("ChecklistGate", "C2_G2_Core_Validation")]
    [Trait("ChecklistGate", "C2_F3")]
    [Trait("Category", "C2_F3")]
    public void C2_F3_DeveValidarNomeUnicoPorClienteEEsteira()
    {
        var sut = CriarSut();
        var entrada = CriarEntradaBase();

        _repo
            .Setup(x => x.ExisteNomeAtivoNoEscopo(
                entrada.ClienteId,
                entrada.EsteiraId,
                entrada.NomeTarefaPersonalizado,
                It.IsAny<int?>()))
            .Returns(true);

        var act = () => sut.CriarOuAtualizar(entrada, 77);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*mesmo nome*cliente/esteira*");

        _tarefas.Verify(x => x.Criar(It.IsAny<TarefaCadastroEntrada>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    [Trait("ChecklistGate", "C2_G2_Core_Validation")]
    [Trait("ChecklistGate", "C2_F17")]
    [Trait("Category", "C2_F17")]
    public void C2_F17_DeveExigirAssinaturaObrigatoriaNoSalvar()
    {
        var sut = CriarSut();
        var entradaSemAssinatura = CriarEntradaBase() with
        {
            ProgramadoPorUserId = 0,
            ProgramadoPorNome = string.Empty
        };

        var act = () => sut.CriarOuAtualizar(entradaSemAssinatura, 77);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ProgramadoPorUserId*obrigatório*");
    }

    [Fact]
    [Trait("ChecklistGate", "C2_G2_Core_Validation")]
    [Trait("ChecklistGate", "C2_F18")]
    [Trait("Category", "C2_F18")]
    public void C2_F18_DeveRespeitarIsolamentoClienteQuandoSolicitanteNaoPodeVisualizar()
    {
        var sut = CriarSut();

        _tarefas
            .Setup(x => x.ObterPorId(It.IsAny<int>(), It.IsAny<int>()))
            .Returns((Tarefa?)null);

        var resultado = sut.ObterPorTarefaId(900, 77);

        resultado.Should().BeNull();
    }

    [Fact]
    [Trait("ChecklistGate", "C2_G2_Core_Validation")]
    [Trait("Category", "C2_AncoraTexto")]
    public void DeveRejeitarModoTextoADireitaSemTextoAncora()
    {
        var sut = CriarSut();
        var ancoraBase = CriarEntradaBase().TemplateAncoras[0];
        var entrada = CriarEntradaBase() with
        {
            TemplateAncoras =
            [
                ancoraBase with
                {
                    ModoAncora = AncorarPdfModoAncora.TextoADireita,
                    TextoAncora = null
                }
            ]
        };

        var act = () => sut.CriarOuAtualizar(entrada, 77);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Texto âncora é obrigatório*");
    }

    [Fact]
    [Trait("ChecklistGate", "C2_G2_Core_Validation")]
    [Trait("Category", "C2_AncoraTexto")]
    public void DeveRejeitarModoTextoADireitaComLarguraExtracaoForaDaFaixa()
    {
        var sut = CriarSut();
        var ancoraBase = CriarEntradaBase().TemplateAncoras[0];
        var entrada = CriarEntradaBase() with
        {
            TemplateAncoras =
            [
                ancoraBase with
                {
                    ModoAncora = AncorarPdfModoAncora.TextoADireita,
                    TextoAncora = "Valor:",
                    LarguraExtracaoRel = 1.5
                }
            ]
        };

        var act = () => sut.CriarOuAtualizar(entrada, 77);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*LarguraExtracaoRel*");
    }

    [Fact]
    [Trait("ChecklistGate", "C2_G2_Core_Validation")]
    [Trait("ChecklistGate", "C2_F6")]
    [Trait("Category", "C2_F6")]
    public void C2_F6_DeveRejeitarTemplateComCorDuplicada()
    {
        var sut = CriarSut();
        var ancoraBase = CriarEntradaBase().TemplateAncoras[0];
        var entrada = CriarEntradaBase() with
        {
            TemplateAncoras =
            [
                ancoraBase,
                ancoraBase with
                {
                    Ordem = 1,
                    Metadado = ancoraBase.Metadado with
                    {
                        ChaveTecnica = "cpf_2"
                    }
                }
            ]
        };

        var act = () => sut.CriarOuAtualizar(entrada, 77);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cor duplicada*");
    }

    [Fact]
    [Trait("ChecklistGate", "C2_G2_Core_Validation")]
    [Trait("ChecklistGate", "C2_F10")]
    [Trait("Category", "C2_F10")]
    public void C2_F10_DeveRejeitarTemplateComChaveTecnicaDuplicada()
    {
        var sut = CriarSut();
        var ancoraBase = CriarEntradaBase().TemplateAncoras[0];
        var entrada = CriarEntradaBase() with
        {
            TemplateAncoras =
            [
                ancoraBase,
                ancoraBase with
                {
                    Ordem = 1,
                    CorHex = "#F5D547",
                    Metadado = ancoraBase.Metadado with
                    {
                        NomeExibido = "CPF secundario",
                        ChaveTecnica = "cpf"
                    }
                }
            ]
        };

        var act = () => sut.CriarOuAtualizar(entrada, 77);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Chave técnica duplicada*");
    }

    [Fact]
    [Trait("ChecklistGate", "C2_G2_Core_Validation")]
    [Trait("ChecklistGate", "C2_F18")]
    [Trait("Category", "C2_F18")]
    public void C2_F18_ListarHistoricoNaoDeveRetornarParaFerramentaNaoAncorarPdf()
    {
        var sut = CriarSut();
        _tarefas
            .Setup(x => x.ObterPorId(145, 77))
            .Returns(new Tarefa
            {
                Id = 145,
                ClienteId = 9,
                FerramentaId = FerramentaTarefaIds.Generica,
                Titulo = "Generica",
                VencimentoUtc = DateTime.UtcNow.AddHours(1),
                ResponsavelUserId = 77,
                Recorrencia = TarefaRecorrencia.Nenhuma,
                EsteiraId = 2,
                CriadoPorUserId = 77,
                Status = TarefaStatus.Agendada,
                Ativa = true,
                CriadoEmUtc = DateTime.UtcNow,
                AtualizadoEmUtc = DateTime.UtcNow
            });

        var historico = sut.ListarHistoricoTemplate(145, 77, 20);

        historico.Should().BeEmpty();
        _repo.Verify(x => x.ListarHistoricoTemplate(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    [Trait("ChecklistGate", "C2_G2_Core_Validation")]
    [Trait("ChecklistGate", "C2_F17")]
    [Trait("Category", "C2_F17")]
    public void C2_F17_DeveUsarTimeProviderParaCamposDeAuditoria()
    {
        var instanteFixo = new DateTimeOffset(2026, 2, 22, 20, 0, 0, TimeSpan.Zero);
        var sut = CriarSut(new FixedTimeProvider(instanteFixo));

        var resultado = sut.CriarOuAtualizar(CriarEntradaBase(), 77);

        resultado.ProgramadoEmUtc.Should().Be(instanteFixo.UtcDateTime);
        resultado.AtualizadoEmUtc.Should().Be(instanteFixo.UtcDateTime);
    }

    [Fact]
    [Trait("ChecklistGate", "C2_P6")]
    [Trait("Category", "C2_P6")]
    public async Task C2_P6_CriarOuAtualizarDeveSerializarChamadasConcorrentes()
    {
        var sut = CriarSut();
        var liberarCriacao = new ManualResetEventSlim(false);

        var emExecucao = 0;
        var maxEmExecucao = 0;
        var sequencialId = 300;

        _tarefas
            .Setup(x => x.Criar(It.IsAny<TarefaCadastroEntrada>(), It.IsAny<int>()))
            .Returns((TarefaCadastroEntrada entrada, int userId) =>
            {
                var atual = Interlocked.Increment(ref emExecucao);
                AtualizarMaximo(ref maxEmExecucao, atual);
                liberarCriacao.Wait(TimeSpan.FromSeconds(2));
                Thread.Sleep(25);
                Interlocked.Decrement(ref emExecucao);

                return new Tarefa
                {
                    Id = Interlocked.Increment(ref sequencialId),
                    ClienteId = entrada.ClienteId,
                    FerramentaId = FerramentaTarefaIds.AncorarPdfCanonico,
                    Titulo = entrada.Titulo,
                    VencimentoUtc = entrada.VencimentoLocal.ToUniversalTime(),
                    ResponsavelUserId = entrada.ResponsavelUserId,
                    Recorrencia = entrada.Recorrencia,
                    EsteiraId = entrada.EsteiraId,
                    CriadoPorUserId = userId,
                    Status = TarefaStatus.Agendada,
                    Ativa = true,
                    CriadoEmUtc = DateTime.UtcNow,
                    AtualizadoEmUtc = DateTime.UtcNow
                };
            });

        var entradaA = CriarEntradaBase() with { NomeTarefaPersonalizado = "Concorrencia A" };
        var entradaB = CriarEntradaBase() with { NomeTarefaPersonalizado = "Concorrencia B" };

        var t1 = Task.Run(() => sut.CriarOuAtualizar(entradaA, 77));
        await Task.Delay(60);
        var t2 = Task.Run(() => sut.CriarOuAtualizar(entradaB, 77));

        await Task.Delay(80);
        liberarCriacao.Set();
        await Task.WhenAll(t1, t2);

        maxEmExecucao.Should().Be(1);
    }

    [Fact]
    [Trait("ChecklistGate", "C2_P8")]
    [Trait("Category", "C2_P8")]
    public async Task C2_P8_EscoposDistintosNaoDevemSerializarGlobalmente()
    {
        var sut = CriarSut();
        var liberarCriacao = new ManualResetEventSlim(false);
        var emExecucao = 0;
        var maxEmExecucao = 0;
        var sequencialId = 400;

        _tarefas
            .Setup(x => x.Criar(It.IsAny<TarefaCadastroEntrada>(), It.IsAny<int>()))
            .Returns((TarefaCadastroEntrada entrada, int userId) =>
            {
                var atual = Interlocked.Increment(ref emExecucao);
                AtualizarMaximo(ref maxEmExecucao, atual);
                liberarCriacao.Wait(TimeSpan.FromSeconds(2));
                Thread.Sleep(25);
                Interlocked.Decrement(ref emExecucao);

                return new Tarefa
                {
                    Id = Interlocked.Increment(ref sequencialId),
                    ClienteId = entrada.ClienteId,
                    FerramentaId = FerramentaTarefaIds.AncorarPdfCanonico,
                    Titulo = entrada.Titulo,
                    VencimentoUtc = entrada.VencimentoLocal.ToUniversalTime(),
                    ResponsavelUserId = entrada.ResponsavelUserId,
                    Recorrencia = entrada.Recorrencia,
                    EsteiraId = entrada.EsteiraId,
                    CriadoPorUserId = userId,
                    Status = TarefaStatus.Agendada,
                    Ativa = true,
                    CriadoEmUtc = DateTime.UtcNow,
                    AtualizadoEmUtc = DateTime.UtcNow
                };
            });

        var entradaA = CriarEntradaBase() with
        {
            NomeTarefaPersonalizado = "Escopo A",
            EsteiraId = 2
        };
        var entradaB = CriarEntradaBase() with
        {
            NomeTarefaPersonalizado = "Escopo B",
            EsteiraId = 7
        };

        var t1 = Task.Run(() => sut.CriarOuAtualizar(entradaA, 77));
        await Task.Delay(60);
        var t2 = Task.Run(() => sut.CriarOuAtualizar(entradaB, 77));

        await Task.Delay(80);
        liberarCriacao.Set();
        await Task.WhenAll(t1, t2);

        maxEmExecucao.Should().BeGreaterThan(1);
    }

    private AncorarPdfConfiguracaoService CriarSut(
        TimeProvider? timeProvider = null,
        UserRole role = UserRole.Admin,
        IAncorarPdfPathPolicy? pathPolicy = null)
    {
        _users
            .Setup(x => x.GetById(It.IsAny<int>()))
            .Returns(new User
            {
                Id = 77,
                Nome = "Checklist C2",
                Email = "c2@protons.local",
                Status = UserStatus.Ativo,
                Role = role
            });

        _tarefas
            .Setup(x => x.Criar(It.IsAny<TarefaCadastroEntrada>(), It.IsAny<int>()))
            .Returns((TarefaCadastroEntrada entrada, int userId) => new Tarefa
            {
                Id = 145,
                ClienteId = entrada.ClienteId,
                FerramentaId = FerramentaTarefaIds.AncorarPdfCanonico,
                Titulo = entrada.Titulo,
                VencimentoUtc = entrada.VencimentoLocal.ToUniversalTime(),
                ResponsavelUserId = entrada.ResponsavelUserId,
                Recorrencia = entrada.Recorrencia,
                EsteiraId = entrada.EsteiraId,
                CriadoPorUserId = userId,
                Status = TarefaStatus.Agendada,
                Ativa = true,
                CriadoEmUtc = DateTime.UtcNow,
                AtualizadoEmUtc = DateTime.UtcNow
            });

        _repo
            .Setup(x => x.ExisteNomeAtivoNoEscopo(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int?>()))
            .Returns(false);
        _repo
            .Setup(x => x.ObterPorTarefaId(It.IsAny<int>()))
            .Returns((AncorarPdfConfiguracaoTarefa?)null);
        _repo
            .Setup(x => x.Salvar(It.IsAny<AncorarPdfConfiguracaoTarefa>(), It.IsAny<AncorarPdfTemplateHistoricoItem?>()));

        return new AncorarPdfConfiguracaoService(_tarefas.Object, _users.Object, _repo.Object, pathPolicy, timeProvider);
    }

    private static AncorarPdfSalvarEntrada CriarEntradaBase()
    {
        return new AncorarPdfSalvarEntrada
        {
            ClienteId = 9,
            EsteiraId = 2,
            NomeTarefaPersonalizado = "Ancorar Folha",
            AgendamentoLocal = new DateTime(2026, 2, 22, 14, 30, 15, DateTimeKind.Local),
            Recorrencia = TarefaRecorrencia.Nenhuma,
            AgendamentoSegundo = 15,
            PastaMonitoradaPath = "/tmp/entrada",
            PdfModeloPath = "/tmp/modelo.pdf",
            NomeReferenciaArquivo = "holerite",
            MonitorarSubpastas = false,
            ValidacaoClienteAtiva = true,
            LimiarSimilaridadeNome = 0.75,
            HighlightOpacity = 0.40,
            ModoSelecao = AncorarPdfModoSelecao.RetanguloLivre,
            PdfModeloCrossCliente = false,
            PdfModeloCrossClienteJustificativa = null,
            ProgramadoPorUserId = 77,
            ProgramadoPorNome = "Checklist C2",
            TemplateAncoras =
            [
                new AncorarPdfTemplateAncora
                {
                    Ordem = 0,
                    CorHex = "#4A90D9",
                    Pagina = 1,
                    XRel = 0.10,
                    YRel = 0.10,
                    LarguraRel = 0.20,
                    AlturaRel = 0.08,
                    Metadado = new AncorarPdfTemplateMetadado
                    {
                        NomeExibido = "CPF",
                        ChaveTecnica = "cpf",
                        TipoEsperado = "texto"
                    }
                }
            ]
        };
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    private sealed class DenyAllPathPolicy : IAncorarPdfPathPolicy
    {
        public PathPolicyResult Validar(string caminho, AncorarPdfPathTipo tipo, int clienteId)
        {
            return PathPolicyResult.Falha("allowlist bloqueou o caminho informado.");
        }
    }

    private static void AtualizarMaximo(ref int destino, int valor)
    {
        while (true)
        {
            var atual = Volatile.Read(ref destino);
            if (valor <= atual)
                return;

            if (Interlocked.CompareExchange(ref destino, valor, atual) == atual)
                return;
        }
    }
}
