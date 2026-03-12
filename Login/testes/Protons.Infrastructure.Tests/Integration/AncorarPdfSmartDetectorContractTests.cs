using FluentAssertions;
using Protons.Core.Tarefas.Models;
using Protons.Infrastructure.Tarefas.Services;
using Xunit;

namespace Protons.Infrastructure.Tests.Integration;

/// <summary>
/// C12 — Gate G2 (blocking): Contratos do SmartDetector sem I/O de PDF.
/// Usa PdfPalavra sintéticas para testar hit-test, agrupamento, classificação e label.
/// Todos os testes são determinísticos e independentes de arquivo externo.
/// </summary>
[Trait("Checklist", "C12")]
[Trait("Category", "C12_G2_Contratos")]
public sealed class AncorarPdfSmartDetectorContractTests
{
    private readonly AncorarPdfSmartDetector _detector = new();

    // Cria uma página com palavras posicionadas pelos parâmetros fornecidos.
    private static IReadOnlyList<PdfPaginaTexto> CriarPagina(
        params (string Texto, double X, double Y, double W, double H)[] palavras)
    {
        var lista = palavras
            .Select(p => new PdfPalavra(p.Texto, new BboxRelativo(p.X, p.Y, p.W, p.H)))
            .ToList();
        return [new PdfPaginaTexto(1, 595, 842, lista)];
    }

    // Cria página com uma única palavra no local padrão.
    private static IReadOnlyList<PdfPaginaTexto> CriarPaginaComPalavra(
        string texto, double x = 0.1, double y = 0.1, double w = 0.15, double h = 0.02)
        => CriarPagina((texto, x, y, w, h));

    // Ponto central de uma bbox.
    private static (double X, double Y) CentroOf(double x, double y, double w, double h)
        => (x + w / 2, y + h / 2);

    // ── T01: Páginas vazias ───────────────────────────────────────────────────

    [Fact]
    public void T01_Paginas_vazias_retorna_nao_encontrado()
    {
        var resultado = _detector.Detectar(
            Array.Empty<PdfPaginaTexto>(),
            new SmartDeteccaoEntrada(0.5, 0.5));

        resultado.Encontrado.Should().BeFalse();
        resultado.MotivoFalha.Should().Be("sem_paginas");
    }

    // ── T02: Clique em área sem texto ────────────────────────────────────────

    [Fact]
    public void T02_Click_em_area_sem_texto_retorna_nao_encontrado()
    {
        var paginas = CriarPaginaComPalavra("texto", 0.1, 0.1, 0.1, 0.02);
        // Clicar bem longe de onde está a palavra.
        var resultado = _detector.Detectar(paginas, new SmartDeteccaoEntrada(0.9, 0.9));

        resultado.Encontrado.Should().BeFalse();
        resultado.MotivoFalha.Should().NotBeNullOrEmpty();
    }

    // ── T03: CNPJ detectado ───────────────────────────────────────────────────

    [Fact]
    public void T03_Click_em_cnpj_detecta_tipo_cnpj_com_confianca_1()
    {
        const string cnpj = "19.864.554/0001-65";
        var paginas = CriarPaginaComPalavra(cnpj, 0.1, 0.1, 0.20, 0.02);
        var (cx, cy) = CentroOf(0.1, 0.1, 0.20, 0.02);

        var resultado = _detector.Detectar(paginas, new SmartDeteccaoEntrada(cx, cy));

        resultado.Encontrado.Should().BeTrue();
        resultado.Tipo.Should().Be(SmartTipoVariavel.Cnpj);
        resultado.Confianca.Should().Be(1.0);
        resultado.RegraNormalizacao.Should().Be("cnpj");
        resultado.TextoNormalizado.Should().Be("19864554000165");
    }

    // ── T04: CPF detectado e normalizado ────────────────────────────────────

    [Fact]
    public void T04_Click_em_cpf_detecta_tipo_cpf_com_normalizacao()
    {
        const string cpf = "382.316.621-20";
        var paginas = CriarPaginaComPalavra(cpf, 0.1, 0.1, 0.15, 0.02);
        var (cx, cy) = CentroOf(0.1, 0.1, 0.15, 0.02);

        var resultado = _detector.Detectar(paginas, new SmartDeteccaoEntrada(cx, cy));

        resultado.Encontrado.Should().BeTrue();
        resultado.Tipo.Should().Be(SmartTipoVariavel.Cpf);
        resultado.TextoNormalizado.Should().Be("38231662120");
    }

    // ── T05: E-mail detectado ────────────────────────────────────────────────

