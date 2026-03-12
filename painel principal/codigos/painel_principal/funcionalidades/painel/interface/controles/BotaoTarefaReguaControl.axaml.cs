using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Protons.UI.Painel.Views.PainelPrincipal.Controles;

public partial class BotaoTarefaReguaControl : UserControl
{
    public static readonly StyledProperty<bool> EstaNoPassadoProperty =
        AvaloniaProperty.Register<BotaoTarefaReguaControl, bool>(nameof(EstaNoPassado), false);

    public bool EstaNoPassado
    {
        get => GetValue(EstaNoPassadoProperty);
        set => SetValue(EstaNoPassadoProperty, value);
    }

    static BotaoTarefaReguaControl()
    {
        EstaNoPassadoProperty.Changed.AddClassHandler<BotaoTarefaReguaControl>(
            (control, e) => control.AtualizarOpacidade());
    }

    public BotaoTarefaReguaControl()
    {
        InitializeComponent();
    }

    private void AtualizarOpacidade()
    {
        this.Opacity = EstaNoPassado ? 0.5 : 1.0; // 50% para passado
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (DataContext is Protons.UI.Painel.ViewModels.TarefaReguaItem item)
        {
            TarefaClicada?.Invoke(this, item.TarefaId);
        }

        e.Handled = true;
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        if (CardBorda is not null)
            CardBorda.Background = new SolidColorBrush(Color.Parse("#EEF4FF"));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (CardBorda is not null)
            CardBorda.Background = new SolidColorBrush(Color.Parse("#F8FAFC"));
    }

    public event EventHandler<int>? TarefaClicada;
}
