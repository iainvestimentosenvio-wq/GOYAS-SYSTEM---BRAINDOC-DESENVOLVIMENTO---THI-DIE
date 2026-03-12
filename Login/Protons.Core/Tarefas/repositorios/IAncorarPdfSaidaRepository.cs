using Protons.Core.Tarefas.Models;

namespace Protons.Core.Tarefas.Repositories;

// Repositorio responsavel pela persistencia e consulta de saidas consolidadas
// de ancoragem — contrato de saida para consumo por tarefas e integrações futuras.
//
// Invariantes obrigatorias:
// - Nunca sobrescrever payload antigo: cada execucao gera novo registro.
// - PayloadHashSha256 deve ser calculado antes de chamar Salvar.
// - SchemaVersion deve ser explicitamente definido no payload.
public interface IAncorarPdfSaidaRepository
{
    // Salva nova saida consolidada.
    // Idempotente: se SaidaId ja existe, nao faz nada e retorna false.
    bool Salvar(SaidaVariavelAncorada saida);

    // Busca saida por ID unico.
    SaidaVariavelAncorada? ObterPorSaidaId(string saidaId);

    // Busca saidas por ID de execucao (pode retornar multiplas se houver varios arquivos no ciclo).
    IReadOnlyList<SaidaVariavelAncorada> ListarPorExecucaoId(string execucaoId);

    // Consulta por cliente/tarefa/faixa de data — principal hot path de consumo.
    IReadOnlyList<SaidaVariavelAncorada> Listar(AncorarPdfSaidasFiltro filtro);

    // Verifica se ja existe saida com o mesmo hash de payload — deduplicacao por conteudo.
    bool ExisteSaidaComHash(int tarefaId, string payloadHashSha256);
}
