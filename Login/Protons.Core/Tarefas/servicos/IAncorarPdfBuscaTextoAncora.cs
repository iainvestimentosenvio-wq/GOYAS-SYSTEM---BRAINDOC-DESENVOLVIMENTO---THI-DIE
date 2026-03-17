using Protons.Core.Tarefas.Models;

namespace Protons.Core.Tarefas.Services;

/// <summary>
/// Busca um fragmento de texto na página por correspondência exata ou fuzzy.
/// Suporta seleção de N-ésima ocorrência para labels repetidos.
/// </summary>
public interface IAncorarPdfBuscaTextoAncora
{
    /// <summary>
    /// Busca o texto na página e retorna o bbox da primeira ocorrência, ou null se não encontrar.
    /// </summary>
    BboxRelativo? Buscar(PdfPaginaTexto pagina, string textoAncora);

    /// <summary>
    /// Busca a N-ésima ocorrência do texto na página.
    /// Tenta match exato primeiro, depois fuzzy (similaridade ≥ 70%).
    /// </summary>
    BboxRelativo? Buscar(PdfPaginaTexto pagina, string textoAncora, int ocorrencia);
}
