using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Protons.UI.Common;
using Protons.UI.Painel.ViewModels;
using Protons.UI.Painel.Views.PainelPrincipal.Controles;

namespace Protons.UI.Painel.Views.PainelPrincipal.Funcionalidades.TarefasRealizadas;

public partial class PainelReguaTempoView : UserControl
{
    // --- Paleta de cores para construção programática da UI ---
    private static readonly SolidColorBrush BrushTextoClaro = new(Color.Parse("#1E293B"));
    private static readonly SolidColorBrush BrushFundoPainel = new(Color.Parse("#D4E0EC"));
    private static readonly SolidColorBrush BrushFundoInput = new(Color.Parse("#FBFCFE"));
    private static readonly SolidColorBrush BrushBordaSutil = new(Color.Parse("#C9D6E3"));
    private static readonly SolidColorBrush BrushErroFundo = new(Color.Parse("#FEE2E2"));
    private static readonly SolidColorBrush BrushErroTexto = new(Color.Parse("#B91C1C"));
    private static readonly SolidColorBrush BrushErroBorda = new(Color.Parse("#FCA5A5"));
    private static readonly SolidColorBrush BrushBotaoPrimarioFundo = new(Color.Parse("#1E3A8A"));
    private static readonly SolidColorBrush BrushBotaoPrimarioTexto = new(Color.Parse("#DBEAFE"));
    private static readonly SolidColorBrush BrushBotaoPrimarioBorda = new(Color.Parse("#3B82F6"));
    private static readonly SolidColorBrush BrushBotaoSecundarioFundo = new(Color.Parse("#E5ECF3"));
    private static readonly SolidColorBrush BrushBotaoSecundarioTexto = new(Color.Parse("#1E293B"));
    private static readonly SolidColorBrush BrushBotaoSecundarioBorda = new(Color.Parse("#C3D1E0"));
    private static readonly SolidColorBrush BrushFlyoutFundo = new(Color.Parse("#FBFCFE"));
    private static readonly SolidColorBrush BrushFlyoutBorda = new(Color.Parse("#CDD9E6"));

    private sealed class FaixaEsteiraRefs
    {
        public required EsteiraViewModel Esteira { get; init; }
        public required ReguaTempoControl Regua { get; init; }
        public required EsteiraTarefasControl Container { get; init; }
        public required Action AtualizarVisualZoom { get; init; }
    }

    private PainelViewModel? _vm;
    private Flyout? _confirmacaoRemoverFlyout;
    private readonly Dictionary<EsteiraViewModel, NotifyCollectionChangedEventHandler> _handlersTarefasPorEsteira = new();
    private readonly Dictionary<EsteiraViewModel, PropertyChangedEventHandler> _handlersZoomPorEsteira = new();
    private readonly Dictionary<EsteiraViewModel, Border> _faixasPorEsteira = new();

