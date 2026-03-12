using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Protons.Core.Tarefas.Services;

namespace Protons.UI.Configuration;

/// <summary>
/// Raiz da configuração da aplicação. Deserializado de <c>appsettings.json</c>
/// e enriquecido com overrides de variáveis de ambiente por <see cref="AppSettingsLoader"/>.
/// </summary>
internal sealed class AppSettings
{
    public DatabaseSettings Database { get; set; } = new();
    public AuditSettings Audit { get; set; } = new();
    public SecuritySettings Security { get; set; } = new();
    public AncorarPdfSettings AncorarPdf { get; set; } = new();
}

/// <summary>
/// Configuração de banco de dados. Define se a aplicação opera em modo local (SQLite)
/// ou em modo servidor (PostgreSQL).
/// </summary>
internal sealed class DatabaseSettings
{
    /// <summary>
    /// Modo de banco ativo. Valores aceitos: <c>"Local"</c> (SQLite) ou <c>"Server"</c> (PostgreSQL).
    /// Padrão: <c>"Local"</c>.
    /// </summary>
    public string Mode { get; set; } = "Local";

    public SqliteSettings Sqlite { get; set; } = new();
    public ServerSettings Server { get; set; } = new();
}

/// <summary>
/// Configurações específicas para o banco SQLite local.
/// </summary>
internal sealed class SqliteSettings
{
    /// <summary>
    /// Caminho absoluto do arquivo <c>.db</c>. Se <c>null</c>, o caminho é derivado
    /// automaticamente por <c>DataPathProvider</c> com base no perfil de dados ativo
    /// (<c>PROTONS_XDG_DATA_HOME</c> ou o padrão de plataforma).
    /// </summary>
    public string? Path { get; set; }
}

/// <summary>
/// Configurações para conexão com banco de dados em servidor (PostgreSQL).
/// Ativo apenas quando <see cref="DatabaseSettings.Mode"/> é <c>"Server"</c>.
/// </summary>
internal sealed class ServerSettings
{
    /// <summary>
    /// Provedor do banco servidor. Padrão: <c>"Postgres"</c>. Reservado para
    /// extensão futura com outros provedores ADO.NET.
    /// </summary>
    public string Provider { get; set; } = "Postgres";

    /// <summary>
    /// String de conexão completa. Pode ser sobrescrita pela variável de ambiente
    /// <c>PROTONS_DB_CONNECTION_STRING</c>, que tem prioridade sobre o arquivo de configuração.
    /// </summary>
    public string? ConnectionString { get; set; }
}

/// <summary>
/// Configurações do subsistema de auditoria.
/// </summary>
internal sealed class AuditSettings
{
    /// <summary>
    /// Quando <c>true</c>, cada entrada de auditoria é encadeada com hash da anterior,
    /// criando uma cadeia imutável detectável de adulteração.
    /// Padrão: <c>true</c>. Env var de override: <c>PROTONS_AUDIT_USE_HASH_CHAIN</c>.
    /// </summary>
    public bool UseHashChain { get; set; } = true;
}

/// <summary>
/// Configurações de segurança. Agrupa controles do painel e de proteção de dados.
/// </summary>
internal sealed class SecuritySettings
{
    public PainelSecuritySettings Painel { get; set; } = new();
    public DataProtectionSettings DataProtection { get; set; } = new();
}

/// <summary>
/// Controles de segurança específicos do painel principal.
/// </summary>
internal sealed class PainelSecuritySettings
{
    /// <summary>
    /// Minutos de inatividade até o painel bloquear a sessão automaticamente.
    /// Padrão: <c>30</c>. Clamp aplicado: mínimo 5, máximo 240.
    /// Env var de override: <c>PROTONS_PAINEL_TIMEOUT_MINUTOS</c>.
    /// </summary>
    public int TimeoutInatividadeMinutos { get; set; } = 30;

