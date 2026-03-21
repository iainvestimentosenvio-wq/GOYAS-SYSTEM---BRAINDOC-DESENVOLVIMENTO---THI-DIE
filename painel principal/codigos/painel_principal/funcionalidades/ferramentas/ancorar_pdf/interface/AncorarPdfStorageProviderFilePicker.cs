using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Protons.UI.Painel.Funcionalidades.Ferramentas.AncorarPdf.Interface;

public sealed class AncorarPdfStorageProviderFilePicker : IAncorarPdfFilePicker
{
    private readonly Func<TopLevel?> _resolveTopLevel;

    public AncorarPdfStorageProviderFilePicker(Func<TopLevel?> resolveTopLevel)
    {
        _resolveTopLevel = resolveTopLevel;
    }

    /// <summary>
    /// Garante que a janela principal esteja em foco antes de abrir o diálogo.
    /// Corrige o bug onde o file picker aparece atrás da janela (Linux/Windows).
    /// Ref: Avalonia #10751, #10998
    /// </summary>
    private void GarantirJanelaEmFoco()
    {
        var topLevel = _resolveTopLevel();
        if (topLevel is Window window)
            window.Activate();
    }

    public async Task<string?> SelecionarPastaAsync()
    {
        var storage = _resolveTopLevel()?.StorageProvider;
        if (storage is null)
            return null;

        GarantirJanelaEmFoco();
        var resultado = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Selecionar pasta monitorada",
            AllowMultiple = false
        });

        return ResolverLocalPath(resultado.FirstOrDefault());
    }

    public async Task<string?> SelecionarPdfModeloArquivoAsync()
    {
        var storage = _resolveTopLevel()?.StorageProvider;
        if (storage is null)
            return null;

        GarantirJanelaEmFoco();
        var resultado = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Selecionar arquivo PDF modelo",
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.Pdf]
        });

        return ResolverLocalPath(resultado.FirstOrDefault());
    }

    public async Task<string?> SelecionarPdfModeloPastaWizardAsync()
    {
        var storage = _resolveTopLevel()?.StorageProvider;
        if (storage is null)
            return null;

        GarantirJanelaEmFoco();
        var resultado = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Selecionar pasta do PDF modelo (wizard)",
            AllowMultiple = false
        });

        return ResolverLocalPath(resultado.FirstOrDefault());
    }

    private static string? ResolverLocalPath(IStorageItem? item)
    {
        if (item is null)
            return null;

        if (item.TryGetLocalPath() is { Length: > 0 } localPath)
            return localPath;

        return item.Path.LocalPath;
    }
}
