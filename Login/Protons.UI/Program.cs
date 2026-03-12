using Avalonia;
using System;

namespace Protons.UI;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (InvalidOperationException ex) when (EhFalhaAceitavelDeStartupProbe(ex))
        {
            // Em modo probe o app pode encerrar o dispatcher cedo.
            // Esse caso nao deve derrubar o smoke.
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static bool EhFalhaAceitavelDeStartupProbe(InvalidOperationException ex)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("PROTONS_STARTUP_PROBE"), "1", StringComparison.Ordinal))
            return false;

        return ex.Message.Contains("Dispatcher shut down", StringComparison.OrdinalIgnoreCase);
    }
}
