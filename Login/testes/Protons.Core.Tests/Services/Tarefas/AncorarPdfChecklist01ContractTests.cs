using FluentAssertions;
using Protons.Core.Tarefas.Models;
using Protons.UI.Painel.Funcionalidades.Ferramentas.AncorarPdf.Dominio;
using Xunit;

namespace Protons.Core.Tests.Services.Tarefas;

public sealed class AncorarPdfChecklist01ContractTests
{
    [Fact]
    public void FerramentaIds_DeveReconhecerCanonicoAncorarPdf()
    {
        FerramentaTarefaIds.EhAncorarPdf("ancorar_pdf").Should().BeTrue();
        FerramentaTarefaIds.EhAncorarPdf("ANCORAR_PDF").Should().BeTrue();
        FerramentaTarefaIds.EhAncorarPdf("extrator_pdf").Should().BeFalse("alias removido na C14");
        FerramentaTarefaIds.EhAncorarPdf("desconhecida").Should().BeFalse();
        FerramentaTarefaIds.EhAncorarPdf(null).Should().BeFalse();
    }

    [Fact]
    public void SelecaoNomeAproximado_DeveAplicarOrdenacaoDeterministica()
    {
        var candidatos = new[]
        {
            new CandidatoArquivoNomeAproximado("nota_fiscal", "/tmp/c.pdf", 0.80, new DateTime(2026, 2, 1, 10, 0, 0, DateTimeKind.Utc)),
            new CandidatoArquivoNomeAproximado("nota_fiscal", "/tmp/b.pdf", 0.92, new DateTime(2026, 2, 1, 9, 0, 0, DateTimeKind.Utc)),
            new CandidatoArquivoNomeAproximado("nota_fiscal", "/tmp/a.pdf", 0.92, new DateTime(2026, 2, 1, 9, 0, 0, DateTimeKind.Utc)),
            new CandidatoArquivoNomeAproximado("nota_fiscal", "/tmp/d.pdf", 0.60, new DateTime(2026, 2, 1, 12, 0, 0, DateTimeKind.Utc))
        };

        var melhor = SelecaoNomeAproximadoPorCiclo.SelecionarMelhor(candidatos, 0.75);

        melhor.Should().NotBeNull();
        melhor!.ArquivoPath.Should().Be("/tmp/a.pdf");
    }

    [Fact]
    public void ConfigPadrao_DeveTrazerRetryBackoffEFlagsEsperados()
    {
        var config = new AncorarPdfConfig();

        config.MonitorarSubpastas.Should().BeFalse();
        config.ValidacaoClienteAtiva.Should().BeTrue();
        config.RetryTentativas.Should().Be(3);
        config.RetryBackoffSegundos.Should().Equal(5, 20, 60);
        config.LimiarSimilaridadeNome.Should().Be(0.75);
    }

    [Fact]
    public void IdempotenciaPorCiclo_DeveBloquearQuandoNomeJaProcessado()
    {
        SelecaoNomeAproximadoPorCiclo.PodeProcessarNoCiclo(nomeEsperadoJaProcessadoNoCiclo: false)
            .Should().BeTrue();

        SelecaoNomeAproximadoPorCiclo.PodeProcessarNoCiclo(nomeEsperadoJaProcessadoNoCiclo: true)
            .Should().BeFalse();
    }
}
