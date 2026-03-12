using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Services;
using Protons.UI.Painel.Funcionalidades.Ferramentas.AncorarPdf.Interface;
using Protons.UI.Painel.ViewModels;

namespace Protons.UI.Painel.Funcionalidades.Ferramentas.AncorarPdf.ModelosDeVisao;

/// <summary>
/// Encapsula a lógica de ciclo de vida: Reset, IniciarNovaPorDrop, AbrirExistenteAsync,
/// EnterAnchorOnlyMode, ConcluirModoAncoras, CancelarModoAncoras.
/// Extraído de AncorarPdfConfiguracaoViewModel para reduzir acoplamento.
/// </summary>
internal sealed class AncorarPdfLifecycleHandler
{
    private readonly IAncorarPdfConfiguracaoService _service;
    private readonly AncorarPdfAgendamentoState _agendamentoState;
    private readonly AncorarPdfAncorasEditorState _ancorasEditorState;
    private readonly AncorarPdfPreviewState _previewState;
    private readonly TimeProvider _timeProvider;
    private readonly Action<string, string?> _registrarEvento;
    private readonly IAncorarPdfLifecycleContext _context;

    public AncorarPdfLifecycleHandler(
        IAncorarPdfConfiguracaoService service,
        AncorarPdfAgendamentoState agendamentoState,
        AncorarPdfAncorasEditorState ancorasEditorState,
        AncorarPdfPreviewState previewState,
        TimeProvider timeProvider,
        Action<string, string?> registrarEvento,
        IAncorarPdfLifecycleContext context)
    {
        _service = service;
        _agendamentoState = agendamentoState;
        _ancorasEditorState = ancorasEditorState;
        _previewState = previewState;
        _timeProvider = timeProvider;
        _registrarEvento = registrarEvento;
        _context = context;
    }

    public void Reset()
    {
        _previewState.ResetRenderState();
        _context.EstaRenderizandoPdf = false;

        var agoraLocal = _timeProvider.GetLocalNow().DateTime;

        _context.EstaAtiva = false;
        _context.SomenteLeitura = false;
        _context.IsBusy = false;
        _context.Mensagem = string.Empty;
        _context.TarefaId = null;
        _context.ClienteId = 0;
        _context.EsteiraId = 0;
        _context.VersaoTemplate = 1;
        _context.Titulo = "Ancorar PDF";
        _context.Subtitulo = string.Empty;
        _context.NomeTarefaPersonalizado = string.Empty;
        _context.DataSelecionada = AncorarPdfAgendamentoState.ToDateOnlyOffset(agoraLocal.Date);
        _context.HoraSelecionada = agoraLocal.Hour;
        _context.MinutoSelecionado = agoraLocal.Minute;
        _context.SegundoSelecionado = 0;
        _context.RecorrenciaSelecionada = "Unica";
        _context.PastaMonitoradaPath = string.Empty;
        _context.PdfModeloPath = string.Empty;
        _context.NomeReferenciaArquivo = string.Empty;
        _context.MonitorarSubpastas = false;
        _context.ValidacaoClienteAtiva = true;
        _context.LimiarSimilaridadeNome = 0.75;
        _context.TimezoneIdSelecionado = TimeZoneInfo.Local.Id;
        _context.PrioridadeExecucaoSelecionada = 3;
        _context.DstHorarioInvalidoPolicySelecionada = nameof(AncorarPdfDstHorarioInvalidoPolicy.AvancarParaProximoHorarioValido);
        _context.DstHorarioAmbiguoPolicySelecionada = nameof(AncorarPdfDstHorarioAmbiguoPolicy.PreferirOffsetMaisCedo);
        _context.OcrFallbackAtivo = false;
        _context.OcrDpi = 300;
        _context.OcrLang = "por+eng";
        _context.HighlightOpacity = 0.40;
        _context.ModoSelecaoSelecionado = nameof(AncorarPdfModoSelecao.RetanguloLivre);
        _context.PdfModeloCrossCliente = false;
        _context.PdfModeloCrossClienteJustificativa = string.Empty;
        _context.SolicitanteEhAdmin = false;
        _context.ProgramadoPorResumo = string.Empty;
        _context.CorSelecionada = AncorarPdfPalettePolicy.CoresFixas[0];
        _context.CorPendenteSubstituicao = string.Empty;
        _context.SelecaoPendenteSubstituicao = null;
        _context.ConfirmacaoSubstituicaoCorAberta = false;
        _context.AncoraSelecionada = null;
        _context.FerramentaInteracaoSelecionada = AncorarPdfFerramentaInteracao.Navegar;
        _context.ZoomPreview = 1.0;
        _context.PaginaPreviewAtual = 1;
        _context.TotalPaginasPreview = 1;
        _context.StatusInteracaoPreview = AncorarPdfFerramentaInteracaoCatalogo.ObterInstrucao(_context.FerramentaInteracaoSelecionada);
        _context.ClearAncoras();
        _context.LimparDocumentoAnalisePreview();
        _context.ModoApenasAncoras = false;
        _context.SetOnAncorasCallbacks(null, null);
        _context.DisposePdfPageBitmap();
        _context.SetPdfPageBitmap(null);

        _ancorasEditorState.DefinirEstadoInicial([]);
        _previewState.AtualizarDocumento(null);
        _previewState.AtualizarAncoras([], _context.HighlightOpacity);
        _previewState.AtualizarViewport(new AncorarPdfPreviewViewportState(_context.ZoomPreview, 0, 0));
        _previewState.DestacarAncora(null);
        _previewState.DefinirCorAtiva(_context.CorSelecionada);
        _context.AtualizarEstadoPreviewBindings();
    }

