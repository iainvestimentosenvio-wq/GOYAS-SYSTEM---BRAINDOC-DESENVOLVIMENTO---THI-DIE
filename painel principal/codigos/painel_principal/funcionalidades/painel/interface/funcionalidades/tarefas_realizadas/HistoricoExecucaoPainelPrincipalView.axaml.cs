using System;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Protons.UI.Painel.ViewModels;

namespace Protons.UI.Painel.Views.PainelPrincipal.Funcionalidades.TarefasRealizadas;

public partial class HistoricoExecucaoPainelPrincipalView : UserControl
{
    private PainelViewModel? _vm;

    public HistoricoExecucaoPainelPrincipalView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        SizeChanged += OnViewSizeChanged;
    }

    private void OnHistoricoItemClicked(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.DataContext is HistoricoExecucaoItem item)
        {
            var vm = DataContext as PainelViewModel;
            vm?.NavegarParaHistoricoItemCommand.Execute(item);
            e.Handled = true;
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        DesregistrarVmAtual();

        _vm = DataContext as PainelViewModel;

        if (_vm is not null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
            _vm.HistoricoExecucao.CollectionChanged += OnHistoricoExecucaoChanged;
        }

        AtualizarDockHistorico();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        DesregistrarVmAtual();
    }

    private void OnViewSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_vm?.PainelHistoricoExecucaoDockAberto == true)
            AtualizarDockHistorico();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PainelViewModel.PainelHistoricoExecucaoDockAberto) ||
            e.PropertyName == nameof(PainelViewModel.ResumoHistoricoExecucao))
        {
            AtualizarDockHistorico();
        }
    }

    private void OnHistoricoExecucaoChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        AtualizarDockHistorico();
    }

    private void AtualizarDockHistorico()
    {
        var painelAberto = _vm?.PainelHistoricoExecucaoDockAberto == true;
        HistoricoExecucaoConteudoHost.Height = painelAberto
            ? ObterAlturaExpandidaConteudo()
            : 0d;
    }

    private double ObterAlturaExpandidaConteudo()
    {
        var larguraDisponivel = Bounds.Width;

        if (larguraDisponivel <= 0d)
            larguraDisponivel = HistoricoExecucaoConteudoHost.Bounds.Width;

        if (larguraDisponivel <= 0d)
            return 300d;

        HistoricoExecucaoConteudoCard.Measure(new Size(larguraDisponivel, double.PositiveInfinity));
        return Math.Max(300d, HistoricoExecucaoConteudoCard.DesiredSize.Height);
    }

    private void DesregistrarVmAtual()
    {
        if (_vm is null)
            return;

        _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm.HistoricoExecucao.CollectionChanged -= OnHistoricoExecucaoChanged;
        _vm = null;
    }
}
