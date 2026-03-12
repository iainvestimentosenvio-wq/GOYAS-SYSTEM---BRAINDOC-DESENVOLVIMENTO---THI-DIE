using System.Globalization;

namespace Protons.Core.Tarefas.Models;

public static class FerramentaTarefaIds
{
    public const string Generica = "generica";
    public const string AncorarPdfCanonico = "ancorar_pdf";

    public static bool EhAncorarPdf(string? ferramentaId)
    {
        if (string.IsNullOrWhiteSpace(ferramentaId))
            return false;

        return string.Equals(ferramentaId, AncorarPdfCanonico, StringComparison.OrdinalIgnoreCase);
    }

    public static string Canonicalizar(string? ferramentaId)
    {
        if (EhAncorarPdf(ferramentaId))
            return AncorarPdfCanonico;

        return string.IsNullOrWhiteSpace(ferramentaId)
            ? Generica
            : ferramentaId.Trim().ToLowerInvariant();
    }
}

public sealed record AncorarPdfTemplateMetadado
{
    public string NomeExibido { get; init; } = string.Empty;
    public string ChaveTecnica { get; init; } = string.Empty;
    public string TipoEsperado { get; init; } = "texto";
    public string? ExemploEsperado { get; init; }
    public string? RegraNormalizacao { get; init; }
}

public sealed record AncorarPdfTemplateAncora
{
    public int Ordem { get; init; }
    public string CorHex { get; init; } = "#4A90D9";
    public int Pagina { get; init; } = 1;
    public double XRel { get; init; }
    public double YRel { get; init; }
    public double LarguraRel { get; init; }
    public double AlturaRel { get; init; }
    public AncorarPdfTemplateMetadado Metadado { get; init; } = new();

    /// <summary>Modo de localização da região de extração.</summary>
    public AncorarPdfModoAncora ModoAncora { get; init; } = AncorarPdfModoAncora.RegiaoFixa;
    /// <summary>Texto a buscar na página (ex: "Valor:", "CPF:"). Usado quando ModoAncora != RegiaoFixa.</summary>
    public string? TextoAncora { get; init; }
    /// <summary>Largura relativa da região de extração (0.0-1.0) quando usa texto âncora. Default 0.2.</summary>
    public double LarguraExtracaoRel { get; init; } = 0.2;
    /// <summary>Altura relativa da região de extração (0.0-1.0) quando usa texto âncora. Default 0.05.</summary>
    public double AlturaExtracaoRel { get; init; } = 0.05;
}

/// <summary>Modo de localização da região de extração.</summary>
public enum AncorarPdfModoAncora
{
    /// <summary>Região fixa (comportamento atual: XRel, YRel, LarguraRel, AlturaRel).</summary>
    RegiaoFixa = 0,
    /// <summary>Buscar texto e extrair à direita.</summary>
    TextoADireita = 1,
    /// <summary>Buscar texto e extrair abaixo.</summary>
    TextoAbaixo = 2,
}

public enum AncorarPdfModoSelecao
{
    RetanguloLivre = 0,
    TextoExpandido = 1
}

public enum AncorarPdfDstHorarioInvalidoPolicy
{
    AvancarParaProximoHorarioValido = 0,
    RecuarParaHorarioValidoAnterior = 1,
    PularOcorrenciaComAuditoria = 2
}

public enum AncorarPdfDstHorarioAmbiguoPolicy
{
    PreferirOffsetMaisCedo = 0,
    PreferirOffsetMaisTarde = 1,
    ExecutarUmaVezNoOffsetMaisCedo = 2
}

public enum AncorarPdfBacklogStatus
{
    Pendente = 0,
    Executar = 1,
    Ignorar = 2
}

