using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Protons.UI.Painel.Views.PainelPrincipal.Controles;

/// <summary>
/// Controle custom Avalonia que renderiza a regua do tempo com zoom, pan e marcador de hora atual.
/// </summary>
public class ReguaTempoControl : Control
{
    private const double ZoomStep = 0.04;
    private const double DragThreshold = 5.0;
    private const double AlturaRegua = 60.0;

    private static readonly Color CorFundo = Color.Parse("#DDEBFF");
    private static readonly Color CorTick = Color.Parse("#9EB8DA");
    private static readonly Color CorLabel = Color.Parse("#2F4F77");
    private static readonly Color CorMarcadorAzul = Color.Parse("#2563EB");
    private static readonly Color CorTickMenor = Color.Parse("#BCD0E8");

    private readonly ConversorTempoPixel _conversor = new();
    private bool _arrastando;
    private Point _pontoInicioArraste;
    private DateTime _centroTemporalInicioArraste;
    private bool _arrasteConfirmado;

    /// <summary>
    /// Disparado quando o usuario ativa o modo manual (arrasta a regua).
    /// </summary>
    public event EventHandler? ModoManualAtivado;

    /// <summary>
    /// Disparado quando o usuario retorna ao modo automatico (clique rapido ou botao Agora).
    /// </summary>
    public event EventHandler? ModoAutomaticoAtivado;

    public static readonly StyledProperty<double> NivelZoomProperty =
        AvaloniaProperty.Register<ReguaTempoControl, double>(nameof(NivelZoom), 0.5,
            coerce: (_, v) => Math.Clamp(v, 0.0, 1.0));

    public static readonly StyledProperty<DateTime> CentroTemporalProperty =
        AvaloniaProperty.Register<ReguaTempoControl, DateTime>(nameof(CentroTemporal), DateTime.UtcNow);

    public static readonly StyledProperty<DateTime> HoraAtualProperty =
        AvaloniaProperty.Register<ReguaTempoControl, DateTime>(nameof(HoraAtual), DateTime.UtcNow);

    public double NivelZoom
    {
        get => GetValue(NivelZoomProperty);
        set => SetValue(NivelZoomProperty, value);
    }

    public DateTime CentroTemporal
    {
        get => GetValue(CentroTemporalProperty);
        set => SetValue(CentroTemporalProperty, value);
    }

    public DateTime HoraAtual
    {
        get => GetValue(HoraAtualProperty);
        set => SetValue(HoraAtualProperty, value);
    }

    static ReguaTempoControl()
    {
        AffectsRender<ReguaTempoControl>(NivelZoomProperty, CentroTemporalProperty, HoraAtualProperty);
    }

    public ReguaTempoControl()
    {
        ClipToBounds = true;
        Height = AlturaRegua;
    }

