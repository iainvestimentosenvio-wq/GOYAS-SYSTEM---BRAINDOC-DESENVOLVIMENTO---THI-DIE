using Protons.Core.Tarefas.Models;

namespace Protons.Core.Tarefas.Services;

/// <summary>
/// Busca um fragmento de texto na página e retorna o bbox da primeira ocorrência.
/// Usado para âncoras por texto (extrair conteúdo relativo a um marcador).
/// </summary>
public interface IAncorarPdfBuscaTextoAncora
{
    /// <summary>
    /// Busca o texto na página e retorna o bbox da primeira ocorrência, ou null se não encontrar.
    /// </summary>
    BboxRelativo? Buscar(PdfPaginaTexto pagina, string textoAncora);
}