    [Fact]
    public void T05_Click_em_email_detecta_tipo_email()
    {
        var paginas = CriarPaginaComPalavra("a@b.com", 0.1, 0.1, 0.10, 0.02);
        var (cx, cy) = CentroOf(0.1, 0.1, 0.10, 0.02);

        var resultado = _detector.Detectar(paginas, new SmartDeteccaoEntrada(cx, cy));

        resultado.Encontrado.Should().BeTrue();
        resultado.Tipo.Should().Be(SmartTipoVariavel.Email);
        resultado.RegraNormalizacao.Should().BeNull("email não tem regra de normalização");
    }

    // ── T06: CEP com hífen detectado ────────────────────────────────────────

    [Fact]
    public void T06_Click_em_cep_com_hifen_detecta_tipo_cep_com_confianca_1()
    {
        var paginas = CriarPaginaComPalavra("76250-000", 0.1, 0.1, 0.10, 0.02);
        var (cx, cy) = CentroOf(0.1, 0.1, 0.10, 0.02);

        var resultado = _detector.Detectar(paginas, new SmartDeteccaoEntrada(cx, cy));

        resultado.Encontrado.Should().BeTrue();
        resultado.Tipo.Should().Be(SmartTipoVariavel.Cep);
        resultado.Confianca.Should().Be(1.0);
    }

    // ── T07: Telefone detectado ──────────────────────────────────────────────

    [Fact]
    public void T07_Click_em_telefone_detecta_tipo_telefone()
    {
        var paginas = CriarPaginaComPalavra("(62)99940-1952", 0.1, 0.1, 0.12, 0.02);
        var (cx, cy) = CentroOf(0.1, 0.1, 0.12, 0.02);

        var resultado = _detector.Detectar(paginas, new SmartDeteccaoEntrada(cx, cy));

        resultado.Encontrado.Should().BeTrue();
        resultado.Tipo.Should().Be(SmartTipoVariavel.Telefone);
    }

    // ── T08: Data brasileira detectada e normalizada ─────────────────────────

    [Fact]
    public void T08_Click_em_data_br_detecta_e_normaliza_para_iso()
    {
        var paginas = CriarPaginaComPalavra("22/04/2024", 0.1, 0.1, 0.10, 0.02);
        var (cx, cy) = CentroOf(0.1, 0.1, 0.10, 0.02);

        var resultado = _detector.Detectar(paginas, new SmartDeteccaoEntrada(cx, cy));

        resultado.Encontrado.Should().BeTrue();
        resultado.Tipo.Should().Be(SmartTipoVariavel.DataBr);
        resultado.TextoNormalizado.Should().Be("2024-04-22");
    }

    // ── T09: Inteiro detectado ───────────────────────────────────────────────

    [Fact]
    public void T09_Click_em_inteiro_detecta_tipo_inteiro()
    {
        var paginas = CriarPaginaComPalavra("17", 0.1, 0.1, 0.04, 0.02);
        var (cx, cy) = CentroOf(0.1, 0.1, 0.04, 0.02);

        var resultado = _detector.Detectar(paginas, new SmartDeteccaoEntrada(cx, cy));

        resultado.Encontrado.Should().BeTrue();
        resultado.Tipo.Should().Be(SmartTipoVariavel.Inteiro);
    }

    // ── T10: UF (estado) detectado ───────────────────────────────────────────

    [Fact]
    public void T10_Click_em_uf_go_detecta_tipo_uf()
    {
        var paginas = CriarPaginaComPalavra("GO", 0.1, 0.1, 0.03, 0.02);
        var (cx, cy) = CentroOf(0.1, 0.1, 0.03, 0.02);

        var resultado = _detector.Detectar(paginas, new SmartDeteccaoEntrada(cx, cy));

        resultado.Encontrado.Should().BeTrue();
        resultado.Tipo.Should().Be(SmartTipoVariavel.Uf);
    }

    // ── T11: Moeda BRL detectada e normalizada ───────────────────────────────

    [Fact]
    public void T11_Click_em_moeda_brl_detecta_e_normaliza()
    {
        // "R$" e "1.234,56" são duas palavras adjacentes na mesma linha.
        var paginas = CriarPagina(
            ("R$", 0.1, 0.1, 0.03, 0.02),
            ("1.234,56", 0.14, 0.1, 0.07, 0.02));
        // Clicar no valor numérico.
        var (cx, cy) = CentroOf(0.14, 0.1, 0.07, 0.02);

        var resultado = _detector.Detectar(paginas, new SmartDeteccaoEntrada(cx, cy));

        resultado.Encontrado.Should().BeTrue();
        resultado.Tipo.Should().Be(SmartTipoVariavel.MoedaBrl);
        resultado.TextoNormalizado.Should().Be("1234.56");
    }