    /// <summary>
    /// Habilita o modo de bypass de login para desenvolvimento.
    /// Padrão: <c>false</c>. Só tem efeito em compilações DEBUG — em Release
    /// o bypass é bloqueado incondicionalmente no código.
    /// Env var de override: <c>PROTONS_PAINEL_DIRETO_HABILITADO</c>.
    /// </summary>
    public bool PermitirBypassPainelDireto { get; set; } = false;
}

/// <summary>
/// Configurações da proteção de dados em repouso (criptografia BYOK).
/// </summary>
internal sealed class DataProtectionSettings
{
    /// <summary>
    /// Exige que uma chave BYOK (Bring Your Own Key) esteja presente para abrir o banco.
    /// Padrão: <c>true</c>. Sem chave válida, o sistema entra em fail-closed.
    /// Env var de override: <c>PROTONS_DATA_REQUIRE_BYOK</c>.
    /// </summary>
    public bool RequireByokKey { get; set; } = true;

    /// <summary>
    /// Nome da variável de ambiente que contém a chave mestra em Base64.
    /// Padrão: <c>"PROTONS_DATA_KEY_BASE64"</c>.
    /// Pode ser sobrescrito por <c>PROTONS_DATA_KEY_ENV_NAME</c> para ambientes
    /// que usam sistemas externos de gerenciamento de segredos.
    /// </summary>
    public string KeyEnvVarName { get; set; } = "PROTONS_DATA_KEY_BASE64";

    /// <summary>
    /// Permite leitura de dados gravados antes da habilitação da criptografia (legado).
    /// Padrão: <c>false</c>. Habilitar apenas para migração controlada.
    /// Env var de override: <c>PROTONS_DATA_ALLOW_LEGACY_PLAINTEXT</c>.
    /// </summary>
    public bool AllowLegacyPlaintext { get; set; } = false;

    /// <summary>
    /// Quando <c>true</c>, gera e persiste automaticamente uma chave local se nenhuma
    /// chave BYOK for fornecida. Útil em modo desenvolvimento para evitar configuração manual.
    /// Padrão: <c>true</c>. Env var de override: <c>PROTONS_DATA_AUTO_PERSIST_LOCAL_KEY</c>.
    /// </summary>
    public bool AutoPersistLocalKey { get; set; } = true;
}

/// <summary>
/// Configurações da ferramenta <c>ancorar_pdf</c>.
/// </summary>
internal sealed class AncorarPdfSettings
{
    /// <summary>
    /// Lista de raízes de rede permitidas para acesso a arquivos PDF (ex: caminhos UNC ou
    /// montagens SMB). Separador: <c>;</c> ou <c>,</c>.
    /// Env var de override: <c>PROTONS_ANCORAR_PDF_ALLOWED_NETWORK_ROOTS</c>.
    /// Padrão: lista vazia (apenas sistema de arquivos local permitido).
    /// </summary>
    public string[] AllowedNetworkRoots { get; set; } = [];

    /// <summary>
    /// Configurações do preview de PDF no modal de âncoras.
    /// </summary>
    public AncorarPdfPreviewSettings Preview { get; set; } = new();

    public AncorarPdfAnalyzerSettings Analyzer { get; set; } = new();

    public AncorarPdfRuntimeSettings Runtime { get; set; } = new();
}

/// <summary>
/// Configurações do preview de PDF no modal Ancorar PDF.
/// </summary>
internal sealed class AncorarPdfPreviewSettings
{
    /// <summary>
    /// Quando <c>true</c>, usa cadeia Docnet + Ghostscript (fallback para PDFs que falham no Docnet).
    /// Quando <c>false</c>, usa apenas Docnet (zero dependência externa de <c>gs</c>).
    /// Env var de override: <c>ANCORA_PDF_PREVIEW_USE_GHOSTSCRIPT</c> (1/true = sim, 0/false = não).
    /// Padrão: <c>true</c>.
    /// </summary>
    public bool UseGhostscriptFallback { get; set; } = true;
}