public sealed record AncorarPdfSchedulerAgendamentoAtivo
{
    public int TarefaId { get; init; }
    public int ClienteId { get; init; }
    public DateTime VencimentoUtc { get; init; }
    public DateTime CriadoEmUtc { get; init; }
    public string TimezoneId { get; init; } = "UTC";
    public int PrioridadeExecucao { get; init; } = 3;
    public AncorarPdfDstHorarioInvalidoPolicy DstHorarioInvalidoPolicy { get; init; } =
        AncorarPdfDstHorarioInvalidoPolicy.AvancarParaProximoHorarioValido;
    public AncorarPdfDstHorarioAmbiguoPolicy DstHorarioAmbiguoPolicy { get; init; } =
        AncorarPdfDstHorarioAmbiguoPolicy.PreferirOffsetMaisCedo;
}

public sealed record AncorarPdfSchedulerBacklogRegistro
{
    public int TarefaId { get; init; }
    public int ClienteId { get; init; }
    public DateTime JanelaAlvoUtc { get; init; }
    public int AtrasoSegundos { get; init; }
    public string Motivo { get; init; } = "misfire_offline";
    public DateTime DetectadoEmUtc { get; init; }
}

public sealed record AncorarPdfBacklogPendenteItem
{
    public long BacklogId { get; init; }
    public int TarefaId { get; init; }
    public int ClienteId { get; init; }
    public DateTime JanelaAlvoUtc { get; init; }
    public int AtrasoSegundos { get; init; }
    public string Motivo { get; init; } = "misfire_offline";
    public DateTime DetectadoEmUtc { get; init; }
    public AncorarPdfBacklogStatus Status { get; init; } = AncorarPdfBacklogStatus.Pendente;
}

public sealed record AncorarPdfBacklogDecisaoEntrada
{
    public long BacklogId { get; init; }
    public bool ExecutarPendentes { get; init; }
    public string? Observacao { get; init; }
}

/// <summary>
/// Registro de evento operacional do scheduler/motor/backlog.
/// Contrato canônico C7: todos os 6 eventos obrigatórios usam este record.
/// </summary>
public sealed record AncorarPdfSchedulerEventoRegistro
{
    public int TarefaId { get; init; }
    public int ClienteId { get; init; }

    /// <summary>
    /// Tipo canônico de evento — usar constantes em <see cref="AncorarPdfErroCodigos"/>:
    /// tarefa_agendada, tarefa_execucao_iniciada, tarefa_execucao_concluida,
    /// tarefa_execucao_falhou, tarefa_misfire_detectado, tarefa_backlog_decisao.
    /// </summary>
    public string TipoEvento { get; init; } = string.Empty;

    /// <summary>Texto livre de contexto adicional (sem PII — será sanitizado no OpsLogger).</summary>
    public string? Detalhes { get; init; }

    public DateTime OcorreuEmUtc { get; init; }

    // Campos adicionados em C7 para rastreabilidade e SLO completos.

    /// <summary>CorrelationId propagado da fila → motor → evento (formato Guid-N 32 chars).</summary>
    public string CorrelationId { get; init; } = string.Empty;

    /// <summary>Status da tarefa antes da transição (vazio para eventos sem transição).</summary>
    public string StatusAnterior { get; init; } = AncorarPdfErroCodigos.StatusDesconhecido;

    /// <summary>Status da tarefa após a transição.</summary>
    public string StatusNovo { get; init; } = AncorarPdfErroCodigos.StatusDesconhecido;

    /// <summary>
    /// Código de erro canônico — usar constantes ANCORA-NEG-* ou ANCORA-TEC-* em
    /// <see cref="AncorarPdfErroCodigos"/>. Vazio para eventos bem-sucedidos.
    /// </summary>
    public string ErroCodigo { get; init; } = AncorarPdfErroCodigos.Nenhum;

    /// <summary>True se a tarefa foi executada com atraso detectado (> limiar de misfire).</summary>
    public bool ExecutadaComAtraso { get; init; }
}

