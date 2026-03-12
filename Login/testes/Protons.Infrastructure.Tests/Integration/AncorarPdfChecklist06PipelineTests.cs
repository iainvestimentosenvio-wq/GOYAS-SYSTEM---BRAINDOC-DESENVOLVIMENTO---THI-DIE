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
/// C6 — Gate G3 (blocking): Testes isolados de cada estágio do pipeline de ancoragem.
/// Verifica extrator, seletor, validador, anchorador e normalizador individualmente.
/// </summary>
[Trait("Checklist", "C6")]
[Trait("Category", "C6_G3_Pipeline")]
public sealed class AncorarPdfChecklist06PipelineTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AncorarPdfExtratorTextoPdfPig _extrator = new();
    private readonly AncorarPdfSeletorArquivoPasta _seletor = new();
    private readonly AncorarPdfValidadorClienteRegex _validador;
    private readonly AncorarPdfAncoradorEspacialBbox _anchorador = new();

    public AncorarPdfChecklist06PipelineTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"c6_pipe_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _validador = new AncorarPdfValidadorClienteRegex(
            new InMemoryClienteRepository(new Dictionary<int, Cliente>
            {
                [111111111] = new()
                {
                    Id = 111111111,
                    CodigoCliente = "CLI-C6-CPF",
                    Nome = "Cliente CPF C6",
                    TipoDocumento = TipoDocumentoCliente.CPF,
                    Documento = "12345678909",
                    Ativo = true
                },
                [999999] = new()
                {
                    Id = 999999,
                    CodigoCliente = "CLI-C6-CNPJ",
                    Nome = "Cliente CNPJ C6",
                    TipoDocumento = TipoDocumentoCliente.CNPJ,
                    Documento = "04252011000110",
                    Ativo = true
                }
            }));
    }

    // --- Extrator ---

    [Fact]
    public async Task T01_Extrator_pdf_nativo_retorna_palavras_com_bbox()
    {
        var path = Path.Combine(_tempDir, "texto.pdf");
        CriarPdfComTexto(path, "Olá Mundo");

        var paginas = await _extrator.ExtrairAsync(path, CancellationToken.None);

        paginas.Should().NotBeEmpty();
        paginas[0].Palavras.Should().NotBeEmpty();
        paginas[0].Palavras.All(p =>
            p.Bbox.X >= 0 && p.Bbox.Y >= 0 &&
            p.Bbox.Largura >= 0 && p.Bbox.Altura >= 0).Should().BeTrue();
        paginas[0].LarguraPt.Should().BeGreaterThan(0);
        paginas[0].AlturaPt.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task T02_Extrator_pdf_minimo_sem_texto_retorna_paginas_vazias()
    {
        var path = Path.Combine(_tempDir, "vazio.pdf");
        CriarPdfSemTexto(path);

        var paginas = await _extrator.ExtrairAsync(path, CancellationToken.None);

        paginas.Should().NotBeEmpty(); // página existe
        paginas.Sum(p => p.Palavras.Count).Should().Be(0); // mas sem palavras
    }

    // --- Seletor ---

    [Fact]
    public async Task T03_Seletor_pasta_vazia_retorna_null()
    {
        var pastaVazia = Path.Combine(_tempDir, "vazia");
        Directory.CreateDirectory(pastaVazia);

        var resultado = await _seletor.SelecionarMelhorAsync(
            pastaVazia, "relatorio_2026", 0.7, false, "ciclo-1", CancellationToken.None);

        resultado.Should().BeNull();
    }

    [Fact]
    public async Task T04_Seletor_nome_exato_retorna_candidato_correto()
    {
        var pastaExato = Path.Combine(_tempDir, "exato");
        Directory.CreateDirectory(pastaExato);
        var pdfPath = Path.Combine(pastaExato, "relatorio_2026.pdf");
        CriarPdfComTexto(pdfPath, "conteúdo");

        var resultado = await _seletor.SelecionarMelhorAsync(
            pastaExato, "relatorio_2026", 0.7, false, "ciclo-2", CancellationToken.None);

        resultado.Should().NotBeNull();
        resultado!.NomeEsperadoLogico.Should().Be("relatorio_2026");
        resultado.ArquivoHash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task T05_Seletor_nome_aproximado_acima_limiar_retorna_candidato()
    {
        var pasta = Path.Combine(_tempDir, "aprox");
        Directory.CreateDirectory(pasta);
        // Nome ligeiramente diferente da referência.
        var pdfPath = Path.Combine(pasta, "relatorio_2026_v2.pdf");
        CriarPdfComTexto(pdfPath, "conteúdo");

        var resultado = await _seletor.SelecionarMelhorAsync(
            pasta, "relatorio_2026", 0.7, false, "ciclo-3", CancellationToken.None);

        resultado.Should().NotBeNull("nome próximo deve ultrapassar limiar 0.7");
    }

    [Fact]
    public async Task T06_Seletor_nome_abaixo_limiar_retorna_fallback_mais_antigo()
    {
        // C11 Fix 2: quando nenhum arquivo passa o limiar de similaridade, o seletor retorna o
        // arquivo mais antigo da pasta como fallback (nunca retorna null quando a pasta não está vazia).
        var pasta = Path.Combine(_tempDir, "baixo");
        Directory.CreateDirectory(pasta);
        // Nome completamente diferente da referência — ficará abaixo do limiar 0.75.
        var pdfPath = Path.Combine(pasta, "fatura_energia_2025.pdf");
        CriarPdfComTexto(pdfPath, "conteúdo");

        var resultado = await _seletor.SelecionarMelhorAsync(
            pasta, "relatorio_financeiro_2026", 0.75, false, "ciclo-4", CancellationToken.None);

        // Comportamento C11: fallback ao mais antigo mesmo sem match por nome.
        resultado.Should().NotBeNull("fallback retorna o único arquivo disponível mesmo sem match por nome");
        resultado!.ArquivoPath.Should().Contain("fatura_energia_2025",
            "único arquivo na pasta deve ser retornado como fallback");
    }

    // --- Validador ---

    [Fact]
    public void T07_Validador_cpf_presente_no_texto_valida()
    {
        // Documento oficial do cliente 111111111 = CPF 123.456.789-09.
        var paginas = CriarPaginasComTexto("Contrato CPF 123.456.789-09 assinado.");
        var resultado = _validador.Validar(paginas, 111111111);

        resultado.Valido.Should().BeTrue();
        resultado.Motivo.Should().BeNull();
    }

    [Fact]
    public void T08_Validador_cpf_ausente_retorna_invalido()
    {
        var paginas = CriarPaginasComTexto("Documento sem identificação fiscal.");
        var resultado = _validador.Validar(paginas, 111111111);

        resultado.Valido.Should().BeFalse();
        resultado.Motivo.Should().Be("cpf_cnpj_nao_encontrado");
    }

    [Fact]
    public void T09_Validador_cpf_errado_retorna_invalido()
    {
        // CPF no PDF não corresponde ao documento oficial do cliente 999999 (CNPJ 04.252.011/0001-10).
        var paginas = CriarPaginasComTexto("CPF do pagador: 123.456.789-09");
        var resultado = _validador.Validar(paginas, 999999);

        resultado.Valido.Should().BeFalse();
        resultado.Motivo.Should().Be("cliente_nao_validado");
    }

    // --- Anchorador ---

    [Fact]
    public void T10_Anchorador_bbox_intersecta_retorna_texto()
    {
        var palavra = new PdfPalavra("R$ 1.234", new BboxRelativo(0.1, 0.1, 0.2, 0.05));
        var pagina = new PdfPaginaTexto(1, 595, 842, [palavra]);

        var ancora = new AncorarPdfTemplateAncora
        {
            Ordem = 1, CorHex = "#4A90D9", Pagina = 1,
            XRel = 0.05, YRel = 0.05, LarguraRel = 0.4, AlturaRel = 0.2,
            Metadado = new AncorarPdfTemplateMetadado { ChaveTecnica = "valor", TipoEsperado = "texto" }
        };

        var resultado = _anchorador.Ancorar([pagina], [ancora]);

        resultado.Should().HaveCount(1);
        resultado[0].ValorBruto.Should().Contain("1.234");
        resultado[0].Confianca.Should().BeApproximately(1.0, 1e-9);
    }

    [Fact]
    public void T11_Anchorador_bbox_disjunto_retorna_vazio()
    {
        // Palavra na região superior esquerda; âncora na região inferior direita.
        var palavra = new PdfPalavra("Texto", new BboxRelativo(0.0, 0.0, 0.1, 0.05));
        var pagina = new PdfPaginaTexto(1, 595, 842, [palavra]);

        var ancora = new AncorarPdfTemplateAncora
        {
            Ordem = 1, CorHex = "#4A90D9", Pagina = 1,
            XRel = 0.8, YRel = 0.8, LarguraRel = 0.2, AlturaRel = 0.2,
            Metadado = new AncorarPdfTemplateMetadado { ChaveTecnica = "valor_longe", TipoEsperado = "texto" }
        };

        var resultado = _anchorador.Ancorar([pagina], [ancora]);

        resultado.Should().HaveCount(1);
        resultado[0].ValorBruto.Should().BeEmpty();
        resultado[0].Confianca.Should().BeApproximately(0.0, 1e-9);
    }

    // --- Normalizador ---

    [Fact]
    public void T12_Normalizador_cpf_remove_pontuacao()
    {
        // Testa normalização "cpf" via anchorador (regra configurada na âncora).
        var palavra = new PdfPalavra("000.000.000-00", new BboxRelativo(0.0, 0.0, 1.0, 1.0));
        var pagina = new PdfPaginaTexto(1, 595, 842, [palavra]);
        var ancora = new AncorarPdfTemplateAncora
        {
            Ordem = 1, CorHex = "#4A90D9", Pagina = 1,
            XRel = 0.0, YRel = 0.0, LarguraRel = 1.0, AlturaRel = 1.0,
            Metadado = new AncorarPdfTemplateMetadado
            {
                ChaveTecnica = "cpf", TipoEsperado = "texto",
                RegraNormalizacao = "cpf"
            }
        };

        var resultado = _anchorador.Ancorar([pagina], [ancora]);

        resultado.Should().HaveCount(1);
        resultado[0].ValorBruto.Should().Be("000.000.000-00");
        resultado[0].ValorNormalizado.Should().Be("00000000000");
    }

    [Fact]
    public void T13_Normalizador_moeda_brl_formata()
    {
        // Testa normalização "moeda_brl" via anchorador.
        var palavra1 = new PdfPalavra("R$", new BboxRelativo(0.0, 0.0, 0.1, 0.1));
        var palavra2 = new PdfPalavra("1.234,56", new BboxRelativo(0.1, 0.0, 0.2, 0.1));
        var pagina = new PdfPaginaTexto(1, 595, 842, [palavra1, palavra2]);
        var ancora = new AncorarPdfTemplateAncora
        {
            Ordem = 1, CorHex = "#F5D547", Pagina = 1,
            XRel = 0.0, YRel = 0.0, LarguraRel = 1.0, AlturaRel = 1.0,
            Metadado = new AncorarPdfTemplateMetadado
            {
                ChaveTecnica = "valor", TipoEsperado = "texto",
                RegraNormalizacao = "moeda_brl"
            }
        };

        var resultado = _anchorador.Ancorar([pagina], [ancora]);

        resultado.Should().HaveCount(1);
        resultado[0].ValorNormalizado.Should().Be("1234.56");
    }

    [Fact]
    public void T14_Anchorador_texto_a_direita_extrai_valor()
    {
        // Página com "Valor: R$ 1.234" — buscar "Valor:" e extrair à direita.
        var palavra1 = new PdfPalavra("Valor:", new BboxRelativo(0.0, 0.1, 0.08, 0.03));
        var palavra2 = new PdfPalavra("R$", new BboxRelativo(0.09, 0.1, 0.04, 0.03));
        var palavra3 = new PdfPalavra("1.234", new BboxRelativo(0.14, 0.1, 0.08, 0.03));
        var pagina = new PdfPaginaTexto(1, 595, 842, [palavra1, palavra2, palavra3]);

        var ancora = new AncorarPdfTemplateAncora
        {
            Ordem = 1, CorHex = "#4A90D9", Pagina = 1,
            ModoAncora = AncorarPdfModoAncora.TextoADireita,
            TextoAncora = "Valor:",
            LarguraExtracaoRel = 0.3,
            AlturaExtracaoRel = 0.05,
            Metadado = new AncorarPdfTemplateMetadado { ChaveTecnica = "valor", TipoEsperado = "texto" }
        };

        var resultado = _anchorador.Ancorar([pagina], [ancora]);

        resultado.Should().HaveCount(1);
        resultado[0].ValorBruto.Should().Contain("1.234");
        resultado[0].Confianca.Should().BeApproximately(1.0, 1e-9);
    }

    [Fact]
    public void T15a_Anchorador_texto_a_direita_na_borda_clampa_regiao()
    {
        // Âncora na borda direita (X=0.9, Largura=0.15 → direita em 1.05). Região deve ser clampeada.
        var palavra1 = new PdfPalavra("Valor:", new BboxRelativo(0.85, 0.1, 0.15, 0.03));
        var palavra2 = new PdfPalavra("R$", new BboxRelativo(0.92, 0.1, 0.04, 0.03));
        var pagina = new PdfPaginaTexto(1, 595, 842, [palavra1, palavra2]);

        var ancora = new AncorarPdfTemplateAncora
        {
            Ordem = 1, CorHex = "#4A90D9", Pagina = 1,
            ModoAncora = AncorarPdfModoAncora.TextoADireita,
            TextoAncora = "Valor:",
            LarguraExtracaoRel = 0.3,
            AlturaExtracaoRel = 0.05,
            Metadado = new AncorarPdfTemplateMetadado { ChaveTecnica = "valor", TipoEsperado = "texto" }
        };

        var resultado = _anchorador.Ancorar([pagina], [ancora]);

        resultado.Should().HaveCount(1);
        // Região clampeada: X=1, largura=0 → sem palavras na região → ValorBruto vazio
        resultado[0].Confianca.Should().BeApproximately(0.0, 1e-9);
    }

    [Fact]
    public void T15_Anchorador_texto_nao_encontrado_confianca_zero()
    {
        var palavra = new PdfPalavra("Outro", new BboxRelativo(0.1, 0.1, 0.1, 0.05));
        var pagina = new PdfPaginaTexto(1, 595, 842, [palavra]);

        var ancora = new AncorarPdfTemplateAncora
        {
            Ordem = 1, CorHex = "#4A90D9", Pagina = 1,
            ModoAncora = AncorarPdfModoAncora.TextoADireita,
            TextoAncora = "Xyz inexistente",
            LarguraExtracaoRel = 0.2,
            AlturaExtracaoRel = 0.05,
            Metadado = new AncorarPdfTemplateMetadado { ChaveTecnica = "vazio", TipoEsperado = "texto" }
        };

        var resultado = _anchorador.Ancorar([pagina], [ancora]);

        resultado.Should().HaveCount(1);
        resultado[0].ValorBruto.Should().BeEmpty();
        resultado[0].Confianca.Should().BeApproximately(0.0, 1e-9);
    }

    // --- Helpers ---

    private static void CriarPdfComTexto(string path, string texto)
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        page.AddText(texto, 12, new UglyToad.PdfPig.Core.PdfPoint(50, 700), font);
        File.WriteAllBytes(path, builder.Build());
    }

    private static void CriarPdfSemTexto(string path)
    {
        var builder = new PdfDocumentBuilder();
        builder.AddPage(PageSize.A4);
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
