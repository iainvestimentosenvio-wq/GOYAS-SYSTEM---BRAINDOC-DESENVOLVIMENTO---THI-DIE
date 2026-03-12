using FluentAssertions;
using Protons.Core.Tarefas.Models;
using Protons.Infrastructure.Tarefas.Services;
using Xunit;

namespace Protons.Infrastructure.Tests.Integration;

/// <summary>
/// C12 — Gate G3 (blocking): Detecção no PDF real FRE CAIAPONIA.
/// Extrai texto via AncorarPdfExtratorTextoPdfPig e verifica que o detector
/// identifica corretamente cada tipo de variável presente no documento.
///
/// Precondição: o PDF deve estar disponível em PROTONS_FRE_PDF_PATH
/// (fallback para caminho padrão do repositório).
/// Sem artefato: skip explicito (local) ou falha explicita (CI estrito).
/// </summary>
[Trait("Checklist", "C12")]
[Trait("Category", "C12_G3_FRE")]
public sealed class AncorarPdfSmartDetectorFRETests
{
    private readonly AncorarPdfSmartDetector _detector = new();
    private readonly AncorarPdfExtratorTextoPdfPig _extrator = new();

    /// <summary>
    /// Extrai as páginas do FRE PDF real.
    /// Se o artefato estiver faltando em CI estrito, falha com mensagem clara.
    /// </summary>
    private async Task<IReadOnlyList<PdfPaginaTexto>> ExtrairPaginasAsync()
    {
        var frePdfPath = InfraTestPreconditions.ObterFrePdfPathOuFalhar();
        return await _extrator.ExtrairAsync(frePdfPath, CancellationToken.None);
    }

    /// <summary>
    /// Encontra a posição de clique para um valor conhecido no PDF.
    /// Busca a palavra que contém o valor (ou parte dele) e retorna o centro do bbox.
    /// </summary>
    private static (double X, double Y)? EncontrarPosicaoClique(
        IReadOnlyList<PdfPaginaTexto> paginas, string textoParaEncontrar, int paginaNum = 1)
    {
        if (paginas.Count < paginaNum)
            return null;

        var pagina = paginas[paginaNum - 1];
        var palavra = pagina.Palavras
            .FirstOrDefault(p => p.Texto.Contains(textoParaEncontrar, StringComparison.OrdinalIgnoreCase));

        if (palavra is null)
            return null;

        var cx = palavra.Bbox.X + palavra.Bbox.Largura / 2;
        var cy = palavra.Bbox.Y + palavra.Bbox.Altura / 2;
        return (cx, cy);
    }

    // ── T01: CNPJ "19.864.554/0001-65" ──────────────────────────────────────

    [RequiresFrePdfFact]
    public async Task T01_Cnpj_CAIAPONIA_detectado_e_normalizado()
    {
        var paginas = await ExtrairPaginasAsync();

        // Procurar em qualquer página por fragmento do CNPJ.
        (double X, double Y)? pos = null;
        for (var pg = 1; pg <= paginas.Count && pos is null; pg++)
            pos = EncontrarPosicaoClique(paginas, "19.864.554", pg) ??
                  EncontrarPosicaoClique(paginas, "0001-65", pg) ??
                  EncontrarPosicaoClique(paginas, "19864554", pg);

        if (pos is null)
        {
            // CNPJ pode estar formatado diferente; tentar busca por apenas dígitos.
            pos = BuscarEmTodasPaginas(paginas, p =>
                AncorarPdfRegexCatalog.CnpjPattern().IsMatch(p.Texto) ||
                (p.Texto.Length >= 5 && p.Texto.Replace(".", "").Replace("/", "").Replace("-", "").Length >= 8));
        }

        pos.Should().NotBeNull("CNPJ do CAIAPONIA deve existir no PDF");
        if (pos is null) return;

        var (entrada, paginaNum) = CriarEntrada(pos.Value, paginas);
        var resultado = _detector.Detectar(paginas, entrada);

        resultado.Encontrado.Should().BeTrue("deve encontrar texto no CNPJ");
        resultado.Tipo.Should().Be(SmartTipoVariavel.Cnpj,
            "19.864.554/0001-65 deve ser classificado como CNPJ");
        resultado.TextoNormalizado.Should().Be("19864554000165",
            "CNPJ normalizado deve ter apenas 14 dígitos");
    }

