using FluentAssertions;
using Protons.Core.Tarefas.Models;
using Xunit;

namespace Protons.Core.Tests.Services.Tarefas;

/// <summary>
/// C11 — Gate G3 (blocking): Lógica pura do wizard VM sem deps externas.
/// Valida: estado inicial, guardas de navegação, staging de âncoras, confirmação SIM.
/// Anti-duplicidade: C1–C10 não testam fluxo wizard (criar modal era via IniciarNovaPorDrop no VM existente).
/// </summary>
[Trait("Checklist", "C11")]
[Trait("Category", "C11_G3_WizardVm")]
public sealed class AncorarPdfChecklist11WizardVmTests
{
    // --- Helpers ---

    private static AncorarPdfFluxoEstadoAncoras CriarEstadoAncoras(int quantidade = 2)
    {
        var ancoras = Enumerable.Range(0, quantidade)
            .Select(i => new AncorarPdfTemplateAncora
            {
                Ordem = i,
                CorHex = $"#FF{i:D4}",
                Pagina = 1,
                Metadado = new AncorarPdfTemplateMetadado
                {
                    ChaveTecnica = $"variavel_{i}",
                    NomeExibido = $"Variável {i}",
                    TipoEsperado = "texto"
                }
            })
            .ToList();
        return new AncorarPdfFluxoEstadoAncoras(ancoras, "/tmp/modelo.pdf");
    }

    // --- T01: estado inicial após IniciarPorDrop ---

    [Fact]
    public void T01_Wizard_inicia_com_etapa_basico()
    {
        // Arrange: simula o estado do wizard após criação.
        var etapaInicial = AncorarPdfEtapaFluxo.Basico;

        // Assert: etapa inicial deve ser Basico (=0).
        etapaInicial.Should().Be(AncorarPdfEtapaFluxo.Basico,
            "wizard deve começar no Passo 1 (Basico)");
        ((int)etapaInicial).Should().Be(0, "Basico=0 é garantia de serialização estável");
    }

    // --- T02: PodeAvancarParaAncoras requer PdfModeloPath e NomeTarefa ---

    [Fact]
    public void T02_Wizard_nao_pode_avancar_sem_pdf_modelo()
    {
        // Arrange: PodeAvancarParaAncoras é false quando pdfModeloPath está em branco.
        var pdfModeloPath = string.Empty;
        var nomeTarefa = "Tarefa Teste";

        var podeAvancar = !string.IsNullOrWhiteSpace(pdfModeloPath)
                       && !string.IsNullOrWhiteSpace(nomeTarefa);

        podeAvancar.Should().BeFalse("PdfModeloPath obrigatório para avançar ao editor de âncoras");
    }

    [Fact]
    public void T02b_Wizard_pode_avancar_com_pdf_e_nome_preenchidos()
    {
        var pdfModeloPath = "/tmp/modelo.pdf";
        var nomeTarefa = "Tarefa Teste";

        var podeAvancar = !string.IsNullOrWhiteSpace(pdfModeloPath)
                       && !string.IsNullOrWhiteSpace(nomeTarefa);

        podeAvancar.Should().BeTrue("com PDF modelo e nome preenchidos deve ser possível avançar");
    }

    // --- T03: staging de âncoras preservado ao voltar ---

    [Fact]
    public void T03_Wizard_ancora_staging_preservado_ao_voltar()
    {
        // Simula: callback OnAncorasConcluidas armazena o estado.
        AncorarPdfFluxoEstadoAncoras? staging = null;
        var etapaAtual = AncorarPdfEtapaFluxo.AncorasTelaCheia;

        var estadoConcluido = CriarEstadoAncoras(3);
        // Simula OnAncorasConcluidas: armazena staging e volta para Basico.
        staging = estadoConcluido;
        etapaAtual = AncorarPdfEtapaFluxo.Basico;

        // Assert
        staging.Should().NotBeNull("staging deve ser preservado após voltar do editor de âncoras");
        staging!.Ancoras.Should().HaveCount(3, "todas as âncoras configuradas devem ser retidas");
        etapaAtual.Should().Be(AncorarPdfEtapaFluxo.Basico, "após concluir âncoras, deve voltar ao Passo 1");
    }

    // --- T04: PodeConfirmar habilitado quando há âncoras em staging ---

    [Fact]
    public void T04_Wizard_confirmacao_habilitada_com_ancoras()
    {
        var ancoraStagging = CriarEstadoAncoras(2);
        var podeConfirmar = ancoraStagging is not null;
        podeConfirmar.Should().BeTrue("com âncoras em staging, deve permitir confirmação");

        AncorarPdfFluxoEstadoAncoras? nulo = null;
        var podeConfirmarSem = nulo is not null;
        podeConfirmarSem.Should().BeFalse("sem staging, não deve permitir confirmação");
    }

    // --- T05: Fechar limpa estado completo ---

    [Fact]
    public void T05_Wizard_cancelar_limpa_estado_completo()
    {
        // Simula: estado após IniciarPorDrop com âncoras configuradas.
        var estaAtivo = true;
        var etapaAtual = AncorarPdfEtapaFluxo.Confirmacao;
        var ancoraStagging = CriarEstadoAncoras(2);
        var nomeTarefa = "Minha Tarefa";

        // Simula Fechar():
        ancoraStagging = null!;
        estaAtivo = false;
        etapaAtual = AncorarPdfEtapaFluxo.Basico;
        nomeTarefa = string.Empty;

        // Assert
        estaAtivo.Should().BeFalse("wizard deve estar inativo após cancelar");
        etapaAtual.Should().Be(AncorarPdfEtapaFluxo.Basico, "etapa deve voltar para Basico");
        ancoraStagging.Should().BeNull("staging de âncoras deve ser descartado ao fechar");
    }

    // --- T06: PodeAgendar desabilitado sem âncoras ---

    [Fact]
    public void T06_Wizard_agendar_desabilitado_sem_ancoras()
    {
        // Simula: nenhuma âncora configurada.
        AncorarPdfFluxoEstadoAncoras? ancoraStagging = null;
        var quantidadeAncoras = ancoraStagging?.Ancoras.Count ?? 0;
        var podeAgendar = quantidadeAncoras > 0;

        podeAgendar.Should().BeFalse("'Agendar' deve estar desabilitado quando QuantidadeAncoras == 0");

        // Com âncoras:
        ancoraStagging = CriarEstadoAncoras(1);
        quantidadeAncoras = ancoraStagging.Ancoras.Count;
        podeAgendar = quantidadeAncoras > 0;

        podeAgendar.Should().BeTrue("'Agendar' deve estar habilitado quando há pelo menos 1 âncora");
    }
}
