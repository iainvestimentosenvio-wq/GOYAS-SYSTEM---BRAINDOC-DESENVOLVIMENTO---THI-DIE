namespace Protons.Core.Tarefas.Models;

/// <summary>
/// Códigos canônicos de evento e erro para o sistema Ancorar PDF.
/// Formato: ANCORA-{TIPO}-{SUBTIPO} para erros; string literal para tipos de evento.
///
/// TipoEvento (6 eventos obrigatórios C7):
///   - tarefa_agendada             : scheduler despachou tarefa para fila
///   - tarefa_execucao_iniciada    : motor iniciou processamento do item
///   - tarefa_execucao_concluida   : motor concluiu com sucesso
///   - tarefa_execucao_falhou      : motor encerrou com falha (negócio ou técnica)
///   - tarefa_misfire_detectado    : misfire registrado em backlog
///   - tarefa_backlog_decisao      : usuário decidiu sobre backlog pendente
///
/// ErroCodigo (ANCORA-NEG-* = falha de negócio, ANCORA-TEC-* = falha técnica):
///   Vazio ("") para eventos sem erro.
/// </summary>
public static class AncorarPdfErroCodigos
{
    // -----------------------------------------------------------------------
    // Tipos de evento canônicos (TipoEvento no AncorarPdfSchedulerEventoRegistro)
    // -----------------------------------------------------------------------

    /// <summary>Scheduler despachou tarefa para a fila de execução.</summary>
    public const string TarefaAgendada = "tarefa_agendada";

    /// <summary>Motor iniciou o processamento do item de fila.</summary>
    public const string ExecucaoIniciada = "tarefa_execucao_iniciada";

    /// <summary>Motor concluiu o processamento com sucesso.</summary>
    public const string ExecucaoConcluida = "tarefa_execucao_concluida";

    /// <summary>Motor encerrou com falha (código em ErroCodigo).</summary>
    public const string ExecucaoFalhou = "tarefa_execucao_falhou";

    /// <summary>Misfire detectado e registrado em backlog pendente.</summary>
    public const string MisfireDetectado = "tarefa_misfire_detectado";

    /// <summary>Usuário registrou decisão sobre backlog pendente.</summary>
    public const string BacklogDecisao = "tarefa_backlog_decisao";

    // -----------------------------------------------------------------------
    // Status de tarefa canônicos (StatusAnterior / StatusNovo)
    // -----------------------------------------------------------------------

    public const string StatusAgendada = "Agendada";
    public const string StatusEmAndamento = "EmAndamento";
    public const string StatusConcluida = "Concluida";
    public const string StatusBloqueada = "Bloqueada";
    public const string StatusDesconhecido = "";

    // -----------------------------------------------------------------------
    // ErroCodigo — falhas de negócio (ANCORA-NEG-*)
    // Ocorrem quando o motor rejeita o item por razão controlada.
    // -----------------------------------------------------------------------

    /// <summary>PDF existe mas não contém texto nativo (escaneado ou vazio).</summary>
    public const string NegPdfSemTexto = "ANCORA-NEG-PDF_SEM_TEXTO";

    /// <summary>CPF/CNPJ do PDF não corresponde ao cliente da tarefa.</summary>
    public const string NegClienteNaoValidado = "ANCORA-NEG-CLIENTE_NAO_VALIDADO";

    /// <summary>Nenhum PDF encontrado na pasta com similaridade acima do limiar.</summary>
    public const string NegArquivoNaoEncontrado = "ANCORA-NEG-ARQUIVO_NAO_ENCONTRADO";

    /// <summary>Configuração da tarefa não encontrada no repositório.</summary>
    public const string NegConfigNaoEncontrada = "ANCORA-NEG-CONFIG_NAO_ENCONTRADA";

    /// <summary>OCR habilitado, mas dependências obrigatórias não estão disponíveis no runtime.</summary>
    public const string NegOcrIndisponivel = "ANCORA-NEG-OCR_INDISPONIVEL";

    // -----------------------------------------------------------------------
    // ErroCodigo — falhas técnicas (ANCORA-TEC-*)
    // Ocorrem por problemas de infraestrutura; candidatos a retry via Polly.
    // -----------------------------------------------------------------------

    /// <summary>Falha de I/O ao acessar arquivo ou pasta (rede, permissão, disco).</summary>
    public const string TecFalhaIo = "ANCORA-TEC-FALHA_IO";

    /// <summary>Timeout de processamento excedido (Polly timeout strategy).</summary>
    public const string TecFalhaTimeout = "ANCORA-TEC-FALHA_TIMEOUT";

    /// <summary>Falha técnica não categorizada.</summary>
    public const string TecFalhaGeral = "ANCORA-TEC-FALHA_GERAL";

    // -----------------------------------------------------------------------
    // Sem erro (eventos de sucesso ou neutros)
    // -----------------------------------------------------------------------

    /// <summary>Sem código de erro — usar em eventos bem-sucedidos ou neutros.</summary>
    public const string Nenhum = "";
}
