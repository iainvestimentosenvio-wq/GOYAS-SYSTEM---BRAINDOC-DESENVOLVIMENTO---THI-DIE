using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using System.Reflection;
using System.Security.Cryptography;
using Protons.Core.Clientes.Repositories;
using Protons.Core.Clientes.Security;
using Protons.Core.Clientes.Services;
using Protons.Core.Login.Models;
using Protons.Core.Login.Repositories;
using Protons.Core.Login.Services;
using Protons.Core.Tarefas.Repositories;
using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Services;
using Protons.Infrastructure.Clientes.Repositories;
using Protons.Infrastructure.Clientes.Security;
using Protons.Infrastructure.Login.Database;
using Protons.Infrastructure.Login.Repositories;
using Protons.Infrastructure.Tarefas.Repositories;
using Protons.Infrastructure.Tarefas.Services;
using Protons.UI.Common;
using Protons.UI.Configuration;
using Protons.UI.Login.Services;
using Protons.UI.Login.ViewModels;
using Protons.UI.Login.Views;

namespace Protons.UI;

/// <summary>
/// Ponto de entrada da aplicação Avalonia. Responsável por:
/// registrar handlers globais de exceção, construir o contêiner de serviços (DI manual),
/// iniciar os serviços de background (fila de execução e scheduler do ancorar_pdf),
/// resolver o modo de operação (login normal vs bypass de desenvolvimento) e
/// encerrar todos os serviços com graceful shutdown.
/// </summary>
/// <remarks>
/// Não usa Microsoft.Extensions.DependencyInjection — o wiring de DI é feito manualmente
/// em <see cref="BuildStartupServicesAsync"/> para manter zero dependências de hosting.
/// O lifecycle de objetos de infraestrutura (banco de dados, logger) é gerenciado
/// diretamente por esta classe via eventos de ciclo de vida do Avalonia.
/// </remarks>
public partial class App : Application
{
    // Locks dedicados por tipo de serviço para evitar deadlock cruzado durante reinicialização.
    private static readonly object SchedulerRuntimeSync = new();
    private static IAncorarPdfScheduler? _schedulerRuntime;
    private static readonly object FilaServiceSync = new();
    private static IAncorarPdfFilaExecucaoService? _filaService;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Chamado pelo framework Avalonia após a inicialização do ambiente de UI.
    /// Registra handlers de exceção global, inicia a janela principal e dispara
    /// a resolução assíncrona de serviços.
    /// </summary>
    /// <remarks>
    /// Os handlers de exceção são registrados ANTES de qualquer código de negócio para
    /// garantir que exceções que escapem de event handlers do Avalonia sejam logadas
    /// em vez de derrubar o processo com SIGABRT (exit code 134).
    ///
    /// Se a variável <c>PROTONS_STARTUP_PROBE=1</c> estiver definida, o modo probe é
    /// ativado: mede o tempo de inicialização de serviços, grava o resultado e encerra
    /// sem abrir a UI. Usado pelos scripts de CI em <c>Login/scripts/startup_probe.sh</c>.
    /// </remarks>
    public override void OnFrameworkInitializationCompleted()
    {
        // ── Handlers globais de exceção não-capturada ────────────────────────────
        // Registrar ANTES de qualquer código de negócio para garantir que qualquer
        // exceção que escape de event handlers do Avalonia seja logada em vez de
        // derrubar o processo com SIGABRT (exit code 134).
        Dispatcher.UIThread.UnhandledException += OnUiThreadUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        // ────────────────────────────────────────────────────────────────────────

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            try
            {
                // Avoid duplicate validations from both Avalonia and the CommunityToolkit.
                // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
                DisableAvaloniaDataAnnotationValidation();

                desktop.Exit += (_, _) => EncerrarServicosBackground();

                if (TryGetStartupProbeOptions(out var probeOptions))
                {
                    _ = RunStartupProbeAsync(desktop, probeOptions);
                }
                else
                {
                    var mainViewModel = new MainWindowViewModel();
                    desktop.MainWindow = new MainWindow
                    {
                        DataContext = mainViewModel
                    };

                    _ = InitializeAsync(desktop, mainViewModel);
                }
            }
            catch (Exception ex)
            {
                HandleStartupError(desktop, ex);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Resolve todos os serviços de forma assíncrona (fora da UI thread) e depois
    /// invoca <see cref="MainWindowViewModel.Initialize"/> na UI thread para exibir
    /// a tela inicial correta (login ou bypass).
    /// </summary>
    /// <remarks>
    /// O split entre thread de background (I/O de banco, leitura de configuração) e
    /// UI thread (inicialização do ViewModel) é intencional: evita congelamento da
    /// janela durante a abertura do banco SQLite ou a resolução da chave BYOK.
    /// </remarks>
    private static async Task InitializeAsync(IClassicDesktopStyleApplicationLifetime desktop, MainWindowViewModel viewModel)
    {
        try
        {
            var startup = await BuildStartupServicesAsync(0).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                viewModel.Initialize(
                    startup.AuthService,
                    startup.Settings,
                    startup.AuditLogQuery,
                    startup.ClienteService,
                    startup.TarefaService,
                    startup.UserDirectoryService,
                    startup.AncorarPdfConfiguracaoService);
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() => HandleStartupError(desktop, ex));
        }
    }

    private static async Task RunStartupProbeAsync(IClassicDesktopStyleApplicationLifetime desktop, StartupProbeOptions options)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var _ = await BuildStartupServicesAsync(options.SlowDelayMs).ConfigureAwait(false);
            stopwatch.Stop();

            var message = $"StartupProbe: {stopwatch.ElapsedMilliseconds} ms | SlowDelayMs={options.SlowDelayMs}";
            Console.WriteLine(message);
            OpsLogger.WriteInfo($"startup_probe: {message}");
            TryWriteProbeOutput(options.OutputPath, message);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var message = $"StartupProbe: FAILED after {stopwatch.ElapsedMilliseconds} ms | {ex.GetType().Name}: {ex.Message}";
            Console.WriteLine(message);
            OpsLogger.WriteError("startup_probe_failed", ex);
            TryWriteProbeOutput(options.OutputPath, message);
        }
        finally
        {
            await EncerrarDesktopComSegurancaAsync(desktop).ConfigureAwait(false);
        }
    }

    private static async Task EncerrarDesktopComSegurancaAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                desktop.Shutdown();
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() => desktop.Shutdown());
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Dispatcher shut down", StringComparison.OrdinalIgnoreCase))
        {
            // Em startup probe, o loop pode encerrar antes do shutdown agendado.
            // Ignoramos para evitar falha espuria de smoke.
        }
    }

    /// <summary>
    /// Fábrica principal de serviços. Executa toda a sequência de inicialização:
    /// resolução de paths, leitura de configuração, abertura do banco de dados,
    /// resolução da chave BYOK, wiring de repositórios e serviços de domínio,
    /// e início dos serviços de background (fila de execução e scheduler).
    /// </summary>
    /// <param name="slowDelayMs">
    /// Atraso artificial (em ms) injetado entre etapas para simular startup lento
    /// em testes de probe. Em operação normal, passar <c>0</c>.
    /// </param>
    /// <returns>
    /// Estrutura com todos os serviços prontos para injeção no <see cref="MainWindowViewModel"/>.
    /// </returns>
    /// <remarks>
    /// Sequência de inicialização:
    /// <list type="number">
    ///   <item>Resolução do <c>AppDataPath</c> (cross-platform) e migração de dados legados.</item>
    ///   <item>Inicialização do <see cref="OpsLogger"/> (JSONL assíncrono).</item>
    ///   <item>Carregamento de <c>AppSettings</c> (JSON + overrides de env vars).</item>
    ///   <item>Escolha do banco: SQLite (Local) ou PostgreSQL (Server).</item>
    ///   <item>Resolução da chave BYOK e criação do <c>IClienteDataProtector</c>.</item>
    ///   <item>Wiring de repositórios concretos com o banco escolhido.</item>
    ///   <item>Construção de serviços de domínio (Auth, Cliente, Tarefa, AncorarPdf).</item>
    ///   <item>Início da fila de execução e do scheduler em background.</item>
    /// </list>
    /// Qualquer falha nas etapas de banco ou BYOK propaga exceção para <c>HandleStartupError</c>.
    /// As etapas de fila e scheduler têm try/catch próprios — falha não bloqueia a UI.
    /// </remarks>
    private static async Task<StartupServices> BuildStartupServicesAsync(int slowDelayMs)
    {
        // Padrão cross-platform para diretório de dados:
        // Linux: XDG_DATA_HOME/Protons ou ~/.local/share/Protons
        // Windows: %AppData%\Protons
        await MaybeDelayAsync(slowDelayMs).ConfigureAwait(false);
        var baseDir = DataPathProvider.GetAppDataPath();
        DataPathProvider.MigrateLegacyLocalAppDataIfNeeded(baseDir);
        await MaybeDelayAsync(slowDelayMs).ConfigureAwait(false);
        var settingsPath = Path.Combine(baseDir, "settings.json");
        DataPathProvider.EnsureAppDataPathExists();
        await MaybeDelayAsync(slowDelayMs).ConfigureAwait(false);
        OpsLogger.Initialize(baseDir);
        var asm = typeof(App).Assembly;
        var ver = asm.GetName().Version?.ToString() ?? "?";
        var hash = asm.ManifestModule.ModuleVersionId.ToString()[..8];
        OpsLogger.WriteInfo($"app_start: version={ver} hash={hash}");

        var session = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") ?? "unknown";
        var display = Environment.GetEnvironmentVariable("DISPLAY") ?? "none";
        var wayland = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") ?? "none";
        OpsLogger.WriteInfo($"app_env: session={session} display={display} wayland={wayland}");

        var appSettings = await AppSettingsLoader.LoadAsync(baseDir).ConfigureAwait(false);
        await MaybeDelayAsync(slowDelayMs).ConfigureAwait(false);
        var dbMode = appSettings.Database.Mode;
        string? sqliteDbPath = null;
        if (!string.Equals(dbMode, "Server", StringComparison.OrdinalIgnoreCase))
        {
            sqliteDbPath = appSettings.Database.Sqlite.Path;
            if (string.IsNullOrWhiteSpace(sqliteDbPath))
                sqliteDbPath = Path.Combine(baseDir, "protons.db");
        }

        OpsLogger.WriteInfo($"data_path_base_dir={baseDir}");
        if (!string.IsNullOrWhiteSpace(sqliteDbPath))
        {
            OpsLogger.WriteInfo($"data_path_sqlite_db={sqliteDbPath}");
            RegistrarAvisoBasesAlternativasConhecidas(baseDir, sqliteDbPath);
        }

        var dataProtection = appSettings.Security.DataProtection ?? new DataProtectionSettings();
        var allowLegacyPlaintext = dataProtection.AllowLegacyPlaintext;
        var clienteDataProtector = ResolverDataProtector(dataProtection, baseDir, dbMode, sqliteDbPath);

        IUserRepository userRepo;
        IAuditLogRepository auditRepo;
        IClienteRepository clienteRepo;
        ITarefaRepository tarefaRepo;
        IClientePermissaoRepository clientePermissaoRepo;
        IAncorarPdfConfiguracaoRepository ancorarPdfConfiguracaoRepo;
        ITarefaAlteracaoRepository tarefaAlteracaoRepo;
        SqliteDb? sqliteDb = null;
        PostgresDb? postgresDb = null;

        if (string.Equals(dbMode, "Server", StringComparison.OrdinalIgnoreCase))
        {
            var provider = appSettings.Database.Server.Provider;
            if (string.Equals(provider, "Postgres", StringComparison.OrdinalIgnoreCase))
            {
                var connectionString = appSettings.Database.Server.ConnectionString;
                if (string.IsNullOrWhiteSpace(connectionString))
                    throw new InvalidOperationException("Server mode requires a non-empty Postgres connection string.");

                var db = new PostgresDb(connectionString, clienteDataProtector, allowLegacyPlaintext);
                db.EnsureCreated();
                postgresDb = db;
                await MaybeDelayAsync(slowDelayMs).ConfigureAwait(false);
                userRepo = new PostgresUserRepository(db);
                auditRepo = new PostgresAuditLogRepository(db);
                clienteRepo = new PostgresClienteRepository(db, clienteDataProtector, allowLegacyPlaintext);
                tarefaRepo = new PostgresTarefaRepository(db);
                clientePermissaoRepo = new PostgresClientePermissaoRepository(db);
                ancorarPdfConfiguracaoRepo = new PostgresAncorarPdfConfiguracaoRepository(db);
                tarefaAlteracaoRepo = new PostgresTarefaAlteracaoRepository(db);
            }
            else
            {
                throw new InvalidOperationException($"Unsupported database provider: {provider}");
            }
        }
        else
        {
            var db = new SqliteDb(sqliteDbPath!, clienteDataProtector, allowLegacyPlaintext);
            db.EnsureCreated();
            sqliteDb = db;
            await MaybeDelayAsync(slowDelayMs).ConfigureAwait(false);
            userRepo = new UserRepository(db);
            auditRepo = new AuditLogRepository(db);
            clienteRepo = new SqliteClienteRepository(db, clienteDataProtector, allowLegacyPlaintext);
            tarefaRepo = new SqliteTarefaRepository(db);
            clientePermissaoRepo = new SqliteClientePermissaoRepository(db);
            ancorarPdfConfiguracaoRepo = new SqliteAncorarPdfConfiguracaoRepository(db);
            tarefaAlteracaoRepo = new SqliteTarefaAlteracaoRepository(db);
        }

        var appVersion = GetAppVersion();
        var auditService = new AuditService(auditRepo);
        var auditLogQuery = new AuditLogQueryService(auditRepo);
        var passwordHasher = new PasswordHasher();
        ResolverContextoBypassPainelDiretoEmDev(userRepo, passwordHasher);
        var clienteService = new ClienteService(
            clienteRepo,
            auditService,
            Environment.MachineName,
            appVersion,
            appSettings.Audit.UseHashChain,
            userRepo,
            clientePermissaoRepo,
            clienteDataProtector);
        var userDirectoryService = new UserDirectoryService(userRepo);
        var tarefaService = new TarefaService(
            tarefaRepo,
            clientePermissaoRepo,
            userRepo,
            clienteRepo,
            auditService,
            Environment.MachineName,
            appVersion,
            appSettings.Audit.UseHashChain);
        var ancorarPdfPathPolicy = new AppSettingsAncorarPdfPathPolicy(
            appSettings.AncorarPdf?.AllowedNetworkRoots ?? Array.Empty<string>());
        var ancorarPdfConfiguracaoService = new AncorarPdfConfiguracaoService(
            tarefaService,
            userRepo,
            ancorarPdfConfiguracaoRepo,
            pathPolicy: ancorarPdfPathPolicy);

        var runtimeConfig = appSettings.AncorarPdf?.Runtime ?? new AncorarPdfRuntimeSettings();
        var runtimeResolved = AncorarPdfRuntimeTuningPolicy.Resolve(
            configuredWorkers: runtimeConfig.Workers,
            processorCount: Environment.ProcessorCount,
            channelCapacity: runtimeConfig.ChannelCapacity,
            schedulerPollingMs: runtimeConfig.SchedulerPollingMs,
            schedulerLimitPerTick: runtimeConfig.SchedulerLimitPerTick,
            misfireMinSeconds: runtimeConfig.MisfireMinSeconds,
            workerTimeoutSeconds: runtimeConfig.WorkerTimeoutSeconds,
            leaseDurationSeconds: runtimeConfig.LeaseDurationSeconds,
            retryDelaySeconds: runtimeConfig.RetryDelaySeconds);

        OpsLogger.WriteInfo(
            "ancorar_pdf_runtime_bootstrap " +
            $"configured_workers={runtimeConfig.Workers} workers={runtimeResolved.Workers} " +
            $"channel_capacity={runtimeResolved.ChannelCapacity} " +
            $"scheduler_polling_ms={runtimeResolved.SchedulerPollingMs} " +
            $"scheduler_limit_per_tick={runtimeResolved.SchedulerLimitPerTick} " +
            $"misfire_min_seconds={runtimeResolved.MisfireMinSeconds} " +
            $"worker_timeout_seconds={runtimeResolved.WorkerTimeoutSeconds} " +
            $"lease_seconds={runtimeResolved.LeaseDurationSeconds} " +
            $"retry_delay_seconds={runtimeResolved.RetryDelaySeconds}");

        IAncorarPdfFilaExecucaoService? filaService = null;
        try
        {
            IAncorarPdfFilaExecucaoRepository filaRepo = postgresDb is not null
                ? new PostgresAncorarPdfExecucaoFilaRepository(postgresDb)
                : new SqliteAncorarPdfExecucaoFilaRepository(sqliteDb!);
            IAncorarPdfExecucaoLeaseRepository leaseRepo = postgresDb is not null
                ? new PostgresAncorarPdfExecucaoLeaseRepository(postgresDb)
                : new SqliteAncorarPdfExecucaoLeaseRepository(sqliteDb!);

            // Repos C3 para o motor real (criados aqui para não sobrepor instâncias existentes).
            IAncorarPdfExecucaoRepository execucaoRepo = postgresDb is not null
                ? new PostgresAncorarPdfExecucaoRepository(postgresDb)
                : new SqliteAncorarPdfExecucaoRepository(sqliteDb!);
            IAncorarPdfSaidaRepository saidaRepo = postgresDb is not null
                ? new PostgresAncorarPdfSaidaRepository(postgresDb)
                : new SqliteAncorarPdfSaidaRepository(sqliteDb!);

            // Motor C6: pipeline híbrido local-first (texto nativo + OCR seletivo).
            var analyzerSettings = appSettings.AncorarPdf?.Analyzer ?? new AncorarPdfAnalyzerSettings();
            var analyzerOptions = new AncorarPdfDocumentoAnalyzerOptions
            {
                AnalyzerV2Ativo = analyzerSettings.AnalyzerV2,
                HybridOcrAtivo = analyzerSettings.HybridOcr,
                MultipageEditorAtivo = analyzerSettings.MultipageEditor,
                AdvancedAnchorsAtivo = analyzerSettings.AdvancedAnchors,
                DiagnosticsOverlayAtivo = analyzerSettings.DiagnosticsOverlay,
                CloudDocumentAiProviderAtivo = analyzerSettings.CloudDocumentAiProvider
            };
            var extratorBase = new AncorarPdfExtratorTextoPdfPig();
            var documentAnalyzer = new AncorarPdfDocumentoAnalyzer(
                extratorBase,
                (dpi, lang) => new AncorarPdfExtratorOcrTesseract(
                    GetTessDataPath(),
                    dpi,
                    lang,
                    (l, m) => OpsLogger.WriteInfo($"[ocr] {m}")),
                (l, m) => OpsLogger.WriteInfo($"[analyzer] {m}"));
            var extratorComAnalyzer = new AncorarPdfExtratorTextoAnalyzerAdapter(
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
            var motor = new AncorarPdfMotorExecucao(
                ancorarPdfConfiguracaoRepo,
                execucaoRepo,
                saidaRepo,
                new AncorarPdfSeletorArquivoPasta(),
                extratorComAnalyzer,
                new AncorarPdfValidadorClienteRegex(clienteRepo),
                new AncorarPdfAncoradorEspacialBbox(),
                TimeProvider.System,
                onLog: (level, msg) => OpsLogger.WriteInfo($"[motor] {msg}"));

            filaService = new AncorarPdfFilaExecucaoService(
                filaRepo,
                leaseRepo,
                motor,
                timeProvider: TimeProvider.System,
                channelCapacity: runtimeResolved.ChannelCapacity,
                leaseDuracao: TimeSpan.FromSeconds(runtimeResolved.LeaseDurationSeconds),
                processoTimeout: TimeSpan.FromSeconds(runtimeResolved.WorkerTimeoutSeconds),
                retryDelay: TimeSpan.FromSeconds(runtimeResolved.RetryDelaySeconds),
                onLog: (level, msg) =>
                {
                    if (string.Equals(level, "warn", StringComparison.OrdinalIgnoreCase))
                        OpsLogger.WriteWarning(msg ?? "");
                    else if (string.Equals(level, "error", StringComparison.OrdinalIgnoreCase))
                        OpsLogger.WriteError(msg ?? "", null);
                    else
                        OpsLogger.WriteInfo(msg ?? "");
                });
            filaService.Start(numeroDeworkers: runtimeResolved.Workers);
            RegistrarFilaService(filaService);
            OpsLogger.WriteInfo($"fila_service_started workers={runtimeResolved.Workers}");
        }
        catch (Exception ex)
        {
            OpsLogger.WriteError("fila_service_start_failed", ex);
        }

        try
        {
            var schedulerRuntime = new AncorarPdfSchedulerRuntime(
                ancorarPdfConfiguracaoRepo,
                tarefaRepo,
                timeProvider: TimeProvider.System,
                intervaloPolling: TimeSpan.FromMilliseconds(runtimeResolved.SchedulerPollingMs),
                limitePorTick: runtimeResolved.SchedulerLimitPerTick,
                misfireAtrasoMinimoSegundos: runtimeResolved.MisfireMinSeconds,
                filaService: filaService);

            schedulerRuntime.Start();
            RegistrarSchedulerRuntime(schedulerRuntime);
            OpsLogger.WriteInfo("scheduler_runtime_started");
        }
        catch (Exception ex)
        {
            OpsLogger.WriteError("scheduler_runtime_start_failed", ex);
        }

        var authService = new AuthService(
            userRepo,
            passwordHasher,
            auditService,
            Environment.MachineName,
            appVersion,
            usarHashChain: appSettings.Audit.UseHashChain);

        var settings = new LocalSettings(settingsPath);
        OpsLogger.WriteInfo("startup_services_created");
        return new StartupServices(
            authService,
            settings,
            auditLogQuery,
            clienteService,
            tarefaService,
            userDirectoryService,
            ancorarPdfConfiguracaoService);
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }

    private static string GetAppVersion()
    {
        var info = typeof(App).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(info))
            return info;

        return typeof(App).Assembly.GetName().Version?.ToString() ?? "0.0.0";
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

        var linux = "/usr/share/tesseract-ocr/5/tessdata";
        if (Directory.Exists(linux))
            return linux;

        var linuxAlt = "/usr/share/tessdata";
        if (Directory.Exists(linuxAlt))
            return linuxAlt;

        return local; // fallback: app pode criar tessdata e baixar
    }

    private static void RegistrarAvisoBasesAlternativasConhecidas(string baseDirAtual, string sqliteDbPathAtual)
    {
        var caminhoAtual = TryGetFullPathNoThrow(sqliteDbPathAtual);
        if (string.IsNullOrWhiteSpace(caminhoAtual))
            return;

        var baseAtual = TryGetFullPathNoThrow(baseDirAtual) ?? baseDirAtual;
        var candidatos = EnumerarBasesAlternativasPossiveis(baseAtual);
        foreach (var candidato in candidatos)
        {
            if (string.Equals(candidato, caminhoAtual, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!File.Exists(candidato))
                continue;

            try
            {
                var info = new FileInfo(candidato);
                if (info.Length <= 0)
                    continue;

                OpsLogger.WriteWarning($"base_alternativa_detectada caminho={candidato} tamanho_bytes={info.Length}");
            }
            catch (Exception ex)
            {
                OpsLogger.WriteWarning($"base_alternativa_diagnostico_falhou caminho={candidato} erro={ex.GetType().Name}");
            }
        }
    }

    private static IReadOnlyList<string> EnumerarBasesAlternativasPossiveis(string baseDirAtual)
    {
        var candidatos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static void Adicionar(HashSet<string> destino, string? caminho)
        {
            if (string.IsNullOrWhiteSpace(caminho))
                return;

            var full = TryGetFullPathNoThrow(caminho);
            if (!string.IsNullOrWhiteSpace(full))
                destino.Add(full);
        }

        Adicionar(candidatos, Path.Combine(Path.GetTempPath(), "Protons", "protons.db"));
        Adicionar(candidatos, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Protons", "protons.db"));
        Adicionar(candidatos, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Protons", "protons.db"));
        Adicionar(candidatos, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "Protons", "protons.db"));

        var xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(xdgDataHome))
            Adicionar(candidatos, Path.Combine(xdgDataHome, "Protons", "protons.db"));

        var baseAtualFull = TryGetFullPathNoThrow(baseDirAtual);
        if (!string.IsNullOrWhiteSpace(baseAtualFull))
            candidatos.Remove(Path.Combine(baseAtualFull, "protons.db"));

        return [.. candidatos];
    }

    private static string? TryGetFullPathNoThrow(string? caminho)
    {
        if (string.IsNullOrWhiteSpace(caminho))
            return null;

        try
        {
            return Path.GetFullPath(caminho);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Resolve e publica nas variáveis de ambiente o usuário dev que será utilizado
    /// pelo bypass de login, garantindo que sempre exista um usuário ativo e admin
    /// para desenvolvimento mesmo em banco vazio.
    /// </summary>
    /// <remarks>
    /// Executado apenas em DEBUG com bypass ativo. Estratégia de resolução (em ordem):
    /// <list type="number">
    ///   <item>Usa o usuário com ID especificado em <c>PROTONS_PAINEL_USER_ID</c>, se ativo.</item>
    ///   <item>Fallback: primeiro usuário ativo ordenado por Admin → ID crescente.</item>
    ///   <item>Fallback final: cria <c>painel.dev@protons.local</c> com Role Admin e senha aleatória.</item>
    /// </list>
    /// O resultado é publicado em <c>PROTONS_PAINEL_USER_ID</c>, <c>PROTONS_PAINEL_EMAIL</c>,
    /// <c>PROTONS_PAINEL_NOME</c> e <c>PROTONS_PAINEL_ROLE</c> para que
    /// <see cref="MainWindowViewModel"/> os leia na criação do <c>PainelViewModel</c>.
    /// </remarks>
    private static void ResolverContextoBypassPainelDiretoEmDev(IUserRepository userRepo, IPasswordHasher passwordHasher)
    {
#if !DEBUG
        return;
#else
        if (!IsPainelDiretoBypassAtivoEmDevelopment())
            return;

        var userIdSolicitado = TryReadPositiveInt("PROTONS_PAINEL_USER_ID");
        var origem = "solicitado";
        User? usuarioResolvido = null;

        if (userIdSolicitado.HasValue)
        {
            var candidato = userRepo.GetById(userIdSolicitado.Value);
            if (candidato is not null && candidato.Status == UserStatus.Ativo)
                usuarioResolvido = candidato;
        }

        if (usuarioResolvido is null)
        {
            usuarioResolvido = userRepo
                .ListarAtivos()
                .OrderByDescending(u => u.Role == UserRole.Admin)
                .ThenBy(u => u.Id)
                .FirstOrDefault();
            if (usuarioResolvido is not null)
                origem = "fallback_ativo";
        }

        if (usuarioResolvido is null)
        {
            usuarioResolvido = CriarUsuarioDevInicialParaBypass(userRepo, passwordHasher);
            origem = "criado_dev_admin";
        }

        Environment.SetEnvironmentVariable("PROTONS_PAINEL_USER_ID", usuarioResolvido.Id.ToString(CultureInfo.InvariantCulture));
        Environment.SetEnvironmentVariable("PROTONS_PAINEL_EMAIL", usuarioResolvido.Email);
        Environment.SetEnvironmentVariable("PROTONS_PAINEL_NOME", usuarioResolvido.Nome);
        Environment.SetEnvironmentVariable(
            "PROTONS_PAINEL_ROLE",
            usuarioResolvido.Role == UserRole.Admin ? "admin" : "usuario");

        OpsLogger.WriteInfo(
            $"painel_direto_contexto_resolvido origem={origem} user_id={usuarioResolvido.Id} status={usuarioResolvido.Status} role={usuarioResolvido.Role}");
#endif
    }

    private static bool IsPainelDiretoBypassAtivoEmDevelopment()
    {
#if !DEBUG
        return false;
#else
        if (!IsEnvTrue(Environment.GetEnvironmentVariable("PROTONS_PAINEL_DIRETO_HABILITADO")))
            return false;

        if (!IsEnvTrue(Environment.GetEnvironmentVariable("PROTONS_PAINEL_DIRETO")))
            return false;

        return IsDevelopmentEnvironment();
#endif
    }

    private static bool IsDevelopmentEnvironment()
    {
        var ambiente = Environment.GetEnvironmentVariable("PROTONS_ENVIRONMENT");
        if (string.IsNullOrWhiteSpace(ambiente))
            ambiente = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        if (string.IsNullOrWhiteSpace(ambiente))
            ambiente = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

        if (string.IsNullOrWhiteSpace(ambiente))
            return false;

        return string.Equals(ambiente, "development", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ambiente, "dev", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ambiente, "local", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsEnvTrue(string? valor)
    {
        return string.Equals(valor, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(valor, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static int? TryReadPositiveInt(string nomeVariavel)
    {
        var raw = Environment.GetEnvironmentVariable(nomeVariavel);
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var valor))
            return null;

        return valor > 0 ? valor : null;
    }

    private static User CriarUsuarioDevInicialParaBypass(IUserRepository userRepo, IPasswordHasher passwordHasher)
    {
        var emailDev = "painel.dev@protons.local";
        var existente = userRepo.GetByEmail(emailDev);
        if (existente is not null)
        {
            existente.Status = UserStatus.Ativo;
            existente.Role = UserRole.Admin;
            existente.AtualizadoEmUtc = DateTime.UtcNow;
            userRepo.Update(existente);
            return existente;
        }

        var senhaTemporaria = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var (hash, salt, iteracoes) = passwordHasher.HashPassword(senhaTemporaria);
        var now = DateTime.UtcNow;
        var user = new User
        {
            Empresa = "Protons Desenvolvimento",
            Nome = "Painel Dev",
            Cpf = "52998224725",
            Cargo = "Administrador",
            Email = emailDev,
            SenhaHash = hash,
            SenhaSalt = salt,
            IteracoesPbkdf2 = iteracoes,
            Status = UserStatus.Ativo,
            Role = UserRole.Admin,
            CriadoEmUtc = now,
            AtualizadoEmUtc = now
        };

        user.Id = userRepo.Create(user);
        return user;
    }

    private static void HandleStartupError(IClassicDesktopStyleApplicationLifetime desktop, Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"Startup error: {ex.GetType().Name}");
        OpsLogger.WriteError("startup_error", ex);
        var message = GetSafeErrorMessage(ex);

        desktop.MainWindow = new Window
        {
            Title = "Protons - Erro",
            Width = 520,
            Height = 220,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "Não foi possível iniciar a aplicação." },
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }
                }
            }
        };
    }

    private static string GetSafeErrorMessage(Exception ex)
    {
        if (ex is InvalidOperationException ioe)
        {
            if (ioe.Message.Contains("chave BYOK incompatível", StringComparison.OrdinalIgnoreCase) ||
                ioe.Message.Contains("não pode ser aberta sem chave BYOK", StringComparison.OrdinalIgnoreCase))
            {
                return "A chave de proteção de dados não corresponde a esta base. Configure a variável PROTONS_DATA_KEY_BASE64 com a mesma chave usada na instalação original.";
            }

            if (ioe.Message.Contains("chave local", StringComparison.OrdinalIgnoreCase))
            {
                return "Falha ao carregar a chave local de proteção. Verifique permissões no diretório de dados.";
            }

            if (ioe.Message.Contains("base local existente", StringComparison.OrdinalIgnoreCase))
            {
                return "Foi encontrada base local já existente sem chave de proteção correspondente. Restaure a chave original ou configure PROTONS_DATA_KEY_BASE64.";
            }

            return "Erro de configuração. Verifique appsettings.json e a chave de proteção de dados.";
        }

        return ex switch
        {
            UnauthorizedAccessException => "Sem permissão para acessar arquivos/diretórios. Verifique permissões.",
            IOException => "Erro ao ler/gravar arquivos de configuração ou dados.",
            _ => "Erro inesperado na inicialização. Verifique configuração e permissões."
        };
    }

    private static void RegistrarSchedulerRuntime(IAncorarPdfScheduler runtime)
    {
        IAncorarPdfScheduler? antiga;
        lock (SchedulerRuntimeSync)
        {
            antiga = _schedulerRuntime;
            _schedulerRuntime = runtime;
        }
        PararEDisporScheduler(antiga, TimeSpan.FromSeconds(3));
    }

    private static void RegistrarFilaService(IAncorarPdfFilaExecucaoService service)
    {
        IAncorarPdfFilaExecucaoService? antiga;
        lock (FilaServiceSync)
        {
            antiga = _filaService;
            _filaService = service;
        }
        PararEDisporFilaService(antiga, TimeSpan.FromSeconds(3));
    }

    private static void PararEDisporScheduler(IAncorarPdfScheduler? runtime, TimeSpan timeout)
    {
        if (runtime is null)
            return;

        try
        {
            runtime.Stop(timeout);
        }
        finally
        {
            runtime.Dispose();
        }
    }

    private static void PararEDisporFilaService(IAncorarPdfFilaExecucaoService? fila, TimeSpan timeout)
    {
        if (fila is null)
            return;

        try
        {
            fila.StopAsync(timeout).GetAwaiter().GetResult();
        }
        finally
        {
            fila.Dispose();
        }
    }

    /// <summary>
    /// Graceful shutdown dos serviços de background. Chamado no evento
    /// <c>IClassicDesktopStyleApplicationLifetime.Exit</c>.
    /// </summary>
    /// <remarks>
    /// Extrai as instâncias dos campos estáticos sob lock (para evitar race com
    /// <see cref="RegistrarSchedulerRuntime"/> e <see cref="RegistrarFilaService"/>),
    /// para o scheduler, aguarda a fila de execução drenar (timeout: 5 s cada) e
    /// faz flush final do <see cref="OpsLogger"/>.
    ///
    /// O timeout de 5 s por serviço é intencional: em máquinas lentas, execuções
    /// de PDF em andamento precisam de tempo para concluir ou cancelar com segurança.
    /// </remarks>
    private static void EncerrarServicosBackground()
    {
        IAncorarPdfScheduler? runtime = null;
        IAncorarPdfFilaExecucaoService? fila = null;
        try
        {
            lock (SchedulerRuntimeSync)
            {
                runtime = _schedulerRuntime;
                _schedulerRuntime = null;
            }

            lock (FilaServiceSync)
            {
                fila = _filaService;
                _filaService = null;
            }

            try
            {
                PararEDisporScheduler(runtime, TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                OpsLogger.WriteError("scheduler_runtime_stop_failed", ex);
            }

            try
            {
                PararEDisporFilaService(fila, TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                OpsLogger.WriteError("fila_service_stop_failed", ex);
            }
        }
        finally
        {
            OpsLogger.FlushAndStop();
        }
    }

    // ── Handlers globais de exceção ─────────────────────────────────────────────

    /// <summary>
    /// Captura exceções não-tratadas na UI thread do Avalonia.
    /// Setar e.Handled = true impede o SIGABRT (exit 134) que ocorre quando uma
    /// exceção escapa de um event handler do Avalonia sem ser capturada.
    /// </summary>
    private static void OnUiThreadUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        OpsLogger.WriteError("ui_unhandled_exception", e.Exception);
        System.Diagnostics.Debug.WriteLine($"[PROTONS] UI UnhandledException: {e.Exception.GetType().Name}: {e.Exception.Message}");
        e.Handled = true; // Impede SIGABRT — o app continua rodando
    }

    /// <summary>
    /// Captura exceções não-tratadas em threads de background.
    /// Não é possível impedir o encerramento quando IsTerminating=true,
    /// mas ao menos logamos antes de morrer.
    /// </summary>
    private static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            OpsLogger.WriteError("appdomain_unhandled_exception", ex);
    }

    /// <summary>
    /// Captura exceções de Tasks async que não tiveram seu resultado observado.
    /// SetObserved() impede que o processo encerre por GC de tasks com exceção.
    /// </summary>
    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        OpsLogger.WriteError("unobserved_task_exception", e.Exception);
        e.SetObserved();
    }

    // ────────────────────────────────────────────────────────────────────────────

    private static async Task MaybeDelayAsync(int delayMs)
    {
        if (delayMs > 0)
            await Task.Delay(delayMs).ConfigureAwait(false);
    }

    private static bool TryGetStartupProbeOptions(out StartupProbeOptions options)
    {
        var enabled = Environment.GetEnvironmentVariable("PROTONS_STARTUP_PROBE");
        if (!string.Equals(enabled, "1", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase))
        {
            options = default!;
            return false;
        }

        var delayRaw = Environment.GetEnvironmentVariable("PROTONS_SIMULATE_SLOW_STARTUP_MS");
        var delayMs = 0;
        if (!string.IsNullOrWhiteSpace(delayRaw) && int.TryParse(delayRaw, out var parsed) && parsed > 0)
            delayMs = parsed;

        var outputPath = Environment.GetEnvironmentVariable("PROTONS_STARTUP_PROBE_OUT");
        options = new StartupProbeOptions(delayMs, outputPath);
        return true;
    }

    private static void TryWriteProbeOutput(string? outputPath, string message)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            return;

        try
        {
            File.AppendAllText(outputPath, message + Environment.NewLine);
        }
        catch
        {
            // Ignorar falhas de escrita do probe
        }
    }

    /// <summary>
    /// Resolve o <c>IClienteDataProtector</c> (AES-GCM 256-bit) adequado para o
    /// contexto de execução, aplicando a política BYOK definida em
    /// <see cref="DataProtectionSettings"/>.
    /// </summary>
    /// <remarks>
    /// Lógica de resolução:
    /// <list type="number">
    ///   <item>Se <c>AutoPersistLocalKey=true</c> e modo Local: delega para
    ///         <see cref="ResolverDataProtectorComPersistenciaLocal"/> (gerencia chave em arquivo).</item>
    ///   <item>Caso contrário: tenta criar protector a partir da env var BYOK.</item>
    ///   <item>Se BYOK inválido e <c>RequireByokKey=true</c>: lança exceção (fail-closed).</item>
    ///   <item>Se BYOK inválido e <c>AllowLegacyPlaintext=true</c>: retorna <c>null</c> (sem criptografia).</item>
    /// </list>
    /// Retornar <c>null</c> significa operar em modo legado sem criptografia —
    /// aceitável apenas em bancos criados antes da habilitação do BYOK.
    /// </remarks>
    private static IClienteDataProtector? ResolverDataProtector(
        DataProtectionSettings settings,
        string baseDir,
        string dbMode,
        string? sqliteDbPath)
    {
        var keyEnvVarName = string.IsNullOrWhiteSpace(settings.KeyEnvVarName)
            ? "PROTONS_DATA_KEY_BASE64"
            : settings.KeyEnvVarName.Trim();
        var keyRaw = NormalizeKey(Environment.GetEnvironmentVariable(keyEnvVarName));
        var useAutoPersistLocalKey =
            settings.AutoPersistLocalKey &&
            string.Equals(dbMode, "Local", StringComparison.OrdinalIgnoreCase);

        if (useAutoPersistLocalKey)
        {
            return ResolverDataProtectorComPersistenciaLocal(settings, baseDir, keyEnvVarName, keyRaw, sqliteDbPath);
        }

        if (AesGcmClienteDataProtector.TryCreateFromBase64(keyRaw, out var protector, out var erro))
            return protector;

        if (settings.RequireByokKey)
        {
            throw new InvalidOperationException(
                $"Política de segurança bloqueou a inicialização dos módulos sensíveis: {erro} (env: {keyEnvVarName}).");
        }

        if (!settings.AllowLegacyPlaintext)
        {
            throw new InvalidOperationException(
                $"Proteção de dados sem fallback legado exige chave BYOK válida: {erro} (env: {keyEnvVarName}).");
        }

        OpsLogger.WriteWarning(
            $"data_protection_legacy_mode_enabled: sem chave BYOK válida ({keyEnvVarName}); fallback legado ativo.");
        return null;
    }

    /// <summary>
    /// Variante de resolução de protector para modo Local com persistência de chave em arquivo.
    /// Gerencia o ciclo completo de chave: leitura de arquivo existente, fallback para env var,
    /// geração automática de nova chave e persistência em <c>data_protection.key</c>.
    /// </summary>
    /// <remarks>
    /// Casos de resolução (em ordem de prioridade):
    /// <list type="number">
    ///   <item><strong>Arquivo existe e é válido:</strong> usa a chave do arquivo. Se env var divergir, loga aviso mas mantém o arquivo (consistência do banco).</item>
    ///   <item><strong>Arquivo inválido + env var válida:</strong> substitui o arquivo pela env var.</item>
    ///   <item><strong>Arquivo inválido + env var inválida:</strong> lança exceção (fail-closed).</item>
    ///   <item><strong>Sem arquivo + env var válida:</strong> persiste a env var como chave local.</item>
    ///   <item><strong>Sem arquivo + sem env var + legado permitido:</strong> retorna <c>null</c>.</item>
    ///   <item><strong>Sem arquivo + sem env var + banco existente:</strong> lança exceção (banco seria ilegível).</item>
    ///   <item><strong>Sem arquivo + sem env var + banco novo:</strong> gera chave AES-256 aleatória e persiste.</item>
    /// </list>
    /// O arquivo de chave é gravado em <c>{baseDir}/data_protection.key</c> com permissões
    /// restritivas (600 no Linux via <c>File.SetUnixFileMode</c>).
    /// </remarks>
    private static IClienteDataProtector? ResolverDataProtectorComPersistenciaLocal(
        DataProtectionSettings settings,
        string baseDir,
        string keyEnvVarName,
        string? keyRaw,
        string? sqliteDbPath)
    {
        var localKeyPath = Path.Combine(baseDir, "data_protection.key");
        var persistedKey = TryReadPersistedKey(localKeyPath);

        if (!string.IsNullOrWhiteSpace(persistedKey))
        {
            if (!AesGcmClienteDataProtector.TryCreateFromBase64(persistedKey, out var persistedProtector, out var persistedErro))
            {
                if (AesGcmClienteDataProtector.TryCreateFromBase64(keyRaw, out var envProtectorFromCorruptedLocalFile, out _))
                {
                    PersistKey(localKeyPath, keyRaw!);
                    OpsLogger.WriteWarning("data_protection_local_key_replaced_from_env");
                    return envProtectorFromCorruptedLocalFile!;
                }

                throw new InvalidOperationException(
                    $"Arquivo de chave local inválido em '{localKeyPath}': {persistedErro}");
            }

            if (!string.IsNullOrWhiteSpace(keyRaw) &&
                AesGcmClienteDataProtector.TryCreateFromBase64(keyRaw, out _, out _) &&
                !string.Equals(persistedKey, keyRaw, StringComparison.Ordinal))
            {
                OpsLogger.WriteWarning(
                    $"data_protection_env_key_ignored: {keyEnvVarName} divergente da chave local persistida.");
            }

            OpsLogger.WriteInfo("data_protection_key_source=local_file");
            return persistedProtector!;
        }

        if (AesGcmClienteDataProtector.TryCreateFromBase64(keyRaw, out var envProtector, out _))
        {
            PersistKey(localKeyPath, keyRaw!);
            OpsLogger.WriteInfo($"data_protection_key_source=env_var:{keyEnvVarName}");
            return envProtector!;
        }

        if (!settings.RequireByokKey && settings.AllowLegacyPlaintext)
        {
            OpsLogger.WriteWarning(
                $"data_protection_legacy_mode_enabled: sem chave BYOK válida ({keyEnvVarName}); fallback legado ativo.");
            return null;
        }

        var hasExistingLocalDatabase = !string.IsNullOrWhiteSpace(sqliteDbPath) &&
                                       File.Exists(sqliteDbPath) &&
                                       new FileInfo(sqliteDbPath).Length > 0;
        if (hasExistingLocalDatabase)
        {
            throw new InvalidOperationException(
                $"Base local existente detectada em '{sqliteDbPath}' sem chave BYOK disponível. Informe a variável {keyEnvVarName} ou restaure '{localKeyPath}'.");
        }

        var generatedKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        PersistKey(localKeyPath, generatedKey);
        if (!AesGcmClienteDataProtector.TryCreateFromBase64(generatedKey, out var generatedProtector, out var generatedErro))
        {
            throw new InvalidOperationException(
                $"Falha ao gerar chave local de proteção de dados: {generatedErro}");
        }

        OpsLogger.WriteWarning(
            $"data_protection_local_key_generated: chave criada e persistida em '{localKeyPath}'.");
        return generatedProtector!;
    }

    private static string? TryReadPersistedKey(string keyPath)
    {
        if (!File.Exists(keyPath))
            return null;

        try
        {
            return NormalizeKey(File.ReadAllText(keyPath));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Falha ao ler chave local de proteção em '{keyPath}'.", ex);
        }
    }

    private static void PersistKey(string keyPath, string keyBase64)
    {
        try
        {
            var directory = Path.GetDirectoryName(keyPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var tmpPath = keyPath + ".tmp";
            File.WriteAllText(tmpPath, keyBase64.Trim() + Environment.NewLine);
            File.Move(tmpPath, keyPath, overwrite: true);
            TrySetRestrictivePermissions(keyPath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Falha ao persistir chave local de proteção em '{keyPath}'.", ex);
        }
    }

    private static void TrySetRestrictivePermissions(string keyPath)
    {
        if (OperatingSystem.IsWindows())
            return;

        try
        {
            File.SetUnixFileMode(keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception ex)
        {
            OpsLogger.WriteWarning($"data_protection_key_permissions_warning: {ex.GetType().Name}");
        }
    }

    private static string? NormalizeKey(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        return raw.Trim();
    }

    /// <summary>
    /// Contêiner imutável com todos os serviços resolvidos durante a inicialização.
    /// Transferido da thread de background para a UI thread via <see cref="Dispatcher.UIThread.InvokeAsync"/>.
    /// </summary>
    private sealed record StartupServices(
        IAuthService AuthService,
        LocalSettings Settings,
        IAuditLogQueryService AuditLogQuery,
        IClienteService ClienteService,
        ITarefaService TarefaService,
        IUserDirectoryService UserDirectoryService,
        IAncorarPdfConfiguracaoService AncorarPdfConfiguracaoService);

    /// <summary>
    /// Opções para execução em modo probe (CI). <paramref name="SlowDelayMs"/> injeta
    /// atraso artificial entre etapas para simular startup lento.
    /// <paramref name="OutputPath"/> é o caminho do arquivo onde o resultado do probe é gravado.
    /// </summary>
    private sealed record StartupProbeOptions(int SlowDelayMs, string? OutputPath);
}
