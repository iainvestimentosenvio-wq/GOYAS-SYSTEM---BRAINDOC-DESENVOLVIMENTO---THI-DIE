using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Services;
using Protons.UI.Painel.Funcionalidades.Ferramentas.AncorarPdf.Interface;
using Protons.UI.Painel.ViewModels;

namespace Protons.UI.Painel.Funcionalidades.Ferramentas.AncorarPdf.ModelosDeVisao;

public sealed partial class AncorarPdfConfiguracaoViewModel : ObservableObject, IDisposable, IAncorarPdfLifecycleContext, IAncorarPdfPdfRenderContext, IAncorarPdfSaveContext, IAncorarPdfPreviewInteractionContext, IAncorarPdfAncorasInteractionContext, IAncorarPdfSmartClickContext
{
    private const double PreviewZoomMin = 0.50;
    private const double PreviewZoomMax = 4.00;
    private const int MaxHistoricoUndoRedo = 120;
    private readonly IAncorarPdfConfiguracaoService _service;
    private IAncorarPdfFilePicker _filePicker;
    private readonly IAncorarPdfPreviewAdapter _previewAdapter;
    private readonly AncorarPdfAgendamentoState _agendamentoState = new();
    private readonly AncorarPdfAncorasEditorState _ancorasEditorState = new(maxHistorico: MaxHistoricoUndoRedo);
    private readonly AncorarPdfSaveOrchestrator _saveOrchestrator = new();
    private readonly AncorarPdfPreviewState _previewState;
    private readonly AncorarPdfLifecycleHandler _lifecycleHandler;
    private readonly AncorarPdfPdfRenderHandler _pdfRenderHandler;
    private readonly AncorarPdfSaveHandler _saveHandler;
    private readonly AncorarPdfPreviewInteractionHandler _previewHandler;
    private readonly AncorarPdfAncorasInteractionHandler _ancorasHandler;
    private readonly AncorarPdfSmartClickHandler _smartClickHandler;
    private readonly Action _fecharModal;
    private readonly Action<string, string?> _registrarEvento;
    private readonly Action<string, long, string, string?> _registrarMetrica;
    private readonly Action<AncorarPdfConfiguracaoTarefa>? _onSaveSucesso;
    private readonly TimeProvider _timeProvider;
    private readonly IAncorarPdfSmartDetector? _smartDetector;
    private readonly IAncorarPdfDocumentoAnalyzer? _documentAnalyzer;
    private readonly IAncorarPdfExtratorTexto? _extratorTextoSmart;
    private readonly bool _featureAnalyzerV2;
    private readonly bool _featureHybridOcr;
    private readonly bool _featureMultipageEditor;
    private readonly bool _featureAdvancedAnchors;
    private readonly bool _featureDiagnosticsOverlay;
    private readonly bool _featureCloudDocumentAiProvider;
    private IReadOnlyList<PdfPaginaTexto>? _pdfPaginasCache;
    private AncorarPdfDocumentoAnalise? _documentoAnaliseAtual;
    private bool _disposed;

    private int _solicitanteUserId;
    private string _solicitanteNome = string.Empty;

    [ObservableProperty] private bool _estaAtiva;
    [ObservableProperty] private bool _somenteLeitura;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _mensagem = string.Empty;
    [ObservableProperty] private AncorarPdfError? _ultimoErroTipado;

    [ObservableProperty] private int? _tarefaId;
    [ObservableProperty] private int _clienteId;
    [ObservableProperty] private int _esteiraId;
    [ObservableProperty] private int _versaoTemplate = 1;

    [ObservableProperty] private string _titulo = "Ancorar PDF";
    [ObservableProperty] private string _subtitulo = string.Empty;

    [ObservableProperty] private string _nomeTarefaPersonalizado = string.Empty;

    /// <summary>
    /// Data selecionada para agendamento. DateTimeOffset? fim-a-fim evita exceções de Kind/offset no binding com DatePicker.
    /// Contrato date-only: offset=0 sempre; não interpretar offset como fuso real — apenas Y/M/D são significativos.
    /// </summary>
    private DateTimeOffset? _dataSelecionada = AncorarPdfAgendamentoState.ToDateOnlyOffset(DateTime.Today);

    public DateTimeOffset? DataSelecionada
    {
        get => _dataSelecionada;
        set => SetProperty(ref _dataSelecionada,
            value.HasValue
                ? AncorarPdfAgendamentoState.ToDateOnlyOffset(value.Value.Date)
                : AncorarPdfAgendamentoState.ToDateOnlyOffset(DateTime.Today));
    }

    [ObservableProperty] private int _horaSelecionada;
    [ObservableProperty] private int _minutoSelecionado;
    [ObservableProperty] private int _segundoSelecionado;
    [ObservableProperty] private string _recorrenciaSelecionada = "Unica";

    [ObservableProperty] private string _pastaMonitoradaPath = string.Empty;
    [ObservableProperty] private string _pdfModeloPath = string.Empty;
    [ObservableProperty] private string _nomeReferenciaArquivo = string.Empty;

    [ObservableProperty] private bool _monitorarSubpastas;
    [ObservableProperty] private bool _validacaoClienteAtiva = true;
    [ObservableProperty] private double _limiarSimilaridadeNome = 0.75;
    [ObservableProperty] private string _timezoneIdSelecionado = TimeZoneInfo.Local.Id;
    [ObservableProperty] private int _prioridadeExecucaoSelecionada = 3;
    [ObservableProperty] private string _dstHorarioInvalidoPolicySelecionada = nameof(AncorarPdfDstHorarioInvalidoPolicy.AvancarParaProximoHorarioValido);
    [ObservableProperty] private string _dstHorarioAmbiguoPolicySelecionada = nameof(AncorarPdfDstHorarioAmbiguoPolicy.PreferirOffsetMaisCedo);
    [ObservableProperty] private bool _ocrFallbackAtivo;
    [ObservableProperty] private int _ocrDpi = 300;
    [ObservableProperty] private string _ocrLang = "por+eng";
    [ObservableProperty] private double _highlightOpacity = 0.40;
    [ObservableProperty] private string _modoSelecaoSelecionado = nameof(AncorarPdfModoSelecao.RetanguloLivre);
    [ObservableProperty] private bool _pdfModeloCrossCliente;
    [ObservableProperty] private string _pdfModeloCrossClienteJustificativa = string.Empty;
    [ObservableProperty] private bool _solicitanteEhAdmin;
    [ObservableProperty] private string _programadoPorResumo = string.Empty;

    [ObservableProperty] private bool _modoApenasAncoras;
    [ObservableProperty] private Bitmap? _pdfPageBitmap;
    [ObservableProperty] private bool _estaRenderizandoPdf;
    private Action<AncorarPdfFluxoEstadoAncoras>? _onAncorasConcluidas;
    private Action? _onAncorasCancelada;

