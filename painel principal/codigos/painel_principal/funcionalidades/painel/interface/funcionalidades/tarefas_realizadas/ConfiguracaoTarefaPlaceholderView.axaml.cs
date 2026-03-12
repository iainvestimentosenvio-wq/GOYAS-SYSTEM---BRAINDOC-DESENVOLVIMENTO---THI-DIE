using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Protons.UI.Painel.Views.PainelPrincipal.Funcionalidades.TarefasRealizadas;

public partial class ConfiguracaoTarefaPlaceholderView : UserControl
{
    public ConfiguracaoTarefaPlaceholderView()
    {
        InitializeComponent();
        PropertyChanged += OnControlPropertyChanged;
    }

    private void OnControlPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != IsVisibleProperty || !IsVisible)
            return;

        if (PainelCard.RenderTransform is not ScaleTransform painelScale)
            return;

        PainelCard.Opacity = 0;
        painelScale.ScaleX = 0.82;
        painelScale.ScaleY = 0.82;

        Dispatcher.UIThread.Post(() =>
        {
            PainelCard.Opacity = 1;
            painelScale.ScaleX = 1;
            painelScale.ScaleY = 1;
        }, DispatcherPriority.Render);
    }
}
