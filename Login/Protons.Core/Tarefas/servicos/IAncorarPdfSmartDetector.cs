using Protons.Core.Tarefas.Models;

namespace Protons.Core.Tarefas.Services;

/// <summary>
/// Detector automático de tipo de variável PDF via clique.
/// Algoritmo puro (sem I/O): recebe as páginas já extraídas e as coordenadas do clique.
/// Identifica o grupo de palavras sob o cursor, classifica o tipo e sugere chave/nome.
/// </summary>
public interface IAncorarPdfSmartDetector
{
    /// <summary>
    /// Detecta o tipo de variável no ponto de clique e retorna sugestão completa de âncora.
    /// </summary>
    /// <param name="paginas">Páginas extraídas pelo IAncorarPdfExtratorTexto.</param>
    /// <param name="entrada">Coordenadas relativas do clique e número da página.</param>
    /// <returns>Resultado com tipo, bbox, normalização e sugestão de chave/nome.</returns>
    SmartDeteccaoResultado Detectar(
        IReadOnlyList<PdfPaginaTexto> paginas,
        SmartDeteccaoEntrada entrada);
}