    public void IniciarNovaPorDrop(
        NovaTarefaDropPayload payload,
        int clienteId,
        int solicitanteUserId,
        string solicitanteNome,
        bool somenteLeitura,
        bool solicitanteEhAdmin = false)
    {
        Reset();

        var horarioLocal = payload.TempoAlvoUtc.ToLocalTime();

        _context.EstaAtiva = true;
        _context.SomenteLeitura = somenteLeitura;
        _context.ClienteId = clienteId;
        _context.EsteiraId = payload.EsteiraId;
        _context.TarefaId = null;
        _context.Titulo = "Ancorar PDF";
        _context.Subtitulo = $"Esteira {payload.EsteiraId}";
        _context.NomeTarefaPersonalizado = "Ancorar PDF";
        _context.DataSelecionada = AncorarPdfAgendamentoState.ToDateOnlyOffset(horarioLocal.Date);
        _context.HoraSelecionada = horarioLocal.Hour;
        _context.MinutoSelecionado = horarioLocal.Minute;
        _context.SegundoSelecionado = horarioLocal.Second;
        _context.RecorrenciaSelecionada = "Unica";
        _context.SetSolicitante(solicitanteUserId, solicitanteNome);
        _context.SolicitanteEhAdmin = solicitanteEhAdmin;
        _context.ProgramadoPorResumo = $"Programado por {solicitanteNome} em {_timeProvider.GetLocalNow():dd/MM/yyyy HH:mm:ss}";
        _context.FerramentaInteracaoSelecionada = AncorarPdfFerramentaInteracao.Navegar;
        _context.PaginaPreviewAtual = 1;
        _context.TotalPaginasPreview = 1;
        _context.StatusInteracaoPreview = AncorarPdfFerramentaInteracaoCatalogo.ObterInstrucao(_context.FerramentaInteracaoSelecionada);

        _ancorasEditorState.DefinirEstadoInicial([]);

        var origem = FerramentaTarefaIds.EhAncorarPdf(payload.FerramentaId)
            ? FerramentaTarefaIds.Canonicalizar(payload.FerramentaId)
            : payload.FerramentaId;

        _registrarEvento(
            "ancorar_pdf_c2_modal_open",
            $"origem={origem} esteira_id={payload.EsteiraId} cliente_id={clienteId}");
        _registrarEvento(
            "ancorar_pdf_c2_overlay_open",
            $"origem={origem} adapter={_context.PreviewAdapterNome}");
    }

