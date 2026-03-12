namespace Protons.Core.Tarefas.Models;

public enum AncorarPdfFilaStatus
{
    Aguardando = 0,
    EmProcessamento = 1,
    Concluido = 2,
    Falhou = 3,
    Cancelado = 4
}

public enum AncorarPdfFilaFalhaCategoria
{
    Nenhuma = 0,
    Tecnica = 1,
    Negocio = 2
}

public sealed record AncorarPdfFilaItem
{
    public string FilaItemId { get; init; } = string.Empty;
    public int TarefaId { get; init; }
    public int ClienteId { get; init; }
    public string CicloId { get; init; } = string.Empty;
    public DateTime JanelaAlvoUtc { get; init; }
    public int PrioridadeExecucao { get; init; } = 3;
    public AncorarPdfFilaStatus Status { get; init; } = AncorarPdfFilaStatus.Aguardando;
    public string Motivo { get; init; } = "scheduler_dispatch";
    public int EnfileiradoPorUserId { get; init; }
    public string EnfileiradoPorNome { get; init; } = string.Empty;
    public DateTime EnfileiradoEmUtc { get; init; }
    public DateTime? IniciadoEmUtc { get; init; }
    public DateTime? FinalizadoEmUtc { get; init; }
    public int TentativaAtual { get; init; }
    public int TentativasMaximas { get; init; } = 3;
    public AncorarPdfFilaFalhaCategoria? CategoriaFalha { get; init; }
    public string? ErroCodigo { get; init; }
    public string? ErroDetalhe { get; init; }
    public string? CanceladoPorNome { get; init; }
    public DateTime? CanceladoEmUtc { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
    public DateTime CriadoEmUtc { get; init; }

    /// <summary>TraceId OTel para propagação cross-thread (channel). Não persistido.</summary>
    public string? TraceId { get; init; }
    /// <summary>SpanId OTel para propagação cross-thread (channel). Não persistido.</summary>
    public string? SpanId { get; init; }
}

public sealed record AncorarPdfExecucaoLease
{
    public string LeaseId { get; init; } = string.Empty;
    public string FilaItemId { get; init; } = string.Empty;
    public int TarefaId { get; init; }
    public int ClienteId { get; init; }
    public string WorkerId { get; init; } = string.Empty;
    public DateTime AcquiredAtUtc { get; init; }
    public DateTime ExpiresAtUtc { get; init; }
    public bool Ativa { get; init; } = true;
    public string? MotivoLiberacao { get; init; }
    public DateTime? LiberadaEmUtc { get; init; }
}

public sealed record AncorarPdfFilaEnfileirarEntrada(
    int TarefaId,
    int ClienteId,
    string CicloId,
    DateTime JanelaAlvoUtc,
    int PrioridadeExecucao,
    string Motivo,
    int EnfileiradoPorUserId,
    string EnfileiradoPorNome,
    string CorrelationId);

public sealed record AncorarPdfFilaFiltro(
    int? ClienteId = null,
    int? TarefaId = null,
    AncorarPdfFilaStatus? Status = null,
    DateTime? DataInicioUtc = null,
    DateTime? DataFimUtc = null,
    int Limite = 50,
    int Offset = 0);

public sealed record AncorarPdfFilaEnfileirarResultado(
    bool Enfileirado,
    string? FilaItemId,
    string? MotivoRejeicao = null);

public sealed record AncorarPdfFilaCancelarResultado(
    bool Cancelado,
    string? MotivoFalha = null);
