using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Protons.UI.Painel.Funcionalidades.Ferramentas.AncorarPdf.Interface;
using Protons.UI.Painel.Funcionalidades.Ferramentas.AncorarPdf.ModelosDeVisao;

namespace Protons.UI.Painel.Views.PainelPrincipal.Funcionalidades.Ferramentas.AncorarPdf;

public partial class AncorarPdfConfiguracaoView : UserControl
{
    private bool _capturaEmAndamento;
    private bool _smartClickArmado;
    private Point _capturaInicio;

    public AncorarPdfConfiguracaoView()
    {
        InitializeComponent();
    }

    private AncorarPdfConfiguracaoViewModel? Vm => DataContext as AncorarPdfConfiguracaoViewModel;

    private void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Vm is null || !Vm.PodeEditar)
            return;

        if (sender is not Canvas canvas)
            return;

        var point = e.GetCurrentPoint(canvas);
        if (!point.Properties.IsLeftButtonPressed)
            return;

        _capturaInicio = point.Position;
        _smartClickArmado = false;

        switch (Vm.FerramentaInteracaoSelecionada)
        {
            case AncorarPdfFerramentaInteracao.RetanguloManual:
                _capturaEmAndamento = true;
                AtualizarRascunhoSelecao(_capturaInicio, _capturaInicio);
                e.Pointer.Capture(canvas);
                e.Handled = true;
                break;

            case AncorarPdfFerramentaInteracao.SmartClick:
                _capturaEmAndamento = false;
                _smartClickArmado = true;
                e.Handled = true;
                break;
        }
    }

    private void OnPreviewPointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not Canvas canvas)
            return;

        if (_smartClickArmado)
        {
            var pontoAtual = e.GetPosition(canvas);
            if (Math.Abs(pontoAtual.X - _capturaInicio.X) >= 4 || Math.Abs(pontoAtual.Y - _capturaInicio.Y) >= 4)
                _smartClickArmado = false;
        }

        if (!_capturaEmAndamento)
            return;

        var atual = e.GetPosition(canvas);
        AtualizarRascunhoSelecao(_capturaInicio, atual);
        e.Handled = true;
    }

    private void OnPreviewPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (Vm is null || sender is not Canvas canvas)
            return;

        if (Vm.FerramentaInteracaoSelecionada == AncorarPdfFerramentaInteracao.SmartClick)
        {
            var fimSmart = e.GetPosition(canvas);
            var larguraSmart = Math.Abs(fimSmart.X - _capturaInicio.X);
            var alturaSmart = Math.Abs(fimSmart.Y - _capturaInicio.Y);
            if (_smartClickArmado && larguraSmart < 4 && alturaSmart < 4)
            {
                var larguraCanvasSmart = Math.Max(1.0, canvas.Bounds.Width);
                var alturaCanvasSmart = Math.Max(1.0, canvas.Bounds.Height);
                var xRel = Math.Clamp(_capturaInicio.X / larguraCanvasSmart, 0, 1);
                var yRel = Math.Clamp(_capturaInicio.Y / alturaCanvasSmart, 0, 1);
                Vm.ExecutarSmartClickCommand.Execute(
                    new Protons.Core.Tarefas.Models.SmartDeteccaoEntrada(xRel, yRel, Vm.PaginaPreviewAtual));
                e.Handled = true;
            }

            _smartClickArmado = false;
            return;
        }

        if (!_capturaEmAndamento)
            return;

        _capturaEmAndamento = false;
        e.Pointer.Capture(null);

        var fim = e.GetPosition(canvas);
        var largura = Math.Abs(fim.X - _capturaInicio.X);
        var altura = Math.Abs(fim.Y - _capturaInicio.Y);
        var larguraCanvas = Math.Max(1.0, canvas.Bounds.Width);
        var alturaCanvas = Math.Max(1.0, canvas.Bounds.Height);

        SelectionDraftBorder.IsVisible = false;

        if (largura < 4 || altura < 4)
            return;

        var x = Math.Min(_capturaInicio.X, fim.X);
        var y = Math.Min(_capturaInicio.Y, fim.Y);
        var selecao = new AncorarPdfPreviewSelection(
            Pagina: Vm.PaginaPreviewAtual,
            XRel: Math.Clamp(x / larguraCanvas, 0, 1),
            YRel: Math.Clamp(y / alturaCanvas, 0, 1),
            LarguraRel: Math.Clamp(largura / larguraCanvas, 0, 1),
            AlturaRel: Math.Clamp(altura / alturaCanvas, 0, 1),
            ViaTexto: false);

        Vm.RegistrarSelecaoPreviewCommand.Execute(selecao);
        e.Handled = true;
    }

    private void OnPreviewScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (Vm is null || sender is not ScrollViewer viewer)
            return;

        var maxX = Math.Max(0.0, viewer.Extent.Width - viewer.Viewport.Width);
        var maxY = Math.Max(0.0, viewer.Extent.Height - viewer.Viewport.Height);
        var relX = maxX <= 0 ? 0 : viewer.Offset.X / maxX;
        var relY = maxY <= 0 ? 0 : viewer.Offset.Y / maxY;

        Vm.AtualizarViewportPreview(relX, relY);
    }

    private void OnAncoraItemPointerEntered(object? sender, PointerEventArgs e)
    {
        if (Vm is null)
            return;

        var ancora = (sender as Control)?.DataContext as AncorarPdfAncoraItemViewModel;
        Vm.DestacarAncoraPorHoverCommand.Execute(ancora);
    }

    private void OnAncoraItemPointerExited(object? sender, PointerEventArgs e)
    {
        Vm?.DestacarAncoraPorHoverCommand.Execute(null);
    }

    private void OnAncoraItemPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Vm is null)
            return;

        var ancora = (sender as Control)?.DataContext as AncorarPdfAncoraItemViewModel;
        Vm.SelecionarAncoraCommand.Execute(ancora);
        e.Handled = true;
    }

    private void OnPreviewWheelZoom(object? sender, PointerWheelEventArgs e)
    {
        if (Vm is null) return;
        e.Handled = true;
        var passo = e.Delta.Y > 0 ? 0.15 : -0.15;
        Vm.ZoomPreview = Math.Clamp(Vm.ZoomPreview + passo, 0.5, 4.0);
    }

    private void AtualizarRascunhoSelecao(Point inicio, Point fim)
    {
        var x = Math.Min(inicio.X, fim.X);
        var y = Math.Min(inicio.Y, fim.Y);
        var largura = Math.Abs(fim.X - inicio.X);
        var altura = Math.Abs(fim.Y - inicio.Y);

        SelectionDraftBorder.IsVisible = true;
        SelectionDraftBorder.Width = largura;
        SelectionDraftBorder.Height = altura;
        Canvas.SetLeft(SelectionDraftBorder, x);
        Canvas.SetTop(SelectionDraftBorder, y);
    }
}
