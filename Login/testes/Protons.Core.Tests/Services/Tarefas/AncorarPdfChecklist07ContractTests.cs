using FluentAssertions;
using Protons.Core.Tarefas.Models;
using Xunit;

namespace Protons.Core.Tests.Services.Tarefas;

/// <summary>
/// C7 — Gate G1 (blocking): Contratos dos modelos de observabilidade e auditoria.
/// Verifica invariantes estáticos sem banco de dados ou I/O.
/// </summary>
[Trait("Checklist", "C7")]
[Trait("Category", "C7_G1_Contratos")]
public sealed class AncorarPdfChecklist07ContractTests
{
    // --- Tipos de evento canônicos ---

    [Fact]
    public void T01_ErroCodigos_TarefaAgendada_eh_string_literal_correta()
    {
        AncorarPdfErroCodigos.TarefaAgendada.Should().Be("tarefa_agendada");
    }

    [Fact]
    public void T02_ErroCodigos_seis_tipos_de_evento_sao_nao_vazios_e_distintos()
    {
        var eventos = new[]
        {
            AncorarPdfErroCodigos.TarefaAgendada,
            AncorarPdfErroCodigos.ExecucaoIniciada,
            AncorarPdfErroCodigos.ExecucaoConcluida,
            AncorarPdfErroCodigos.ExecucaoFalhou,
            AncorarPdfErroCodigos.MisfireDetectado,
            AncorarPdfErroCodigos.BacklogDecisao
        };

        eventos.Should().OnlyContain(e => !string.IsNullOrWhiteSpace(e), "eventos devem ser não-vazios");
        eventos.Distinct().Should().HaveCount(6, "cada tipo de evento deve ser único");
    }

    [Fact]
    public void T03_ErroCodigos_NEG_seguem_formato_ANCORA_NEG()
    {
        var codigos = new[]
        {
            AncorarPdfErroCodigos.NegPdfSemTexto,
            AncorarPdfErroCodigos.NegClienteNaoValidado,
            AncorarPdfErroCodigos.NegArquivoNaoEncontrado,
            AncorarPdfErroCodigos.NegConfigNaoEncontrada
        };

        codigos.Should().OnlyContain(c => c.StartsWith("ANCORA-NEG-", StringComparison.Ordinal),
            "todos os erros de negócio devem ter prefixo ANCORA-NEG-");
    }

    [Fact]
    public void T04_ErroCodigos_TEC_seguem_formato_ANCORA_TEC()
    {
        var codigos = new[]
        {
            AncorarPdfErroCodigos.TecFalhaIo,
            AncorarPdfErroCodigos.TecFalhaTimeout,
            AncorarPdfErroCodigos.TecFalhaGeral
        };

        codigos.Should().OnlyContain(c => c.StartsWith("ANCORA-TEC-", StringComparison.Ordinal),
            "todos os erros técnicos devem ter prefixo ANCORA-TEC-");
    }

    [Fact]
    public void T05_ErroCodigos_Nenhum_eh_string_vazia()
    {
        AncorarPdfErroCodigos.Nenhum.Should().BeEmpty("eventos sem erro devem usar string vazia");
    }

    [Fact]
    public void T06_ErroCodigos_status_constants_sao_nao_vazios_exceto_desconhecido()
    {
        AncorarPdfErroCodigos.StatusAgendada.Should().NotBeNullOrEmpty();
        AncorarPdfErroCodigos.StatusEmAndamento.Should().NotBeNullOrEmpty();
        AncorarPdfErroCodigos.StatusConcluida.Should().NotBeNullOrEmpty();
        AncorarPdfErroCodigos.StatusBloqueada.Should().NotBeNullOrEmpty();
        AncorarPdfErroCodigos.StatusDesconhecido.Should().BeEmpty("desconhecido representa ausência de status");
    }

    // --- AncorarPdfSchedulerEventoRegistro (campos C7) ---

    [Fact]
    public void T07_EventoRegistro_inicializa_com_defaults_C7_corretos()
    {
        var evento = new AncorarPdfSchedulerEventoRegistro
        {
            TarefaId = 1,
            ClienteId = 1,
            TipoEvento = AncorarPdfErroCodigos.TarefaAgendada,
            OcorreuEmUtc = DateTime.UtcNow
        };

        // Defaults C7 corretos
        evento.CorrelationId.Should().BeEmpty("CorrelationId default deve ser string vazia");
        evento.StatusAnterior.Should().Be(AncorarPdfErroCodigos.StatusDesconhecido);
        evento.StatusNovo.Should().Be(AncorarPdfErroCodigos.StatusDesconhecido);
        evento.ErroCodigo.Should().Be(AncorarPdfErroCodigos.Nenhum);
        evento.ExecutadaComAtraso.Should().BeFalse();
    }

