namespace Protons.Core.Tarefas.Models;

/// <summary>Etapas do wizard de agendamento AncorarPdf (criação via drop na régua).</summary>
public enum AncorarPdfEtapaFluxo
{
    Basico = 0,
    AncorasTelaCheia = 1,
    Confirmacao = 2
}

/// <summary>
/// Snapshot das âncoras coletadas no Passo 2, antes de persistir.
/// Mantido em memória no wizard até o Confirmar do Passo 3.
/// </summary>
public sealed record AncorarPdfFluxoEstadoAncoras(
    IReadOnlyList<AncorarPdfTemplateAncora> Ancoras,
    string PdfModeloPath);