internal sealed class AncorarPdfAnalyzerSettings
{
    public bool AnalyzerV2 { get; set; } = true;
    public bool HybridOcr { get; set; } = true;
    public bool MultipageEditor { get; set; } = true;
    public bool AdvancedAnchors { get; set; } = true;
    public bool DiagnosticsOverlay { get; set; } = true;
    public bool CloudDocumentAiProvider { get; set; }
}

/// <summary>
/// Parâmetros de tuning de runtime para a ferramenta <c>ancorar_pdf</c>.
/// Todos os valores passam por funções de resolução em <c>AncorarPdfRuntimeTuningPolicy</c>
/// que aplicam clamps e lógica de auto-detecção antes do uso.
/// </summary>
internal sealed class AncorarPdfRuntimeSettings
{
    /// <summary>
    /// Número de workers paralelos para processamento de PDF.
    /// <c>0</c> = auto-detecção por contagem de CPUs (clamp: 1..16).
    /// Env var de override: <c>PROTONS_ANCORAR_PDF_WORKERS</c>.
    /// </summary>
    public int Workers { get; set; } = AncorarPdfRuntimeTuningPolicy.DefaultWorkers;

    /// <summary>
    /// Capacidade do channel interno de despacho de tarefas.
    /// Env var de override: <c>PROTONS_ANCORAR_PDF_CHANNEL_CAPACITY</c>.
    /// </summary>
    public int ChannelCapacity { get; set; } = AncorarPdfRuntimeTuningPolicy.DefaultChannelCapacity;

    /// <summary>
    /// Intervalo (em ms) de polling do scheduler de agendamentos.
    /// Env var de override: <c>PROTONS_ANCORAR_PDF_SCHEDULER_POLLING_MS</c>.
    /// </summary>
    public int SchedulerPollingMs { get; set; } = AncorarPdfRuntimeTuningPolicy.DefaultSchedulerPollingMs;

    /// <summary>
    /// Número máximo de agendamentos disparados por tick do scheduler.
    /// Limita burst de execuções simultâneas em máquinas fracas.
    /// Env var de override: <c>PROTONS_ANCORAR_PDF_SCHEDULER_LIMIT_PER_TICK</c>.
    /// </summary>
    public int SchedulerLimitPerTick { get; set; } = AncorarPdfRuntimeTuningPolicy.DefaultSchedulerLimitPerTick;

    /// <summary>
    /// Atraso mínimo (em segundos) entre detecção de misfire e reexecução.
    /// Alinhado com <c>AncorarPdfMisfireBacklogPolicy.LimiarPadraoMisfire</c> (15 s).
    /// Env var de override: <c>PROTONS_ANCORAR_PDF_MISFIRE_MIN_SECONDS</c>.
    /// </summary>
    public int MisfireMinSeconds { get; set; } = AncorarPdfRuntimeTuningPolicy.DefaultMisfireMinSeconds;

    /// <summary>
    /// Timeout (em segundos) de execução de um worker individual.
    /// Execuções que excedem este limite são canceladas e marcadas como falha.
    /// Env var de override: <c>PROTONS_ANCORAR_PDF_WORKER_TIMEOUT_SECONDS</c>.
    /// </summary>
    public int WorkerTimeoutSeconds { get; set; } = AncorarPdfRuntimeTuningPolicy.DefaultWorkerTimeoutSeconds;

    /// <summary>
    /// Duração (em segundos) do lease de execução distribuída.
    /// Usado para evitar dupla execução em cenários multi-instância.
    /// Env var de override: <c>PROTONS_ANCORAR_PDF_LEASE_SECONDS</c>.
    /// </summary>
    public int LeaseDurationSeconds { get; set; } = AncorarPdfRuntimeTuningPolicy.DefaultLeaseDurationSeconds;

    /// <summary>
    /// Delay (em segundos) entre tentativas de retry em caso de falha de execução.
    /// Env var de override: <c>PROTONS_ANCORAR_PDF_RETRY_DELAY_SECONDS</c>.
    /// </summary>
    public int RetryDelaySeconds { get; set; } = AncorarPdfRuntimeTuningPolicy.DefaultRetryDelaySeconds;
}

