using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Protons.Core.Clientes.Services;
using Protons.Core.Login.Models;
using Protons.Core.Login.Services;
using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Services;
using Protons.Infrastructure.Tarefas.Services;
using Protons.UI.Login.Models;
using Protons.UI.Login.Services;
using Protons.UI.Login.ViewModels;
using Protons.UI.Painel.Funcionalidades.Ferramentas.AncorarPdf.Interface;
using Protons.UI.Painel.Funcionalidades.Ferramentas.AncorarPdf.ModelosDeVisao;

namespace Protons.UI.Painel.ViewModels;

public sealed partial class PainelViewModel : ViewModelBase, IDisposable
{
    private bool _disposed;
    private readonly IAuthService _authService;
    private readonly LocalSettings _settings;
    private readonly IAuditLogQueryService _auditLogQuery;
    private readonly IClienteService _clienteService;
    private readonly ITarefaService _tarefaService;
    private readonly IUserDirectoryService _userDirectoryService;
    private readonly IAncorarPdfConfiguracaoService _ancorarPdfConfiguracaoService;
    private readonly Action<ViewModelBase> _navigate;
    private readonly int _userId;
    private readonly string _email;
    private readonly TimeSpan _timeoutSessaoInatividade;
    private readonly DispatcherTimer _timerSessaoInatividade;
    private readonly DispatcherTimer _timerHoraComputador;
    private DateTime _ultimaInteracaoPainelUtc = DateTime.UtcNow;

    [ObservableProperty] private string? _nome;
    [ObservableProperty] private string? _perfil;
    [ObservableProperty] private bool _isAdmin;
    [ObservableProperty] private bool _isSupremo;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _notificacoesAbertas;
    [ObservableProperty] private UserSummary? _pendenciaAtual;
    [ObservableProperty] private string? _mensagemPendencias;
    [ObservableProperty] private UserSummary? _orfaoAtual;
    [ObservableProperty] private string? _mensagemOrfaos;
    [ObservableProperty] private string _buscaTexto = string.Empty;
    [ObservableProperty] private string _statusSessao = "Sessão ativa.";
    [ObservableProperty] private string _horaAtualComputadorTexto = "Hora Atual do Computador: --:--";
    [ObservableProperty] private string _horaAtualComputadorValor = "--:--";
    [ObservableProperty] private int _notasHoje = 0;
    [ObservableProperty] private int _errosCriticos = 0;

    public string Email => _email;
    public string Saudacao => string.IsNullOrWhiteSpace(Nome) ? "Bem-vindo!" : $"Bem-vindo, {Nome}";
    public string NomeInicial => string.IsNullOrWhiteSpace(Nome) ? "?" : Nome[..1].ToUpperInvariant();

    public ObservableCollection<UserSummary> Pendentes { get; } = new();

    /// <summary>
    ///   Usuários cujo admin responsável foi excluído e que foram transferidos automaticamente
    ///   para o Supremo. O Supremo pode promover um deles a Admin ou mantê-los sob sua gestão.
    /// </summary>
    public ObservableCollection<UserSummary> OrfaosAdminExcluido { get; } = new();
    public AncorarPdfConfiguracaoViewModel AncorarPdfConfiguracao { get; }
    public AncorarPdfAgendamentoBasicoViewModel AncorarPdfAgendamentoBasico { get; }

    /// <summary>
    /// Controla visibilidade do modal básico do wizard (Passo 1 + 3).
    /// Passo 2 (editor de âncoras) é controlado por ConfiguracaoAncorarPdfVisivel.
    /// </summary>
    public bool AgendamentoBasicoAberto =>
        AncorarPdfAgendamentoBasico.EstaAtivo &&
        AncorarPdfAgendamentoBasico.EtapaAtual != AncorarPdfEtapaFluxo.AncorasTelaCheia;

