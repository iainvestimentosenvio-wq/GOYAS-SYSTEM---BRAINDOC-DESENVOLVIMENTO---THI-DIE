using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Protons.UI.Common;

namespace Protons.UI.Login.Services;

public sealed class LocalSettings
{
    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };
    private readonly string _settingsPath;

    public LocalSettings(string settingsPath)
    {
        _settingsPath = settingsPath;
    }

    public string? LoadLastEmail()
    {
        if (!File.Exists(_settingsPath))
            return null;

        try
        {
            var json = File.ReadAllText(_settingsPath);
            if (string.IsNullOrWhiteSpace(json))
                return null;
            var data = JsonSerializer.Deserialize<SettingsData>(json);
            return data?.UltimoEmail;
        }
        catch (UnauthorizedAccessException ex)
        {
            OpsLogger.WriteError("local_settings: falha ao ler", ex);
            return null;
        }
        catch (IOException ex)
        {
            OpsLogger.WriteError("local_settings: falha ao ler", ex);
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<string?> LoadLastEmailAsync()
    {
        if (!File.Exists(_settingsPath))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(_settingsPath).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
                return null;
            var data = JsonSerializer.Deserialize<SettingsData>(json);
            return data?.UltimoEmail;
        }
        catch (UnauthorizedAccessException ex)
        {
            OpsLogger.WriteError("local_settings: falha ao ler", ex);
            return null;
        }
        catch (IOException ex)
        {
            OpsLogger.WriteError("local_settings: falha ao ler", ex);
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void SaveLastEmail(string? email)
    {
        var data = new SettingsData { UltimoEmail = email };
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath) ?? ".");
            var json = JsonSerializer.Serialize(data, s_jsonOptions);
            File.WriteAllText(_settingsPath, json);
        }
        catch (UnauthorizedAccessException ex)
        {
            OpsLogger.WriteError("local_settings: falha ao salvar", ex);
        }
        catch (IOException ex)
        {
            OpsLogger.WriteError("local_settings: falha ao salvar", ex);
        }
    }

    public async Task SaveLastEmailAsync(string? email)
    {
        var data = new SettingsData { UltimoEmail = email };
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath) ?? ".");
            var json = JsonSerializer.Serialize(data, s_jsonOptions);
            await File.WriteAllTextAsync(_settingsPath, json).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException ex)
        {
            OpsLogger.WriteError("local_settings: falha ao salvar", ex);
        }
        catch (IOException ex)
        {
            OpsLogger.WriteError("local_settings: falha ao salvar", ex);
        }
    }

    private sealed class SettingsData
    {
        public string? UltimoEmail { get; set; }
    }
}