    [Fact]
    public void T08_EventoRegistro_campos_C7_sao_configuráveis()
    {
        var correlationId = Guid.NewGuid().ToString("N");
        var evento = new AncorarPdfSchedulerEventoRegistro
        {
            TarefaId = 42,
            ClienteId = 10,
            TipoEvento = AncorarPdfErroCodigos.ExecucaoFalhou,
            OcorreuEmUtc = DateTime.UtcNow,
            CorrelationId = correlationId,
            StatusAnterior = AncorarPdfErroCodigos.StatusEmAndamento,
            StatusNovo = AncorarPdfErroCodigos.StatusBloqueada,
            ErroCodigo = AncorarPdfErroCodigos.NegPdfSemTexto,
            ExecutadaComAtraso = true
        };

        evento.CorrelationId.Should().Be(correlationId);
        evento.StatusAnterior.Should().Be(AncorarPdfErroCodigos.StatusEmAndamento);
        evento.StatusNovo.Should().Be(AncorarPdfErroCodigos.StatusBloqueada);
        evento.ErroCodigo.Should().Be(AncorarPdfErroCodigos.NegPdfSemTexto);
        evento.ExecutadaComAtraso.Should().BeTrue();
    }

    // --- AncorarPdfEventoHistoricoFiltro ---

    [Fact]
    public void T09_EventoHistoricoFiltro_limite_default_eh_100()
    {
        var filtro = new AncorarPdfEventoHistoricoFiltro { ClienteId = 1 };

        filtro.Limite.Should().Be(100, "limite padrão deve ser 100 registros");
    }

    [Fact]
    public void T10_EventoHistoricoFiltro_campos_opcionais_sao_nulos_por_padrao()
    {
        var filtro = new AncorarPdfEventoHistoricoFiltro { ClienteId = 5 };

        filtro.TarefaId.Should().BeNull();
        filtro.TipoEvento.Should().BeNull();
        filtro.DataInicioUtc.Should().BeNull();
        filtro.DataFimUtc.Should().BeNull();
    }

    // --- AncorarPdfEventoHistoricoItem ---

    [Fact]
    public void T11_EventoHistoricoItem_todos_campos_acessiveis()
    {
        var agora = new DateTime(2026, 2, 26, 12, 0, 0, DateTimeKind.Utc);
        var item = new AncorarPdfEventoHistoricoItem
        {
            Id = 99,
            TarefaId = 1,
            ClienteId = 2,
            TipoEvento = AncorarPdfErroCodigos.ExecucaoConcluida,
            Detalhes = "variaveis=3",
            OcorreuEmUtc = agora,
            CorrelationId = "abc123",
            StatusAnterior = AncorarPdfErroCodigos.StatusEmAndamento,
            StatusNovo = AncorarPdfErroCodigos.StatusConcluida,
            ErroCodigo = AncorarPdfErroCodigos.Nenhum,
            ExecutadaComAtraso = false
        };

        item.Id.Should().Be(99);
        item.TipoEvento.Should().Be(AncorarPdfErroCodigos.ExecucaoConcluida);
        item.OcorreuEmUtc.Should().Be(agora);
        item.CorrelationId.Should().Be("abc123");
        item.ExecutadaComAtraso.Should().BeFalse();
    }

    // --- Namespace Core (sem dep de infra) ---

    [Fact]
    public void T12_ErroCodigos_e_modelos_C7_em_namespace_Core()
    {
        typeof(AncorarPdfErroCodigos).Assembly.GetName().Name
            .Should().Be("Protons.Core", "ErroCodigos deve estar em Protons.Core");

        typeof(AncorarPdfSchedulerEventoRegistro).Assembly.GetName().Name
            .Should().Be("Protons.Core", "EventoRegistro deve estar em Protons.Core");

        typeof(AncorarPdfEventoHistoricoItem).Assembly.GetName().Name
            .Should().Be("Protons.Core", "EventoHistoricoItem deve estar em Protons.Core");

        typeof(AncorarPdfEventoHistoricoFiltro).Assembly.GetName().Name
            .Should().Be("Protons.Core", "EventoHistoricoFiltro deve estar em Protons.Core");
    }
}