/// <summary>Item de histórico operacional retornado pela query de ListarEventosOperacionais.</summary>
public sealed record AncorarPdfEventoHistoricoItem
{
    public long Id { get; init; }
    public int TarefaId { get; init; }
    public int ClienteId { get; init; }
    public string TipoEvento { get; init; } = string.Empty;
    public string? Detalhes { get; init; }
    public DateTime OcorreuEmUtc { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
    public string StatusAnterior { get; init; } = string.Empty;
    public string StatusNovo { get; init; } = string.Empty;
    public string ErroCodigo { get; init; } = string.Empty;
    public bool ExecutadaComAtraso { get; init; }
}

/// <summary>Filtro para consulta de histórico operacional por cliente/tarefa/janela de tempo.</summary>
public sealed record AncorarPdfEventoHistoricoFiltro
{
    /// <summary>Obrigatório: filtra pelo cliente.</summary>
    public int ClienteId { get; init; }

    /// <summary>Opcional: filtra por tarefa específica.</summary>
    public int? TarefaId { get; init; }

    /// <summary>Opcional: filtra pelo tipo de evento canônico.</summary>
    public string? TipoEvento { get; init; }

    /// <summary>Opcional: início da janela de tempo (UTC).</summary>
    public DateTime? DataInicioUtc { get; init; }

    /// <summary>Opcional: fim da janela de tempo (UTC).</summary>
    public DateTime? DataFimUtc { get; init; }

