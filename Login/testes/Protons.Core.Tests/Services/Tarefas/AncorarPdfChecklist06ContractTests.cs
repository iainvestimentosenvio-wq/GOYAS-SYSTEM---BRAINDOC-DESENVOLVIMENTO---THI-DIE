using FluentAssertions;
using Protons.Core.Tarefas.Models;
using Xunit;

namespace Protons.Core.Tests.Services.Tarefas;

/// <summary>
/// C6 — Gate G1 (blocking): Contratos dos modelos do motor de ancoragem PDF.
/// Verifica invariantes estáticos sem banco de dados ou I/O.
/// </summary>
[Trait("Checklist", "C6")]
[Trait("Category", "C6_G1_Contratos")]
public sealed class AncorarPdfChecklist06ContractTests
{
    [Fact]
    public void T01_PdfPalavra_inicializa_corretamente()
    {
        var bbox = new BboxRelativo(0.1, 0.2, 0.3, 0.05);
        var palavra = new PdfPalavra("João", bbox);

        palavra.Texto.Should().Be("João");
        palavra.Bbox.X.Should().BeApproximately(0.1, 1e-9);
        palavra.Bbox.Y.Should().BeApproximately(0.2, 1e-9);
        palavra.Bbox.Largura.Should().BeApproximately(0.3, 1e-9);
        palavra.Bbox.Altura.Should().BeApproximately(0.05, 1e-9);
    }

    [Fact]
    public void T02_PdfPaginaTexto_sem_palavras_eh_valida()
    {
        var pagina = new PdfPaginaTexto(1, 595.0, 842.0, []);

        pagina.Numero.Should().Be(1);
        pagina.LarguraPt.Should().BeApproximately(595.0, 1e-9);
        pagina.AlturaPt.Should().BeApproximately(842.0, 1e-9);
        pagina.Palavras.Should().BeEmpty();
    }

    [Fact]
    public void T03_SelecaoArquivoResultado_campos_obrigatorios()
    {
        var sel = new SelecaoArquivoResultado(
            "/tmp/doc.pdf",
            "relatorio_2026",
            "abc123",
            102400L,
            new DateTime(2026, 2, 26, 10, 0, 0, DateTimeKind.Utc));

        sel.ArquivoPath.Should().NotBeNullOrEmpty();
        sel.NomeEsperadoLogico.Should().NotBeNullOrEmpty();
        sel.ArquivoHash.Should().NotBeNullOrEmpty();
        sel.TamanhoBytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public void T04_ValidacaoClienteResultado_invalido_tem_motivo()
    {
        var invalido = new ValidacaoClienteResultado(false, "cpf_cnpj_nao_encontrado");

        invalido.Valido.Should().BeFalse();
        invalido.Motivo.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void T05_CandidatoArquivoTexto_similaridade_range_0_1()
    {
        var candidato = new CandidatoArquivoTexto(
            "/tmp/relatorio.pdf",
            "relatorio",
            0.85,
            new DateTime(2026, 2, 26, 8, 0, 0, DateTimeKind.Utc),
            204800L,
            string.Empty);

        candidato.Similaridade.Should().BeInRange(0.0, 1.0);
        candidato.TamanhoBytes.Should().BeGreaterThanOrEqualTo(0);
        candidato.ArquivoPath.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void T06_MotorModels_namespace_core()
    {
        // Garante que os tipos do motor estão em Protons.Core — sem deps de infra.
        typeof(PdfPalavra).Assembly.GetName().Name.Should().Be("Protons.Core");
        typeof(PdfPaginaTexto).Assembly.GetName().Name.Should().Be("Protons.Core");
        typeof(SelecaoArquivoResultado).Assembly.GetName().Name.Should().Be("Protons.Core");
        typeof(ValidacaoClienteResultado).Assembly.GetName().Name.Should().Be("Protons.Core");
        typeof(CandidatoArquivoTexto).Assembly.GetName().Name.Should().Be("Protons.Core");
    }
}
