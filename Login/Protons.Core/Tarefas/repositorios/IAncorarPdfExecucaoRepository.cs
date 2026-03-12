using Protons.Core.Tarefas.Models;

namespace Protons.Core.Tarefas.Repositories;

// Repositorio responsavel pelo lifecycle de execucao de ciclos da ferramenta ancorar_pdf.
// Todas as operacoes de escrita sao atomicas e garantem idempotencia por chave de ciclo.
public interface IAncorarPdfExecucaoRepository
{
    // --- Idempotencia por arquivo/ciclo ---

    // Tenta reservar o processamento de um arquivo num ciclo.
    // Retorna Reservado=true somente se a insercao foi bem-sucedida (nao existia ainda).
    // Implementacao usa INSERT ... ON CONFLICT DO NOTHING para atomicidade.
    AncorarPdfReservaIdempotencia TentarReservarProcessamento(
        int tarefaId,
        string cicloId,
        string nomeEsperadoLogico,
        string arquivoHash,
        string arquivoPath,
        long tamanhoBytes,
        DateTime mtimeUtc);

    // --- Lifecycle da execucao ---

    // Cria uma nova execucao com status inicial.
    // Retorna false se ja existir execucao com mesmo ExecucaoId (idempotente).
    bool CriarExecucao(TarefaAncorarPdfExecucao execucao);

    // Atualiza o status de uma execucao existente.
    void AtualizarStatus(
        string execucaoId,
        string novoStatus,
        DateTime? iniciadaEmUtc = null,
        DateTime? finalizadaEmUtc = null,
        string? erroCodigo = null,
        string? erroDetalhe = null);

    // Salva execucao + resultados + saida em uma unica transacao atomica.
    // Garante que ou tudo persiste ou nada persiste.
    void SalvarExecucaoCompleta(AncorarPdfSalvarExecucaoEntrada entrada);

    // --- Consultas ---

    TarefaAncorarPdfExecucao? ObterPorExecucaoId(string execucaoId);

    IReadOnlyList<TarefaAncorarPdfExecucao> Listar(AncorarPdfExecucoesFiltro filtro);

    // Lista execucoes com status Agendada ou Executando para um cliente — hot path do scheduler.
    // Em Postgres: usa FOR UPDATE SKIP LOCKED para exclusao mutua entre workers.
    IReadOnlyList<TarefaAncorarPdfExecucao> ListarPendentesParaExecucao(int clienteId, int limite = 10);

    // Verifica se um arquivo ja foi processado em qualquer ciclo da tarefa (por hash).
    bool ArquivoJaProcessado(int tarefaId, string arquivoHash, string cicloId);
}
