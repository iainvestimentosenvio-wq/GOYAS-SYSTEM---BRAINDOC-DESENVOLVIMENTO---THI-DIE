using FluentAssertions;
using Protons.Core.Clientes.Models;
using Protons.Core.Clientes.Repositories;
using Protons.Core.Tarefas.Models;
using Protons.Infrastructure.Tarefas.Services;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Writer;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using Xunit;

namespace Protons.Infrastructure.Tests.Integration;

/// <summary>
/// C9 — Gate G3 (blocking): Testes adversariais de pipeline, estágio a estágio.
/// Cobre lacunas reais identificadas na matriz anti-duplicidade de C9:
/// validação CNPJ (C6 só testou CPF), normalizações ausentes (CNPJ, data_br, inteiro, null),
/// seletor com 2 candidatos, âncoras edge cases e validação de cliente por match exato de documento.
/// </summary>
[Trait("Checklist", "C9")]
[Trait("Category", "C9_G3_Pipeline")]
public sealed class AncorarPdfChecklist09PipelineAdversarialTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AncorarPdfExtratorTextoPdfPig _extrator = new();
    private readonly AncorarPdfSeletorArquivoPasta _seletor = new();
    private readonly AncorarPdfValidadorClienteRegex _validador;
    private readonly AncorarPdfAncoradorEspacialBbox _anchorador = new();

    public AncorarPdfChecklist09PipelineAdversarialTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"c9_pipe_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _validador = new AncorarPdfValidadorClienteRegex(
            new InMemoryClienteRepository(new Dictionary<int, Cliente>
            {
                [4252011] = new()
                {
                    Id = 4252011,
                    CodigoCliente = "CLI-C9-CNPJ",
                    Nome = "Cliente CNPJ C9",
                    TipoDocumento = TipoDocumentoCliente.CNPJ,
                    Documento = "04252011000110",
                    Ativo = true
                },
                [9999999] = new()
                {
                    Id = 9999999,
                    CodigoCliente = "CLI-C9-CPF",
                    Nome = "Cliente CPF C9",
                    TipoDocumento = TipoDocumentoCliente.CPF,
                    Documento = "12345678909",
                    Ativo = true
                }
            }));
    }

    // --- Validador CNPJ (lacuna real: C6 só testou CPF T07-T09) ---

    [Fact]
    public void T01_Validador_cnpj_presente_no_texto_valida_cliente()
    {
        // Documento oficial do cliente 4252011 = CNPJ 04.252.011/0001-10.
        var paginas = CriarPaginasComTexto("CNPJ 04.252.011/0001-10 contrato empresa");
        var resultado = _validador.Validar(paginas, 4252011);

        resultado.Valido.Should().BeTrue("CNPJ encontrado deve bater exatamente com o documento oficial do cliente");
        resultado.Motivo.Should().BeNull();
    }

    [Fact]
    public void T02_Validador_cnpj_ausente_retorna_invalido_cpf_cnpj_nao_encontrado()
    {
        // Texto sem CPF nem CNPJ — nenhum padrão encontrado.
        var paginas = CriarPaginasComTexto("Documento administrativo sem identificacao fiscal");
        var resultado = _validador.Validar(paginas, 4252011);

        resultado.Valido.Should().BeFalse();
        resultado.Motivo.Should().Be("cpf_cnpj_nao_encontrado");
    }

    [Fact]
    public void T03_Validador_cnpj_errado_retorna_invalido_cliente_nao_validado()
    {
        // CNPJ presente no PDF não corresponde ao documento oficial do cliente 9999999.
        var paginas = CriarPaginasComTexto("CNPJ 04.252.011/0001-10 referencia contrato");
        var resultado = _validador.Validar(paginas, 9999999);

        resultado.Valido.Should().BeFalse();
        resultado.Motivo.Should().Be("cliente_nao_validado");
    }

    // --- Normalizações ausentes (C6 cobriu CPF e moeda_brl; C9 cobre o restante) ---

    [Fact]
    public void T04_Normalizador_cnpj_via_anchorador_remove_pontuacao()
    {
        // "04.252.011/0001-10" com RegraNormalizacao="cnpj" → "04252011000110"
        var palavra = new PdfPalavra("04.252.011/0001-10", new BboxRelativo(0.0, 0.0, 1.0, 1.0));
        var pagina = new PdfPaginaTexto(1, 595, 842, [palavra]);
        var ancora = CriarAncora("cnpj_empresa", "cnpj");

        var resultado = _anchorador.Ancorar([pagina], [ancora]);

        resultado.Should().HaveCount(1);
        resultado[0].ValorBruto.Should().Be("04.252.011/0001-10");
        resultado[0].ValorNormalizado.Should().Be("04252011000110",
            "CNPJ normalizado deve remover pontuação (., /, -)");
    }

    [Fact]
    public void T05_Normalizador_data_br_via_anchorador_converte_para_iso8601()
    {
        // "28/02/2026" com RegraNormalizacao="data_br" → "2026-02-28"
        var palavra = new PdfPalavra("28/02/2026", new BboxRelativo(0.0, 0.0, 1.0, 1.0));
        var pagina = new PdfPaginaTexto(1, 595, 842, [palavra]);
        var ancora = CriarAncora("data_vencimento", "data_br");

        var resultado = _anchorador.Ancorar([pagina], [ancora]);

        resultado.Should().HaveCount(1);
        resultado[0].ValorBruto.Should().Be("28/02/2026");
        resultado[0].ValorNormalizado.Should().Be("2026-02-28",
            "data BR dd/MM/yyyy deve ser convertida para ISO 8601 yyyy-MM-dd");
    }

    [Fact]
    public void T06_Normalizador_inteiro_via_anchorador_remove_nao_digitos()
    {
        // "1.234" com RegraNormalizacao="inteiro" → "1234" (remove ponto)
        var palavra = new PdfPalavra("1.234", new BboxRelativo(0.0, 0.0, 0.5, 0.1));
        var palavraUnid = new PdfPalavra("unidades", new BboxRelativo(0.5, 0.0, 0.5, 0.1));
        var pagina = new PdfPaginaTexto(1, 595, 842, [palavra, palavraUnid]);
        var ancora = CriarAncora("quantidade", "inteiro");

        var resultado = _anchorador.Ancorar([pagina], [ancora]);

        resultado.Should().HaveCount(1);
        resultado[0].ValorBruto.Should().Be("1.234 unidades");
        resultado[0].ValorNormalizado.Should().Be("1234",
            "normalização inteiro deve reter somente dígitos");
    }

    [Fact]
    public void T07_Normalizador_regra_null_retorna_texto_bruto_sem_transformacao()
    {
        // Sem regra de normalização: ValorNormalizado == ValorBruto (trim apenas).
        var p1 = new PdfPalavra("Contrato", new BboxRelativo(0.0, 0.0, 0.3, 0.1));
        var p2 = new PdfPalavra("Social", new BboxRelativo(0.3, 0.0, 0.3, 0.1));
        var pagina = new PdfPaginaTexto(1, 595, 842, [p1, p2]);
        var ancora = new AncorarPdfTemplateAncora
        {
            Ordem = 1, CorHex = "#4A90D9", Pagina = 1,
            XRel = 0.0, YRel = 0.0, LarguraRel = 1.0, AlturaRel = 1.0,
            Metadado = new AncorarPdfTemplateMetadado
            {
                ChaveTecnica = "descricao", TipoEsperado = "texto",
                RegraNormalizacao = null  // sem regra → passthrough
            }
        };

        var resultado = _anchorador.Ancorar([pagina], [ancora]);

        resultado.Should().HaveCount(1);
        resultado[0].ValorNormalizado.Should().Be(resultado[0].ValorBruto.Trim(),
            "sem regra de normalização deve retornar texto bruto com trim");
    }

    [Fact]
    public void T08_Normalizador_moeda_brl_valor_minimo_centavos()
    {
        // "R$ 0,01" → "0.01" — valor mínimo de centavo deve ser formatado corretamente.
        var p1 = new PdfPalavra("R$", new BboxRelativo(0.0, 0.0, 0.1, 0.1));
        var p2 = new PdfPalavra("0,01", new BboxRelativo(0.1, 0.0, 0.2, 0.1));
        var pagina = new PdfPaginaTexto(1, 595, 842, [p1, p2]);
        var ancora = CriarAncora("valor_minimo", "moeda_brl");

        var resultado = _anchorador.Ancorar([pagina], [ancora]);

        resultado.Should().HaveCount(1);
        resultado[0].ValorNormalizado.Should().Be("0.01",
            "R$ 0,01 deve normalizar para '0.01' (invariant decimal)");
    }

    // --- Seletor com múltiplos candidatos ---

    [Fact]
    public async Task T09_Seletor_dois_pdfs_seleciona_o_mais_similar()
    {
        var pasta = Path.Combine(_tempDir, "t09_dois");
        Directory.CreateDirectory(pasta);

        // "relatorio_jan_2026.pdf" ≈ "relatorio_2026" (alta sim)
        // "fatura_jan_2026.pdf" ≈ "relatorio_2026" (baixa sim)
        CriarPdfComTexto(Path.Combine(pasta, "relatorio_jan_2026.pdf"), "conteudo relatorio");
        CriarPdfComTexto(Path.Combine(pasta, "fatura_jan_2026.pdf"), "conteudo fatura");

        var resultado = await _seletor.SelecionarMelhorAsync(
            pasta, "relatorio_2026", limiarSimilaridade: 0.5,
            monitorarSubpastas: false, cicloId: "c9-t09", CancellationToken.None);

        resultado.Should().NotBeNull("pelo menos um candidato deve passar o limiar");
        resultado!.NomeEsperadoLogico.Should().Be("relatorio_jan_2026",
            "relatorio_jan_2026 tem maior similaridade com 'relatorio_2026'");
    }

    // --- Anchorador edge cases ---

    [Fact]
    public void T10_Anchorador_ancora_em_pagina_inexistente_retorna_vazio_e_confianca_zero()
    {
        // Âncora aponta para página 5, mas o documento só tem 1 página.
        var palavra = new PdfPalavra("Texto", new BboxRelativo(0.0, 0.0, 1.0, 1.0));
        var pagina = new PdfPaginaTexto(1, 595, 842, [palavra]);

        var ancora = new AncorarPdfTemplateAncora
        {
            Ordem = 1, CorHex = "#4A90D9", Pagina = 5,  // página inexistente
            XRel = 0.0, YRel = 0.0, LarguraRel = 1.0, AlturaRel = 1.0,
            Metadado = new AncorarPdfTemplateMetadado
            {
                ChaveTecnica = "pagina_ausente", TipoEsperado = "texto"
            }
        };

        var resultado = _anchorador.Ancorar([pagina], [ancora]);

        resultado.Should().HaveCount(1);
        resultado[0].ValorBruto.Should().BeEmpty("página inexistente → sem texto extraído");
        resultado[0].Confianca.Should().BeApproximately(0.0, 1e-9);
    }

    [Fact]
    public void T11_Anchorador_multiplas_ancoras_respeitam_ordem_ascendente()
    {
        // 3 palavras em regiões distintas, 3 âncoras com Ordem 3, 1, 2 (scrambled).
        // Resultado deve ser ordenado por Ordem (1, 2, 3).
        var palavras = new[]
        {
            new PdfPalavra("Tres",  new BboxRelativo(0.7, 0.0, 0.3, 0.1)),  // → âncora Ordem=3
            new PdfPalavra("Um",    new BboxRelativo(0.0, 0.0, 0.3, 0.1)),  // → âncora Ordem=1
            new PdfPalavra("Dois",  new BboxRelativo(0.35, 0.0, 0.3, 0.1)) // → âncora Ordem=2
        };
        var pagina = new PdfPaginaTexto(1, 595, 842, palavras);

        var ancoras = new[]
        {
            CriarAncora("v3", null, ordem: 3, x: 0.7, y: 0.0, w: 0.3, h: 0.2),
            CriarAncora("v1", null, ordem: 1, x: 0.0, y: 0.0, w: 0.3, h: 0.2),
            CriarAncora("v2", null, ordem: 2, x: 0.35, y: 0.0, w: 0.3, h: 0.2)
        };

        var resultado = _anchorador.Ancorar([pagina], ancoras);

        resultado.Should().HaveCount(3);
        resultado[0].Chave.Should().Be("v1", "Ordem=1 deve ser primeiro");
        resultado[0].ValorBruto.Should().Contain("Um");
        resultado[1].Chave.Should().Be("v2", "Ordem=2 deve ser segundo");
        resultado[1].ValorBruto.Should().Contain("Dois");
        resultado[2].Chave.Should().Be("v3", "Ordem=3 deve ser terceiro");
        resultado[2].ValorBruto.Should().Contain("Tres");
    }

    [Fact]
    public void T12_Validador_clienteId_como_substring_de_cnpj_normalizado_nao_deve_validar_sem_match_exato()
    {
        // Cenário adversarial: clienteId numérico pode aparecer como substring em CNPJ,
        // mas isso não deve validar quando documento oficial do cliente for diferente.
        var validador = new AncorarPdfValidadorClienteRegex(
            new InMemoryClienteRepository(new Dictionary<int, Cliente>
            {
                [4252011] = new()
                {
                    Id = 4252011,
                    CodigoCliente = "CLI-C9-SUB",
                    Nome = "Cliente Substring",
                    TipoDocumento = TipoDocumentoCliente.CNPJ,
                    Documento = "11222333000181",
                    Ativo = true
                }
            }));
        var paginas = CriarPaginasComTexto("Razao Social CNPJ 04.252.011/0001-10 SP");
        var resultado = validador.Validar(paginas, clienteId: 4252011);

        resultado.Valido.Should().BeFalse();
        resultado.Motivo.Should().Be("cliente_nao_validado");
    }

    // --- Helpers ---

    private static AncorarPdfTemplateAncora CriarAncora(
        string chave, string? regra,
        int ordem = 1, double x = 0.0, double y = 0.0, double w = 1.0, double h = 1.0)
        => new()
        {
            Ordem = ordem, CorHex = "#4A90D9", Pagina = 1,
            XRel = x, YRel = y, LarguraRel = w, AlturaRel = h,
            Metadado = new AncorarPdfTemplateMetadado
            {
                ChaveTecnica = chave, TipoEsperado = "texto",
                RegraNormalizacao = regra
            }
        };

    private static void CriarPdfComTexto(string path, string texto)
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        page.AddText(texto, 12, new UglyToad.PdfPig.Core.PdfPoint(50, 700), font);
        File.WriteAllBytes(path, builder.Build());
    }

    private static IReadOnlyList<PdfPaginaTexto> CriarPaginasComTexto(string texto)
    {
        var palavras = texto.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select((t, i) => new PdfPalavra(t, new BboxRelativo(i * 0.05, 0.1, 0.04, 0.02)))
            .ToList();
        return [new PdfPaginaTexto(1, 595, 842, palavras)];
    }

    private sealed class InMemoryClienteRepository : IClienteRepository
    {
        private readonly IReadOnlyDictionary<int, Cliente> _clientes;

        public InMemoryClienteRepository(IReadOnlyDictionary<int, Cliente> clientes)
        {
            _clientes = clientes;
        }

        public Cliente? GetById(int id) => _clientes.TryGetValue(id, out var cliente) ? cliente : null;
        public Cliente? GetByDocumento(string? documentoSomenteDigitos) => throw new NotSupportedException();
        public Cliente? GetByCodigoCliente(string? codigoCliente) => throw new NotSupportedException();
        public IReadOnlyList<Cliente> ListarTodos() => throw new NotSupportedException();
        public IReadOnlyList<Cliente> Buscar(string? termo, int pagina, int tamanhoPagina) => throw new NotSupportedException();
        public int Contar(string? termo) => throw new NotSupportedException();
        public int Create(Cliente cliente) => throw new NotSupportedException();
        public void Update(Cliente cliente) => throw new NotSupportedException();
        public void Inativar(int clienteId, int atualizadoPorUserId, DateTime atualizadoEmUtc) => throw new NotSupportedException();
        public GrupoEmpresarial? GetGrupoById(int grupoId) => throw new NotSupportedException();
        public GrupoEmpresarial? GetGrupoByNomeNormalizado(string? nomeNormalizado) => throw new NotSupportedException();
        public IReadOnlyList<GrupoEmpresarial> ListarGrupos() => throw new NotSupportedException();
        public int CreateGrupo(GrupoEmpresarial grupo) => throw new NotSupportedException();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}