    /// <summary>
    /// True quando o editor de âncoras deve ser exibido em tela cheia (Passo 2 do wizard).
    /// </summary>
    public bool ConfiguracaoAncorarPdfVisivelWizard =>
        AncorarPdfAgendamentoBasico.EstaAtivo &&
        AncorarPdfAgendamentoBasico.EtapaAtual == AncorarPdfEtapaFluxo.AncorasTelaCheia;

    /// <summary>
    /// Binding combinado para a AncorarPdfConfiguracaoView:
    /// mostra tanto ao editar tarefa existente quanto no Passo 2 do wizard.
    /// </summary>
    public bool ConfiguracaoAncorarPdfVisivel =>
        ConfiguracaoTarefaEhAncorarPdf || ConfiguracaoAncorarPdfVisivelWizard;

    /// <summary>
    /// Controla visibilidade do overlay ZIndex=105 (container do modal de configuração).
    /// Inclui tanto edição normal quanto o Passo 2 do wizard (editor de âncoras em tela cheia).
    /// </summary>
    public bool ModalConfiguracaoTarefaVisivel => ConfiguracaoTarefaAberta;

    /// <summary>
    /// Conecta o FilePicker real ao ViewModel de configuração do AncorarPdf.
    /// Deve ser chamado pela PainelView após o TopLevel estar disponível (AttachedToVisualTree).
    /// </summary>
    public void ConfigurarFilePickerAncorarPdf(Func<TopLevel?> resolverTopLevel)
    {
        var picker = new AncorarPdfStorageProviderFilePicker(resolverTopLevel);
        AncorarPdfConfiguracao.AtualizarFilePicker(picker);
        AncorarPdfAgendamentoBasico.AtualizarFilePicker(new AncorarPdfStorageProviderFilePicker(resolverTopLevel));
    }