    // ── T02: CPF "382316621-20" ──────────────────────────────────────────────

    [RequiresFrePdfFact]
    public async Task T02_Cpf_CAIAPONIA_detectado_e_normalizado()
    {
        var paginas = await ExtrairPaginasAsync();

        var pos = BuscarEmTodasPaginas(paginas, p =>
            p.Texto.Contains("382316621") || p.Texto.Contains("382.316.621") ||
            AncorarPdfRegexCatalog.CpfPattern().IsMatch(p.Texto));

        pos.Should().NotBeNull("CPF do responsável deve existir no FRE");
        if (pos is null) return;

        var (entrada, _) = CriarEntrada(pos.Value, paginas);
        var resultado = _detector.Detectar(paginas, entrada);

        resultado.Encontrado.Should().BeTrue();
        resultado.Tipo.Should().Be(SmartTipoVariavel.Cpf,
            "CPF com 11 dígitos deve ser classificado como Cpf");
        resultado.TextoNormalizado.Should().Be("38231662120");
    }

    // ── T03: E-mail principal ────────────────────────────────────────────────

    [RequiresFrePdfFact]
    public async Task T03_Email_gmail_detectado_no_FRE()
    {
        var paginas = await ExtrairPaginasAsync();

        var pos = BuscarEmTodasPaginas(paginas, p =>
            p.Texto.Contains("@gmail.com") || p.Texto.Contains("@"));

        pos.Should().NotBeNull("e-mail deve existir no FRE");
        if (pos is null) return;

        var (entrada, _) = CriarEntrada(pos.Value, paginas);
        var resultado = _detector.Detectar(paginas, entrada);

        resultado.Encontrado.Should().BeTrue();
        resultado.Tipo.Should().Be(SmartTipoVariavel.Email,
            "texto com @ deve ser classificado como Email");
    }

    // ── T04: CEP "76250-000" ─────────────────────────────────────────────────

    [RequiresFrePdfFact]
    public async Task T04_Cep_CAIAPONIA_detectado_no_FRE()
    {
        var paginas = await ExtrairPaginasAsync();

        var pos = BuscarEmTodasPaginas(paginas, p =>
            p.Texto.Contains("76250-000") || p.Texto == "76250");

        pos.Should().NotBeNull("CEP 76250-000 deve existir no FRE");
        if (pos is null) return;

        var (entrada, _) = CriarEntrada(pos.Value, paginas);
        var resultado = _detector.Detectar(paginas, entrada);

        resultado.Encontrado.Should().BeTrue();
        resultado.Tipo.Should().BeOneOf(
            new[] { SmartTipoVariavel.Cep, SmartTipoVariavel.Inteiro },
            "CEP deve ser detectado (com ou sem hífen dependendo da extração)");
    }

    // ── T05: Telefone "(62)999401952" ────────────────────────────────────────

    [RequiresFrePdfFact]
    public async Task T05_Telefone_detectado_no_FRE()
    {
        var paginas = await ExtrairPaginasAsync();

        var pos = BuscarEmTodasPaginas(paginas, p =>
            p.Texto.Contains("999401952") || p.Texto.Contains("(62)") ||
            AncorarPdfRegexCatalog.TelefoneBrPattern().IsMatch(p.Texto));

        pos.Should().NotBeNull("telefone deve existir no FRE para evitar falso PASS por skip silencioso");
        if (pos is null) return;

        var (entrada, _) = CriarEntrada(pos.Value, paginas);
        var resultado = _detector.Detectar(paginas, entrada);

        resultado.Encontrado.Should().BeTrue();
        resultado.Tipo.Should().Be(SmartTipoVariavel.Telefone,
            "número com DDD deve ser classificado como Telefone");
    }

