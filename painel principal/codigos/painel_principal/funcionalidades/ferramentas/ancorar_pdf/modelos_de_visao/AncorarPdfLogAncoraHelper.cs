using System.Globalization;
using System.Text;
using Protons.Core.Tarefas.Models;

namespace Protons.UI.Painel.Funcionalidades.Ferramentas.AncorarPdf.ModelosDeVisao;

/// <summary>
/// Helper para formatação de dados de âncora em logs operacionais (log_ops.jsonl).
/// Permite análise de testes sem print: o que o usuário ancorou, coordenadas, tipo.
/// </summary>
internal static class AncorarPdfLogAncoraHelper
{
    public static string FormatarContextoAncora(AncorarPdfAncoraItemViewModel ancora, string? cor)
    {
        var nome = Sanitizar(ancora.NomeExibido, 40);
        var chave = Sanitizar(ancora.ChaveTecnica, 40);
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(cor))
            sb.Append($"cor={cor} ");
        sb.Append($"nome={nome} chave={chave} pagina={ancora.Pagina} ");
        sb.Append($"xRel={ancora.XRel.ToString("F3", CultureInfo.InvariantCulture)} yRel={ancora.YRel.ToString("F3", CultureInfo.InvariantCulture)} ");
        sb.Append($"wRel={ancora.LarguraRel.ToString("F3", CultureInfo.InvariantCulture)} hRel={ancora.AlturaRel.ToString("F3", CultureInfo.InvariantCulture)} ");
        sb.Append($"modo={ancora.ModoAncora} tipo={Sanitizar(ancora.TipoEsperado, 20)}");
        if (!string.IsNullOrWhiteSpace(ancora.TextoAncora))
            sb.Append($" textoAncora={Sanitizar(ancora.TextoAncora, 30)}");
        return sb.ToString();
    }

    public static string Sanitizar(string? valor, int maxLen = 60)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return "_";
        var s = valor.Trim().Replace(' ', '_');
        return s.Length > maxLen ? s[..maxLen] + "…" : s;
    }
}
