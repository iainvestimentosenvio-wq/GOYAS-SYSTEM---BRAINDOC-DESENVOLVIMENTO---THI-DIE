using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Protons.Core.Tarefas.Services;

namespace Protons.Infrastructure.Tarefas.Services;

/// <summary>
/// Renderer baseado no Ghostscript (executável externo <c>gs</c>).
/// <para>
/// Vantagens: antialiasing de texto de alta qualidade (TextAlphaBits=4, GraphicsAlphaBits=4),
/// ampla compatibilidade com PDFs protegidos por senha de impressão.
/// Desvantagens: requer instalação no host, latência de spawn de processo.
/// </para>
/// </summary>
public sealed class AncorarPdfGhostscriptRenderer : IPdfPreviewRenderer
{
    private static readonly string GsExecutable = ResolverExecutavelGs();

    private static string ResolverExecutavelGs()
    {
        if (!OperatingSystem.IsWindows())
            return "gs";

        foreach (var nome in new[] { "gswin64c", "gswin32c", "gs" })
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = nome,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p is not null && p.WaitForExit(2000) && p.ExitCode == 0)
                    return nome;
            }
            catch (Exception) { }
        }

        return "gs";
    }

    private static readonly PdfRendererCapabilities _capabilities = new(
        SuportaLinux: true,
        SuportaWindows: true,
        SuportaMacOs: true,
        DpiMinimo: 72,
        DpiMaximo: 600,
        RequereExternoInstalado: true,
        ExternoNecessario: "gs");

    public string NomeRenderer => "Ghostscript";
    public PdfRendererCapabilities Capabilities => _capabilities;

    /// <summary>
    /// Verifica se o executável <c>gs</c> está disponível no PATH.
    /// Executa <c>gs --version</c> com timeout de 2 s.
    /// </summary>
    public bool Disponivel
    {
        get
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = GsExecutable,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p is null) return false;
                bool exited = p.WaitForExit(2000);
                return exited && p.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <inheritdoc/>
    public async Task<PdfRenderResult> RenderizarAsync(
        PdfRenderRequest request,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var utcInicio = DateTimeOffset.UtcNow;
        int dpi = request.DpiNormalizado;

        if (!File.Exists(request.PdfPath))
        {
            return Falhou("Arquivo não encontrado.", "GS_FILE_NOT_FOUND", dpi, sw.ElapsedMilliseconds, utcInicio);
        }

        var tempFile = Path.Combine(Path.GetTempPath(), $"protons_gs_{Guid.NewGuid():N}.png");
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = GsExecutable,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-dNOPAUSE");
            psi.ArgumentList.Add("-dBATCH");
            psi.ArgumentList.Add("-sDEVICE=png16m");
            psi.ArgumentList.Add($"-r{dpi}");
            psi.ArgumentList.Add("-dTextAlphaBits=4");
            psi.ArgumentList.Add("-dGraphicsAlphaBits=4");
            psi.ArgumentList.Add($"-dFirstPage={request.Pagina}");
            psi.ArgumentList.Add($"-dLastPage={request.Pagina}");
            psi.ArgumentList.Add($"-sOutputFile={tempFile}");
            psi.ArgumentList.Add(request.PdfPath);

            using var process = Process.Start(psi)!;
            await process.WaitForExitAsync(ct);

            if (ct.IsCancellationRequested)
                return Falhou("Cancelado pelo chamador.", "GS_CANCELLED", dpi, sw.ElapsedMilliseconds, utcInicio);

            if (process.ExitCode != 0 || !File.Exists(tempFile))
            {
                return Falhou(
                    $"Ghostscript encerrou com código {process.ExitCode}. Verifique se o PDF não está protegido.",
                    "GS_EXIT_ERROR",
                    dpi, sw.ElapsedMilliseconds, utcInicio);
            }

            var bytes = await File.ReadAllBytesAsync(tempFile, CancellationToken.None);
            var ms = new MemoryStream(bytes, writable: false);
            sw.Stop();

            return new PdfRenderResult.Sucedido(ms, new PdfRenderMetrics
            {
                NomeRenderer = NomeRenderer,
                DpiRealizado = dpi,
                DuracaoMs = sw.ElapsedMilliseconds,
                ViouFallback = false,
                OcorreuEmUtc = utcInicio
            });
        }
        catch (OperationCanceledException)
        {
            return Falhou("Cancelado pelo chamador.", "GS_CANCELLED", dpi, sw.ElapsedMilliseconds, utcInicio);
        }
        catch (Exception ex)
        {
            return Falhou($"Erro inesperado: {ex.Message}", "GS_EXCEPTION", dpi, sw.ElapsedMilliseconds, utcInicio);
        }
        finally
        {
            if (File.Exists(tempFile))
                try { File.Delete(tempFile); } catch { /* best-effort */ }
        }
    }

    private PdfRenderResult.Falhou Falhou(
        string mensagem, string codigo, int dpi, long ms, DateTimeOffset utc) =>
        new(mensagem, codigo, new PdfRenderMetrics
        {
            NomeRenderer = NomeRenderer,
            DpiRealizado = dpi,
            DuracaoMs = ms,
            ViouFallback = false,
            OcorreuEmUtc = utc
        });
}
