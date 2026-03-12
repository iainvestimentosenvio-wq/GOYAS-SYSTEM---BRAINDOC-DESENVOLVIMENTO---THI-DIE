using FluentAssertions;
using Protons.Core.Tarefas.Models;
using Protons.Infrastructure.Tarefas.Services;
using Xunit;

namespace Protons.Infrastructure.Tests.Integration;

/// <summary>
/// Testes unitários para AncorarPdfBuscaTextoAncora.
/// </summary>
[Trait("Checklist", "C6")]
[Trait("Category", "C6_BuscaTextoAncora")]
public sealed class AncorarPdfBuscaTextoAncoraTests
{
    private readonly AncorarPdfBuscaTextoAncora _busca = new();

    [Fact]
    public void Buscar_Valor_retorna_bbox()
    {
        var palavra = new PdfPalavra("Valor:", new BboxRelativo(0.1, 0.2, 0.08, 0.03));
        var pagina = new PdfPaginaTexto(1, 595, 842, [palavra]);

        var bbox = _busca.Buscar(pagina, "Valor:");

        bbox.Should().NotBeNull();
        bbox!.X.Should().BeApproximately(0.1, 1e-9);
        bbox.Y.Should().BeApproximately(0.2, 1e-9);
    }

    [Fact]
    public void Buscar_texto_nao_encontrado_retorna_null()
    {
        var palavra = new PdfPalavra("Outro", new BboxRelativo(0.1, 0.1, 0.1, 0.05));
        var pagina = new PdfPaginaTexto(1, 595, 842, [palavra]);

        var bbox = _busca.Buscar(pagina, "Xyz inexistente");

        bbox.Should().BeNull();
    }

    [Fact]
    public void Buscar_multiplas_palavras_junta_e_encontra()
    {
        var p1 = new PdfPalavra("CPF", new BboxRelativo(0.0, 0.1, 0.05, 0.02));
        var p2 = new PdfPalavra("do", new BboxRelativo(0.06, 0.1, 0.03, 0.02));
        var p3 = new PdfPalavra("cliente:", new BboxRelativo(0.10, 0.1, 0.08, 0.02));
        var pagina = new PdfPaginaTexto(1, 595, 842, [p1, p2, p3]);

        var bbox = _busca.Buscar(pagina, "CPF do cliente:");

        bbox.Should().NotBeNull();
        bbox!.X.Should().BeApproximately(0.0, 1e-9);
    }

    [Fact]
    public void Buscar_case_insensitive_encontra()
    {
        var palavra = new PdfPalavra("VALOR:", new BboxRelativo(0.1, 0.2, 0.08, 0.03));
        var pagina = new PdfPaginaTexto(1, 595, 842, [palavra]);

        var bbox = _busca.Buscar(pagina, "valor:");

        bbox.Should().NotBeNull();
    }

    [Fact]
    public void Buscar_pagina_vazia_retorna_null()
    {
        var pagina = new PdfPaginaTexto(1, 595, 842, []);

        var bbox = _busca.Buscar(pagina, "Valor:");

        bbox.Should().BeNull();
    }

    [Fact]
    public void Buscar_texto_ancora_vazio_retorna_null()
    {
        var palavra = new PdfPalavra("Valor:", new BboxRelativo(0.1, 0.2, 0.08, 0.03));
        var pagina = new PdfPaginaTexto(1, 595, 842, [palavra]);

        var bbox = _busca.Buscar(pagina, "");

        bbox.Should().BeNull();
    }

    [Fact]
    public void Buscar_palavras_em_linhas_diferentes_nao_cola()
    {
        // "Valor" na linha Y=0.1, "100" na linha Y=0.2 (delta 0.005 separa as linhas).
        var p1 = new PdfPalavra("Valor", new BboxRelativo(0.0, 0.10, 0.08, 0.02));
        var p2 = new PdfPalavra("100", new BboxRelativo(0.0, 0.20, 0.05, 0.02));
        var pagina = new PdfPaginaTexto(1, 595, 842, [p1, p2]);

        var bbox = _busca.Buscar(pagina, "Valor 100");

        bbox.Should().BeNull("palavras em linhas distintas não devem ser coladas");
    }
}