    /// <summary>Número máximo de registros retornados (padrão 100, máximo 1000).</summary>
    public int Limite { get; init; } = 100;
}

public sealed record AncorarPdfConfiguracaoTarefa
{
    public int TarefaId { get; init; }
    public int ClienteId { get; init; }
    public int EsteiraId { get; init; }
    public string NomeTarefaPersonalizado { get; init; } = string.Empty;
    public string PastaMonitoradaPath { get; init; } = string.Empty;
    public string PdfModeloPath { get; init; } = string.Empty;
    public string NomeReferenciaArquivo { get; init; } = string.Empty;
    public bool MonitorarSubpastas { get; init; }
    public bool ValidacaoClienteAtiva { get; init; } = true;
    public double LimiarSimilaridadeNome { get; init; } = 0.75;
    public double HighlightOpacity { get; init; } = 0.40;
    public AncorarPdfModoSelecao ModoSelecao { get; init; } = AncorarPdfModoSelecao.RetanguloLivre;
    public bool PdfModeloCrossCliente { get; init; }
    public string? PdfModeloCrossClienteJustificativa { get; init; }
    public TarefaRecorrencia Recorrencia { get; init; } = TarefaRecorrencia.Nenhuma;
    public int AgendamentoSegundo { get; init; }
    public string TimezoneId { get; init; } = TimeZoneInfo.Local.Id;
    public int PrioridadeExecucao { get; init; } = 3;
    public AncorarPdfDstHorarioInvalidoPolicy DstHorarioInvalidoPolicy { get; init; } =
        AncorarPdfDstHorarioInvalidoPolicy.AvancarParaProximoHorarioValido;
    public AncorarPdfDstHorarioAmbiguoPolicy DstHorarioAmbiguoPolicy { get; init; } =
        AncorarPdfDstHorarioAmbiguoPolicy.PreferirOffsetMaisCedo;
    public IReadOnlyList<AncorarPdfTemplateAncora> TemplateAncoras { get; init; } = [];
    public bool OcrFallbackAtivo { get; init; }
    public int OcrDpi { get; init; } = 300;
    public string OcrLang { get; init; } = "por+eng";
    public int ProgramadoPorUserId { get; init; }
    public string ProgramadoPorNome { get; init; } = string.Empty;
    public DateTime ProgramadoEmUtc { get; init; }
    public int AtualizadoPorUserId { get; init; }
    public DateTime AtualizadoEmUtc { get; init; }
    public int VersaoTemplate { get; init; } = 1;
}

public sealed record AncorarPdfSalvarEntrada
{
    public int? TarefaId { get; init; }
    public int ClienteId { get; init; }
    public int EsteiraId { get; init; }
    public string NomeTarefaPersonalizado { get; init; } = string.Empty;
    public DateTime AgendamentoLocal { get; init; }
    public TarefaRecorrencia Recorrencia { get; init; } = TarefaRecorrencia.Nenhuma;
    public int AgendamentoSegundo { get; init; }
    public string PastaMonitoradaPath { get; init; } = string.Empty;
    public string PdfModeloPath { get; init; } = string.Empty;
    public string NomeReferenciaArquivo { get; init; } = string.Empty;
    public bool MonitorarSubpastas { get; init; }
    public bool ValidacaoClienteAtiva { get; init; } = true;
    public double LimiarSimilaridadeNome { get; init; } = 0.75;
    public double HighlightOpacity { get; init; } = 0.40;
    public AncorarPdfModoSelecao ModoSelecao { get; init; } = AncorarPdfModoSelecao.RetanguloLivre;
    public bool PdfModeloCrossCliente { get; init; }
    public string? PdfModeloCrossClienteJustificativa { get; init; }
    public string TimezoneId { get; init; } = TimeZoneInfo.Local.Id;
    public int PrioridadeExecucao { get; init; } = 3;
    public AncorarPdfDstHorarioInvalidoPolicy DstHorarioInvalidoPolicy { get; init; } =
        AncorarPdfDstHorarioInvalidoPolicy.AvancarParaProximoHorarioValido;
    public AncorarPdfDstHorarioAmbiguoPolicy DstHorarioAmbiguoPolicy { get; init; } =
        AncorarPdfDstHorarioAmbiguoPolicy.PreferirOffsetMaisCedo;
    public IReadOnlyList<AncorarPdfTemplateAncora> TemplateAncoras { get; init; } = [];
    public bool OcrFallbackAtivo { get; init; }
    public int OcrDpi { get; init; } = 300;
    public string OcrLang { get; init; } = "por+eng";
    public int ProgramadoPorUserId { get; init; }
    public string ProgramadoPorNome { get; init; } = string.Empty;
}

public sealed record AncorarPdfTemplateHistoricoItem
{
    public int Id { get; init; }
    public int TarefaId { get; init; }
    public int Versao { get; init; }
    public string AntesJson { get; init; } = string.Empty;
    public string DepoisJson { get; init; } = string.Empty;
    public int AlteradoPorUserId { get; init; }
    public string AlteradoPorNome { get; init; } = string.Empty;
    public DateTime AlteradoEmUtc { get; init; }
}

public sealed class AncorarPdfCommandStackState
{
    // LinkedList remove primeiro/ultimo em O(1), evitando custo de trim O(n) no historico.
    private readonly object _sync = new();
    private readonly LinkedList<IReadOnlyList<AncorarPdfTemplateAncora>> _undo = new();
    private readonly LinkedList<IReadOnlyList<AncorarPdfTemplateAncora>> _redo = new();

    public AncorarPdfCommandStackState(int maxHistorico = 100)
    {
        MaxHistorico = Math.Clamp(maxHistorico, 10, 500);
    }

    public int MaxHistorico { get; }

    public IReadOnlyList<AncorarPdfTemplateAncora> EstadoAtual { get; private set; } = [];

    public bool PodeUndo
    {
        get
        {
            lock (_sync)
            {
                return _undo.Count > 0;
            }
        }
    }

    public bool PodeRedo
    {
        get
        {
            lock (_sync)
            {
                return _redo.Count > 0;
            }
        }
    }

    public void DefinirEstadoInicial(IReadOnlyList<AncorarPdfTemplateAncora> estado)
    {
        lock (_sync)
        {
            _undo.Clear();
            _redo.Clear();
            EstadoAtual = ClonarEstado(estado);
        }
    }