    [ObservableProperty] private string _corSelecionada = AncorarPdfPalettePolicy.CoresFixas[0];
    [ObservableProperty] private bool _confirmacaoSubstituicaoCorAberta;
    [ObservableProperty] private string _corPendenteSubstituicao = string.Empty;
    [ObservableProperty] private AncorarPdfPreviewSelection? _selecaoPendenteSubstituicao;
    [ObservableProperty] private AncorarPdfAncoraItemViewModel? _ancoraSelecionada;
    [ObservableProperty] private string _statusInteracaoPreview = "Modo navegação ativo. Use o canvas para inspecionar o PDF.";
    [ObservableProperty] private AncorarPdfFerramentaInteracao _ferramentaInteracaoSelecionada = AncorarPdfFerramentaInteracao.Navegar;
    [ObservableProperty] private double _zoomPreview = 1.0;
    [ObservableProperty] private int _paginaPreviewAtual = 1;
    [ObservableProperty] private int _totalPaginasPreview = 1;
    [ObservableProperty] private bool _mostrarTokensNativos = true;
    [ObservableProperty] private bool _mostrarTokensOcr = true;
    [ObservableProperty] private bool _mostrarDiagnosticosPreview = true;
    [ObservableProperty] private bool _mostrarHeatmapConfianca = true;
    [ObservableProperty] private string _diagnosticoPaginaAtualResumo = string.Empty;
    [ObservableProperty] private bool _painelAvancadoAberto;

    // Smart Click (C12)
    [ObservableProperty] private bool _extracaoEmAndamento;
    [ObservableProperty] private SmartDeteccaoResultado? _smartDeteccaoAtual;
    [ObservableProperty] private string _smartNomeEditavel = string.Empty;
    [ObservableProperty] private string _smartChaveEditavel = string.Empty;
    [ObservableProperty] private string _smartTipoEditavel = string.Empty;

    // F2: Destaques transientes smart click (C13)
    public ObservableCollection<TextoDestaqueSmartItem> DestaquesSmart { get; } = new();
    private IReadOnlyList<BboxRelativo> _destaquesRelBboxCache = [];
    private string _destaqueCorCache = "#4A90D9";
    private int _destaquesPaginaCache = 1;
    public string QuantidadeDestaquesSmartTexto => DestaquesSmart.Count == 0 ? string.Empty
        : $"({DestaquesSmart.Count} ocorrência{(DestaquesSmart.Count == 1 ? "" : "s")})";

    public static IReadOnlyList<string> RecorrenciasPermitidas { get; } =
        ["Unica", "Diaria", "Semanal", "Mensal"];

    public static IReadOnlyList<string> TiposEsperados { get; } =
        ["texto", "cnpj", "cpf", "email", "cep", "telefone",
         "data_br", "data_extenso", "uf", "moeda_brl", "inteiro",
         "moeda", "data", "numero"];

    public static IReadOnlyList<string> ModosSelecaoPermitidos { get; } =
        [nameof(AncorarPdfModoSelecao.RetanguloLivre), nameof(AncorarPdfModoSelecao.TextoExpandido)];

    public static IReadOnlyList<int> PrioridadesExecucaoPermitidas { get; } = [1, 2, 3, 4, 5];

    public static IReadOnlyList<string> DstHorarioInvalidoPolicies { get; } =
    [
        nameof(AncorarPdfDstHorarioInvalidoPolicy.AvancarParaProximoHorarioValido),
        nameof(AncorarPdfDstHorarioInvalidoPolicy.RecuarParaHorarioValidoAnterior),
        nameof(AncorarPdfDstHorarioInvalidoPolicy.PularOcorrenciaComAuditoria)
    ];

    public static IReadOnlyList<string> DstHorarioAmbiguoPolicies { get; } =
    [
        nameof(AncorarPdfDstHorarioAmbiguoPolicy.PreferirOffsetMaisCedo),
        nameof(AncorarPdfDstHorarioAmbiguoPolicy.PreferirOffsetMaisTarde),
        nameof(AncorarPdfDstHorarioAmbiguoPolicy.ExecutarUmaVezNoOffsetMaisCedo)
    ];

    public static IReadOnlyList<AncorarPdfFerramentaInteracao> FerramentasInteracaoDisponiveis { get; } =
        AncorarPdfFerramentaInteracaoCatalogo.Todas;

    public static IReadOnlyList<string> CoresPaleta { get; } =
        AncorarPdfPalettePolicy.CoresFixas;

    public ObservableCollection<AncorarPdfAncoraItemViewModel> Ancoras { get; } = new();
    public ObservableCollection<AncorarPdfTokenPreviewItemViewModel> TokensPreview { get; } = new();
    public ObservableCollection<AncorarPdfPaginaDiagnosticoItemViewModel> DiagnosticosPaginas { get; } = new();
    public IEnumerable<AncorarPdfAncoraItemViewModel> AncorasNaPaginaAtual => Ancoras.Where(a => a.Pagina == PaginaPreviewAtual);
    public bool TemAncorasNaPaginaAtual => Ancoras.Any(a => a.Pagina == PaginaPreviewAtual);

    IEnumerable<AncorarPdfAncoraItemViewModel> IAncorarPdfPreviewInteractionContext.Ancoras => Ancoras;
    int IAncorarPdfPreviewInteractionContext.AncorasCount => Ancoras.Count;