    public PainelReguaTempoView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.Esteiras.CollectionChanged -= OnEsteirasChanged;
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }

        DesregistrarHandlersEsteiras();
        _confirmacaoRemoverFlyout?.Hide();
        _confirmacaoRemoverFlyout = null;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.Esteiras.CollectionChanged -= OnEsteirasChanged;
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }
        DesregistrarHandlersEsteiras();

        _vm = DataContext as PainelViewModel;

        if (_vm is not null)
        {
            _vm.Esteiras.CollectionChanged += OnEsteirasChanged;
            _vm.PropertyChanged += OnVmPropertyChanged;
            ReconstruirEsteiras();
            AtualizarZoomLabel();
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PainelViewModel.HoraAtualRegua))
        {
            AtualizarOpacidadeTarefas();
        }
    }

    private void OnEsteirasChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ReconstruirEsteiras();
    }

    private void ReconstruirEsteiras()
    {
        if (_vm is null)
            return;

        DesregistrarHandlersEsteiras();
        EsteirasContainer.Children.Clear();

        foreach (var esteira in _vm.Esteiras.OrderBy(x => x.Ordem))
        {
            var faixa = CriarFaixaEsteira(esteira);
            EsteirasContainer.Children.Add(faixa);

            RegistrarHandlerTarefasEsteira(esteira, faixa);
            RegistrarHandlerZoomEsteira(esteira, faixa);
            ReconstruirBotoesEsteira(faixa, esteira);
        }
    }

    private void RegistrarHandlerTarefasEsteira(EsteiraViewModel esteira, Border faixa)
    {
        _faixasPorEsteira[esteira] = faixa;

        if (_handlersTarefasPorEsteira.TryGetValue(esteira, out var existente))
            esteira.Tarefas.CollectionChanged -= existente;

        NotifyCollectionChangedEventHandler handler = (_, _) =>
        {
            if (_faixasPorEsteira.TryGetValue(esteira, out var faixaAtual))
                ReconstruirBotoesEsteira(faixaAtual, esteira);
        };

        _handlersTarefasPorEsteira[esteira] = handler;
        esteira.Tarefas.CollectionChanged += handler;
    }

    private void RegistrarHandlerZoomEsteira(EsteiraViewModel esteira, Border faixa)
    {
        if (_handlersZoomPorEsteira.TryGetValue(esteira, out var existente))
            esteira.PropertyChanged -= existente;

        PropertyChangedEventHandler handler = (_, args) =>
        {
            if (args.PropertyName != nameof(EsteiraViewModel.NivelZoom))
                return;

            if (_faixasPorEsteira.TryGetValue(esteira, out var faixaAtual) &&
                faixaAtual.Tag is FaixaEsteiraRefs refs)
            {
                refs.AtualizarVisualZoom();
            }
        };

        _handlersZoomPorEsteira[esteira] = handler;
        esteira.PropertyChanged += handler;

        if (faixa.Tag is FaixaEsteiraRefs refs)
            refs.AtualizarVisualZoom();
    }

    private void DesregistrarHandlersEsteiras()
    {
        foreach (var (esteira, handler) in _handlersTarefasPorEsteira)
            esteira.Tarefas.CollectionChanged -= handler;

        foreach (var (esteira, handler) in _handlersZoomPorEsteira)
            esteira.PropertyChanged -= handler;

        _handlersTarefasPorEsteira.Clear();
        _handlersZoomPorEsteira.Clear();
        _faixasPorEsteira.Clear();
    }

    private Border CriarFaixaEsteira(EsteiraViewModel esteira)
    {
        var regua = new ReguaTempoControl
        {
            Height = 60,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0)
        };

        var container = new EsteiraTarefasControl
        {
            Height = 76,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0),
            EsteiraId = esteira.Id
        };

        if (_vm is not null)
        {
            regua.Bind(ReguaTempoControl.NivelZoomProperty, new Binding { Source = esteira, Path = nameof(EsteiraViewModel.NivelZoom), Mode = BindingMode.TwoWay });
            regua.Bind(ReguaTempoControl.CentroTemporalProperty, new Binding { Source = esteira, Path = nameof(EsteiraViewModel.CentroTemporal), Mode = BindingMode.TwoWay });
            regua.Bind(ReguaTempoControl.HoraAtualProperty, new Binding { Source = _vm, Path = nameof(PainelViewModel.HoraAtualRegua) });

            container.Bind(EsteiraTarefasControl.NivelZoomProperty, new Binding { Source = esteira, Path = nameof(EsteiraViewModel.NivelZoom) });
            container.Bind(EsteiraTarefasControl.CentroTemporalProperty, new Binding { Source = esteira, Path = nameof(EsteiraViewModel.CentroTemporal) });
        }

        regua.ModoManualAtivado += (_, _) =>
        {
            if (_vm != null)
                _vm.ModoAutomaticoRegua = false;
        };

        regua.ModoAutomaticoAtivado += (_, _) =>
        {
            if (_vm != null)
                _vm.ModoAutomaticoRegua = true;
        };

        container.FerramentaSoltaNaEsteira += (_, args) =>
        {
            _vm?.AbrirConfigNovaTarefaPorDropCommand.Execute(
                new NovaTarefaDropPayload(args.FerramentaId, args.NomeFerramenta, args.EsteiraId, args.TempoAlvo));
        };

        container.TarefaPassouPeloPonteiro -= OnTarefaPassouPeloPonteiro;
        container.TarefaPassouPeloPonteiro += OnTarefaPassouPeloPonteiro;

        var nomeTexto = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = BrushTextoClaro,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        nomeTexto.Bind(TextBlock.TextProperty, new Binding(nameof(EsteiraViewModel.NomeSalvo)) { Source = esteira });

        var nomeVisual = new Border
        {
            Padding = new Thickness(2, 0),
            Background = Brushes.Transparent,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            Child = nomeTexto
        };

        var nomeBox = new TextBox
        {
            IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Watermark = "Nome da esteira",
            Background = BrushFundoInput,
            Foreground = BrushTextoClaro,
            BorderBrush = BrushBordaSutil,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 4),
            CornerRadius = new CornerRadius(6),
            MinWidth = 220,
            MaxWidth = 420
        };
        nomeBox.Bind(TextBox.TextProperty, new Binding(nameof(EsteiraViewModel.NomeEmEdicao)) { Source = esteira, Mode = BindingMode.TwoWay });

        void SairModoEdicao(bool restaurarNome)
        {
            if (restaurarNome)
            {
                nomeBox.Text = esteira.NomeSalvo;
                esteira.NomeEmEdicao = esteira.NomeSalvo;
            }

            nomeBox.IsVisible = false;
            nomeVisual.IsVisible = true;
        }

        void EntrarModoEdicao()
        {
            nomeBox.Text = esteira.NomeSalvo;
            esteira.NomeEmEdicao = esteira.NomeSalvo;
            nomeVisual.IsVisible = false;
            nomeBox.IsVisible = true;

            Dispatcher.UIThread.Post(() =>
            {
                nomeBox.Focus();
                nomeBox.SelectionStart = 0;
                nomeBox.SelectionEnd = nomeBox.Text?.Length ?? 0;
            }, DispatcherPriority.Input);
        }

        nomeVisual.PointerPressed += (_, e) =>
        {
            if (e.ClickCount != 2)
                return;

            EntrarModoEdicao();
            e.Handled = true;
        };

        nomeBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                SairModoEdicao(restaurarNome: true);
                e.Handled = true;
                return;
            }

            if (e.Key != Key.Enter)
                return;

            var nomeDigitado = (nomeBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(nomeDigitado))
            {
                SairModoEdicao(restaurarNome: true);
                e.Handled = true;
                return;
            }

            if (string.Equals(nomeDigitado, esteira.NomeSalvo, StringComparison.Ordinal))
            {
                SairModoEdicao(restaurarNome: true);
                e.Handled = true;
                return;
            }

            if (!esteira.NomePersonalizadoSalvo)
            {
                AplicarNomeEsteira(esteira, nomeDigitado);
                SairModoEdicao(restaurarNome: false);
                e.Handled = true;
                return;
            }

            MostrarConfirmacaoRenomear(nomeBox, esteira, nomeDigitado, confirmou =>
            {
                SairModoEdicao(restaurarNome: !confirmou);
            });
            e.Handled = true;
        };

        nomeBox.LostFocus += (_, _) =>
        {
            if (nomeBox.IsVisible)
                SairModoEdicao(restaurarNome: true);
        };

        var removerButton = new Button
        {
            Height = 28,
            MinWidth = 86,
            Padding = new Thickness(12, 0),
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            HorizontalAlignment = HorizontalAlignment.Right,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = "Remover"
        };
        removerButton.Classes.Add("btn-danger-soft");
        removerButton.Click += (_, _) =>
        {
            MostrarConfirmacaoRemover(removerButton, esteira.Id);
        };

        var zoomTitulo = new TextBlock
        {
            Text = "Zoom",
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#425B78"))
        };

        const double larguraZoom = 144;
        const double alturaZoom = 24;
        const double diametroThumbZoom = 16;
        const double alturaTrilhoZoom = 2;
        var larguraUtilZoom = larguraZoom - diametroThumbZoom;
        var paddingTrilhoZoom = diametroThumbZoom / 2.0;

        var zoomTrackHost = new Canvas
        {
            Width = larguraZoom,
            Height = alturaZoom,
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            ClipToBounds = false
        };
        zoomTrackHost.Classes.Add("zoom-control");

        var zoomTrilhoBase = new Border
        {
            Width = larguraUtilZoom,
            Height = alturaTrilhoZoom,
            Background = new SolidColorBrush(Color.Parse("#94A3B8")),
            CornerRadius = new CornerRadius(999)
        };
        Canvas.SetLeft(zoomTrilhoBase, paddingTrilhoZoom);
        Canvas.SetTop(zoomTrilhoBase, (alturaZoom - alturaTrilhoZoom) / 2.0);

        var zoomTrilhoAtivo = new Border
        {
            Width = 0,
            Height = alturaTrilhoZoom,
            Background = new SolidColorBrush(Color.Parse("#67D0DF")),
            CornerRadius = new CornerRadius(999)
        };
        Canvas.SetLeft(zoomTrilhoAtivo, paddingTrilhoZoom);
        Canvas.SetTop(zoomTrilhoAtivo, (alturaZoom - alturaTrilhoZoom) / 2.0);

        var zoomThumb = new Border
        {
            Width = diametroThumbZoom,
            Height = diametroThumbZoom,
            Background = new SolidColorBrush(Color.Parse("#5FC6D9")),
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(999)
        };
        Canvas.SetTop(zoomThumb, (alturaZoom - diametroThumbZoom) / 2.0);

        var zoomValor = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#2F4F77")),
            TextAlignment = TextAlignment.Center
        };

        void AtualizarEscalaZoomTexto()
        {
            zoomValor.Text = ObterEscalaZoomDoTempo(esteira.NivelZoom);
        }

        void AtualizarVisualZoom()
        {
            var valor = Math.Clamp(esteira.NivelZoom, 0.0, 1.0);
            var deslocamento = larguraUtilZoom * valor;
            zoomTrilhoAtivo.Width = deslocamento;
            Canvas.SetLeft(zoomThumb, deslocamento);
            AtualizarEscalaZoomTexto();
        }

        void AplicarZoomPorPosicao(double posicaoX)
        {
            var valor = Math.Clamp((posicaoX - paddingTrilhoZoom) / larguraUtilZoom, 0.0, 1.0);
            esteira.NivelZoom = valor;
            AtualizarVisualZoom();
        }

        var arrasteZoomAtivo = false;
        zoomTrackHost.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(zoomTrackHost).Properties.IsLeftButtonPressed)
                return;

            arrasteZoomAtivo = true;
            AplicarZoomPorPosicao(e.GetPosition(zoomTrackHost).X);
            e.Pointer.Capture(zoomTrackHost);
            e.Handled = true;
        };
        zoomTrackHost.PointerMoved += (_, e) =>
        {
            if (!arrasteZoomAtivo)
                return;

            AplicarZoomPorPosicao(e.GetPosition(zoomTrackHost).X);
            e.Handled = true;
        };
        zoomTrackHost.PointerReleased += (_, e) =>
        {
            if (!arrasteZoomAtivo)
                return;

            arrasteZoomAtivo = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        };
        zoomTrackHost.PointerCaptureLost += (_, _) => arrasteZoomAtivo = false;

        zoomTrackHost.Children.Add(zoomTrilhoBase);
        zoomTrackHost.Children.Add(zoomTrilhoAtivo);
        zoomTrackHost.Children.Add(zoomThumb);
        AtualizarEscalaZoomTexto();
        AtualizarVisualZoom();

        var escalaBadge = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#EAF4FF")),
            BorderBrush = new SolidColorBrush(Color.Parse("#C6D9F1")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 4),
            Child = zoomValor
        };

        var separadorAcoes = new Border
        {
            Width = 1,
            Height = 18,
            Background = BrushBotaoSecundarioBorda,
            VerticalAlignment = VerticalAlignment.Center
        };

        var zoomHost = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            ClipToBounds = false
        };
        zoomHost.Children.Add(zoomTitulo);
        zoomHost.Children.Add(zoomTrackHost);
        zoomHost.Children.Add(escalaBadge);
        zoomHost.Children.Add(separadorAcoes);
        zoomHost.Children.Add(removerButton);

        var barraAcoesTopo = new Border
        {
            Background = BrushFundoInput,
            BorderBrush = BrushBordaSutil,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 9),
            Margin = new Thickness(0, 0, 18, -1),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            ZIndex = 30,
            ClipToBounds = false,
            Child = zoomHost
        };

        var nomeHost = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            MinWidth = 140,
            MaxWidth = 420
        };
        nomeHost.Children.Add(nomeVisual);
        nomeHost.Children.Add(nomeBox);

        var cabecalhoAba = new Border
        {
            Background = BrushFundoInput,
            BorderBrush = BrushBordaSutil,
            BorderThickness = new Thickness(1, 1, 1, 0),
            CornerRadius = new CornerRadius(12, 12, 0, 0),
            Padding = new Thickness(18, 7, 18, 6),
            Margin = new Thickness(18, 0, 0, -1),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            ZIndex = 20,
            Child = nomeHost
        };

        var reguaHost = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0),
            ClipToBounds = true
        };
        reguaHost.Children.Add(regua);

        var tarefasHost = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0),
            ClipToBounds = true
        };
        tarefasHost.Children.Add(container);
        tarefasHost.Children.Add(new Border
        {
            Width = 2,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = new SolidColorBrush(Color.Parse("#2563EB")),
            IsHitTestVisible = false
        });

        var conteudo = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            RowSpacing = 0
        };
        var arrasteReguaViaFaixaAtivo = false;
        conteudo.PointerWheelChanged += (_, e) =>
        {
            if (e.Handled || OrigemPointerEmControleInterativo(e.Source))
                return;

            if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                var posicaoNaRegua = e.GetPosition(regua);
                regua.AplicarZoomNoCursor(posicaoNaRegua.X, e.Delta.Y);
                e.Handled = true;
                return;
            }

            regua.AplicarPanPorRoda(e.Delta.Y);
            e.Handled = true;
        };
        conteudo.PointerPressed += (_, e) =>
        {
            if (e.Handled || OrigemPointerBloqueiaArrasteRegua(e.Source))
                return;

            if (!e.GetCurrentPoint(conteudo).Properties.IsLeftButtonPressed)
                return;

            arrasteReguaViaFaixaAtivo = true;
            regua.IniciarArrasteManual(e.GetPosition(regua).X);
            e.Pointer.Capture(conteudo);
            e.Handled = true;
        };
        conteudo.PointerMoved += (_, e) =>
        {
            if (!arrasteReguaViaFaixaAtivo || e.Handled)
                return;

            regua.AtualizarArrasteManual(e.GetPosition(regua).X);
            e.Handled = true;
        };
        conteudo.PointerReleased += (_, e) =>
        {
            if (!arrasteReguaViaFaixaAtivo)
                return;

            arrasteReguaViaFaixaAtivo = false;
            regua.FinalizarArrasteManual();
            e.Pointer.Capture(null);
            e.Handled = true;
        };
        conteudo.PointerCaptureLost += (_, _) =>
        {
            if (!arrasteReguaViaFaixaAtivo)
                return;

            arrasteReguaViaFaixaAtivo = false;
            regua.FinalizarArrasteManual();
        };
        Grid.SetRow(reguaHost, 0);
        conteudo.Children.Add(reguaHost);
        Grid.SetRow(tarefasHost, 1);
        conteudo.Children.Add(tarefasHost);

        var corpoBorda = new Border
        {
            Background = BrushFundoPainel,
            CornerRadius = new CornerRadius(8),
            BorderBrush = BrushBordaSutil,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0),
            ClipToBounds = true,
            Child = conteudo
        };

        var faixaLayout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            RowSpacing = 0,
            ClipToBounds = false
        };
        var topoFaixa = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 10,
            ClipToBounds = false
        };
        Grid.SetColumn(cabecalhoAba, 0);
        topoFaixa.Children.Add(cabecalhoAba);
        Grid.SetColumn(barraAcoesTopo, 2);
        topoFaixa.Children.Add(barraAcoesTopo);

        Grid.SetRow(topoFaixa, 0);
        faixaLayout.Children.Add(topoFaixa);
        Grid.SetRow(corpoBorda, 1);
        faixaLayout.Children.Add(corpoBorda);

        var faixaRefs = new FaixaEsteiraRefs
        {
            Esteira = esteira,
            Regua = regua,
            Container = container,
            AtualizarVisualZoom = AtualizarVisualZoom
        };

        var faixaBorda = new Border
        {
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            ClipToBounds = false,
            Child = faixaLayout,
            Tag = faixaRefs
        };

        // Toda a faixa (cabecalho + regua + area de tarefas) vira alvo de drop.
        EsteiraTarefasControl.SetDropTargetEsteira(faixaBorda, container);
        EsteiraTarefasControl.SetDropTargetEsteira(faixaLayout, container);
        EsteiraTarefasControl.SetDropTargetEsteira(topoFaixa, container);
        EsteiraTarefasControl.SetDropTargetEsteira(corpoBorda, container);
        EsteiraTarefasControl.SetDropTargetEsteira(conteudo, container);
        EsteiraTarefasControl.SetDropTargetEsteira(reguaHost, container);
        EsteiraTarefasControl.SetDropTargetEsteira(regua, container);
        EsteiraTarefasControl.SetDropTargetEsteira(cabecalhoAba, container);
        EsteiraTarefasControl.SetDropTargetEsteira(barraAcoesTopo, container);
        EsteiraTarefasControl.SetDropTargetEsteira(nomeHost, container);
        EsteiraTarefasControl.SetDropTargetEsteira(nomeVisual, container);
        EsteiraTarefasControl.SetDropTargetEsteira(nomeTexto, container);
        EsteiraTarefasControl.SetDropTargetEsteira(nomeBox, container);
        EsteiraTarefasControl.SetDropTargetEsteira(tarefasHost, container);
        EsteiraTarefasControl.SetDropTargetEsteira(zoomHost, container);
        EsteiraTarefasControl.SetDropTargetEsteira(zoomTitulo, container);
        EsteiraTarefasControl.SetDropTargetEsteira(zoomValor, container);
        EsteiraTarefasControl.SetDropTargetEsteira(zoomTrackHost, container);
        EsteiraTarefasControl.SetDropTargetEsteira(zoomTrilhoBase, container);
        EsteiraTarefasControl.SetDropTargetEsteira(zoomTrilhoAtivo, container);
        EsteiraTarefasControl.SetDropTargetEsteira(zoomThumb, container);
        EsteiraTarefasControl.SetDropTargetEsteira(escalaBadge, container);
        EsteiraTarefasControl.SetDropTargetEsteira(separadorAcoes, container);
        EsteiraTarefasControl.SetDropTargetEsteira(removerButton, container);
        EsteiraTarefasControl.SetDropTargetEsteira(container, container);

        return faixaBorda;
    }

    private void ReconstruirBotoesEsteira(Border faixaBorda, EsteiraViewModel esteira)
    {
        if (faixaBorda.Tag is not FaixaEsteiraRefs refs)
            return;

        var container = refs.Container;
        container.Children.Clear();

        foreach (var tarefa in esteira.Tarefas)
        {
            var botao = new BotaoTarefaReguaControl
            {
                DataContext = tarefa
            };

            EsteiraTarefasControl.SetTempoTarefa(botao, tarefa.VencimentoUtc);
            botao.EstaNoPassado = tarefa.VencimentoUtc < (_vm?.HoraAtualRegua ?? DateTime.UtcNow);

            botao.TarefaClicada += (_, tarefaId) =>
            {
                _vm?.AbrirConfigTarefaCommand.Execute(tarefaId);
            };

            container.Children.Add(botao);
        }
    }

    private void OnTarefaPassouPeloPonteiro(object? sender, TarefaExecutadaEventArgs e)
    {
        if (_vm == null)
            return;

        DispatcherHelper.PostAsyncSafe(
            () => _vm.ExecutarTarefaAutomaticaAsync(e.TarefaId, e.TempoExecucao),
            "regua_tarefa_auto_falha",
            DispatcherPriority.Background);
    }

    private void AtualizarEsteiras()
    {
        foreach (var child in EsteirasContainer.Children.OfType<Border>())
        {
            if (child.Tag is not FaixaEsteiraRefs refs)
                continue;

            refs.Regua.NivelZoom = refs.Esteira.NivelZoom;
            refs.Regua.CentroTemporal = refs.Esteira.CentroTemporal;
            refs.Regua.HoraAtual = _vm?.HoraAtualRegua ?? DateTime.UtcNow;
            refs.Regua.InvalidateVisual();

            refs.Container.NivelZoom = refs.Esteira.NivelZoom;
            refs.Container.CentroTemporal = refs.Esteira.CentroTemporal;
            refs.Container.InvalidateArrange();
            refs.AtualizarVisualZoom();

            AtualizarOpacidadeContainer(refs.Container);
        }
    }

    private void AtualizarOpacidadeTarefas()
    {
        foreach (var child in EsteirasContainer.Children.OfType<Border>())
        {
            if (child.Tag is not FaixaEsteiraRefs refs)
                continue;

            AtualizarOpacidadeContainer(refs.Container);
            refs.Regua.HoraAtual = _vm?.HoraAtualRegua ?? DateTime.UtcNow;
            refs.Regua.InvalidateVisual();
        }
    }

    private void AtualizarOpacidadeContainer(EsteiraTarefasControl container)
    {
        var horaAtual = _vm?.HoraAtualRegua ?? DateTime.UtcNow;
        foreach (var botao in container.Children.OfType<BotaoTarefaReguaControl>())
        {
            var tempo = EsteiraTarefasControl.GetTempoTarefa(botao);
            botao.EstaNoPassado = tempo < horaAtual;
        }
    }

    private void AtualizarZoomLabel()
    {
        ZoomLevelTexto.Text = "Zoom do tempo: controle da esteira ou Ctrl + roda";
    }

    private static string ObterEscalaZoomDoTempo(double nivelZoom)
    {
        var conversor = new ConversorTempoPixel
        {
            NivelZoom = Math.Clamp(nivelZoom, 0.0, 1.0)
        };

        var intervalo = conversor.IntervaloTickAtual();
        if (intervalo.TotalDays >= 28)
            return "Meses";
        if (intervalo.TotalDays >= 1)
            return "Dias";
        if (intervalo.TotalHours >= 1)
            return "Horas";

        return "Minutos";
    }

    private static bool OrigemPointerEmControleInterativo(object? source)
    {
        var atual = source as Visual;
        while (atual is not null)
        {
            if (atual is Button || atual is TextBox)
                return true;

            if (atual is StyledElement elemento && elemento.Classes.Contains("zoom-control"))
                return true;

            atual = atual.GetVisualParent();
        }

        return false;
    }

    private static bool OrigemPointerBloqueiaArrasteRegua(object? source)
    {
        var atual = source as Visual;
        while (atual is not null)
        {
            if (atual is Button || atual is TextBox || atual is BotaoTarefaReguaControl)
                return true;

            if (atual is StyledElement elemento && elemento.Classes.Contains("zoom-control"))
                return true;

            atual = atual.GetVisualParent();
        }

        return false;
    }

    private static void AplicarNomeEsteira(EsteiraViewModel esteira, string nomeNovo)
    {
        esteira.Nome = nomeNovo;
        esteira.NomeSalvo = nomeNovo;
        esteira.NomeEmEdicao = nomeNovo;
        esteira.NomePersonalizadoSalvo = true;
    }

    private void MostrarConfirmacaoRemover(Button removerButton, int esteiraId)
    {
        if (_vm is null)
            return;

        _vm.SolicitarRemoverEsteiraCommand.Execute(esteiraId);
        if (!_vm.ConfirmacaoRemoverEsteiraAberta || _vm.ConfirmacaoRemoverEsteiraId != esteiraId)
            return;

        _confirmacaoRemoverFlyout?.Hide();

        var titulo = new TextBlock
        {
            Text = _vm.ConfirmacaoRemoverEsteiraTitulo,
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Foreground = BrushTextoClaro
        };

        var mensagem = new TextBlock
        {
            Text = _vm.ConfirmacaoRemoverEsteiraMensagem,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.Parse("#5B6B80")),
            MaxWidth = 320
        };

        var cancelar = new Button
        {
            Content = "Cancelar",
            Padding = new Thickness(12, 6),
            Background = BrushBotaoSecundarioFundo,
            Foreground = BrushBotaoSecundarioTexto,
            BorderBrush = BrushBotaoSecundarioBorda,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8)
        };

        var confirmar = new Button
        {
            Content = "Remover Esteira",
            Padding = new Thickness(12, 6),
            Background = new SolidColorBrush(Color.Parse("#DC2626")),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.Parse("#B91C1C")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8)
        };

        var acoes = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        acoes.Children.Add(cancelar);
        acoes.Children.Add(confirmar);

        var conteudo = new StackPanel
        {
            Spacing = 12,
            Width = 360
        };
        conteudo.Children.Add(titulo);
        conteudo.Children.Add(mensagem);
        conteudo.Children.Add(acoes);

        var flyout = new Flyout
        {
            Placement = PlacementMode.TopEdgeAlignedRight,
            ShowMode = FlyoutShowMode.Transient,
            Content = new Border
            {
                Background = BrushFlyoutFundo,
                BorderBrush = BrushFlyoutBorda,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14),
                Child = conteudo
            }
        };

        var finalizado = false;
        void Finalizar(bool confirmarRemocao)
        {
            if (finalizado)
                return;

            finalizado = true;
            if (confirmarRemocao)
                _vm.ConfirmarRemoverEsteiraCommand.Execute(null);
            else
                _vm.CancelarRemoverEsteiraCommand.Execute(null);
        }

        cancelar.Click += (_, _) =>
        {
            Finalizar(confirmarRemocao: false);
            flyout.Hide();
        };

        confirmar.Click += (_, _) =>
        {
            Finalizar(confirmarRemocao: true);
            flyout.Hide();
        };

        flyout.Closed += (_, _) =>
        {
            if (!finalizado)
                Finalizar(confirmarRemocao: false);

            if (ReferenceEquals(_confirmacaoRemoverFlyout, flyout))
                _confirmacaoRemoverFlyout = null;
        };

        _confirmacaoRemoverFlyout = flyout;
        flyout.ShowAt(removerButton);
    }

    private void MostrarConfirmacaoRenomear(TextBox nomeBox, EsteiraViewModel esteira, string nomeNovo, Action<bool> aoFinalizar)
    {
        var confirmar = new Button
        {
            Content = "Confirmar",
            Padding = new Thickness(10, 4),
            Background = BrushBotaoPrimarioFundo,
            Foreground = BrushBotaoPrimarioTexto,
            BorderBrush = BrushBotaoPrimarioBorda,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6)
        };

        var cancelar = new Button
        {
            Content = "Cancelar",
            Padding = new Thickness(10, 4),
            Background = BrushBotaoSecundarioFundo,
            Foreground = BrushBotaoSecundarioTexto,
            BorderBrush = BrushBotaoSecundarioBorda,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6)
        };

        var botoes = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        botoes.Children.Add(cancelar);
        botoes.Children.Add(confirmar);

        var conteudo = new StackPanel
        {
            Spacing = 8,
            Width = 320
        };
        conteudo.Children.Add(new TextBlock
        {
            Text = "Tem certeza que deseja mudar o nome da esteira de tarefas?",
            Foreground = BrushTextoClaro,
            TextWrapping = TextWrapping.Wrap
        });
        conteudo.Children.Add(botoes);

        var flyout = new Flyout
        {
            Placement = PlacementMode.Bottom,
            ShowMode = FlyoutShowMode.Transient,
            Content = new Border
            {
                Background = BrushFlyoutFundo,
                BorderBrush = BrushFlyoutBorda,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12),
                Child = conteudo
            }
        };

        var finalizado = false;
        void Finalizar(bool confirmado)
        {
            if (finalizado)
                return;

            finalizado = true;
            aoFinalizar(confirmado);
        }

        confirmar.Click += (_, _) =>
        {
            AplicarNomeEsteira(esteira, nomeNovo);
            Finalizar(confirmado: true);
            flyout.Hide();
        };

        cancelar.Click += (_, _) =>
        {
            nomeBox.Text = esteira.NomeSalvo;
            Finalizar(confirmado: false);
            flyout.Hide();
        };

        flyout.Closed += (_, _) =>
        {
            nomeBox.Text = esteira.NomeSalvo;
            Finalizar(confirmado: false);
        };

        flyout.ShowAt(nomeBox);
    }
}
