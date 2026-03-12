using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Protons.UI.Common;

/// <summary>
/// Fornece caminhos de dados cross-platform seguindo padrões do sistema operacional.
/// - Linux: XDG_DATA_HOME/Protons ou ~/.local/share/Protons
/// - Windows: %AppData%\Protons
/// - macOS: ~/Library/Application Support/Protons
/// </summary>
internal static class DataPathProvider
{
    /// <summary>
    /// Obtém o diretório base de dados do aplicativo seguindo padrões do OS.
    /// </summary>
    public static string GetAppDataPath()
    {
        string path;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Seguir XDG Base Directory Specification
            // https://specifications.freedesktop.org/basedir-spec/basedir-spec-latest.html
            var xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");

            if (!string.IsNullOrWhiteSpace(xdgDataHome))
            {
                path = Path.Combine(xdgDataHome, "Protons");
                return NormalizeOrFallback(path);
            }

            // Fallback padrão: ~/.local/share/Protons
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            path = Path.Combine(home, ".local", "share", "Protons");
            return NormalizeOrFallback(path);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows: %AppData%\Protons
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            path = Path.Combine(appData, "Protons");
            return NormalizeOrFallback(path);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // macOS: ~/Library/Application Support/Protons
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            path = Path.Combine(home, "Library", "Application Support", "Protons");
            return NormalizeOrFallback(path);
        }
        else
        {
            // Fallback genérico
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            path = Path.Combine(appData, "Protons");
            return NormalizeOrFallback(path);
        }
    }

    /// <summary>
    /// Obtém o diretório de dados para um módulo específico.
    /// </summary>
    public static string GetModuleDataPath(string moduleName)
    {
        if (string.IsNullOrWhiteSpace(moduleName))
            throw new ArgumentException("Module name cannot be null or whitespace.", nameof(moduleName));

        return Path.Combine(GetAppDataPath(), moduleName);
    }

    /// <summary>
    /// Cria o diretório de dados do aplicativo se não existir.
    /// </summary>
    public static void EnsureAppDataPathExists()
    {
        var path = GetAppDataPath();
        Directory.CreateDirectory(path);
    }

    /// <summary>
    /// Migra dados legados de %LocalAppData% para %AppData% no Windows, sem sobrescrever.
    /// </summary>
    public static void MigrateLegacyLocalAppDataIfNeeded(string? targetAppDataPath = null)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        var legacyBase = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var legacyPath = Path.Combine(legacyBase, "Protons");
        if (!Directory.Exists(legacyPath))
            return;

        var currentPath = targetAppDataPath;
        if (string.IsNullOrWhiteSpace(currentPath))
            currentPath = GetAppDataPath();

        try
        {
            var legacyFull = Path.GetFullPath(legacyPath);
            var currentFull = Path.GetFullPath(currentPath);
            if (string.Equals(legacyFull, currentFull, StringComparison.OrdinalIgnoreCase))
                return;

            Directory.CreateDirectory(currentFull);

            foreach (var dir in Directory.EnumerateDirectories(legacyFull, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(legacyFull, dir);
                var targetDir = Path.Combine(currentFull, rel);
                Directory.CreateDirectory(targetDir);
            }

            foreach (var file in Directory.EnumerateFiles(legacyFull, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(legacyFull, file);
                var targetFile = Path.Combine(currentFull, rel);
                var targetDir = Path.GetDirectoryName(targetFile);
                if (!string.IsNullOrWhiteSpace(targetDir))
                    Directory.CreateDirectory(targetDir);

                if (!File.Exists(targetFile))
                    File.Copy(file, targetFile, overwrite: false);
            }

            Debug.WriteLine($"DataPathProvider migration completed: {legacyFull} -> {currentFull}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"DataPathProvider migration failed: {ex}");
        }
    }

    /// <summary>
    /// Cria o diretório de dados de um módulo se não existir.
    /// </summary>
    public static void EnsureModuleDataPathExists(string moduleName)
    {
        var path = GetModuleDataPath(moduleName);
        Directory.CreateDirectory(path);
    }

    private static string NormalizeOrFallback(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!string.IsNullOrWhiteSpace(fullPath) && Path.IsPathRooted(fullPath))
                return fullPath;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"DataPathProvider.NormalizeOrFallback exception: {ex}");
        }

        var fallback = Path.Combine(Path.GetTempPath(), "Protons");
        var fallbackFull = Path.GetFullPath(fallback);
        Debug.WriteLine($"DataPathProvider fallback to temp path: {fallbackFull}");
        return fallbackFull;
    }
}
