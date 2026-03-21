using System;
using System.Globalization;
using System.Text;

namespace Protons.UI.Painel.Funcionalidades.Ferramentas.AncorarPdf.Dominio;

/// <summary>Validação de documento e nome para matching de cliente em PDFs.</summary>
public sealed class AncorarPdfValidacaoClientePolicy
{
    public static bool ValidarDocumentoENome(
        string? documentoEsperado,
        string? nomeEsperado,
        string? documentoEncontrado,
        string? nomeEncontrado)
    {
        var esperadoNormalizado = NormalizarDocumento(documentoEsperado);
        var encontradoNormalizado = NormalizarDocumento(documentoEncontrado);

        if (string.IsNullOrWhiteSpace(esperadoNormalizado) || string.IsNullOrWhiteSpace(encontradoNormalizado))
            return false;

        if (!string.Equals(esperadoNormalizado, encontradoNormalizado, StringComparison.Ordinal))
            return false;

        var nomeEsperadoNormalizado = NormalizarTexto(nomeEsperado);
        var nomeEncontradoNormalizado = NormalizarTexto(nomeEncontrado);
        if (string.IsNullOrWhiteSpace(nomeEsperadoNormalizado) || string.IsNullOrWhiteSpace(nomeEncontradoNormalizado))
            return false;

        return nomeEncontradoNormalizado.Contains(nomeEsperadoNormalizado, StringComparison.OrdinalIgnoreCase) ||
               nomeEsperadoNormalizado.Contains(nomeEncontradoNormalizado, StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizarDocumento(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return string.Empty;

        var sb = new StringBuilder(valor.Length);
        foreach (var ch in valor)
        {
            if (char.IsDigit(ch))
                sb.Append(ch);
        }

        return sb.ToString();
    }

    public static string NormalizarTexto(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return string.Empty;

        var decomposed = valor.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);

        foreach (var ch in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}

/// <summary>Política de detecção de misfire (atraso) em execuções agendadas.</summary>
public static class AncorarPdfMisfireBacklogPolicy
{
    private static readonly TimeSpan LimiarPadraoMisfire = TimeSpan.FromSeconds(15);

    public static bool DetectarMisfire(DateTime centroAnteriorUtc, DateTime centroAtualUtc, out TimeSpan atraso)
    {
        atraso = centroAtualUtc - centroAnteriorUtc;
        return atraso > LimiarPadraoMisfire;
    }

    public static string CriarResumoBacklog(int totalPendencias, TimeSpan atraso)
    {
        var segundos = Math.Max(0, (int)Math.Round(atraso.TotalSeconds, MidpointRounding.AwayFromZero));
        return $"Backlog detectado: {totalPendencias} tarefa(s) pendente(s) apos atraso de {segundos}s.";
    }
}