/// <summary>
/// Responsável por carregar <see cref="AppSettings"/> do disco e aplicar overrides
/// de variáveis de ambiente sobre o resultado.
/// </summary>
/// <remarks>
/// <para><strong>Ordem de resolução de configuração:</strong></para>
/// <list type="number">
///   <item>Tenta <c>appsettings.json</c> no diretório de dados do usuário (<paramref name="appDataPath"/>).</item>
///   <item>Fallback: <c>appsettings.json</c> no diretório do executável (<c>AppContext.BaseDirectory</c>).</item>
///   <item>Fallback: <see cref="AppSettings"/> com valores padrão se nenhum arquivo for encontrado.</item>
///   <item>Sempre: overrides de variáveis de ambiente aplicados sobre o resultado via <see cref="AplicarOverridesPorAmbiente"/>.</item>
/// </list>
/// Variáveis de ambiente sempre têm prioridade sobre o arquivo JSON.
/// Erros de I/O ou JSON inválido fazem a carga usar os valores padrão silenciosamente.
/// </remarks>
internal static class AppSettingsLoader
{
    /// <summary>
    /// Carrega as configurações de forma assíncrona. Nunca lança exceções — em caso de
    /// falha retorna <see cref="AppSettings"/> com valores padrão.
    /// </summary>
    /// <param name="appDataPath">
    /// Diretório de dados da aplicação onde o <c>appsettings.json</c> do usuário pode existir.
    /// </param>
    public static async Task<AppSettings> LoadAsync(string appDataPath)
    {
        var appDataConfig = Path.Combine(appDataPath, "appsettings.json");
        var localConfig = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

        var configPath = File.Exists(appDataConfig)
            ? appDataConfig
            : File.Exists(localConfig) ? localConfig : null;

        if (configPath is null)
            return AplicarOverridesPorAmbiente(new AppSettings());

        try
        {
            var json = await File.ReadAllTextAsync(configPath).ConfigureAwait(false);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            var settings = JsonSerializer.Deserialize<AppSettings>(json, options) ?? new AppSettings();
            return AplicarOverridesPorAmbiente(settings);
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.WriteLine(ex);
            return AplicarOverridesPorAmbiente(new AppSettings());
        }
        catch (IOException ex)
        {
            Debug.WriteLine(ex);
            return AplicarOverridesPorAmbiente(new AppSettings());
        }
        catch (JsonException)
        {
            return AplicarOverridesPorAmbiente(new AppSettings());
        }
    }

