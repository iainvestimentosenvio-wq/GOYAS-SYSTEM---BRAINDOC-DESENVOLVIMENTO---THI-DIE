using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Protons.Core.Tarefas.Services;

namespace Protons.Infrastructure.Tarefas.Services;

/// <summary>
/// Renderer de fallback: tenta cada renderer da cadeia em ordem
/// e retorna o resultado do primeiro que obtiver sucesso.
/// <para>
/// Uso recomendado de produção (PDFium prioritário, Ghostscript como fallback):
/// <code>
/// new AncorarPdfFallbackPreviewRenderer(
///     [new AncorarPdfDocnetRenderer(), new AncorarPdfGhostscriptRenderer()],
///     onLog: msg => logger.Write(msg))
/// </code>
/// </para>
/// <para>
/// Quando o fallback é acionado, o campo <see cref="PdfRenderMetrics.ViouFallback"/>
/// da métrica é definido como <c>true</c>, permitindo alertas operacionais.
/// </para>
/// </summary>
public sealed class AncorarPdfFallbackPreviewRenderer : IPdfPreviewRenderer
{
    private readonly IReadOnlyList<IPdfPreviewRenderer> _chain;
    private readonly Action<string>? _onLog;

    /// <param name="chain">
    /// Cadeia de renderers em ordem de preferência (primeiro = prioritário).
    /// Deve conter pelo menos um elemento.
    /// </param>
    /// <param name="onLog">
    /// Callback opcional para logging de tentativas e falhas.
    /// Fire-and-forget: exceção no callback é ignorada.
    /// </param>
    public AncorarPdfFallbackPreviewRenderer(
        IReadOnlyList<IPdfPreviewRenderer> chain,
        Action<string>? onLog = null)
    {
        if (chain is null || chain.Count == 0)
            throw new ArgumentException("A cadeia de fallback deve ter ao menos um renderer.", nameof(chain));

        _chain = chain;
        _onLog = onLog;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Formato: <c>"Renderer1→Renderer2→..."</c>.
    /// </remarks>
    public string NomeRenderer => string.Join("→", _chain.Select(r => r.NomeRenderer));

    /// <inheritdoc/>
    /// <remarks>Capacidades do renderer primário (primeiro da cadeia).</remarks>
    public PdfRendererCapabilities Capabilities => _chain[0].Capabilities;

    /// <inheritdoc/>
    /// <remarks>Verdadeiro se qualquer renderer da cadeia estiver disponível.</remarks>
    public bool Disponivel => _chain.Any(r => r.Disponivel);

    /// <inheritdoc/>
    public async Task<PdfRenderResult> RenderizarAsync(
        PdfRenderRequest request,
        CancellationToken ct = default)
    {
        PdfRenderResult? ultimaFalha = null;
        bool viouFallback = false;

        for (int i = 0; i < _chain.Count; i++)
        {
            var renderer = _chain[i];

            if (!renderer.Disponivel)
            {
                Log($"[FallbackRenderer] {renderer.NomeRenderer} indisponível — pulando (índice {i}).");
                viouFallback = true;
                continue;
            }

            Log($"[FallbackRenderer] Tentando {renderer.NomeRenderer} (índice {i}, fallback={viouFallback})...");

            var resultado = await renderer.RenderizarAsync(request, ct);

            if (resultado.Sucesso)
            {
                // Decora a métrica com ViouFallback quando não foi o renderer primário.
                if (viouFallback && resultado is PdfRenderResult.Sucedido sucedido)
                {
                    var metricasAtualizadas = sucedido.Metricas with { ViouFallback = true };
                    Log($"[FallbackRenderer] Sucesso via fallback em {renderer.NomeRenderer}.");
                    return new PdfRenderResult.Sucedido(sucedido.ImagemPng, metricasAtualizadas);
                }

                Log($"[FallbackRenderer] Sucesso em {renderer.NomeRenderer}.");
                return resultado;
            }

            var falhou = (PdfRenderResult.Falhou)resultado;
            Log($"[FallbackRenderer] {renderer.NomeRenderer} falhou: [{falhou.CodigoErro}] {falhou.MensagemErro}");
            ultimaFalha = resultado;
            viouFallback = true;
        }

        // Todos os renderers falharam ou estavam indisponíveis.
        return ultimaFalha ?? new PdfRenderResult.Falhou(
            "Nenhum renderer disponível na cadeia de fallback.",
            "FALLBACK_NO_RENDERER",
            new PdfRenderMetrics
            {
                NomeRenderer = NomeRenderer,
                DpiRealizado = request.DpiNormalizado,
                DuracaoMs = 0,
                ViouFallback = false,
                OcorreuEmUtc = DateTimeOffset.UtcNow
            });
    }

    private void Log(string mensagem)
    {
        try { _onLog?.Invoke(mensagem); } catch { /* fire-and-forget: log jamais para execução */ }
    }
}
