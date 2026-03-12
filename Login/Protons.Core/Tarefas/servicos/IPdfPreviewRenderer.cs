using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Protons.Core.Tarefas.Services;

// ─────────────────────────────────────────────────────────────────────────────
// Modelos de requisição e resultado
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Parâmetros de uma requisição de renderização PDF.
/// </summary>
public sealed record PdfRenderRequest(
    string PdfPath,
    int Pagina = 1,
    int Dpi = 300)
{
    /// <summary>DPI normalizado para o intervalo suportado [72, 1200].</summary>
    public int DpiNormalizado => Math.Clamp(Dpi, 72, 1200);
}

/// <summary>
/// Métricas coletadas durante uma renderização (struct para evitar alocação desnecessária).
/// </summary>
public readonly struct PdfRenderMetrics
{
    /// <summary>Nome do renderer que produziu este resultado.</summary>
    public string NomeRenderer { get; init; }

    /// <summary>DPI efetivamente utilizado na renderização.</summary>
    public int DpiRealizado { get; init; }

    /// <summary>Duração total do render em milissegundos.</summary>
    public long DuracaoMs { get; init; }

    /// <summary>Verdadeiro quando o renderer utilizado não foi o primário da cadeia.</summary>
    public bool ViouFallback { get; init; }

    /// <summary>Momento de início do render (UTC).</summary>
    public DateTimeOffset OcorreuEmUtc { get; init; }
}

/// <summary>
/// Capacidades declaradas de um renderer (imutável).
/// </summary>
public sealed record PdfRendererCapabilities(
    bool SuportaLinux,
    bool SuportaWindows,
    bool SuportaMacOs,
    int DpiMinimo,
    int DpiMaximo,
    bool RequereExternoInstalado,
    string? ExternoNecessario = null);

/// <summary>
/// Resultado de uma tentativa de renderização — discriminated union.
/// Sempre retornado pelo renderer; nunca são lançadas exceções ao chamador.
/// </summary>
public abstract record PdfRenderResult
{
    private PdfRenderResult() { }

    /// <summary>Verdadeiro quando a renderização produziu uma imagem válida.</summary>
    public abstract bool Sucesso { get; }

    /// <summary>Métricas coletadas durante o render.</summary>
    public abstract PdfRenderMetrics Metricas { get; init; }

    /// <summary>
    /// Renderização bem-sucedida.
    /// <para>
    /// O <see cref="ImagemPng"/> é propriedade do chamador — descarte após uso.
    /// </para>
    /// </summary>
    public sealed record Sucedido(MemoryStream ImagemPng, PdfRenderMetrics Metricas) : PdfRenderResult
    {
        public override bool Sucesso => true;
        public override PdfRenderMetrics Metricas { get; init; } = Metricas;
    }

    /// <summary>
    /// Renderização falhou. O chamador pode tentar o próximo renderer da cadeia.
    /// </summary>
    public sealed record Falhou(
        string MensagemErro,
        string CodigoErro,
        PdfRenderMetrics Metricas) : PdfRenderResult
    {
        public override bool Sucesso => false;
        public override PdfRenderMetrics Metricas { get; init; } = Metricas;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Interface principal
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Abstração de renderização de página PDF em imagem PNG em memória.
/// <para>
/// Implementações concretas:
/// <list type="bullet">
///   <item><description><c>AncorarPdfGhostscriptRenderer</c> — processo externo 'gs', alta qualidade de antialiasing.</description></item>
///   <item><description><c>AncorarPdfDocnetRenderer</c> — PDFium embutido via Docnet.Core, sem instalação externa.</description></item>
///   <item><description><c>AncorarPdfFallbackPreviewRenderer</c> — cadeia de renderers com fallback automático e métricas.</description></item>
/// </list>
/// </para>
/// </summary>
public interface IPdfPreviewRenderer
{
    /// <summary>Nome identificador do renderer (ex: "Ghostscript", "Docnet.PDFium", "Null").</summary>
    string NomeRenderer { get; }

    /// <summary>Capacidades declaradas: plataformas suportadas, intervalo de DPI, dependências externas.</summary>
    PdfRendererCapabilities Capabilities { get; }

    /// <summary>
    /// Indica se o renderer está operacional no ambiente atual.
    /// <list type="bullet">
    ///   <item><description>Ghostscript: verifica se <c>gs</c> está no PATH.</description></item>
    ///   <item><description>Docnet/PDFium: verifica se as bibliotecas nativas estão acessíveis.</description></item>
    ///   <item><description>Null: sempre <c>false</c>.</description></item>
    /// </list>
    /// Esta propriedade pode ser consultada antes de chamar <see cref="RenderizarAsync"/>,
    /// mas não garante ausência de falha durante a renderização.
    /// </summary>
    bool Disponivel { get; }

    /// <summary>
    /// Renderiza a página especificada do PDF como imagem PNG em memória.
    /// <para>
    /// Garante: nunca lança exceção ao chamador — sempre retorna <see cref="PdfRenderResult"/>.
    /// O stream no resultado <see cref="PdfRenderResult.Sucedido.ImagemPng"/> é propriedade do chamador.
    /// </para>
    /// </summary>
    Task<PdfRenderResult> RenderizarAsync(PdfRenderRequest request, CancellationToken ct = default);
}

// ─────────────────────────────────────────────────────────────────────────────
// Null object (Core — sem dependência de Infrastructure)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Renderer nulo: sempre retorna falha com código <c>NULL_RENDERER</c>.
/// Usado como default seguro quando nenhum renderer foi injetado
/// (ex: testes unitários de ViewModel que não testam renderização).
/// </summary>
public sealed class AncorarPdfNullPreviewRenderer : IPdfPreviewRenderer
{
    private static readonly PdfRendererCapabilities _cap =
        new(false, false, false, 0, 0, false);

    public string NomeRenderer => "Null";
    public PdfRendererCapabilities Capabilities => _cap;
    public bool Disponivel => false;

    public Task<PdfRenderResult> RenderizarAsync(PdfRenderRequest request, CancellationToken ct = default)
    {
        return Task.FromResult<PdfRenderResult>(new PdfRenderResult.Falhou(
            "Renderer não configurado (NullPreviewRenderer).",
            "NULL_RENDERER",
            new PdfRenderMetrics
            {
                NomeRenderer = NomeRenderer,
                DpiRealizado = request.DpiNormalizado,
                DuracaoMs = 0,
                ViouFallback = false,
                OcorreuEmUtc = DateTimeOffset.UtcNow
            }));
    }
}
