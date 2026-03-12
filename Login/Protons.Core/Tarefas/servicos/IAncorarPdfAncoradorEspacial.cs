using Protons.Core.Tarefas.Models;

namespace Protons.Core.Tarefas.Services;

// Ancora variáveis nos bboxes definidos no template, filtrando palavras por interseção espacial.
// Retorna uma SaidaVariavelItem por âncora do template, com texto, normalização e confiança.
public interface IAncorarPdfAncoradorEspacial
{
    IReadOnlyList<SaidaVariavelItem> Ancorar(
        IReadOnlyList<PdfPaginaTexto> paginas,
        IReadOnlyList<AncorarPdfTemplateAncora> ancoras);
}