    public async Task AbrirExistenteAsync(
        Tarefa tarefa,
        int solicitanteUserId,
        string solicitanteNome,
        bool somenteLeitura,
        bool solicitanteEhAdmin = false)
    {
        var config = await Task.Run(() => _service.ObterPorTarefaId(tarefa.Id, solicitanteUserId))
            ?? throw new InvalidOperationException("Configuração da tarefa ancorar_pdf não encontrada.");

        Reset();

        _context.EstaAtiva = true;
        _context.SomenteLeitura = somenteLeitura;
        _context.TarefaId = tarefa.Id;
        _context.ClienteId = config.ClienteId;
        _context.EsteiraId = config.EsteiraId;
        _context.Titulo = "Ancorar PDF";
        _context.Subtitulo = somenteLeitura ? "Leitura da tarefa passada" : "Edição da tarefa";
        _context.NomeTarefaPersonalizado = config.NomeTarefaPersonalizado;

        var vencimentoLocal = _agendamentoState.ConverterUtcParaTimezone(tarefa.VencimentoUtc, config.TimezoneId);
        _context.DataSelecionada = AncorarPdfAgendamentoState.ToDateOnlyOffset(vencimentoLocal.Date);
        _context.HoraSelecionada = vencimentoLocal.Hour;
        _context.MinutoSelecionado = vencimentoLocal.Minute;
        _context.SegundoSelecionado = config.AgendamentoSegundo;

        _context.RecorrenciaSelecionada = _agendamentoState.MapRecorrenciaParaUi(config.Recorrencia);
        _context.PastaMonitoradaPath = config.PastaMonitoradaPath;
        _context.PdfModeloPath = config.PdfModeloPath;
        _context.NomeReferenciaArquivo = config.NomeReferenciaArquivo;
        _context.MonitorarSubpastas = config.MonitorarSubpastas;
        _context.ValidacaoClienteAtiva = config.ValidacaoClienteAtiva;
        _context.LimiarSimilaridadeNome = config.LimiarSimilaridadeNome;
        _context.TimezoneIdSelecionado = config.TimezoneId;
        _context.PrioridadeExecucaoSelecionada = config.PrioridadeExecucao;
        _context.DstHorarioInvalidoPolicySelecionada = _agendamentoState.MapDstHorarioInvalidoPolicyParaUi(config.DstHorarioInvalidoPolicy);
        _context.DstHorarioAmbiguoPolicySelecionada = _agendamentoState.MapDstHorarioAmbiguoPolicyParaUi(config.DstHorarioAmbiguoPolicy);
        _context.VersaoTemplate = config.VersaoTemplate;
        _context.OcrFallbackAtivo = config.OcrFallbackAtivo;
        _context.OcrDpi = Math.Clamp(config.OcrDpi, 150, 600);
        _context.OcrLang = string.IsNullOrWhiteSpace(config.OcrLang) ? "por+eng" : config.OcrLang;

        _context.SetSolicitante(solicitanteUserId, solicitanteNome);
        _context.SolicitanteEhAdmin = solicitanteEhAdmin;
        _context.ProgramadoPorResumo = $"Criada por {config.ProgramadoPorNome} em {_agendamentoState.ConverterUtcParaTimezone(config.ProgramadoEmUtc, config.TimezoneId):dd/MM/yyyy HH:mm:ss}";
        _context.HighlightOpacity = config.HighlightOpacity;
        _context.ModoSelecaoSelecionado = _agendamentoState.MapModoSelecaoParaUi(config.ModoSelecao);
        _context.FerramentaInteracaoSelecionada = AncorarPdfFerramentaInteracao.Navegar;
        _context.StatusInteracaoPreview = AncorarPdfFerramentaInteracaoCatalogo.ObterInstrucao(_context.FerramentaInteracaoSelecionada);
        _context.PdfModeloCrossCliente = config.PdfModeloCrossCliente;
        _context.PdfModeloCrossClienteJustificativa = config.PdfModeloCrossClienteJustificativa ?? string.Empty;

        _context.AplicarEstadoAncoras(config.TemplateAncoras, registrarHistorico: false);
        _ancorasEditorState.DefinirEstadoInicial(config.TemplateAncoras);
        _previewState.AtualizarDocumento(_context.PdfModeloPath);
        _previewState.AtualizarAncoras(config.TemplateAncoras, _context.HighlightOpacity);
        _previewState.AtualizarViewport(new AncorarPdfPreviewViewportState(_context.ZoomPreview, 0, 0));
        _context.AtualizarEstadoPreviewBindings();
        _context.IniciarRenderEextracaoPdf(_context.PdfModeloPath);

        _registrarEvento(
            "ancorar_pdf_c2_modal_open",
            $"origem=edicao tarefa_id={tarefa.Id} cliente_id={_context.ClienteId} readonly={somenteLeitura}");
        _registrarEvento(
            "ancorar_pdf_c2_overlay_open",
            $"origem=edicao adapter={_context.PreviewAdapterNome}");

        if (_context.PdfModeloCrossCliente)
            _registrarEvento("ancorar_pdf_c2_cross_cliente_modelo", $"tarefa_id={tarefa.Id} ativo=true");
    }

    public void EnterAnchorOnlyMode(string pdfPath, Action<AncorarPdfFluxoEstadoAncoras> onConcluido, Action? onCancelado = null)
    {
        _context.EstaAtiva = true;
        _context.SomenteLeitura = false;
        _context.ModoApenasAncoras = true;
        _context.FerramentaInteracaoSelecionada = AncorarPdfFerramentaInteracao.RetanguloManual;
        _context.PaginaPreviewAtual = 1;
        _context.TotalPaginasPreview = 1;
        _context.StatusInteracaoPreview = AncorarPdfFerramentaInteracaoCatalogo.ObterInstrucao(_context.FerramentaInteracaoSelecionada);
        _context.SetOnAncorasCallbacks(onConcluido, onCancelado);
        _context.PdfModeloPath = pdfPath;
        _previewState.AtualizarDocumento(pdfPath);
        _context.AtualizarEstadoPreviewBindings();
        _context.IniciarRenderEextracaoPdf(pdfPath);
        _context.AtualizarEstadoComandosEdicao();
    }

