using System;

namespace Protons.UI.Painel.Views.PainelPrincipal.Controles;

/// <summary>
/// Converte entre coordenadas de tempo (DateTime) e coordenadas de pixel na regua do tempo.
/// Zoom 0.0 = ~1 ano visivel, Zoom 1.0 = segundos visiveis.
/// </summary>
public sealed class ConversorTempoPixel
{
    private const double MinPxPorSegundo = 0.000032; // ~1 ano em ~1000px
    private const double MaxPxPorSegundo = 50.0;     // ~20s em 1000px
    private const double EspacamentoMinTick = 80.0;
    private const double EspacamentoMaxTick = 200.0;

    private static readonly TimeSpan[] IntervalosTickDisponiveis =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(3),
        TimeSpan.FromHours(6),
        TimeSpan.FromHours(12),
        TimeSpan.FromDays(1),
        TimeSpan.FromDays(7),
        TimeSpan.FromDays(30),
        TimeSpan.FromDays(90),
        TimeSpan.FromDays(365)
    ];

    private double _nivelZoom = 0.5;

    public double NivelZoom
    {
        get => _nivelZoom;
        set => _nivelZoom = Math.Clamp(value, 0.0, 1.0);
    }

    public DateTime CentroTemporal { get; set; } = DateTime.UtcNow;

    public double LarguraViewport { get; set; } = 1000.0;

    /// <summary>
    /// Indica se a esteira esta em modo automatico (tempo real).
    /// Quando true, CentroTemporal avanca automaticamente.
    /// </summary>
    public bool ModoAutomatico { get; set; } = true;

    /// <summary>
    /// Posicao fixa do ponteiro azul (sempre no centro do viewport).
    /// </summary>
    public double PosicaoPonteiroFixo => LarguraViewport / 2.0;

    public double PixelsPorSegundo => MinPxPorSegundo * Math.Pow(MaxPxPorSegundo / MinPxPorSegundo, _nivelZoom);

    public double TempoParaPixel(DateTime tempo)
    {
        var deltaSeg = (tempo - CentroTemporal).TotalSeconds;
        return (LarguraViewport / 2.0) + (deltaSeg * PixelsPorSegundo);
    }

    public DateTime PixelParaTempo(double x)
    {
        var deltaSeg = (x - LarguraViewport / 2.0) / PixelsPorSegundo;
        return CentroTemporal.AddSeconds(deltaSeg);
    }

    public DateTime ViewportInicio => PixelParaTempo(0);
    public DateTime ViewportFim => PixelParaTempo(LarguraViewport);

    public TimeSpan IntervaloTickAtual()
    {
        var pxPorSeg = PixelsPorSegundo;

        foreach (var intervalo in IntervalosTickDisponiveis)
        {
            var espacamento = intervalo.TotalSeconds * pxPorSeg;
            if (espacamento >= EspacamentoMinTick && espacamento <= EspacamentoMaxTick)
                return intervalo;
        }

        // Se nenhum intervalo cabe no range ideal, pega o mais proximo de EspacamentoMinTick
        TimeSpan melhor = IntervalosTickDisponiveis[0];
        double melhorDist = double.MaxValue;

        foreach (var intervalo in IntervalosTickDisponiveis)
        {
            var espacamento = intervalo.TotalSeconds * pxPorSeg;
            var dist = Math.Abs(espacamento - EspacamentoMinTick);
            if (espacamento >= EspacamentoMinTick * 0.5 && dist < melhorDist)
            {
                melhor = intervalo;
                melhorDist = dist;
            }
        }

        return melhor;
    }

    public string FormatarTick(DateTime tick, TimeSpan intervalo)
    {
        if (intervalo.TotalDays >= 365)
            return tick.ToLocalTime().ToString("yyyy");
        if (intervalo.TotalDays >= 28)
            return tick.ToLocalTime().ToString("MMM yyyy");
        if (intervalo.TotalDays >= 1)
            return tick.ToLocalTime().ToString("dd/MM");
        if (intervalo.TotalHours >= 1)
            return tick.ToLocalTime().ToString("HH:mm");

        return tick.ToLocalTime().ToString("HH:mm:ss");
    }

    public DateTime PrimeiroTickAlinhado(DateTime inicio, TimeSpan intervalo)
    {
        var ticks = inicio.Ticks;
        var intervaloTicks = intervalo.Ticks;

        if (intervaloTicks <= 0)
            return inicio;

        var alinhado = ticks - (ticks % intervaloTicks);
        var resultado = new DateTime(alinhado, inicio.Kind);

        if (resultado < inicio)
            resultado = resultado.Add(intervalo);

        return resultado;
    }

    /// <summary>
    /// Verifica se um tempo esta no passado em relacao ao CentroTemporal.
    /// </summary>
    public bool EstaNoPassado(DateTime tempo)
    {
        return tempo < CentroTemporal;
    }

    /// <summary>
    /// Calcula quantos segundos faltam ate o tempo atingir o ponteiro fixo.
    /// Valores negativos indicam que ja passou pelo ponteiro.
    /// </summary>
    public double SegundosAtePonteiro(DateTime tempo)
    {
        return (tempo - CentroTemporal).TotalSeconds;
    }
}