    /// <summary>
    /// Aplica overrides de variáveis de ambiente sobre um <see cref="AppSettings"/> já
    /// carregado do disco. Variáveis ausentes ou inválidas não modificam o valor atual.
    /// </summary>
    /// <remarks>
    /// Após aplicar os overrides, os valores resolvidos de runtime do <c>ancorar_pdf</c>
    /// são republished como variáveis de ambiente para que processos filhos herdem
    /// as mesmas configurações sem necessidade de re-cálculo.
    /// </remarks>
    private static AppSettings AplicarOverridesPorAmbiente(AppSettings settings)
    {
        settings.AncorarPdf ??= new AncorarPdfSettings();
        settings.AncorarPdf.Preview ??= new AncorarPdfPreviewSettings();
        settings.AncorarPdf.Analyzer ??= new AncorarPdfAnalyzerSettings();
        settings.AncorarPdf.Runtime ??= new AncorarPdfRuntimeSettings();

        if (TryParseBool(Environment.GetEnvironmentVariable("ANCORA_PDF_PREVIEW_USE_GHOSTSCRIPT"), out var useGhostscript))
        {
            settings.AncorarPdf.Preview.UseGhostscriptFallback = useGhostscript;
        }
        Environment.SetEnvironmentVariable(
            "ANCORA_PDF_PREVIEW_USE_GHOSTSCRIPT",
            settings.AncorarPdf.Preview.UseGhostscriptFallback ? "1" : "0");

        var analyzer = settings.AncorarPdf.Analyzer;
        if (TryParseBool(Environment.GetEnvironmentVariable("PROTONS_ANCORAR_PDF_ANALYZER_V2"), out var analyzerV2))
            analyzer.AnalyzerV2 = analyzerV2;
        if (TryParseBool(Environment.GetEnvironmentVariable("PROTONS_ANCORAR_PDF_HYBRID_OCR"), out var hybridOcr))
            analyzer.HybridOcr = hybridOcr;
        if (TryParseBool(Environment.GetEnvironmentVariable("PROTONS_ANCORAR_PDF_MULTIPAGE_EDITOR"), out var multipageEditor))
            analyzer.MultipageEditor = multipageEditor;
        if (TryParseBool(Environment.GetEnvironmentVariable("PROTONS_ANCORAR_PDF_ADVANCED_ANCHORS"), out var advancedAnchors))
            analyzer.AdvancedAnchors = advancedAnchors;
        if (TryParseBool(Environment.GetEnvironmentVariable("PROTONS_ANCORAR_PDF_DIAGNOSTICS_OVERLAY"), out var diagnosticsOverlay))
            analyzer.DiagnosticsOverlay = diagnosticsOverlay;
        if (TryParseBool(Environment.GetEnvironmentVariable("PROTONS_ANCORAR_PDF_CLOUD_DOCUMENT_AI_PROVIDER"), out var cloudDocumentAiProvider))
            analyzer.CloudDocumentAiProvider = cloudDocumentAiProvider;

        Environment.SetEnvironmentVariable("PROTONS_ANCORAR_PDF_ANALYZER_V2", analyzer.AnalyzerV2 ? "1" : "0");
        Environment.SetEnvironmentVariable("PROTONS_ANCORAR_PDF_HYBRID_OCR", analyzer.HybridOcr ? "1" : "0");
        Environment.SetEnvironmentVariable("PROTONS_ANCORAR_PDF_MULTIPAGE_EDITOR", analyzer.MultipageEditor ? "1" : "0");
        Environment.SetEnvironmentVariable("PROTONS_ANCORAR_PDF_ADVANCED_ANCHORS", analyzer.AdvancedAnchors ? "1" : "0");
        Environment.SetEnvironmentVariable("PROTONS_ANCORAR_PDF_DIAGNOSTICS_OVERLAY", analyzer.DiagnosticsOverlay ? "1" : "0");
        Environment.SetEnvironmentVariable("PROTONS_ANCORAR_PDF_CLOUD_DOCUMENT_AI_PROVIDER", analyzer.CloudDocumentAiProvider ? "1" : "0");

        var connectionString = Environment.GetEnvironmentVariable("PROTONS_DB_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            settings.Database.Server.ConnectionString = connectionString.Trim();
        }

        if (TryParseBool(Environment.GetEnvironmentVariable("PROTONS_AUDIT_USE_HASH_CHAIN"), out var hashChain))
        {
            settings.Audit.UseHashChain = hashChain;
        }

        if (int.TryParse(Environment.GetEnvironmentVariable("PROTONS_PAINEL_TIMEOUT_MINUTOS"), out var timeoutMin))
        {
            settings.Security.Painel.TimeoutInatividadeMinutos = Clamp(timeoutMin, 5, 240);
        }
        else
        {
            settings.Security.Painel.TimeoutInatividadeMinutos = Clamp(settings.Security.Painel.TimeoutInatividadeMinutos, 5, 240);
            Environment.SetEnvironmentVariable(
                "PROTONS_PAINEL_TIMEOUT_MINUTOS",
                settings.Security.Painel.TimeoutInatividadeMinutos.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (TryParseBool(Environment.GetEnvironmentVariable("PROTONS_PAINEL_DIRETO_HABILITADO"), out var bypassHabilitado))
        {
            settings.Security.Painel.PermitirBypassPainelDireto = bypassHabilitado;
        }
        else
        {
            Environment.SetEnvironmentVariable(
                "PROTONS_PAINEL_DIRETO_HABILITADO",
                settings.Security.Painel.PermitirBypassPainelDireto ? "1" : "0");
        }

        if (TryParseBool(Environment.GetEnvironmentVariable("PROTONS_DATA_REQUIRE_BYOK"), out var requireByok))
        {
            settings.Security.DataProtection.RequireByokKey = requireByok;
        }

        var keyVarName = Environment.GetEnvironmentVariable("PROTONS_DATA_KEY_ENV_NAME");
        if (!string.IsNullOrWhiteSpace(keyVarName))
            settings.Security.DataProtection.KeyEnvVarName = keyVarName.Trim();

        if (TryParseBool(Environment.GetEnvironmentVariable("PROTONS_DATA_ALLOW_LEGACY_PLAINTEXT"), out var allowLegacy))
        {
            settings.Security.DataProtection.AllowLegacyPlaintext = allowLegacy;
        }

        if (TryParseBool(Environment.GetEnvironmentVariable("PROTONS_DATA_AUTO_PERSIST_LOCAL_KEY"), out var autoPersistLocalKey))
        {
            settings.Security.DataProtection.AutoPersistLocalKey = autoPersistLocalKey;
        }

        var allowedRootsRaw = Environment.GetEnvironmentVariable("PROTONS_ANCORAR_PDF_ALLOWED_NETWORK_ROOTS");
        if (!string.IsNullOrWhiteSpace(allowedRootsRaw))
        {
            settings.AncorarPdf.AllowedNetworkRoots = allowedRootsRaw
                .Split(new[] { ';', ',' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var runtime = settings.AncorarPdf.Runtime;

        if (int.TryParse(Environment.GetEnvironmentVariable("PROTONS_ANCORAR_PDF_WORKERS"), out var workers))
            runtime.Workers = workers;
        runtime.Workers = runtime.Workers == 0
            ? AncorarPdfRuntimeTuningPolicy.DefaultWorkers
            : AncorarPdfRuntimeTuningPolicy.ResolveWorkers(runtime.Workers, processorCount: 1);

        if (int.TryParse(Environment.GetEnvironmentVariable("PROTONS_ANCORAR_PDF_CHANNEL_CAPACITY"), out var channelCapacity))
            runtime.ChannelCapacity = channelCapacity;
        runtime.ChannelCapacity = AncorarPdfRuntimeTuningPolicy.ResolveChannelCapacity(runtime.ChannelCapacity);

        if (int.TryParse(Environment.GetEnvironmentVariable("PROTONS_ANCORAR_PDF_SCHEDULER_POLLING_MS"), out var schedulerPollingMs))
            runtime.SchedulerPollingMs = schedulerPollingMs;
        runtime.SchedulerPollingMs = AncorarPdfRuntimeTuningPolicy.ResolveSchedulerPollingMs(runtime.SchedulerPollingMs);

        if (int.TryParse(Environment.GetEnvironmentVariable("PROTONS_ANCORAR_PDF_SCHEDULER_LIMIT_PER_TICK"), out var schedulerLimitPerTick))
            runtime.SchedulerLimitPerTick = schedulerLimitPerTick;
        runtime.SchedulerLimitPerTick = AncorarPdfRuntimeTuningPolicy.ResolveSchedulerLimitPerTick(runtime.SchedulerLimitPerTick);

        if (int.TryParse(Environment.GetEnvironmentVariable("PROTONS_ANCORAR_PDF_MISFIRE_MIN_SECONDS"), out var misfireMinSeconds))
            runtime.MisfireMinSeconds = misfireMinSeconds;
        runtime.MisfireMinSeconds = AncorarPdfRuntimeTuningPolicy.ResolveMisfireMinSeconds(runtime.MisfireMinSeconds);

        if (int.TryParse(Environment.GetEnvironmentVariable("PROTONS_ANCORAR_PDF_WORKER_TIMEOUT_SECONDS"), out var workerTimeoutSeconds))
            runtime.WorkerTimeoutSeconds = workerTimeoutSeconds;
        runtime.WorkerTimeoutSeconds = AncorarPdfRuntimeTuningPolicy.ResolveWorkerTimeoutSeconds(runtime.WorkerTimeoutSeconds);

        if (int.TryParse(Environment.GetEnvironmentVariable("PROTONS_ANCORAR_PDF_LEASE_SECONDS"), out var leaseDurationSeconds))
            runtime.LeaseDurationSeconds = leaseDurationSeconds;
        runtime.LeaseDurationSeconds = AncorarPdfRuntimeTuningPolicy.ResolveLeaseDurationSeconds(runtime.LeaseDurationSeconds);

        if (int.TryParse(Environment.GetEnvironmentVariable("PROTONS_ANCORAR_PDF_RETRY_DELAY_SECONDS"), out var retryDelaySeconds))
            runtime.RetryDelaySeconds = retryDelaySeconds;
        runtime.RetryDelaySeconds = AncorarPdfRuntimeTuningPolicy.ResolveRetryDelaySeconds(runtime.RetryDelaySeconds);

        // Republica valores resolvidos como env vars para que processos filhos herdem
        // as configurações já normalizadas sem necessidade de re-cálculo.
        Environment.SetEnvironmentVariable(
            "PROTONS_ANCORAR_PDF_WORKERS",
            runtime.Workers.ToString(CultureInfo.InvariantCulture));
        Environment.SetEnvironmentVariable(
            "PROTONS_ANCORAR_PDF_CHANNEL_CAPACITY",
            runtime.ChannelCapacity.ToString(CultureInfo.InvariantCulture));
        Environment.SetEnvironmentVariable(
            "PROTONS_ANCORAR_PDF_SCHEDULER_POLLING_MS",
            runtime.SchedulerPollingMs.ToString(CultureInfo.InvariantCulture));
        Environment.SetEnvironmentVariable(
            "PROTONS_ANCORAR_PDF_SCHEDULER_LIMIT_PER_TICK",
            runtime.SchedulerLimitPerTick.ToString(CultureInfo.InvariantCulture));
        Environment.SetEnvironmentVariable(
            "PROTONS_ANCORAR_PDF_MISFIRE_MIN_SECONDS",
            runtime.MisfireMinSeconds.ToString(CultureInfo.InvariantCulture));
        Environment.SetEnvironmentVariable(
            "PROTONS_ANCORAR_PDF_WORKER_TIMEOUT_SECONDS",
            runtime.WorkerTimeoutSeconds.ToString(CultureInfo.InvariantCulture));
        Environment.SetEnvironmentVariable(
            "PROTONS_ANCORAR_PDF_LEASE_SECONDS",
            runtime.LeaseDurationSeconds.ToString(CultureInfo.InvariantCulture));
        Environment.SetEnvironmentVariable(
            "PROTONS_ANCORAR_PDF_RETRY_DELAY_SECONDS",
            runtime.RetryDelaySeconds.ToString(CultureInfo.InvariantCulture));

        return settings;
    }

    /// <summary>
    /// Tenta interpretar uma string de variável de ambiente como booleano.
    /// Aceita: <c>"1"</c>/<c>"true"</c> → <c>true</c>; <c>"0"</c>/<c>"false"</c> → <c>false</c>.
    /// Comparação case-insensitive. Retorna <c>false</c> se a string for inválida ou nula.
    /// </summary>
    private static bool TryParseBool(string? raw, out bool value)
    {
        value = false;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        if (string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase))
        {
            value = true;
            return true;
        }

        if (string.Equals(raw, "0", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase))
        {
            value = false;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Limita <paramref name="value"/> ao intervalo [<paramref name="min"/>, <paramref name="max"/>].
    /// Substitui <see cref="Math.Clamp"/> para evitar dependência implícita de sobrecarga.
    /// </summary>
    private static int Clamp(int value, int min, int max)
    {
        if (value < min)
            return min;
        if (value > max)
            return max;
        return value;
    }
}
