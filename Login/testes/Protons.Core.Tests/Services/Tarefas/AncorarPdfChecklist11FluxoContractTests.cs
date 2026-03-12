using FluentAssertions;
using Protons.Core.Tarefas.Models;
using Xunit;

namespace Protons.Core.Tests.Services.Tarefas;

/// <summary>
/// C11 — Gate G1 (blocking): Contratos de domínio para o fluxo wizard AncorarPdf.
/// Zero dependências externas — apenas modelos e enums do Core.
/// Anti-duplicidade: C1–C10 não cobrem AncorarPdfEtapaFluxo nem AncorarPdfFluxoEstadoAncoras.
/// </summary>
[Trait("Checklist", "C11")]
[Trait("Category", "C11_G1_FluxoContratos")]
public sealed class AncorarPdfChecklist11FluxoContractTests
{
    // --- AncorarPdfEtapaFluxo ---

    [Fact]
    public void T01_AncorarPdfEtapaFluxo_tem_3_valores()
    {
        Enum.GetValues<AncorarPdfEtapaFluxo>().Should().HaveCount(3,
            "wizard tem exatamente 3 passos: Basico, AncorasTelaCheia, Confirmacao");
    }

    [Fact]
    public void T02_EtapaFluxo_Basico_valor_0()
    {
        ((int)AncorarPdfEtapaFluxo.Basico).Should().Be(0,
            "Basico=0 é o passo inicial — valor numérico deve ser estável para serialização");
    }

    [Fact]
    public void T03_EtapaFluxo_AncorasTelaCheia_valor_1()
    {
        ((int)AncorarPdfEtapaFluxo.AncorasTelaCheia).Should().Be(1,
            "AncorasTelaCheia=1 deve ser o segundo passo do wizard");
    }

    [Fact]
    public void T04_EtapaFluxo_Confirmacao_valor_2()
    {
        ((int)AncorarPdfEtapaFluxo.Confirmacao).Should().Be(2,
            "Confirmacao=2 deve ser o terceiro e último passo do wizard");
    }

    // --- AncorarPdfFluxoEstadoAncoras ---

    [Fact]
    public void T05_FluxoEstadoAncoras_campos_obrigatorios_nao_nulos()
    {
        var ancoras = new List<AncorarPdfTemplateAncora>
        {
            new() { CorHex = "#4A90D9", Pagina = 1, Metadado = new() { ChaveTecnica = "v1", NomeExibido = "Variavel 1", TipoEsperado = "texto" } }
        };
        var estado = new AncorarPdfFluxoEstadoAncoras(ancoras, "/tmp/modelo.pdf");

        estado.Ancoras.Should().NotBeNull().And.HaveCount(1,
            "snapshot deve preservar a lista de âncoras");
        estado.PdfModeloPath.Should().Be("/tmp/modelo.pdf",
            "snapshot deve preservar o caminho do PDF modelo");
    }

    [Fact]
    public void T06_FluxoEstadoAncoras_lista_vazia_e_valida()
    {
        var estado = new AncorarPdfFluxoEstadoAncoras([], "/tmp/teste.pdf");

        estado.Ancoras.Should().BeEmpty("lista vazia é estruturalmente válida — regra de negócio ≥1 é do wizard VM");
        estado.PdfModeloPath.Should().NotBeNull();
    }

    [Fact]
    public void T07_FluxoEstadoAncoras_namespace_pertence_ao_core()
    {
        typeof(AncorarPdfFluxoEstadoAncoras).Namespace.Should()
            .StartWith("Protons.Core", "modelos de fluxo não podem ter dependência de infraestrutura");
    }

    [Fact]
    public void T08_AncorarPdfEtapaFluxo_namespace_pertence_ao_core()
    {
        typeof(AncorarPdfEtapaFluxo).Namespace.Should()
            .StartWith("Protons.Core", "enums de fluxo não podem ter dependência de infraestrutura");
    }
}