    int IAncorarPdfLifecycleContext.AncorasCount => Ancoras.Count;
    string IAncorarPdfLifecycleContext.PreviewAdapterNome => PreviewAdapterNome;
    List<AncorarPdfTemplateAncora> IAncorarPdfLifecycleContext.ConverterAncorasAtuais() =>
        AncorarPdfAncorasEditorState.ConverterAncorasAtuais(Ancoras);
    void IAncorarPdfLifecycleContext.SetSolicitante(int userId, string nome)
    {
        _solicitanteUserId = userId;
        _solicitanteNome = nome;
    }
    void IAncorarPdfLifecycleContext.ClearAncoras() => Ancoras.Clear();
    void IAncorarPdfLifecycleContext.SetOnAncorasCallbacks(Action<AncorarPdfFluxoEstadoAncoras>? onConcluido, Action? onCancelado)
    {
        _onAncorasConcluidas = onConcluido;
        _onAncorasCancelada = onCancelado;
    }
    Action? IAncorarPdfLifecycleContext.GetOnAncorasCancelada() => _onAncorasCancelada;
    void IAncorarPdfLifecycleContext.InvokeOnAncorasConcluidas(AncorarPdfFluxoEstadoAncoras snapshot) =>
        _onAncorasConcluidas?.Invoke(snapshot);
    void IAncorarPdfLifecycleContext.DisposePdfPageBitmap() => PdfPageBitmap?.Dispose();
    void IAncorarPdfLifecycleContext.SetPdfPageBitmap(Bitmap? bitmap) => PdfPageBitmap = bitmap;
    void IAncorarPdfLifecycleContext.LimparDocumentoAnalisePreview() => LimparDocumentoAnalisePreview();
    void IAncorarPdfLifecycleContext.AplicarEstadoAncoras(IReadOnlyList<AncorarPdfTemplateAncora> estado, bool registrarHistorico) =>
        AplicarEstadoAncoras(estado, registrarHistorico);
    void IAncorarPdfLifecycleContext.AtualizarEstadoPreviewBindings() => AtualizarEstadoPreviewBindings();
    void IAncorarPdfLifecycleContext.AtualizarEstadoComandosEdicao() => AtualizarEstadoComandosEdicao();
    void IAncorarPdfLifecycleContext.IniciarRenderEextracaoPdf(string pdfPath)
    {
        _ = RenderizarPdfAsync(pdfPath);
        _ = CarregarTextoPdfAsync(pdfPath);
    }
    void IAncorarPdfPdfRenderContext.AplicarBitmap(Bitmap? bitmap) => AplicarBitmapRenderizado(bitmap);
    void IAncorarPdfPdfRenderContext.SetPdfPaginasCache(IReadOnlyList<PdfPaginaTexto>? cache) => _pdfPaginasCache = cache;
    void IAncorarPdfPdfRenderContext.SetDocumentoAnalise(AncorarPdfDocumentoAnalise? analise)
    {
        _documentoAnaliseAtual = analise;
        AtualizarDiagnosticosPreview();
        AtualizarTokensPreview();
    }
    void IAncorarPdfPdfRenderContext.DefinirTotalPaginasPreview(int totalPaginas)
    {
        TotalPaginasPreview = Math.Max(1, totalPaginas);
        if (PaginaPreviewAtual > TotalPaginasPreview)
            PaginaPreviewAtual = TotalPaginasPreview;
        AtualizarPaginaPreviewSnapshot();
    }
    AncorarPdfDocumentoAnalyzerOptions IAncorarPdfPdfRenderContext.BuildDocumentoAnalyzerOptions() => BuildDocumentoAnalyzerOptions();
    int IAncorarPdfSaveContext.SolicitanteUserId => _solicitanteUserId;
    Action<AncorarPdfConfiguracaoTarefa>? IAncorarPdfSaveContext.OnSaveSucesso => _onSaveSucesso;
    AncorarPdfSaveValidationInput IAncorarPdfSaveContext.BuildValidationInput() => new(
        NomeTarefaPersonalizado, PastaMonitoradaPath, PdfModeloPath, HighlightOpacity,
        PdfModeloCrossCliente, SolicitanteEhAdmin, HoraSelecionada, MinutoSelecionado, SegundoSelecionado,
        LimiarSimilaridadeNome, PrioridadeExecucaoSelecionada, DstHorarioInvalidoPolicySelecionada, DstHorarioAmbiguoPolicySelecionada,
        DstHorarioInvalidoPolicies, DstHorarioAmbiguoPolicies, TimezoneIdSelecionado, PdfModeloCrossClienteJustificativa,
        OcrFallbackAtivo, OcrDpi, OcrLang);
    AncorarPdfSalvarEntrada IAncorarPdfSaveContext.BuildEntrada()
    {
        var dataBase = DataSelecionada ?? AncorarPdfAgendamentoState.ToDateOnlyOffset(DateTime.Today);
        var agendamentoLocal = new DateTime(dataBase.Year, dataBase.Month, dataBase.Day,
            Math.Clamp(HoraSelecionada, 0, 23), Math.Clamp(MinutoSelecionado, 0, 59), Math.Clamp(SegundoSelecionado, 0, 59), DateTimeKind.Local);
        return new AncorarPdfSalvarEntrada
        {
            TarefaId = TarefaId, ClienteId = ClienteId, EsteiraId = EsteiraId, NomeTarefaPersonalizado = NomeTarefaPersonalizado,
            AgendamentoLocal = agendamentoLocal, Recorrencia = _agendamentoState.MapRecorrenciaParaDominio(RecorrenciaSelecionada),
            AgendamentoSegundo = Math.Clamp(SegundoSelecionado, 0, 59), PastaMonitoradaPath = PastaMonitoradaPath, PdfModeloPath = PdfModeloPath,
            NomeReferenciaArquivo = NomeReferenciaArquivo, MonitorarSubpastas = MonitorarSubpastas, ValidacaoClienteAtiva = ValidacaoClienteAtiva,
            LimiarSimilaridadeNome = LimiarSimilaridadeNome, TimezoneId = _agendamentoState.NormalizarTimezoneIdUi(TimezoneIdSelecionado),
            PrioridadeExecucao = Math.Clamp(PrioridadeExecucaoSelecionada, 1, 5),
            DstHorarioInvalidoPolicy = _agendamentoState.MapDstHorarioInvalidoPolicyParaDominio(DstHorarioInvalidoPolicySelecionada),
            DstHorarioAmbiguoPolicy = _agendamentoState.MapDstHorarioAmbiguoPolicyParaDominio(DstHorarioAmbiguoPolicySelecionada),
            HighlightOpacity = Math.Clamp(HighlightOpacity, 0.30, 0.45), ModoSelecao = _agendamentoState.MapModoSelecaoParaDominio(ModoSelecaoSelecionado),
            OcrFallbackAtivo = OcrFallbackAtivo, OcrDpi = Math.Clamp(OcrDpi, 150, 600),
            OcrLang = string.IsNullOrWhiteSpace(OcrLang) ? "por+eng" : OcrLang.Trim().ToLowerInvariant(),
            PdfModeloCrossCliente = PdfModeloCrossCliente,
            PdfModeloCrossClienteJustificativa = string.IsNullOrWhiteSpace(PdfModeloCrossClienteJustificativa) ? null : PdfModeloCrossClienteJustificativa.Trim(),
            TemplateAncoras = AncorarPdfAncorasEditorState.ConverterAncorasAtuais(Ancoras), ProgramadoPorUserId = _solicitanteUserId, ProgramadoPorNome = _solicitanteNome
        };
    }
    void IAncorarPdfSaveContext.ApplySaveResult(AncorarPdfConfiguracaoTarefa resultado)
    {
        TarefaId = resultado.TarefaId; ClienteId = resultado.ClienteId; EsteiraId = resultado.EsteiraId; VersaoTemplate = resultado.VersaoTemplate;
        ProgramadoPorResumo = $"Criada por {resultado.ProgramadoPorNome} em {_agendamentoState.ConverterUtcParaTimezone(resultado.ProgramadoEmUtc, resultado.TimezoneId):dd/MM/yyyy HH:mm:ss}";
    }
    List<AncorarPdfTemplateAncora> IAncorarPdfPreviewInteractionContext.ConverterAncorasAtuais() =>
        AncorarPdfAncorasEditorState.ConverterAncorasAtuais(Ancoras);
    void IAncorarPdfPreviewInteractionContext.AplicarEstadoComHistorico(List<AncorarPdfTemplateAncora> estado) =>
        AplicarEstadoComHistorico(estado);
    void IAncorarPdfPreviewInteractionContext.AtualizarEstadoPreviewBindings() => AtualizarEstadoPreviewBindings();
    void IAncorarPdfPreviewInteractionContext.LimparDeteccaoSmartSeNecessario(AncorarPdfFerramentaInteracao ferramenta)
    {
        if (ferramenta == AncorarPdfFerramentaInteracao.SmartClick)
            return;

        LimparDeteccaoSmartInterna(limparMensagem: false);
    }

    IEnumerable<AncorarPdfAncoraItemViewModel> IAncorarPdfAncorasInteractionContext.Ancoras => Ancoras;
    int IAncorarPdfAncorasInteractionContext.AncorasCount => Ancoras.Count;
    List<AncorarPdfTemplateAncora> IAncorarPdfAncorasInteractionContext.ConverterAncorasAtuais() =>
        AncorarPdfAncorasEditorState.ConverterAncorasAtuais(Ancoras);
    void IAncorarPdfAncorasInteractionContext.AplicarEstadoComHistorico(List<AncorarPdfTemplateAncora> estado) =>
        AplicarEstadoComHistorico(estado);
    void IAncorarPdfAncorasInteractionContext.AplicarEstadoAncoras(IReadOnlyList<AncorarPdfTemplateAncora> estado, bool registrarHistorico) =>
        AplicarEstadoAncoras(estado, registrarHistorico);
    void IAncorarPdfAncorasInteractionContext.AtualizarDestaquePreview(AncorarPdfAncoraItemViewModel? ancora) =>
        AtualizarDestaquePreview(ancora);

