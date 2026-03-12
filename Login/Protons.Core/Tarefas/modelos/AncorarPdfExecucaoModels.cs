using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Protons.Core.Tarefas.Models;

// Coordenada relativa normalizada (0.0 a 1.0) em relacao a dimensao da pagina.
public sealed record BboxRelativo(
    double X,
    double Y,
    double Largura,
    double Altura);

// Uma variavel extraida e normalizada dentro de um ciclo de execucao.
public sealed record SaidaVariavelItem
{
    public string Chave { get; init; } = string.Empty;
    public string Tipo { get; init; } = "texto";
    public string ValorBruto { get; init; } = string.Empty;
    public string ValorNormalizado { get; init; } = string.Empty;
    public double Confianca { get; init; }
    public string CorTemplate { get; init; } = string.Empty;
    public int Pagina { get; init; } = 1;
    public BboxRelativo? BboxRelativo { get; init; }
}

// Payload consolidado de saida por arquivo/ciclo — pronto para consumo por tarefas futuras.
// Nunca sobrescrever: criar novo registro por execucao.
public sealed record SaidaVariavelAncorada
{
    public string SaidaId { get; init; } = string.Empty;
    public string ExecucaoId { get; init; } = string.Empty;
    public int TarefaId { get; init; }
    public int ClienteId { get; init; }

    // Arquivo processado.
    public string ArquivoPath { get; init; } = string.Empty;
    public string ArquivoHash { get; init; } = string.Empty;
    public string ArquivoNomeLogico { get; init; } = string.Empty;

    // Momento da execucao.
    public DateTime DataExecucaoUtc { get; init; }

    // Versao do schema do payload para versionamento de contrato.
    public int SchemaVersion { get; init; } = 1;

    // Variaveis extraidas do PDF.
    public IReadOnlyList<SaidaVariavelItem> Variaveis { get; init; } = [];

    // Hash SHA-256 deterministico do payload JSON para deduplicacao e auditoria.
    // Calculado externamente via AncorarPdfPayloadHash.Computar(saida).
    public string PayloadHashSha256 { get; init; } = string.Empty;

    public DateTime CriadoEmUtc { get; init; }
}

// Resultado de uma variavel individual extraida em um ciclo — granularidade para debug e reprocessamento.
public sealed record ResultadoAncoraVariavel
{
    public string ResultadoId { get; init; } = string.Empty;
    public string ExecucaoId { get; init; } = string.Empty;
    public int TarefaId { get; init; }
    public int ClienteId { get; init; }
    public string ArquivoPath { get; init; } = string.Empty;
    public string ArquivoHash { get; init; } = string.Empty;
    public string Chave { get; init; } = string.Empty;
    public string ValorBruto { get; init; } = string.Empty;
    public string ValorNormalizado { get; init; } = string.Empty;
    public string Tipo { get; init; } = "texto";
    public string CorTemplate { get; init; } = string.Empty;
    public double Confianca { get; init; }
    public int Pagina { get; init; } = 1;
    public string? BboxRelativoJson { get; init; }
}

// Execucao de um ciclo completo da tarefa ancorar_pdf.
// Status lifecycle: Agendada -> Executando -> Concluida | Falhou | Cancelada
// Atrasada e um estado de alerta (misfire), nao terminal.
public sealed record TarefaAncorarPdfExecucao
{
    public string ExecucaoId { get; init; } = string.Empty;
    public int TarefaId { get; init; }
    public int ClienteId { get; init; }
    public int EsteiraId { get; init; }

    // Identificador do ciclo logico (ex: data do agendamento, formato ISO date).
    public string CicloId { get; init; } = string.Empty;

    // Janela de tempo alvo para este ciclo (UTC).
    public DateTime JanelaAlvoUtc { get; init; }

    // Timestamps do ciclo de vida.
    public DateTime? IniciadaEmUtc { get; init; }
    public DateTime? FinalizadaEmUtc { get; init; }

    // Status atual (ver enum AncorarPdfExecucaoStatus em AncorarPdfContratos.cs do UI).
    public string Status { get; init; } = "Agendada";

    // Erro tecnico (apenas para falhas tecnicas, nao de regra de negocio).
    public string? ErroCodigo { get; init; }
    public string? ErroDetalhe { get; init; }

    // Indica se o disparo ocorreu com atraso (misfire).
    public bool ExecutadaComAtraso { get; init; }

    // Rastreabilidade: quem programou e quem executou.
    public int ProgramadoPorUserId { get; init; }
    public string ProgramadoPorNome { get; init; } = string.Empty;
    public DateTime ProgramadoEmUtc { get; init; }
    public int? ExecutadoPorUserId { get; init; }