    public ConversorTempoPixel Conversor => _conversor;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == NivelZoomProperty)
            _conversor.NivelZoom = NivelZoom;
        else if (change.Property == CentroTemporalProperty)
            _conversor.CentroTemporal = CentroTemporal;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(availableSize.Width, AlturaRegua);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = Bounds;
        _conversor.LarguraViewport = bounds.Width;
        _conversor.NivelZoom = NivelZoom;
        _conversor.CentroTemporal = CentroTemporal;

        // Fundo
        context.FillRectangle(new SolidColorBrush(CorFundo), new Rect(0, 0, bounds.Width, bounds.Height));

        // Tick marks
        DesenharTicks(context, bounds);

        // Marcador azul (hora atual)
        DesenharMarcadorHoraAtual(context, bounds);
    }

    private void DesenharTicks(DrawingContext context, Rect bounds)
    {
        var intervalo = _conversor.IntervaloTickAtual();
        var inicio = _conversor.ViewportInicio.AddTicks(-intervalo.Ticks);
        var fim = _conversor.ViewportFim.AddTicks(intervalo.Ticks);
        var tickAlinhado = _conversor.PrimeiroTickAlinhado(inicio, intervalo);

        var penTick = new Pen(new SolidColorBrush(CorTick), 1);
        var penTickMenor = new Pen(new SolidColorBrush(CorTickMenor), 1);
        var brushLabel = new SolidColorBrush(CorLabel);
        const double yLabel = 4.0;
        const double yTickInicio = 13.0;
        const double yTickFim = 25.0;
        const double ySubTickInicio = 19.0;
        const double ySubTickFim = 25.0;

        var atual = tickAlinhado;
        double ultimoLabelX = double.NegativeInfinity;

        while (atual <= fim)
        {
            var x = _conversor.TempoParaPixel(atual);

            if (x >= -50 && x <= bounds.Width + 50)
            {
                // Tick principal
                context.DrawLine(penTick, new Point(x, yTickInicio), new Point(x, yTickFim));

                // Label
                var texto = _conversor.FormatarTick(atual, intervalo);
                var formattedText = new FormattedText(
                    texto,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold),
                    12,
                    brushLabel);

                var labelX = x - formattedText.Width / 2;

                // Evita sobreposicao de labels
                if (labelX > ultimoLabelX + 8)
                {
                    context.DrawText(formattedText, new Point(labelX, yLabel));
                    ultimoLabelX = labelX + formattedText.Width;
                }

                // Sub-ticks (metade do intervalo)
                var metade = atual.AddTicks(intervalo.Ticks / 2);
                if (metade <= fim)
                {
                    var xMeio = _conversor.TempoParaPixel(metade);
                    if (xMeio >= 0 && xMeio <= bounds.Width)
                    {
                        context.DrawLine(penTickMenor, new Point(xMeio, ySubTickInicio), new Point(xMeio, ySubTickFim));
                    }
                }
            }

            atual = atual.Add(intervalo);
        }
    }

    private void DesenharMarcadorHoraAtual(DrawingContext context, Rect bounds)
    {
        // MUDANCA CRITICA: Ponteiro SEMPRE no centro (posicao fixa)
        var xAtual = bounds.Width / 2.0;

        var pen = new Pen(new SolidColorBrush(CorMarcadorAzul), 2);
        context.DrawLine(pen, new Point(xAtual, 0), new Point(xAtual, bounds.Height));

        // Triangulo no topo
        var triangulo = new PathGeometry();
        var figure = new PathFigure { StartPoint = new Point(xAtual - 5, 0), IsClosed = true, IsFilled = true };
        figure.Segments!.Add(new LineSegment { Point = new Point(xAtual + 5, 0) });
        figure.Segments.Add(new LineSegment { Point = new Point(xAtual, 6) });
        triangulo.Figures!.Add(figure);

        context.DrawGeometry(new SolidColorBrush(CorMarcadorAzul), null, triangulo);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            var posicaoCursor = e.GetPosition(this);
            AplicarZoomNoCursor(posicaoCursor.X, e.Delta.Y);
            e.Handled = true;
            return;
        }

        AplicarPanPorRoda(e.Delta.Y);
        e.Handled = true;
    }

    /// <summary>
    /// Aplica deslocamento horizontal (pan) proporcional à roda do mouse.
    /// </summary>
    public void AplicarPanPorRoda(double deltaRodaY)
    {
        _conversor.LarguraViewport = Bounds.Width;
        _conversor.NivelZoom = NivelZoom;
        _conversor.CentroTemporal = CentroTemporal;

        var pixelsPorStep = Bounds.Width * 0.08;
        var deltaSeg = (pixelsPorStep * -deltaRodaY) / _conversor.PixelsPorSegundo;
        CentroTemporal = CentroTemporal.AddSeconds(deltaSeg);
        InvalidateVisual();
    }

    /// <summary>
    /// Aplica zoom temporal no ponto X informado (em coordenada local do controle).
    /// </summary>
    public void AplicarZoomNoCursor(double xCursorLocal, double deltaRodaY)
    {
        _conversor.LarguraViewport = Bounds.Width;
        _conversor.NivelZoom = NivelZoom;
        _conversor.CentroTemporal = CentroTemporal;

        var tempoSobCursor = _conversor.PixelParaTempo(xCursorLocal);

        // Zoom centrado no cursor.
        var novoZoom = Math.Clamp(NivelZoom + deltaRodaY * ZoomStep, 0.0, 1.0);
        NivelZoom = novoZoom;
        _conversor.NivelZoom = novoZoom;

        // Recalcular centro para manter o tempo sob cursor fixo.
        var pixelAtualDoCursor = _conversor.TempoParaPixel(tempoSobCursor);
        var deltaPixel = xCursorLocal - pixelAtualDoCursor;
        var deltaSeg = deltaPixel / _conversor.PixelsPorSegundo;
        CentroTemporal = CentroTemporal.AddSeconds(-deltaSeg);

        InvalidateVisual();
    }

    /// <summary>
    /// Inicia arraste manual de pan temporal usando uma coordenada local em X.
    /// </summary>
    public void IniciarArrasteManual(double xCursorLocal)
    {
        _conversor.LarguraViewport = Bounds.Width;
        _conversor.NivelZoom = NivelZoom;
        _conversor.CentroTemporal = CentroTemporal;

        _conversor.ModoAutomatico = false;
        ModoManualAtivado?.Invoke(this, EventArgs.Empty);

        _arrastando = true;
        _arrasteConfirmado = false;
        _pontoInicioArraste = new Point(xCursorLocal, 0);
        _centroTemporalInicioArraste = CentroTemporal;
    }

    /// <summary>
    /// Atualiza arraste manual de pan temporal usando uma coordenada local em X.
    /// </summary>
    public void AtualizarArrasteManual(double xCursorLocal)
    {
        if (!_arrastando)
            return;

        _conversor.LarguraViewport = Bounds.Width;
        _conversor.NivelZoom = NivelZoom;

        var deltaX = xCursorLocal - _pontoInicioArraste.X;

        if (!_arrasteConfirmado && Math.Abs(deltaX) < DragThreshold)
            return;

        _arrasteConfirmado = true;
        var deltaSeg = deltaX / _conversor.PixelsPorSegundo;
        CentroTemporal = _centroTemporalInicioArraste.AddSeconds(-deltaSeg);
        InvalidateVisual();
    }

    /// <summary>
    /// Finaliza arraste manual e retorna ao modo automático quando foi apenas clique.
    /// </summary>
    public void FinalizarArrasteManual()
    {
        if (!_arrastando)
            return;

        _arrastando = false;

        if (!_arrasteConfirmado)
        {
            _conversor.ModoAutomatico = true;
            ModoAutomaticoAtivado?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            IniciarArrasteManual(e.GetPosition(this).X);
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_arrastando)
        {
            AtualizarArrasteManual(e.GetPosition(this).X);
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_arrastando)
        {
            FinalizarArrasteManual();
            e.Pointer.Capture(null);

            e.Handled = true;
        }
    }

    /// <summary>
    /// Retorna ao modo automatico, sincronizando com o tempo real.
    /// </summary>
    public void RetornarModoAutomatico()
    {
        _conversor.ModoAutomatico = true;
        CentroTemporal = HoraAtual;
        ModoAutomaticoAtivado?.Invoke(this, EventArgs.Empty);
    }
}
