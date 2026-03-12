using Protons.Core.Tarefas.Models;

namespace Protons.Core.Tarefas.Services;

// Descobre e seleciona o melhor arquivo PDF em uma pasta por similaridade de nome.
// Verifica estabilidade (mtime estável) e computa hash SHA-256 do arquivo selecionado.
// Retorna null se nenhum candidato atinge o limiar de similaridade.
public interface IAncorarPdfSeletorArquivo
{
    Task<SelecaoArquivoResultado?> SelecionarMelhorAsync(
        string pastaPath,
        string nomeReferencia,
        double limiarSimilaridade,
        bool monitorarSubpastas,
        string cicloId,
        CancellationToken ct);
}