    // Correlation ID para rastreio distribuido (OTel/logging).
    public string CorrelationId { get; init; } = string.Empty;

    public DateTime CriadoEmUtc { get; init; }
}

// Resultado de tentativa de reserva de idempotencia.
// Permite ao chamador decidir se deve processar ou ignorar o arquivo.
public sealed record AncorarPdfReservaIdempotencia(
    bool Reservado,
    string NomeEsperadoLogico,
    string ArquivoHash,
    string ArquivoPath);

// Entrada para salvar execucao completa de forma transacional.
public sealed record AncorarPdfSalvarExecucaoEntrada
{
    public TarefaAncorarPdfExecucao Execucao { get; init; } = new();
    public IReadOnlyList<ResultadoAncoraVariavel> Resultados { get; init; } = [];
    public SaidaVariavelAncorada? Saida { get; init; }
}

// Utilitario para hash deterministico de payload (RFC 8785 simplificado).
// Serializa o payload em JSON com propriedades ordenadas alfabeticamente
// e computa SHA-256 do resultado para garantir deduplicacao e integridade.
public static class AncorarPdfPayloadHash
{
    private static readonly JsonSerializerOptions OpcoesOrdenadas = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // Hash canônico V2 (default): exclui campos voláteis de execução
    // (ExecucaoId e DataExecucaoUtc) para deduplicação semântica por conteúdo.
    public static string Computar(SaidaVariavelAncorada saida)
        => ComputarV2(saida);

    public static string ComputarV2(SaidaVariavelAncorada saida)
    {
        // Monta payload canonico para hash: exclui os proprios campos de hash e metadata de banco.
        var payload = new
        {
            schemaVersion = saida.SchemaVersion,
            tarefaId = saida.TarefaId,
            clienteId = saida.ClienteId,
            arquivo = new
            {
                path = saida.ArquivoPath,
                hash = saida.ArquivoHash,
                nomeLogico = saida.ArquivoNomeLogico
            },
            variaveis = saida.Variaveis
                .OrderBy(v => v.Chave, StringComparer.Ordinal)
                .Select(v => new
                {
                    chave = v.Chave,
                    tipo = v.Tipo,
                    valorBruto = v.ValorBruto,
                    valorNormalizado = v.ValorNormalizado,
                    confianca = v.Confianca,
                    pagina = v.Pagina,
                    corTemplate = v.CorTemplate
                })
                .ToArray()
        };

        return ComputarHashHex(payload);
    }

    // Compatibilidade histórica com hashes já persistidos antes do V2.
    public static string ComputarV1Legado(SaidaVariavelAncorada saida)
    {
        var payload = new
        {
            schemaVersion = saida.SchemaVersion,
            tarefaId = saida.TarefaId,
            execucaoId = saida.ExecucaoId,
            clienteId = saida.ClienteId,
            arquivo = new
            {
                path = saida.ArquivoPath,
                hash = saida.ArquivoHash,
                nomeLogico = saida.ArquivoNomeLogico
            },
            execucao = new { dataUtc = saida.DataExecucaoUtc.ToString("o") },
            variaveis = saida.Variaveis
                .OrderBy(v => v.Chave, StringComparer.Ordinal)
                .Select(v => new
                {
                    chave = v.Chave,
                    tipo = v.Tipo,
                    valorBruto = v.ValorBruto,
                    valorNormalizado = v.ValorNormalizado,
                    confianca = v.Confianca,
                    pagina = v.Pagina,
                    corTemplate = v.CorTemplate
                })
                .ToArray()
        };

        return ComputarHashHex(payload);
    }

    private static string ComputarHashHex<T>(T payload)
    {
        var json = JsonSerializer.Serialize(payload, OpcoesOrdenadas);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

// Filtros para consulta de execucoes por cliente/tarefa/faixa de data.
public sealed record AncorarPdfExecucoesFiltro
{
    public int ClienteId { get; init; }
    public int? TarefaId { get; init; }
    public DateTime? DataInicioUtc { get; init; }
    public DateTime? DataFimUtc { get; init; }
    public string? Status { get; init; }
    public int Limite { get; init; } = 50;
    public int Offset { get; init; } = 0;
}

// Filtros para consulta de saidas por cliente/tarefa/faixa de data.
public sealed record AncorarPdfSaidasFiltro
{
    public int ClienteId { get; init; }
    public int? TarefaId { get; init; }
    public DateTime? DataInicioUtc { get; init; }
    public DateTime? DataFimUtc { get; init; }
    public int Limite { get; init; } = 50;
    public int Offset { get; init; } = 0;
}