    // ── T12: Texto genérico (fallback) ───────────────────────────────────────

    [Fact]
    public void T12_Click_em_texto_generico_detecta_tipo_texto_com_confianca_0_5()
    {
        // Usar palavras que não correspondem a nenhum padrão de dado estruturado.
        var paginas = CriarPagina(
            ("CAIAPONIA", 0.1, 0.1, 0.08, 0.02),
            ("1", 0.19, 0.1, 0.02, 0.02));  // "1" sozinho poderia ser inteiro...
        // Para garantir "Texto", usar texto completamente alfanumérico sem padrão.
        var paginas2 = CriarPaginaComPalavra("MUNICIPIO", 0.1, 0.1, 0.10, 0.02);
        var (cx, cy) = CentroOf(0.1, 0.1, 0.10, 0.02);

        var resultado = _detector.Detectar(paginas2, new SmartDeteccaoEntrada(cx, cy));

        resultado.Encontrado.Should().BeTrue();
        resultado.Tipo.Should().Be(SmartTipoVariavel.Texto);
        resultado.Confianca.Should().Be(0.5);
    }

    // ── T13: Label acima do valor é inferido ─────────────────────────────────

    [Fact]
    public void T13_Label_acima_do_valor_e_inferido_como_label()
    {
        // "CNPJ" aparece na linha acima (Y menor = mais para cima na coordenada top-left).
        var paginas = CriarPagina(
            ("CNPJ", 0.10, 0.07, 0.04, 0.015),     // label acima
            ("19.864.554/0001-65", 0.10, 0.10, 0.20, 0.02));  // valor

        // Clicar no centro do valor CNPJ.
        var (cx, cy) = CentroOf(0.10, 0.10, 0.20, 0.02);

        var resultado = _detector.Detectar(paginas, new SmartDeteccaoEntrada(cx, cy));

        resultado.Encontrado.Should().BeTrue();
        resultado.LabelInferido.Should().Be("CNPJ");
    }

    // ── T14: ChaveTecnica em snake_case sem acento ───────────────────────────

    [Fact]
    public void T14_ChaveTecnica_gerada_em_snake_case_sem_acento()
    {
        // Testa ToSnakeCase e RemoverAcentos diretamente via helper interno.
        var nome = "CPF do Responsável Técnico";
        var semAcento = AncorarPdfSmartDetector.RemoverAcentos(nome);
        var chave = AncorarPdfSmartDetector.ToSnakeCase(semAcento);

        chave.Should().Be("cpf_do_responsavel_tecnico");
    }

    // ── T15: CNPJ tem prioridade sobre CPF (14 vs 11 dígitos) ────────────────

    [Fact]
    public void T15_Cnpj_tem_prioridade_sobre_cpf_para_14_digitos()
    {
        // "19.864.554/0001-65" tem 14 dígitos → CNPJ, não CPF.
        var paginas = CriarPaginaComPalavra("19.864.554/0001-65", 0.1, 0.1, 0.20, 0.02);
        var (cx, cy) = CentroOf(0.1, 0.1, 0.20, 0.02);

        var resultado = _detector.Detectar(paginas, new SmartDeteccaoEntrada(cx, cy));

        resultado.Tipo.Should().Be(SmartTipoVariavel.Cnpj,
            "CNPJ é verificado antes de CPF e tem 14 dígitos");
    }

    // ── T16: Palavra fragmentada pelo PdfPig é reagrupada ────────────────────

    [Fact]
    public void T16_Palavra_fragmentada_e_reagrupada_em_cnpj()
    {
        // Simula fragmentação: PdfPig extraiu o CNPJ em 2 tokens adjacentes.
        // Gap horizontal: 0.195 - (0.1 + 0.09) = 0.005 < MaxGap(0.025) → agrupados.
        var paginas = CriarPagina(
            ("19.864.554", 0.10, 0.10, 0.09, 0.02),
            ("/0001-65", 0.195, 0.10, 0.06, 0.02));

        // Clicar no centro do primeiro fragmento.
        var (cx, cy) = CentroOf(0.10, 0.10, 0.09, 0.02);

        var resultado = _detector.Detectar(paginas, new SmartDeteccaoEntrada(cx, cy));

        resultado.Encontrado.Should().BeTrue();
        resultado.Tipo.Should().Be(SmartTipoVariavel.Cnpj,
            "os dois fragmentos adjacentes devem ser reagrupados → CNPJ detectado");
        resultado.TextoBruto.Should().Contain("19.864.554");
    }
}