    /// <summary>
    /// Lê se o fallback Ghostscript deve ser usado. Env var ANCORA_PDF_PREVIEW_USE_GHOSTSCRIPT
    /// (1/true = sim, 0/false = não). O AppSettingsLoader preenche essa env a partir de
    /// appsettings AncorarPdf.Preview.UseGhostscriptFallback na inicialização.
    /// </summary>
    private static bool LerUseGhostscriptFallback()
    {
        var env = Environment.GetEnvironmentVariable("ANCORA_PDF_PREVIEW_USE_GHOSTSCRIPT");
        if (string.IsNullOrWhiteSpace(env)) return true;
        var v = env.Trim();
        if (string.Equals(v, "0", StringComparison.Ordinal) ||
            string.Equals(v, "false", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    private static bool LerFlagBoolEnv(string nome, bool defaultValue)
    {
        var env = Environment.GetEnvironmentVariable(nome);
        if (string.IsNullOrWhiteSpace(env))
            return defaultValue;

        var valor = env.Trim();
        if (string.Equals(valor, "1", StringComparison.Ordinal) ||
            string.Equals(valor, "true", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(valor, "0", StringComparison.Ordinal) ||
            string.Equals(valor, "false", StringComparison.OrdinalIgnoreCase))
            return false;

        return defaultValue;
    }

    private static string GetTessDataPath()
    {
        var env = Environment.GetEnvironmentVariable("ANCORA_TESSDATA_PATH");
        if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env))
            return env;

        var baseDir = AppContext.BaseDirectory;
        var local = Path.Combine(baseDir, "tessdata");
        if (Directory.Exists(local))
            return local;

        if (OperatingSystem.IsWindows())
        {
            var progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var win = Path.Combine(progFiles, "Tesseract-OCR", "tessdata");
            if (Directory.Exists(win))
                return win;

            var winX86 = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Tesseract-OCR", "tessdata");
            if (Directory.Exists(winX86))
                return winX86;
        }
        else
        {
            var linux = "/usr/share/tesseract-ocr/5/tessdata";
            if (Directory.Exists(linux))
                return linux;

            var linuxAlt = "/usr/share/tessdata";
            if (Directory.Exists(linuxAlt))
                return linuxAlt;
        }

        return local;
    }

    private static AncorarPdfDocumentoAnalyzerOptions BuildAnalyzerOptionsFromEnvironment() =>
        new()
        {
            AnalyzerV2Ativo = LerFlagBoolEnv("PROTONS_ANCORAR_PDF_ANALYZER_V2", true),
            HybridOcrAtivo = LerFlagBoolEnv("PROTONS_ANCORAR_PDF_HYBRID_OCR", true),
            MultipageEditorAtivo = LerFlagBoolEnv("PROTONS_ANCORAR_PDF_MULTIPAGE_EDITOR", true),
            AdvancedAnchorsAtivo = LerFlagBoolEnv("PROTONS_ANCORAR_PDF_ADVANCED_ANCHORS", true),
            DiagnosticsOverlayAtivo = LerFlagBoolEnv("PROTONS_ANCORAR_PDF_DIAGNOSTICS_OVERLAY", true),
            CloudDocumentAiProviderAtivo = LerFlagBoolEnv("PROTONS_ANCORAR_PDF_CLOUD_DOCUMENT_AI_PROVIDER", false)
        };

    private IAncorarPdfPreviewAdapter CriarPreviewAdapterAncorarPdf()
    {
        var webViewAdapter = new AncorarPdfWebViewPreviewAdapter();
        if (webViewAdapter.SuportaDocumentoRenderizado)
            return webViewAdapter;

        RegistrarEventoPainel(
            "ancorar_pdf_c2_preview_fallback",
            $"adapter={webViewAdapter.NomeAdapter} motivo=webview_indisponivel");
        return new AncorarPdfCanvasFallbackPreviewAdapter();
    }

    public bool TemPendencias => Pendentes.Count > 0;
    public int QuantidadePendencias => Pendentes.Count;
    public int PendenciasKpi => QuantidadePendencias;
    // Notificações abertas quando há pendências OU órfãos aguardando decisão do Supremo.
    public bool TemNotificacoes => TemPendencias || TemOrfaos;
    public double NotificacoesOpacity => TemNotificacoes ? 1.0 : 0.45;
    public string NotificacoesBackground => TemNotificacoes ? NotificacoesBackgroundAtivo : NotificacoesBackgroundInativo;
    public string NotificacoesForeground => TemNotificacoes ? NotificacoesForegroundAtivo : NotificacoesForegroundInativo;
    public bool IsNotAdmin => !IsAdmin;
    public bool IsNotSupremo => !IsSupremo;
    public bool TemOrfaos => OrfaosAdminExcluido.Count > 0;

    partial void OnNomeChanged(string? value)
    {
        OnPropertyChanged(nameof(Saudacao));
        OnPropertyChanged(nameof(NomeInicial));
    }

    partial void OnIsSupremoChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotSupremo));
        if (value) Perfil = "Supremo";
    }

    partial void OnIsAdminChanged(bool value)
    {
        if (!IsSupremo)
            Perfil = value ? "Administrador" : "Usuário";
        OnPropertyChanged(nameof(IsNotAdmin));
        AbrirNotificacoesCommand.NotifyCanExecuteChanged();
        LiberarAcessoCommand.NotifyCanExecuteChanged();
        LiberarComoAdministradorCommand.NotifyCanExecuteChanged();
        NaoConhecoCommand.NotifyCanExecuteChanged();
        PromoverOrfaoAAdminCommand.NotifyCanExecuteChanged();
        ManterOrfaoComSupremoCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value)
    {
        AbrirNotificacoesCommand.NotifyCanExecuteChanged();
        LiberarAcessoCommand.NotifyCanExecuteChanged();
        LiberarComoAdministradorCommand.NotifyCanExecuteChanged();
        NaoConhecoCommand.NotifyCanExecuteChanged();
        PromoverOrfaoAAdminCommand.NotifyCanExecuteChanged();
        ManterOrfaoComSupremoCommand.NotifyCanExecuteChanged();
        SalvarClienteCommand.NotifyCanExecuteChanged();
        ConfirmarSalvarClienteCommand.NotifyCanExecuteChanged();
        SolicitarInativarClienteCommand.NotifyCanExecuteChanged();
        ConfirmarInativarClienteCommand.NotifyCanExecuteChanged();
        SalvarGrupoEmpresarialCommand.NotifyCanExecuteChanged();
    }

    public PainelViewModel(
        IAuthService authService,
        LocalSettings settings,
        IAuditLogQueryService auditLogQuery,
        IClienteService clienteService,
        ITarefaService tarefaService,
        IUserDirectoryService userDirectoryService,
        IAncorarPdfConfiguracaoService ancorarPdfConfiguracaoService,
        Action<ViewModelBase> navigate,
        int userId,
        string email,
        string? nome,
        UserRole? role,
        TimeProvider? timeProvider = null)
    {
        _authService = authService;
        _settings = settings;
        _auditLogQuery = auditLogQuery;
        _clienteService = clienteService;
        _tarefaService = tarefaService;
        _userDirectoryService = userDirectoryService;
        _ancorarPdfConfiguracaoService = ancorarPdfConfiguracaoService;
        _navigate = navigate;
        _userId = userId;
        _email = email;

        Nome = string.IsNullOrWhiteSpace(nome) ? email : nome;
        IsSupremo = role == UserRole.Supremo;
        IsAdmin = role == UserRole.Admin || role == UserRole.Supremo;
        Perfil = role switch
        {
            UserRole.Supremo => "Supremo",
            UserRole.Admin => "Administrador",
            _ => "Usuário"
        };
        _timeoutSessaoInatividade = LerTimeoutSessaoInatividade();
        _timerSessaoInatividade = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _timerSessaoInatividade.Tick += (_, _) => VerificarSessaoInatividade();
        _timerHoraComputador = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timerHoraComputador.Tick += (_, _) =>
        {
            AtualizarHoraAtualComputador();
            AtualizarHoraAtualRegua();
        };

        var previewAdapter = CriarPreviewAdapterAncorarPdf();

        // Cadeia de renderers PDF: PDFium (built-in) → Ghostscript (fallback opcional).
        // ANCORA_PDF_PREVIEW_USE_GHOSTSCRIPT=false ou appsettings AncorarPdf.Preview.UseGhostscriptFallback=false:
        //   apenas Docnet (zero dependência externa). PDFs que falharem no Docnet não terão fallback.
        var useGhostscript = LerUseGhostscriptFallback();
        var pdfRenderer = useGhostscript
            ? new AncorarPdfFallbackPreviewRenderer(
                [new AncorarPdfDocnetRenderer(), new AncorarPdfGhostscriptRenderer()])
            : new AncorarPdfFallbackPreviewRenderer([new AncorarPdfDocnetRenderer()]);
        var analyzerOptions = BuildAnalyzerOptionsFromEnvironment();
        var extratorBase = new AncorarPdfExtratorTextoPdfPig();
        var documentAnalyzer = new AncorarPdfDocumentoAnalyzer(
            extratorBase,
            (dpi, lang) => new AncorarPdfExtratorOcrTesseract(GetTessDataPath(), dpi, lang));
        var extratorCompat = new AncorarPdfExtratorTextoAnalyzerAdapter(
            documentAnalyzer,
            () =>
            {
                var config = AncorarPdfOcrConfigContext.Current;
                return analyzerOptions with
                {
                    HybridOcrAtivo = analyzerOptions.HybridOcrAtivo && (config?.OcrFallbackAtivo ?? false),
                    OcrDpi = config?.OcrDpi ?? analyzerOptions.OcrDpi,
                    OcrLang = string.IsNullOrWhiteSpace(config?.OcrLang) ? analyzerOptions.OcrLang : config!.OcrLang
                };
            });

        AncorarPdfConfiguracao = new AncorarPdfConfiguracaoViewModel(
            _ancorarPdfConfiguracaoService,
            new AncorarPdfNullFilePicker(),
            previewAdapter,
            () => FecharConfigTarefaCommand.Execute(null),
            (eventoId, contexto) => RegistrarEventoPainel(eventoId, contexto),
            (metricaId, valor, unidade, contexto) => RegistrarMetricaPainel(metricaId, valor, unidade, contexto),
            resultado =>
            {
                ConfiguracaoTarefaId = resultado.TarefaId;
                ConfiguracaoTarefaTitulo = resultado.NomeTarefaPersonalizado;
                ConfiguracaoTarefaSubtitulo = "Configuracao salva";
                ConfiguracaoTarefaDetalhes = $"tarefa_id={resultado.TarefaId} versao_template={resultado.VersaoTemplate}";
                DispararComSeguranca(CarregarTarefasClienteAsync(), "ancorar_pdf_c2_recarregar_tarefas_falha");
            },
            smartDetector: new AncorarPdfSmartDetector(),
            extratorTexto: extratorCompat,
            documentAnalyzer: documentAnalyzer,
            documentAnalyzerOptions: analyzerOptions,
            renderer: pdfRenderer);

        AncorarPdfAgendamentoBasico = new AncorarPdfAgendamentoBasicoViewModel(
            _ancorarPdfConfiguracaoService,
            _tarefaService,
            AncorarPdfConfiguracao,
            fecharWizard: () =>
            {
                OnPropertyChanged(nameof(AgendamentoBasicoAberto));
                OnPropertyChanged(nameof(ConfiguracaoAncorarPdfVisivelWizard));
                OnPropertyChanged(nameof(ConfiguracaoAncorarPdfVisivel));
                OnPropertyChanged(nameof(ModalConfiguracaoTarefaVisivel));
                OnPropertyChanged(nameof(ConfiguracaoTarefaEhGenerica));
                if (ClienteContextoId.HasValue)
                    DispararComSeguranca(CarregarTarefasClienteAsync(), "ancorar_pdf_c11_recarregar_tarefas_falha");
            },
            timeProvider: timeProvider);
        AncorarPdfAgendamentoBasico.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AncorarPdfAgendamentoBasicoViewModel.EstaAtivo)
                               or nameof(AncorarPdfAgendamentoBasicoViewModel.EtapaAtual))
            {
                OnPropertyChanged(nameof(AgendamentoBasicoAberto));
                OnPropertyChanged(nameof(ConfiguracaoAncorarPdfVisivelWizard));
                OnPropertyChanged(nameof(ConfiguracaoAncorarPdfVisivel));
                OnPropertyChanged(nameof(ModalConfiguracaoTarefaVisivel));
                OnPropertyChanged(nameof(ConfiguracaoTarefaEhGenerica));
            }
        };

        InicializarPlaceholders();
        Dispatcher.UIThread.Post(CarregarClientesPersistidos, DispatcherPriority.Background);
        RegistrarInteracaoPainel();
        AtualizarHoraAtualComputador();
        _timerHoraComputador.Start();
        _timerSessaoInatividade.Start();
        RegistrarEventoPainel("sessao_timeout_configurado", $"timeout_min={(int)_timeoutSessaoInatividade.TotalMinutes}");

        Pendentes.CollectionChanged += (_, _) => AtualizarEstadoPendencias();
        OrfaosAdminExcluido.CollectionChanged += (_, _) => AtualizarEstadoPendencias();
        if (IsAdmin) // IsAdmin é true para Admin e Supremo
        {
            // Carrega pendencias com pequeno atraso para priorizar o primeiro render do painel.
            DispararComSeguranca(Task.Run(async () =>
            {
                await Task.Delay(500);
                await Dispatcher.UIThread.InvokeAsync(CarregarPendenciasAsync);
            }), "pendencias_carga_inicial_falha");
        }
    }

    private bool PodeAbrirNotificacoes() => IsAdmin && TemNotificacoes && !IsBusy;
    private bool PodeDecidirPendencia() => IsAdmin && PendenciaAtual is not null && !IsBusy;
    private bool PodeDecidirOrfao() => IsSupremo && OrfaoAtual is not null && !IsBusy;

    private void SelecionarProximaPendencia()
    {
        PendenciaAtual = Pendentes.Count > 0 ? Pendentes[0] : null;
        if (!TemNotificacoes)
            NotificacoesAbertas = false;
        AtualizarEstadoPendencias();
    }

    private void SelecionarProximoOrfao()
    {
        OrfaoAtual = OrfaosAdminExcluido.Count > 0 ? OrfaosAdminExcluido[0] : null;
        if (!TemNotificacoes)
            NotificacoesAbertas = false;
        AtualizarEstadoPendencias();
    }

    private void AtualizarEstadoPendencias()
    {
        OnPropertyChanged(nameof(TemPendencias));
        OnPropertyChanged(nameof(QuantidadePendencias));
        OnPropertyChanged(nameof(PendenciasKpi));
        OnPropertyChanged(nameof(TemOrfaos));
        OnPropertyChanged(nameof(TemNotificacoes));
        OnPropertyChanged(nameof(NotificacoesOpacity));
        OnPropertyChanged(nameof(NotificacoesBackground));
        OnPropertyChanged(nameof(NotificacoesForeground));
        AbrirNotificacoesCommand.NotifyCanExecuteChanged();
        LiberarAcessoCommand.NotifyCanExecuteChanged();
        LiberarComoAdministradorCommand.NotifyCanExecuteChanged();
        NaoConhecoCommand.NotifyCanExecuteChanged();
        PromoverOrfaoAAdminCommand.NotifyCanExecuteChanged();
        ManterOrfaoComSupremoCommand.NotifyCanExecuteChanged();
    }

    private static TimeSpan LerTimeoutSessaoInatividade()
    {
        var raw = Environment.GetEnvironmentVariable("PROTONS_PAINEL_TIMEOUT_MINUTOS");
        if (!int.TryParse(raw, out var minutos))
            minutos = 30;

        if (minutos < 5)
            minutos = 5;
        if (minutos > 240)
            minutos = 240;

        return TimeSpan.FromMinutes(minutos);
    }

    private void AtualizarHoraAtualComputador()
    {
        HoraAtualComputadorValor = DateTime.Now.ToString("HH:mm");
        HoraAtualComputadorTexto = $"Hora Atual do Computador: {HoraAtualComputadorValor}";
    }

    private void VerificarSessaoInatividade()
    {
        var inativoPor = DateTime.UtcNow - _ultimaInteracaoPainelUtc;
        if (inativoPor < _timeoutSessaoInatividade)
            return;

        Dispose();
        StatusSessao = "Sessão encerrada por inatividade.";
        RegistrarEventoPainel("sessao_timeout", $"inativo_min={Math.Round(inativoPor.TotalMinutes, 1)}");

        try
        {
            _authService.RegistrarLogout(_userId, _email);
        }
        catch (Exception ex)
        {
            RegistrarErroPainel("sessao_timeout_logout_falha", ex);
        }

        _navigate(new LoginViewModel(
            _authService,
            _settings,
            _auditLogQuery,
            _clienteService,
            _tarefaService,
            _userDirectoryService,
            _ancorarPdfConfiguracaoService,
            _navigate));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _timerSessaoInatividade.Stop();
        _timerHoraComputador.Stop();
        AncorarPdfConfiguracao.Dispose();
        _execucaoAutomaticaSerial.Dispose();

        try
        {
            _buscaClientesCts?.Cancel();
            _buscaClientesCts?.Dispose();
            _buscaClientesCts = null;
            _cargaTarefasCts?.Cancel();
            _cargaTarefasCts?.Dispose();
            _cargaTarefasCts = null;
        }
        catch (ObjectDisposedException)
        {
            // Already disposed, safe to ignore.
        }
    }
}
