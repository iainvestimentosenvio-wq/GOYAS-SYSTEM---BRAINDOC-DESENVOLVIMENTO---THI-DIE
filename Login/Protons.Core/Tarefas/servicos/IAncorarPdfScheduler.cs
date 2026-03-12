namespace Protons.Core.Tarefas.Services;

/// <summary>
/// Abstração do scheduler de ancorar_pdf.
/// Permite trocar a implementação (ex.: Quartz.NET) sem alterar consumidores.
/// </summary>
public interface IAncorarPdfScheduler : IDisposable
{
    void Start();
    void Stop(TimeSpan? timeout = null);
}