    public void ConcluirModoAncoras()
    {
        if (_context.AncorasCount == 0)
        {
            _context.Mensagem = "Adicione ao menos uma âncora.";
            return;
        }
        var snapshot = new AncorarPdfFluxoEstadoAncoras(_context.ConverterAncorasAtuais(), _context.PdfModeloPath);
        _context.ModoApenasAncoras = false;
        _context.InvokeOnAncorasConcluidas(snapshot);
        _context.SetOnAncorasCallbacks(null, null);
    }

    public void CancelarModoAncoras()
    {
        _context.ModoApenasAncoras = false;
        _context.ClearAncoras();
        var cancelCallback = _context.GetOnAncorasCancelada();
        _context.SetOnAncorasCallbacks(null, null);
        cancelCallback?.Invoke();
    }
}

/// <summary>
/// Contrato mínimo para o handler de lifecycle interagir com a VM.
/// </summary>
internal interface IAncorarPdfLifecycleContext
{
    bool EstaAtiva { get; set; }
    bool SomenteLeitura { get; set; }
    bool IsBusy { get; set; }
    string Mensagem { get; set; }
    int? TarefaId { get; set; }
    int ClienteId { get; set; }
    int EsteiraId { get; set; }
    int VersaoTemplate { get; set; }
    string Titulo { get; set; }
    string Subtitulo { get; set; }
    string NomeTarefaPersonalizado { get; set; }
    DateTimeOffset? DataSelecionada { get; set; }
    int HoraSelecionada { get; set; }
    int MinutoSelecionado { get; set; }
    int SegundoSelecionado { get; set; }
    string RecorrenciaSelecionada { get; set; }
    string PastaMonitoradaPath { get; set; }
    string PdfModeloPath { get; set; }
    string NomeReferenciaArquivo { get; set; }
    bool MonitorarSubpastas { get; set; }
    bool ValidacaoClienteAtiva { get; set; }
    double LimiarSimilaridadeNome { get; set; }
    string TimezoneIdSelecionado { get; set; }
    int PrioridadeExecucaoSelecionada { get; set; }
    string DstHorarioInvalidoPolicySelecionada { get; set; }
    string DstHorarioAmbiguoPolicySelecionada { get; set; }
    bool OcrFallbackAtivo { get; set; }
    int OcrDpi { get; set; }
    string OcrLang { get; set; }
    double HighlightOpacity { get; set; }
    string ModoSelecaoSelecionado { get; set; }
    bool PdfModeloCrossCliente { get; set; }
    string PdfModeloCrossClienteJustificativa { get; set; }
    bool SolicitanteEhAdmin { get; set; }
    string ProgramadoPorResumo { get; set; }
    bool ModoApenasAncoras { get; set; }
    bool EstaRenderizandoPdf { get; set; }
    string CorSelecionada { get; set; }
    string CorPendenteSubstituicao { get; set; }
    AncorarPdfPreviewSelection? SelecaoPendenteSubstituicao { get; set; }
    bool ConfirmacaoSubstituicaoCorAberta { get; set; }
    AncorarPdfAncoraItemViewModel? AncoraSelecionada { get; set; }
    AncorarPdfFerramentaInteracao FerramentaInteracaoSelecionada { get; set; }
    double ZoomPreview { get; set; }
    int PaginaPreviewAtual { get; set; }
    int TotalPaginasPreview { get; set; }
    string StatusInteracaoPreview { get; set; }
    string PreviewAdapterNome { get; }
    int AncorasCount { get; }
    List<AncorarPdfTemplateAncora> ConverterAncorasAtuais();
    void SetSolicitante(int userId, string nome);
    void ClearAncoras();
    void SetOnAncorasCallbacks(Action<AncorarPdfFluxoEstadoAncoras>? onConcluido, Action? onCancelado);
    Action? GetOnAncorasCancelada();
    void InvokeOnAncorasConcluidas(AncorarPdfFluxoEstadoAncoras snapshot);
    void DisposePdfPageBitmap();
    void SetPdfPageBitmap(Bitmap? bitmap);
    void LimparDocumentoAnalisePreview();
    void AplicarEstadoAncoras(IReadOnlyList<AncorarPdfTemplateAncora> estado, bool registrarHistorico);
    void AtualizarEstadoPreviewBindings();
    void AtualizarEstadoComandosEdicao();
    void IniciarRenderEextracaoPdf(string pdfPath);
}