    IReadOnlyList<PdfPaginaTexto>? IAncorarPdfSmartClickContext.PdfPaginasCache => _pdfPaginasCache;
    IEnumerable<AncorarPdfAncoraItemViewModel> IAncorarPdfSmartClickContext.Ancoras => Ancoras;
    int IAncorarPdfSmartClickContext.AncorasCount => Ancoras.Count;
    void IAncorarPdfSmartClickContext.SetDestaquesRelBboxCache(IReadOnlyList<BboxRelativo> bboxes) => _destaquesRelBboxCache = bboxes;
    void IAncorarPdfSmartClickContext.SetDestaqueCorCache(string cor) => _destaqueCorCache = cor;
    void IAncorarPdfSmartClickContext.SetDestaquesPaginaCache(int pagina) => _destaquesPaginaCache = Math.Max(1, pagina);
    void IAncorarPdfSmartClickContext.ReconstruirDestaquesSmart() => ReconstruirDestaquesSmart();
    List<AncorarPdfTemplateAncora> IAncorarPdfSmartClickContext.ConverterAncorasAtuais() =>
        AncorarPdfAncorasEditorState.ConverterAncorasAtuais(Ancoras);
    void IAncorarPdfSmartClickContext.AplicarEstadoComHistorico(List<AncorarPdfTemplateAncora> estado) => AplicarEstadoComHistorico(estado);
    string IAncorarPdfSmartClickContext.PdfModeloPath => PdfModeloPath;
    int IAncorarPdfSmartClickContext.PaginaPreviewAtual => PaginaPreviewAtual;
    AncorarPdfDocumentoAnalyzerOptions IAncorarPdfSmartClickContext.BuildDocumentoAnalyzerOptions() => BuildDocumentoAnalyzerOptions();

    public string TarefaIdTexto => TarefaId.HasValue ? $"Tarefa: #{TarefaId}" : "Nova tarefa";

    public bool PodeEditar => EstaAtiva && !SomenteLeitura && !IsBusy;
    public bool PodeDesfazer => PodeEditar && _ancorasEditorState.PodeUndo;
    public bool PodeRefazer => PodeEditar && _ancorasEditorState.PodeRedo;
    public bool PodeAdicionarAncora => PodeEditar && Ancoras.Count < AncorarPdfPalettePolicy.MaximoAncoras;
    public bool PodeRemoverAncoraSelecionada => PodeEditar && AncoraSelecionada is not null;
    public bool PodeAtivarPdfModeloCrossCliente => PodeEditar && SolicitanteEhAdmin;
    public bool PodeEditarJustificativaCrossCliente => PodeAtivarPdfModeloCrossCliente && PdfModeloCrossCliente;
    public bool PodeEditarOcrParametros => PodeEditar && OcrFallbackAtivo;
    public bool TemAncoraSelecionada => AncoraSelecionada is not null;
    public bool FerramentaNavegarAtiva => FerramentaInteracaoSelecionada == AncorarPdfFerramentaInteracao.Navegar;
    public bool FerramentaSmartClickAtiva => FerramentaInteracaoSelecionada == AncorarPdfFerramentaInteracao.SmartClick;
    public bool FerramentaRetanguloManualAtiva => FerramentaInteracaoSelecionada == AncorarPdfFerramentaInteracao.RetanguloManual;
    public string FerramentaInteracaoTitulo => AncorarPdfFerramentaInteracaoCatalogo.ObterTitulo(FerramentaInteracaoSelecionada);
    public string FerramentaInteracaoInstrucao => AncorarPdfFerramentaInteracaoCatalogo.ObterInstrucao(FerramentaInteracaoSelecionada);
    public string PreviewAdapterNome => _previewState.NomeAdapter;
    public bool PreviewRenderizadoDisponivel => _previewState.PreviewRenderizadoDisponivel;
    public bool PreviewFallbackAtivo => _previewState.PreviewFallbackAtivo;
    public AncorarPdfPreviewDocumentoState PreviewSnapshot => _previewState.Snapshot;
    public double PreviewSurfaceWidth => AncorarPdfPreviewLayout.BaseWidth * ZoomPreview;
    public double PreviewSurfaceHeight => AncorarPdfPreviewLayout.BaseHeight * ZoomPreview;
    public double ZoomPreviewPercentual => ZoomPreview * 100.0;
    public string PaginaPreviewResumo => $"{PaginaPreviewAtual}/{TotalPaginasPreview}";
    public bool PodeIrPaginaAnterior => PaginaPreviewAtual > 1;
    public bool PodeIrPaginaSeguinte => PaginaPreviewAtual < TotalPaginasPreview;
    public bool TemDiagnosticosPreview => DiagnosticosPaginas.Count > 0;
    public bool PodeExibirOverlaysDiagnostico => _featureDiagnosticsOverlay && _documentoAnaliseAtual is not null;
    public bool PodeUsarMultipagina => _featureMultipageEditor;

    partial void OnTarefaIdChanged(int? value) => OnPropertyChanged(nameof(TarefaIdTexto));

    partial void OnIsBusyChanged(bool value)
    {
        AtualizarEstadoComandosEdicao();
    }

    partial void OnSomenteLeituraChanged(bool value)
    {
        AtualizarEstadoComandosEdicao();
    }

    partial void OnEstaAtivaChanged(bool value) => AtualizarEstadoComandosEdicao();

    partial void OnOcrFallbackAtivoChanged(bool value)
    {
        OnPropertyChanged(nameof(PodeEditarOcrParametros));
        if (!string.IsNullOrWhiteSpace(PdfModeloPath))
            _ = CarregarTextoPdfAsync(PdfModeloPath);
    }

    partial void OnOcrDpiChanged(int value)
    {
        if (!string.IsNullOrWhiteSpace(PdfModeloPath) && OcrFallbackAtivo)
            _ = CarregarTextoPdfAsync(PdfModeloPath);
    }

