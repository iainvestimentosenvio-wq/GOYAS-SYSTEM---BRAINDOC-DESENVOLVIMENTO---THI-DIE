using System.Threading.Tasks;

namespace Protons.UI.Painel.Funcionalidades.Ferramentas.AncorarPdf.Interface;

public interface IAncorarPdfFilePicker
{
    Task<string?> SelecionarPastaAsync();
    Task<string?> SelecionarPdfModeloArquivoAsync();
    Task<string?> SelecionarPdfModeloPastaWizardAsync();
}

public sealed class AncorarPdfNullFilePicker : IAncorarPdfFilePicker
{
    public Task<string?> SelecionarPastaAsync()
    {
        return Task.FromResult<string?>(null);
    }

    public Task<string?> SelecionarPdfModeloArquivoAsync()
    {
        return Task.FromResult<string?>(null);
    }

    public Task<string?> SelecionarPdfModeloPastaWizardAsync()
    {
        return Task.FromResult<string?>(null);
    }
}