    public void AplicarNovoEstado(IReadOnlyList<AncorarPdfTemplateAncora> novoEstado)
    {
        lock (_sync)
        {
            _undo.AddLast(ClonarEstado(EstadoAtual));
            if (_undo.Count > MaxHistorico)
                _undo.RemoveFirst();

            _redo.Clear();
            EstadoAtual = ClonarEstado(novoEstado);
        }
    }

    public bool TentarUndo(out IReadOnlyList<AncorarPdfTemplateAncora> estado)
    {
        lock (_sync)
        {
            if (_undo.Count == 0)
            {
                estado = EstadoAtual;
                return false;
            }

            _redo.AddLast(ClonarEstado(EstadoAtual));
            EstadoAtual = _undo.Last!.Value;
            _undo.RemoveLast();
            estado = EstadoAtual;
            return true;
        }
    }

    public bool TentarRedo(out IReadOnlyList<AncorarPdfTemplateAncora> estado)
    {
        lock (_sync)
        {
            if (_redo.Count == 0)
            {
                estado = EstadoAtual;
                return false;
            }

            _undo.AddLast(ClonarEstado(EstadoAtual));
            if (_undo.Count > MaxHistorico)
                _undo.RemoveFirst();

            EstadoAtual = _redo.Last!.Value;
            _redo.RemoveLast();
            estado = EstadoAtual;
            return true;
        }
    }

    private static IReadOnlyList<AncorarPdfTemplateAncora> ClonarEstado(IReadOnlyList<AncorarPdfTemplateAncora> estado)
    {
        if (estado.Count == 0)
            return [];

        return estado
            .Select(a => a with
            {
                Metadado = a.Metadado with { }
            })
            .ToArray();
    }
}

public static class AncorarPdfPalettePolicy
{
    public const int MaximoAncoras = 10;
    public const double ContrasteMinimoSobreBranco = 1.4;

    public static readonly IReadOnlyList<string> CoresFixas =
    [
        "#4A90D9",
        "#F5D547",
        "#5CB85C",
        "#F08A24",
        "#8E5AD7",
        "#34C6D3",
        "#E85D9E",
        "#E34D4D",
        "#9ACD32",
        "#A87A5A"
    ];

    // HashSet para busca O(1) em vez de O(n) linear na lista.
    private static readonly HashSet<string> CoresPermitidasSet =
        new(CoresFixas, StringComparer.OrdinalIgnoreCase);

    public static bool EhCorPermitida(string? corHex)
    {
        if (string.IsNullOrWhiteSpace(corHex))
            return false;

        return CoresPermitidasSet.Contains(corHex.Trim());
    }

    public static bool TemContrasteMinimoSobreBranco(string corHex)
    {
        return CalcularContrasteComBranco(corHex) >= ContrasteMinimoSobreBranco;
    }

    public static double CalcularContrasteComBranco(string corHex)
    {
        if (!TryParseHexColor(corHex, out var r, out var g, out var b))
            return 1;

        var luminancia = 0.2126 * ToLinear(r) + 0.7152 * ToLinear(g) + 0.0722 * ToLinear(b);
        const double luminanciaBranco = 1.0;
        return (luminanciaBranco + 0.05) / (luminancia + 0.05);
    }

    private static bool TryParseHexColor(string? hex, out byte r, out byte g, out byte b)
    {
        r = 0;
        g = 0;
        b = 0;

        if (string.IsNullOrWhiteSpace(hex))
            return false;

        var normalized = hex.Trim();
        if (normalized.StartsWith('#'))
            normalized = normalized[1..];

        if (normalized.Length != 6)
            return false;

        return byte.TryParse(normalized[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r) &&
               byte.TryParse(normalized[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g) &&
               byte.TryParse(normalized[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b);
    }

    private static double ToLinear(byte value)
    {
        var srgb = value / 255.0;
        return srgb <= 0.04045
            ? srgb / 12.92
            : Math.Pow((srgb + 0.055) / 1.055, 2.4);
    }
}