    // ── T06: Nome genérico "CAIAPONIA 1" detectado como Texto ───────────────

    [RequiresFrePdfFact]
    public async Task T06_Nome_empreendimento_detectado_como_texto()
    {
        var paginas = await ExtrairPaginasAsync();

        var pos = BuscarEmTodasPaginas(paginas, p => p.Texto == "CAIAPONIA");
        pos.Should().NotBeNull("nome do empreendimento deve existir no FRE para garantir cobertura real");
        if (pos is null) return;

        var (entrada, _) = CriarEntrada(pos.Value, paginas);
        var resultado = _detector.Detectar(paginas, entrada);

        resultado.Encontrado.Should().BeTrue();
        resultado.Tipo.Should().Be(SmartTipoVariavel.Texto,
            "nome de empreendimento não estruturado deve ser classificado como Texto");
    }

    // ── T07: Inteiro "17" detectado ──────────────────────────────────────────

    [RequiresFrePdfFact]
    public async Task T07_Inteiro_17_detectado_como_inteiro()
    {
        var paginas = await ExtrairPaginasAsync();

        var pos = BuscarEmTodasPaginas(paginas, p => p.Texto == "17");
        pos.Should().NotBeNull("valor inteiro de referencia deve existir no FRE para validar classificacao");
        if (pos is null) return;

        var (entrada, _) = CriarEntrada(pos.Value, paginas);
        var resultado = _detector.Detectar(paginas, entrada);

        resultado.Encontrado.Should().BeTrue();
        resultado.Tipo.Should().Be(SmartTipoVariavel.Inteiro,
            "valor '17' (somente dígitos) deve ser classificado como Inteiro");
        resultado.TextoBruto.Should().Contain("17");
    }

    // ── T08: Data "22/04/2024" detectada e normalizada ───────────────────────

    [RequiresFrePdfFact]
    public async Task T08_Data_br_detectada_e_normalizada_para_iso()
    {
        var paginas = await ExtrairPaginasAsync();

        var pos = BuscarEmTodasPaginas(paginas, p =>
            p.Texto.Contains("22/04/2024") || AncorarPdfRegexCatalog.DataBrPattern().IsMatch(p.Texto));

        pos.Should().NotBeNull("data de referencia deve existir no FRE para garantir cobertura da regra DataBr");
        if (pos is null) return;

        var (entrada, _) = CriarEntrada(pos.Value, paginas);
        var resultado = _detector.Detectar(paginas, entrada);

        resultado.Encontrado.Should().BeTrue();
        resultado.Tipo.Should().Be(SmartTipoVariavel.DataBr,
            "data DD/MM/AAAA deve ser classificada como DataBr");
        resultado.TextoNormalizado.Should().MatchRegex(@"\d{4}-\d{2}-\d{2}",
            "data normalizada deve estar em formato ISO 8601");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // Busca uma palavra em todas as páginas e retorna o centro do bbox.
    private static (double X, double Y)? BuscarEmTodasPaginas(
        IReadOnlyList<PdfPaginaTexto> paginas,
        Func<PdfPalavra, bool> predicate)
    {
        foreach (var pagina in paginas)
        {
            var palavra = pagina.Palavras.FirstOrDefault(predicate);
            if (palavra is not null)
            {
                var cx = palavra.Bbox.X + palavra.Bbox.Largura / 2;
                var cy = palavra.Bbox.Y + palavra.Bbox.Altura / 2;
                return (cx, cy);
            }
        }
        return null;
    }

    // Determina o número de página e cria a entrada de detecção.
    private static (SmartDeteccaoEntrada Entrada, int PaginaNum) CriarEntrada(
        (double X, double Y) pos, IReadOnlyList<PdfPaginaTexto> paginas)
    {
        // Para simplificar: usar página 1. Se necessário, pode ser expandido.
        return (new SmartDeteccaoEntrada(pos.X, pos.Y, 1), 1);
    }
}