    partial void OnOcrLangChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(PdfModeloPath) && OcrFallbackAtivo)
            _ = CarregarTextoPdfAsync(PdfModeloPath);
    }

    partial void OnFerramentaInteracaoSelecionadaChanged(AncorarPdfFerramentaInteracao value)
    {
        if (value != AncorarPdfFerramentaInteracao.SmartClick)
            LimparDeteccaoSmartInterna(limparMensagem: false);

        StatusInteracaoPreview = AncorarPdfFerramentaInteracaoCatalogo.ObterInstrucao(value);
        OnPropertyChanged(nameof(FerramentaNavegarAtiva));
        OnPropertyChanged(nameof(FerramentaSmartClickAtiva));
        OnPropertyChanged(nameof(FerramentaRetanguloManualAtiva));
        OnPropertyChanged(nameof(FerramentaInteracaoTitulo));
        OnPropertyChanged(nameof(FerramentaInteracaoInstrucao));
    }

    partial void OnAncoraSelecionadaChanged(AncorarPdfAncoraItemViewModel? value)
    {
        if (value is not null && value.Pagina != PaginaPreviewAtual && value.Pagina >= 1 && value.Pagina <= TotalPaginasPreview)
            PaginaPreviewAtual = value.Pagina;

        AtualizarDestaquePreview(value);
        foreach (var item in Ancoras)
            item.SelecionadaNaLista = ReferenceEquals(item, value);

        OnPropertyChanged(nameof(TemAncoraSelecionada));
        OnPropertyChanged(nameof(PodeRemoverAncoraSelecionada));
        RemoverAncoraSelecionadaCommand.NotifyCanExecuteChanged();
    }

    partial void OnHighlightOpacityChanged(double value)
    {
        var opacityClamp = Math.Clamp(value, 0.30, 0.45);
        _previewState.AtualizarAncoras(AncorarPdfAncorasEditorState.ConverterAncorasAtuais(Ancoras), opacityClamp);
        foreach (var ancora in Ancoras)
            ancora.HighlightOpacity = opacityClamp;
        AtualizarEstadoPreviewBindings();
    }

    partial void OnZoomPreviewChanged(double value)
    {
        var zoomNormalizado = Math.Clamp(value, PreviewZoomMin, PreviewZoomMax);
        if (Math.Abs(zoomNormalizado - value) > 0.0001)
        {
            ZoomPreview = zoomNormalizado;
            return;
        }

        _previewState.AtualizarViewport(PreviewSnapshot.Viewport with { Zoom = zoomNormalizado });
        foreach (var ancora in Ancoras)
            ancora.AtualizarZoomPreview(zoomNormalizado);

        OnPropertyChanged(nameof(PreviewSurfaceWidth));
        OnPropertyChanged(nameof(PreviewSurfaceHeight));
        OnPropertyChanged(nameof(ZoomPreviewPercentual));
        AtualizarEstadoPreviewBindings();
        AtualizarTokensPreview();
        ReconstruirDestaquesSmart();

        // Agendamento de upgrade de DPI quando zoom excede cobertura do tier atual
        if (_previewState.DeveAgendarUpgrade(ZoomPreview, PdfModeloPath))
            _ = AgendarUpgradeRenderAsync();
    }

    partial void OnPaginaPreviewAtualChanged(int value)
    {
        var paginaNormalizada = Math.Clamp(value, 1, Math.Max(1, TotalPaginasPreview));
        if (paginaNormalizada != value)
        {
            PaginaPreviewAtual = paginaNormalizada;
            return;
        }

        AtualizarPaginaPreviewSnapshot();
        AtualizarTokensPreview();
        ReconstruirDestaquesSmart();

        if (!string.IsNullOrWhiteSpace(PdfModeloPath))
        {
            _ = RenderizarPdfAsync(PdfModeloPath);
            if (_featureMultipageEditor)
                _ = PrefetchPaginasAdjacentesAsync();
        }

        OnPropertyChanged(nameof(PaginaPreviewResumo));
        OnPropertyChanged(nameof(PodeIrPaginaAnterior));
        OnPropertyChanged(nameof(PodeIrPaginaSeguinte));
        OnPropertyChanged(nameof(AncorasNaPaginaAtual));
        OnPropertyChanged(nameof(TemAncorasNaPaginaAtual));
        IrPaginaAnteriorCommand.NotifyCanExecuteChanged();
        IrPaginaSeguinteCommand.NotifyCanExecuteChanged();
    }

    partial void OnTotalPaginasPreviewChanged(int value)
    {
        AtualizarPaginaPreviewSnapshot();
        OnPropertyChanged(nameof(PaginaPreviewResumo));
        OnPropertyChanged(nameof(PodeIrPaginaAnterior));
        OnPropertyChanged(nameof(PodeIrPaginaSeguinte));
        OnPropertyChanged(nameof(TemDiagnosticosPreview));
        OnPropertyChanged(nameof(AncorasNaPaginaAtual));
        OnPropertyChanged(nameof(TemAncorasNaPaginaAtual));
        IrPaginaAnteriorCommand.NotifyCanExecuteChanged();
        IrPaginaSeguinteCommand.NotifyCanExecuteChanged();
    }

    partial void OnMostrarTokensNativosChanged(bool value) => AtualizarTokensPreview();
    partial void OnMostrarTokensOcrChanged(bool value) => AtualizarTokensPreview();
    partial void OnMostrarDiagnosticosPreviewChanged(bool value) => AtualizarTokensPreview();
    partial void OnMostrarHeatmapConfiancaChanged(bool value) => AtualizarTokensPreview();

    [RelayCommand] private void ZoomIn()    => ZoomPreview = Math.Min(4.0, Math.Round(ZoomPreview + 0.25, 2));
    [RelayCommand] private void ZoomOut()   => ZoomPreview = Math.Max(0.5, Math.Round(ZoomPreview - 0.25, 2));
    [RelayCommand] private void ZoomReset() => ZoomPreview = 1.0;
    [RelayCommand] private void AlternarPainelAvancado() => PainelAvancadoAberto = !PainelAvancadoAberto;
    [RelayCommand(CanExecute = nameof(PodeIrPaginaAnterior))]
    private void IrPaginaAnterior() => PaginaPreviewAtual = Math.Max(1, PaginaPreviewAtual - 1);
    [RelayCommand(CanExecute = nameof(PodeIrPaginaSeguinte))]
    private void IrPaginaSeguinte() => PaginaPreviewAtual = Math.Min(TotalPaginasPreview, PaginaPreviewAtual + 1);

    partial void OnPdfModeloCrossClienteChanged(bool value)
    {
        if (value && !SolicitanteEhAdmin)
        {
            PdfModeloCrossCliente = false;
            Mensagem = "Uso de PDF modelo cross-cliente permitido somente para Admin.";
            _registrarEvento("ancorar_pdf_c2_cross_cliente_modelo", "permitido=false motivo=nao_admin");
            return;
        }

        if (!value)
            PdfModeloCrossClienteJustificativa = string.Empty;

        OnPropertyChanged(nameof(PodeEditarJustificativaCrossCliente));

        if (value)
            _registrarEvento("ancorar_pdf_c2_cross_cliente_modelo", "permitido=true");
    }

    partial void OnSolicitanteEhAdminChanged(bool value)
    {
        if (!value && PdfModeloCrossCliente)
            PdfModeloCrossCliente = false;

        OnPropertyChanged(nameof(PodeAtivarPdfModeloCrossCliente));
        OnPropertyChanged(nameof(PodeEditarJustificativaCrossCliente));
    }

    public AncorarPdfConfiguracaoViewModel(
        IAncorarPdfConfiguracaoService service,
        IAncorarPdfFilePicker filePicker,
        Action fecharModal,
        Action<string, string?> registrarEvento,
        Action<string, long, string, string?> registrarMetrica,
        Action<AncorarPdfConfiguracaoTarefa>? onSaveSucesso = null,
        TimeProvider? timeProvider = null)
        : this(
            service,
            filePicker,
            previewAdapter: null,
            fecharModal,
            registrarEvento,
            registrarMetrica,
            onSaveSucesso,
            timeProvider)
    {
    }

    public AncorarPdfConfiguracaoViewModel(
        IAncorarPdfConfiguracaoService service,
        IAncorarPdfFilePicker filePicker,
        IAncorarPdfPreviewAdapter? previewAdapter,
        Action fecharModal,
        Action<string, string?> registrarEvento,
        Action<string, long, string, string?> registrarMetrica,
        Action<AncorarPdfConfiguracaoTarefa>? onSaveSucesso = null,
        TimeProvider? timeProvider = null,
        IAncorarPdfSmartDetector? smartDetector = null,
        IAncorarPdfExtratorTexto? extratorTexto = null,
        IAncorarPdfDocumentoAnalyzer? documentAnalyzer = null,
        AncorarPdfDocumentoAnalyzerOptions? documentAnalyzerOptions = null,
        IPdfPreviewRenderer? renderer = null)
    {
        _service = service;
        _filePicker = filePicker;
        _previewAdapter = previewAdapter ?? new AncorarPdfCanvasFallbackPreviewAdapter();
        _previewState = new AncorarPdfPreviewState(_previewAdapter, renderer);
        _fecharModal = fecharModal;
        _registrarEvento = registrarEvento;
        _registrarMetrica = registrarMetrica;
        _onSaveSucesso = onSaveSucesso;
        _timeProvider = timeProvider ?? TimeProvider.System;
        var analyzerOptions = documentAnalyzerOptions ?? new AncorarPdfDocumentoAnalyzerOptions();
        _featureAnalyzerV2 = analyzerOptions.AnalyzerV2Ativo;
        _featureHybridOcr = analyzerOptions.HybridOcrAtivo;
        _featureMultipageEditor = analyzerOptions.MultipageEditorAtivo;
        _featureAdvancedAnchors = analyzerOptions.AdvancedAnchorsAtivo;
        _featureDiagnosticsOverlay = analyzerOptions.DiagnosticsOverlayAtivo;
        _featureCloudDocumentAiProvider = analyzerOptions.CloudDocumentAiProviderAtivo;
        _lifecycleHandler = new AncorarPdfLifecycleHandler(_service, _agendamentoState, _ancorasEditorState, _previewState, _timeProvider, registrarEvento, this);
        _pdfRenderHandler = new AncorarPdfPdfRenderHandler(_previewState, extratorTexto, documentAnalyzer, this, registrarEvento);
        _saveHandler = new AncorarPdfSaveHandler(_service, _saveOrchestrator, _agendamentoState, registrarEvento, registrarMetrica, this);
        _previewHandler = new AncorarPdfPreviewInteractionHandler(_previewState, _previewAdapter, registrarEvento, this);
        _ancorasHandler = new AncorarPdfAncorasInteractionHandler(_ancorasEditorState, registrarEvento, registrarMetrica, this);
        _smartClickHandler = new AncorarPdfSmartClickHandler(smartDetector, documentAnalyzer, registrarEvento, this);
        _smartDetector = smartDetector;
        _documentAnalyzer = documentAnalyzer;
        _extratorTextoSmart = extratorTexto;

        _previewAdapter.SelecaoCapturada += OnPreviewSelecaoCapturada;

        Reset();
    }

    /// <summary>
    /// Substitui o FilePicker pelo picker real (StorageProvider) após o TopLevel estar disponível.
    /// Deve ser chamado pela View no evento AttachedToVisualTree.
    /// </summary>
    public void AtualizarFilePicker(IAncorarPdfFilePicker filePicker)
    {
        _filePicker = filePicker ?? throw new ArgumentNullException(nameof(filePicker));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _previewAdapter.SelecaoCapturada -= OnPreviewSelecaoCapturada;
        _previewState.Dispose();
        _saveOrchestrator.Dispose();
        PdfPageBitmap?.Dispose();
        PdfPageBitmap = null;
    }

    /// <summary>Sincroniza a posição de scroll do preview com os valores normalizados [0,1].</summary>
    public void AtualizarViewportPreview(double scrollXRel, double scrollYRel)
    {
        var viewportAtual = PreviewSnapshot.Viewport;
        _previewState.AtualizarViewport(viewportAtual with
        {
            ScrollXRel = Math.Clamp(scrollXRel, 0, 1),
            ScrollYRel = Math.Clamp(scrollYRel, 0, 1)
        });
        AtualizarEstadoPreviewBindings();
    }

    /// <summary>Inicializa a VM para nova tarefa a partir de drag-and-drop na esteira.</summary>
    public void IniciarNovaPorDrop(
        NovaTarefaDropPayload payload,
        int clienteId,
        int solicitanteUserId,
        string solicitanteNome,
        bool somenteLeitura,
        bool solicitanteEhAdmin = false) =>
        _lifecycleHandler.IniciarNovaPorDrop(payload, clienteId, solicitanteUserId, solicitanteNome, somenteLeitura, solicitanteEhAdmin);

    /// <summary>Carrega tarefa existente para edição ou leitura.</summary>
    public async Task AbrirExistenteAsync(
        Tarefa tarefa,
        int solicitanteUserId,
        string solicitanteNome,
        bool somenteLeitura,
        bool solicitanteEhAdmin = false) =>
        await _lifecycleHandler.AbrirExistenteAsync(tarefa, solicitanteUserId, solicitanteNome, somenteLeitura, solicitanteEhAdmin);

    public void Reset() => _lifecycleHandler.Reset();

    public void DefinirMensagemInformativa(string mensagem)
    {
        Mensagem = mensagem;
    }

    [RelayCommand]
    private void SelecionarCor(string? corHex) => _previewHandler.SelecionarCor(corHex);

    [RelayCommand]
    private void SelecionarFerramentaInteracao(AncorarPdfFerramentaInteracao ferramenta) =>
        _previewHandler.SelecionarFerramentaInteracao(ferramenta);

    [RelayCommand]
    private void RegistrarSelecaoPreview(AncorarPdfPreviewSelection? selecao)
    {
        if (selecao is null)
            return;

        _previewAdapter.SimularSelecaoUsuario(selecao);
    }

    private void OnPreviewSelecaoCapturada(object? sender, AncorarPdfPreviewSelection selecao) =>
        _previewHandler.OnPreviewSelecaoCapturada(selecao);

    [RelayCommand(CanExecute = nameof(PodeAdicionarAncora))]
    private void AdicionarAncora() => _ancorasHandler.AdicionarAncora();

    [RelayCommand]
    private void ConfirmarSubstituicaoCor(string substituirRaw) => _previewHandler.ConfirmarSubstituicaoCor(substituirRaw);

    [RelayCommand]
    private void RemoverAncora(AncorarPdfAncoraItemViewModel? ancora) => _ancorasHandler.RemoverAncora(ancora);

    [RelayCommand(CanExecute = nameof(PodeRemoverAncoraSelecionada))]
    private void RemoverAncoraSelecionada() => RemoverAncora(AncoraSelecionada);

    [RelayCommand]
    private void SelecionarAncora(AncorarPdfAncoraItemViewModel? ancora) => _ancorasHandler.SelecionarAncora(ancora);

    [RelayCommand]
    private void DestacarAncoraPorHover(AncorarPdfAncoraItemViewModel? ancora) => _ancorasHandler.DestacarAncoraPorHover(ancora);

    [RelayCommand(CanExecute = nameof(PodeDesfazer))]
    private void Desfazer() => _ancorasHandler.Desfazer();

    [RelayCommand(CanExecute = nameof(PodeRefazer))]
    private void Refazer() => _ancorasHandler.Refazer();

    [RelayCommand(CanExecute = nameof(PodeEditar))]
    private async Task SelecionarPastaMonitoradaAsync()
    {
        var path = await _filePicker.SelecionarPastaAsync();
        if (string.IsNullOrWhiteSpace(path))
            return;

        PastaMonitoradaPath = path;
    }

    [RelayCommand(CanExecute = nameof(PodeEditar))]
    private async Task SelecionarPdfModeloAsync()
    {
        var path = await _filePicker.SelecionarPdfModeloArquivoAsync();
        if (string.IsNullOrWhiteSpace(path))
            return;

        PdfModeloPath = path;
        PaginaPreviewAtual = 1;
        TotalPaginasPreview = 1;
        LimparDocumentoAnalisePreview();
        _previewState.AtualizarDocumento(path);
        AtualizarEstadoPreviewBindings();
        _ = RenderizarPdfAsync(path);
        _ = CarregarTextoPdfAsync(path);
    }

    [RelayCommand]
    private void Fechar()
    {
        // No modo wizard (apenas âncoras), o X do header deve cancelar o modo
        // e retornar ao passo 1 do wizard — não fechar o modal inteiro.
        if (ModoApenasAncoras)
        {
            CancelarModoAncoras();
            return;
        }
        _fecharModal();
    }

    /// <summary>
    /// Chamado pelo wizard ao entrar no Passo 2. Coloca a VM em modo restrito:
    /// apenas a seção de âncoras fica visível; ao concluir, chama onConcluido.
    /// </summary>
    /// <param name="pdfPath">Caminho para o PDF modelo já definido no Passo 1.</param>
    /// <param name="onConcluido">Callback invocado com o estado de âncoras ao clicar em "Concluir".</param>
    /// <param name="onCancelado">Callback opcional invocado ao cancelar ou fechar o modo âncoras.</param>
    public void EnterAnchorOnlyMode(string pdfPath, Action<AncorarPdfFluxoEstadoAncoras> onConcluido, Action? onCancelado = null) =>
        _lifecycleHandler.EnterAnchorOnlyMode(pdfPath, onConcluido, onCancelado);

    [RelayCommand]
    private void ConcluirModoAncoras() => _lifecycleHandler.ConcluirModoAncoras();

    [RelayCommand]
    private void CancelarModoAncoras() => _lifecycleHandler.CancelarModoAncoras();

    private async Task RenderizarPdfAsync(string? pdfPath, int dpi = 300, CancellationToken ct = default) =>
        await _pdfRenderHandler.RenderizarPdfAsync(pdfPath, dpi, ct);

    private async Task AgendarUpgradeRenderAsync() =>
        await _pdfRenderHandler.AgendarUpgradeRenderAsync();

    private async Task PrefetchPaginasAdjacentesAsync(CancellationToken ct = default) =>
        await _pdfRenderHandler.PrefetchPaginasAdjacentesAsync(ct);

    private void AplicarBitmapRenderizado(Bitmap? novoBitmap)
    {
        var anterior = PdfPageBitmap;
        PdfPageBitmap = novoBitmap;
        if (!ReferenceEquals(anterior, novoBitmap) && anterior is not null)
        {
            var paraDispor = anterior;
            Dispatcher.UIThread.Post(() => paraDispor.Dispose(), DispatcherPriority.Background);
        }
    }

    [RelayCommand(CanExecute = nameof(PodeEditar))]
    private async Task SalvarConfiguracaoAsync() => await _saveHandler.SalvarAsync();

    // ── Smart Click (C12) ─────────────────────────────────────────────────────

    [RelayCommand]
    private async Task ExecutarSmartClick(SmartDeteccaoEntrada entrada) =>
        await _smartClickHandler.ExecutarSmartClickAsync(entrada);

    [RelayCommand(CanExecute = nameof(PodeEditar))]
    private void ConfirmarSmartDeteccao() =>
        _smartClickHandler.ConfirmarSmartDeteccao();

    [RelayCommand]
    private void CancelarSmartDeteccao() =>
        _smartClickHandler.CancelarSmartDeteccao();

    private async Task CarregarTextoPdfAsync(string pdfPath) =>
        await _pdfRenderHandler.CarregarTextoPdfAsync(pdfPath);

    private void ReconstruirDestaquesSmart()
    {
        DestaquesSmart.Clear();
        if (_destaquesRelBboxCache.Count > 0 && _destaquesPaginaCache == PaginaPreviewAtual)
        {
            var alpha = ((int)Math.Round(Math.Clamp(HighlightOpacity, 0.30, 0.45) * 255)).ToString("X2");
            var cor = _destaqueCorCache.Length == 7
                ? $"#{alpha}{_destaqueCorCache[1..]}" : "#55FFD700";
            foreach (var bbox in _destaquesRelBboxCache)
                DestaquesSmart.Add(new TextoDestaqueSmartItem(
                    bbox.X * AncorarPdfPreviewLayout.BaseWidth * ZoomPreview,
                    bbox.Y * AncorarPdfPreviewLayout.BaseHeight * ZoomPreview,
                    bbox.Largura * AncorarPdfPreviewLayout.BaseWidth * ZoomPreview,
                    bbox.Altura * AncorarPdfPreviewLayout.BaseHeight * ZoomPreview,
                    cor));
        }
        OnPropertyChanged(nameof(QuantidadeDestaquesSmartTexto));
    }

    // ── Fim Smart Click ───────────────────────────────────────────────────────

    private void AplicarEstadoComHistorico(IReadOnlyList<AncorarPdfTemplateAncora> novoEstado)
    {
        _ancorasEditorState.AplicarNovoEstado(novoEstado);
        AplicarEstadoAncoras(_ancorasEditorState.EstadoAtual, registrarHistorico: false);
    }

    private void AplicarEstadoAncoras(IReadOnlyList<AncorarPdfTemplateAncora> estado, bool registrarHistorico)
    {
        var corSelecionadaAntes = AncoraSelecionada?.CorHex;

        Ancoras.Clear();
        foreach (var ancora in estado.OrderBy(a => a.Ordem))
        {
            Ancoras.Add(AncorarPdfAncoraItemViewModel.FromModel(ancora, ZoomPreview, HighlightOpacity));
        }

        // Preserva seleção por cor após undo/redo; cai na primeira se não encontrar.
        AncoraSelecionada = corSelecionadaAntes is not null
            ? Ancoras.FirstOrDefault(a => string.Equals(a.CorHex, corSelecionadaAntes, StringComparison.OrdinalIgnoreCase))
                ?? (Ancoras.Count > 0 ? Ancoras[0] : null)
            : (Ancoras.Count > 0 ? Ancoras[0] : null);

        AtualizarEstadoComandosEdicao();
        _previewState.AtualizarAncoras(estado, HighlightOpacity);
        AtualizarEstadoPreviewBindings();
        OnPropertyChanged(nameof(AncorasNaPaginaAtual));
        OnPropertyChanged(nameof(TemAncorasNaPaginaAtual));
        AtualizarDestaquePreview(AncoraSelecionada);

        if (registrarHistorico)
            _ancorasEditorState.DefinirEstadoInicial(estado);
    }

    private void AtualizarEstadoPreviewBindings()
    {
        OnPropertyChanged(nameof(PreviewRenderizadoDisponivel));
        OnPropertyChanged(nameof(PreviewFallbackAtivo));
        OnPropertyChanged(nameof(PreviewSnapshot));
        OnPropertyChanged(nameof(PaginaPreviewResumo));
        OnPropertyChanged(nameof(PodeIrPaginaAnterior));
        OnPropertyChanged(nameof(PodeIrPaginaSeguinte));
        OnPropertyChanged(nameof(TemDiagnosticosPreview));
        OnPropertyChanged(nameof(PodeExibirOverlaysDiagnostico));
        OnPropertyChanged(nameof(PodeUsarMultipagina));
        OnPropertyChanged(nameof(TemAncorasNaPaginaAtual));
    }

    private void AtualizarDestaquePreview(AncorarPdfAncoraItemViewModel? ancora)
    {
        var chave = ancora?.ChaveTecnica;
        _previewState.DestacarAncora(chave);

        foreach (var item in Ancoras)
        {
            item.DestacadaNoPreview = !string.IsNullOrWhiteSpace(chave) &&
                string.Equals(item.ChaveTecnica, chave, StringComparison.OrdinalIgnoreCase);
        }

        AtualizarEstadoPreviewBindings();
    }

    private void LimparDeteccaoSmartInterna(bool limparMensagem)
    {
        SmartDeteccaoAtual = null;
        _destaquesRelBboxCache = [];
        _destaquesPaginaCache = PaginaPreviewAtual;
        DestaquesSmart.Clear();
        OnPropertyChanged(nameof(QuantidadeDestaquesSmartTexto));

        if (limparMensagem)
            Mensagem = string.Empty;
    }

    private void AtualizarEstadoComandosEdicao()
    {
        OnPropertyChanged(nameof(PodeEditar));
        OnPropertyChanged(nameof(PodeDesfazer));
        OnPropertyChanged(nameof(PodeRefazer));
        OnPropertyChanged(nameof(PodeAdicionarAncora));
        OnPropertyChanged(nameof(PodeRemoverAncoraSelecionada));
        OnPropertyChanged(nameof(PodeAtivarPdfModeloCrossCliente));
        OnPropertyChanged(nameof(PodeEditarJustificativaCrossCliente));
        OnPropertyChanged(nameof(PodeEditarOcrParametros));
        OnPropertyChanged(nameof(TemAncoraSelecionada));

        SalvarConfiguracaoCommand.NotifyCanExecuteChanged();
        SelecionarPastaMonitoradaCommand.NotifyCanExecuteChanged();
        SelecionarPdfModeloCommand.NotifyCanExecuteChanged();
        AdicionarAncoraCommand.NotifyCanExecuteChanged();
        DesfazerCommand.NotifyCanExecuteChanged();
        RefazerCommand.NotifyCanExecuteChanged();
        RemoverAncoraSelecionadaCommand.NotifyCanExecuteChanged();
        ConfirmarSmartDeteccaoCommand.NotifyCanExecuteChanged();
        IrPaginaAnteriorCommand.NotifyCanExecuteChanged();
        IrPaginaSeguinteCommand.NotifyCanExecuteChanged();
    }

    private void AtualizarPaginaPreviewSnapshot()
    {
        _previewState.AtualizarPagina(PaginaPreviewAtual, TotalPaginasPreview);
        AtualizarEstadoPreviewBindings();
    }

    private AncorarPdfDocumentoAnalyzerOptions BuildDocumentoAnalyzerOptions() =>
        new()
        {
            AnalyzerV2Ativo = _featureAnalyzerV2,
            HybridOcrAtivo = _featureHybridOcr && OcrFallbackAtivo,
            MultipageEditorAtivo = _featureMultipageEditor,
            AdvancedAnchorsAtivo = _featureAdvancedAnchors,
            DiagnosticsOverlayAtivo = _featureDiagnosticsOverlay,
            CloudDocumentAiProviderAtivo = _featureCloudDocumentAiProvider,
            OcrDpi = Math.Clamp(OcrDpi, 150, 600),
            OcrLang = string.IsNullOrWhiteSpace(OcrLang) ? "por+eng" : OcrLang.Trim().ToLowerInvariant()
        };

    private void AtualizarDiagnosticosPreview()
    {
        DiagnosticosPaginas.Clear();
        if (_documentoAnaliseAtual is null)
        {
            DiagnosticoPaginaAtualResumo = string.Empty;
            return;
        }

        foreach (var diagnostico in _documentoAnaliseAtual.Diagnosticos.OrderBy(x => x.Pagina))
        {
            DiagnosticosPaginas.Add(new AncorarPdfPaginaDiagnosticoItemViewModel
            {
                Pagina = diagnostico.Pagina,
                Rota = diagnostico.RotaExtracao.ToString(),
                Motivo = diagnostico.MotivoRota,
                OcrExecutado = diagnostico.OcrExecutado,
                BaixaCobertura = diagnostico.CoberturaTextoNativo < 0.005,
                Resumo = $"p{diagnostico.Pagina} {diagnostico.RotaExtracao} nativo={diagnostico.TokensNativos} ocr={diagnostico.TokensOcr} cobertura={diagnostico.CoberturaTextoNativo:0.000}"
            });
        }

        AtualizarResumoDiagnosticoPaginaAtual();
        OnPropertyChanged(nameof(TemDiagnosticosPreview));
    }

    private void LimparDocumentoAnalisePreview()
    {
        _pdfPaginasCache = null;
        _documentoAnaliseAtual = null;
        TokensPreview.Clear();
        DiagnosticosPaginas.Clear();
        DiagnosticoPaginaAtualResumo = string.Empty;
        _destaquesRelBboxCache = [];
        _destaquesPaginaCache = 1;
        DestaquesSmart.Clear();
        OnPropertyChanged(nameof(QuantidadeDestaquesSmartTexto));
        OnPropertyChanged(nameof(TemDiagnosticosPreview));
    }

    private void AtualizarResumoDiagnosticoPaginaAtual()
    {
        if (_documentoAnaliseAtual is null)
        {
            DiagnosticoPaginaAtualResumo = string.Empty;
            return;
        }

        var diagnostico = _documentoAnaliseAtual.Diagnosticos.FirstOrDefault(x => x.Pagina == PaginaPreviewAtual);
        DiagnosticoPaginaAtualResumo = diagnostico is null
            ? string.Empty
            : $"Página {diagnostico.Pagina}: {diagnostico.RotaExtracao} | nativo={diagnostico.TokensNativos} | ocr={diagnostico.TokensOcr} | cobertura={diagnostico.CoberturaTextoNativo:0.000}";
    }

    private void AtualizarTokensPreview()
    {
        TokensPreview.Clear();
        AtualizarResumoDiagnosticoPaginaAtual();

        if (_documentoAnaliseAtual is null || !_featureDiagnosticsOverlay)
            return;

        var pagina = _documentoAnaliseAtual.ObterPagina(PaginaPreviewAtual);
        if (pagina is null)
            return;

        foreach (var token in pagina.Tokens)
        {
            if (token.Origem == AncorarPdfTextoOrigem.Nativo && !MostrarTokensNativos)
                continue;
            if (token.Origem != AncorarPdfTextoOrigem.Nativo && !MostrarTokensOcr)
                continue;

            TokensPreview.Add(new AncorarPdfTokenPreviewItemViewModel
            {
                Texto = token.Texto,
                Origem = token.Origem,
                ConfiancaFonte = token.ConfiancaFonte,
                OverlayOpacity = MostrarHeatmapConfianca
                    ? Math.Clamp(0.12 + (token.ConfiancaFonte * 0.78), 0.12, 0.92)
                    : 0.30,
                PreviewX = token.Bbox.X * AncorarPdfPreviewLayout.BaseWidth * ZoomPreview,
                PreviewY = token.Bbox.Y * AncorarPdfPreviewLayout.BaseHeight * ZoomPreview,
                PreviewLargura = token.Bbox.Largura * AncorarPdfPreviewLayout.BaseWidth * ZoomPreview,
                PreviewAltura = token.Bbox.Altura * AncorarPdfPreviewLayout.BaseHeight * ZoomPreview
            });
        }
    }

}
