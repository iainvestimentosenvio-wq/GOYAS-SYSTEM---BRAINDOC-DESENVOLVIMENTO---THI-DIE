using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Protons.UI.Painel.Views.PainelPrincipal.Partes;

public partial class AcoesSuperioresPainelPrincipalView : UserControl
{
    public AcoesSuperioresPainelPrincipalView()
    {
        InitializeComponent();
    }

    private Window? GetParentWindow()
    {
        return this.FindAncestorOfType<Window>();
    }

    private void MinimizeButton_Click(object? sender, RoutedEventArgs e)
    {
        var window = GetParentWindow();
        if (window is not null)
        {
            window.WindowState = WindowState.Minimized;
        }
    }

    private void MaximizeRestoreButton_Click(object? sender, RoutedEventArgs e)
    {
        var window = GetParentWindow();
        if (window is null)
        {
            return;
        }

        if (window.WindowState == WindowState.Maximized || window.WindowState == WindowState.FullScreen)
        {
            window.WindowState = WindowState.Normal;
            window.Width = 1280;
            window.Height = 720;
            CenterWindow(window);
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            window.WindowState = WindowState.FullScreen;
            return;
        }

        window.WindowState = WindowState.Maximized;
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        var window = GetParentWindow();
        window?.Close();
    }

    private static void CenterWindow(Window window)
    {
        var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var workArea = screen.WorkingArea;
        var x = (int)workArea.X + (int)((workArea.Width - window.Width) / 2);
        var y = (int)workArea.Y + (int)((workArea.Height - window.Height) / 2);
        window.Position = new Avalonia.PixelPoint(x, y);
    }
}
