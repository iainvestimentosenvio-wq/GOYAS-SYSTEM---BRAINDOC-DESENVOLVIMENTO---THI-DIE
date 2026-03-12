using Protons.Infrastructure.Tarefas.Services;
using Xunit;
using Xunit.Sdk;

namespace Protons.Infrastructure.Tests.Integration;

internal static class InfraTestPreconditions
{
    private const string FrePdfPathPadrao =
        "/srv/DocumentosCompartilhados/CAIAPONIA/PDF CAIAPONIA 25102025/FRE CAIAPONIA  27102025 SEM MACRO (1).pdf";

    private const string EnvFrePdfPath = "PROTONS_FRE_PDF_PATH";
    private const string EnvRequireFrePdf = "PROTONS_CI_REQUIRE_FRE_PDF";
    private const string EnvRequirePreviewRenderers = "PROTONS_CI_REQUIRE_PREVIEW_RENDERERS";

    public static string ObterFrePdfPath()
    {
        var envPath = Environment.GetEnvironmentVariable(EnvFrePdfPath);
        return string.IsNullOrWhiteSpace(envPath) ? FrePdfPathPadrao : envPath.Trim();
    }

    public static bool DeveExigirFrePdfEmCi() => FlagHabilitada(EnvRequireFrePdf);

    public static bool DeveExigirRenderersPreviewEmCi() => FlagHabilitada(EnvRequirePreviewRenderers);

    public static string ObterFrePdfPathOuFalhar()
    {
        var path = ObterFrePdfPath();
        if (File.Exists(path))
            return path;

        throw new XunitException(
            $"Artefato obrigatorio ausente: FRE PDF nao encontrado em '{path}'. " +
            $"Defina {EnvFrePdfPath} para o caminho correto.");
    }

    public static void GarantirDocnetDisponivelOuFalhar()
    {
        var disponivel = false;
        try
        {
            disponivel = new AncorarPdfDocnetRenderer().Disponivel;
        }
        catch
        {
            // Sem rethrow: falha sera reportada com mensagem padronizada abaixo.
        }

        if (!disponivel)
        {
            throw new XunitException(
                "Renderer Docnet.PDFium indisponivel no ambiente. " +
                "Instale/valide dependencias nativas do PDFium ou execute fora do modo CI estrito.");
        }
    }

    public static void GarantirGhostscriptDisponivelOuFalhar()
    {
        var disponivel = false;
        try
        {
            disponivel = new AncorarPdfGhostscriptRenderer().Disponivel;
        }
        catch
        {
            // Sem rethrow: falha sera reportada com mensagem padronizada abaixo.
        }

        if (!disponivel)
        {
            throw new XunitException(
                "Renderer Ghostscript indisponivel no ambiente. " +
                "Instale 'gs' no host ou execute fora do modo CI estrito.");
        }
    }

    private static bool FlagHabilitada(string envVar)
    {
        var raw = Environment.GetEnvironmentVariable(envVar);
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        return string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase)
               || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)
               || string.Equals(raw, "yes", StringComparison.OrdinalIgnoreCase)
               || string.Equals(raw, "on", StringComparison.OrdinalIgnoreCase);
    }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class RequiresFrePdfFactAttribute : FactAttribute
{
    public RequiresFrePdfFactAttribute()
    {
        var path = InfraTestPreconditions.ObterFrePdfPath();
        if (File.Exists(path))
            return;

        if (InfraTestPreconditions.DeveExigirFrePdfEmCi())
            return;

        Skip =
            $"Precondicao ausente: FRE PDF nao encontrado em '{path}'. " +
            "Defina PROTONS_FRE_PDF_PATH ou habilite o artefato no ambiente.";
    }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class RequiresDocnetRendererFactAttribute : FactAttribute
{
    public RequiresDocnetRendererFactAttribute()
    {
        var disponivel = false;
        try
        {
            disponivel = new AncorarPdfDocnetRenderer().Disponivel;
        }
        catch
        {
            disponivel = false;
        }

        if (disponivel)
            return;

        if (InfraTestPreconditions.DeveExigirRenderersPreviewEmCi())
            return;

        Skip =
            "Precondicao ausente: Docnet.PDFium indisponivel no ambiente atual. " +
            "Instale/valide dependencias nativas ou rode com PROTONS_CI_REQUIRE_PREVIEW_RENDERERS=1 para falha explicita.";
    }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class RequiresGhostscriptRendererFactAttribute : FactAttribute
{
    public RequiresGhostscriptRendererFactAttribute()
    {
        var disponivel = false;
        try
        {
            disponivel = new AncorarPdfGhostscriptRenderer().Disponivel;
        }
        catch
        {
            disponivel = false;
        }

        if (disponivel)
            return;

        if (InfraTestPreconditions.DeveExigirRenderersPreviewEmCi())
            return;

        Skip =
            "Precondicao ausente: Ghostscript ('gs') indisponivel no ambiente atual. " +
            "Instale/valide o executavel ou rode com PROTONS_CI_REQUIRE_PREVIEW_RENDERERS=1 para falha explicita.";
    }
}
