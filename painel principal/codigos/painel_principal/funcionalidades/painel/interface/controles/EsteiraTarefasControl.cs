using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Protons.UI.Painel.ViewModels;

namespace Protons.UI.Painel.Views.PainelPrincipal.Controles;

/// <summary>
/// Painel custom que posiciona filhos (botoes de tarefa) horizontalmente
/// na coordenada X correspondente ao tempo de cada tarefa.
/// Aceita drop de ferramentas da sidebar para criar novas tarefas.
/// </summary>
public class EsteiraTarefasControl : Panel
{
    private const double AlturaEsteira = 76.0;
    private const double LarguraBotao = 212.0;
    private const double AlturaBotao = 46.0;
    private const double EspacamentoColisao = 14.0;
    private const double MargemCulling = 240.0;
    private static readonly IBrush FillDropAtivo = new SolidColorBrush(Color.Parse("#DDEBFF"));
    private static readonly IBrush FillDropInativo = new SolidColorBrush(Color.Parse("#DDEBFF"));

    private readonly ConversorTempoPixel _conversor = new();
    private readonly Dictionary<Control, double> _centrosTemporaisAnteriores = new();
    private bool _dragSobreEsteira;

    /// <summary>
    /// Disparado quando uma tarefa cruza o ponteiro fixo (da direita para esquerda).
    /// </summary>
    public event EventHandler<TarefaExecutadaEventArgs>? TarefaPassouPeloPonteiro;

    public static readonly StyledProperty<double> NivelZoomProperty =
        AvaloniaProperty.Register<EsteiraTarefasControl, double>(nameof(NivelZoom), 0.5);

    public static readonly StyledProperty<DateTime> CentroTemporalProperty =
        AvaloniaProperty.Register<EsteiraTarefasControl, DateTime>(nameof(CentroTemporal), DateTime.UtcNow);

    public static readonly StyledProperty<int> EsteiraIdProperty =
        AvaloniaProperty.Register<EsteiraTarefasControl, int>(nameof(EsteiraId), 0);

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

    public int EsteiraId
    {
        get => GetValue(EsteiraIdProperty);
        set => SetValue(EsteiraIdProperty, value);
    }

    public static readonly AttachedProperty<DateTime> TempoTarefaProperty =
        AvaloniaProperty.RegisterAttached<EsteiraTarefasControl, Control, DateTime>("TempoTarefa");

    // Mapeia qualquer visual da faixa para a esteira real de drop.
    public static readonly AttachedProperty<EsteiraTarefasControl?> DropTargetEsteiraProperty =
        AvaloniaProperty.RegisterAttached<EsteiraTarefasControl, AvaloniaObject, EsteiraTarefasControl?>("DropTargetEsteira");

    public static DateTime GetTempoTarefa(Control element) => element.GetValue(TempoTarefaProperty);
    public static void SetTempoTarefa(Control element, DateTime value) => element.SetValue(TempoTarefaProperty, value);
    public static EsteiraTarefasControl? GetDropTargetEsteira(AvaloniaObject element) => element.GetValue(DropTargetEsteiraProperty);
    public static void SetDropTargetEsteira(AvaloniaObject element, EsteiraTarefasControl? value) => element.SetValue(DropTargetEsteiraProperty, value);

    static EsteiraTarefasControl()
    {
        AffectsArrange<EsteiraTarefasControl>(NivelZoomProperty, CentroTemporalProperty);
    }

    public EsteiraTarefasControl()
    {
        Height = AlturaEsteira;
        ClipToBounds = true;
        Background = FillDropInativo;

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    public ConversorTempoPixel Conversor => _conversor;

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
        {
            child.Measure(new Size(LarguraBotao, AlturaBotao));
        }

        return new Size(availableSize.Width, AlturaEsteira);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _conversor.NivelZoom = NivelZoom;
        _conversor.CentroTemporal = CentroTemporal;
        _conversor.LarguraViewport = finalSize.Width;

        var posPonteiro = finalSize.Width / 2.0;
        var deslocamentosPorColisao = CalcularDeslocamentosColisaoPorMinuto();

        foreach (var child in Children)
        {
            var tempo = GetTempoTarefa(child);
            var centroTemporal = _conversor.TempoParaPixel(tempo);
            var deslocamentoX = deslocamentosPorColisao.TryGetValue(child, out var deslocamento) ? deslocamento : 0.0;
            var x = centroTemporal - LarguraBotao / 2 + deslocamentoX;
            var y = (AlturaEsteira - AlturaBotao) / 2;

            // Detectar cruzamento do ponteiro
            if (_centrosTemporaisAnteriores.TryGetValue(child, out var centroAnterior))
            {
                // Cruzou da direita para esquerda?
                if (centroAnterior > posPonteiro && centroTemporal <= posPonteiro)
                {
                    if (child.DataContext is TarefaReguaItem item)
                    {
                        TarefaPassouPeloPonteiro?.Invoke(this,
                            new TarefaExecutadaEventArgs(item.TarefaId, tempo));
                    }
                }
            }

            _centrosTemporaisAnteriores[child] = centroTemporal;

            // Viewport culling
            if (x + LarguraBotao < -MargemCulling || x > finalSize.Width + MargemCulling)
            {
                child.IsVisible = false;
                child.Arrange(new Rect(-9999, 0, LarguraBotao, AlturaBotao));
            }
            else
            {
                child.IsVisible = true;
                child.Arrange(new Rect(x, y, LarguraBotao, AlturaBotao));
            }
        }

        return finalSize;
    }

    private Dictionary<Control, double> CalcularDeslocamentosColisaoPorMinuto()
    {
        var entradas = new List<ColisaoEntrada>(Children.Count);
        for (var indice = 0; indice < Children.Count; indice++)
        {
            var child = Children[indice];
            entradas.Add(new ColisaoEntrada(
                indice,
                GetTempoTarefa(child),
                ObterCriadoEmOrdenacao(child),
                ObterTarefaIdOrdenacao(child)));
        }

        var deslocamentosPorIndice = CalcularOffsetsColisaoPorMinuto(
            entradas,
            LarguraBotao,
            EspacamentoColisao);

        var deslocamentos = new Dictionary<Control, double>(Children.Count);
        for (var indice = 0; indice < Children.Count; indice++)
        {
            var child = Children[indice];
            deslocamentos[child] = deslocamentosPorIndice.TryGetValue(indice, out var valor) ? valor : 0.0;
        }

        return deslocamentos;
    }

    internal static IReadOnlyDictionary<int, double> CalcularOffsetsColisaoPorMinuto(
        IReadOnlyList<ColisaoEntrada> entradas,
        double larguraCard,
        double espacamento)
    {
        var gruposPorMinuto = new Dictionary<long, List<ColisaoEntrada>>();
        foreach (var entrada in entradas)
        {
            var chave = ObterChaveMinutoUtc(entrada.VencimentoUtc);
            if (!gruposPorMinuto.TryGetValue(chave, out var lista))
            {
                lista = [];
                gruposPorMinuto[chave] = lista;
            }

            lista.Add(entrada);
        }

        var deslocamentos = new Dictionary<int, double>(entradas.Count);
        foreach (var grupo in gruposPorMinuto.Values)
        {
            if (grupo.Count <= 1)
            {
                deslocamentos[grupo[0].IndiceOriginal] = 0.0;
                continue;
            }

            // Regra: mais antiga à direita; demais seguem para a esquerda.
            var ordenadas = grupo
                .OrderBy(x => x.CriadoEmUtcOrdenacao)
                .ThenBy(x => x.TarefaId)
                .ToList();

            for (var indice = 0; indice < ordenadas.Count; indice++)
            {
                var deslocamento = -indice * (larguraCard + espacamento);
                deslocamentos[ordenadas[indice].IndiceOriginal] = deslocamento;
            }
        }

        return deslocamentos;
    }

    internal static Size ObterTamanhoCardNoZoom(double nivelZoom)
    {
        _ = nivelZoom; // zoom temporal não altera dimensão do card.
        return new Size(LarguraBotao, AlturaBotao);
    }

    private static DateTime ObterCriadoEmOrdenacao(Control control)
    {
        if (control.DataContext is TarefaReguaItem item)
            return item.CriadoEmUtcOrdenacao;

        return GetTempoTarefa(control);
    }

    private static int ObterTarefaIdOrdenacao(Control control)
    {
        return control.DataContext is TarefaReguaItem item ? item.TarefaId : 0;
    }

    private static long ObterChaveMinutoUtc(DateTime valor)
    {
        var utc = valor.Kind switch
        {
            DateTimeKind.Utc => valor,
            DateTimeKind.Local => valor.ToUniversalTime(),
            _ => DateTime.SpecifyKind(valor, DateTimeKind.Utc)
        };

        return utc.Ticks / TimeSpan.TicksPerMinute;
    }

    internal readonly record struct ColisaoEntrada(
        int IndiceOriginal,
        DateTime VencimentoUtc,
        DateTime CriadoEmUtcOrdenacao,
        int TarefaId);

    #pragma warning disable CS0618 // Legado nativo de DragDrop mantido como fallback
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var podeDropar = e.Data.Contains("FerramentaTarefaLateral");
        e.DragEffects = podeDropar ? DragDropEffects.Copy : DragDropEffects.None;
        DefinirDropHover(podeDropar);
    }

    private void OnDragLeave(object? sender, RoutedEventArgs e)
    {
        DefinirDropHover(false);
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        DefinirDropHover(false);

        if (!e.Data.Contains("FerramentaTarefaLateral"))
            return;

        var nomeFerramenta = e.Data.Get("FerramentaTarefaLateral") as string ?? "Ferramenta";
        ProcessarDropFerramenta("legacy_tool", nomeFerramenta, e.GetPosition(this));
    }
    #pragma warning restore CS0618

    public void DefinirDropHover(bool ativo)
    {
        if (_dragSobreEsteira == ativo)
            return;

        _dragSobreEsteira = ativo;
        Background = _dragSobreEsteira ? FillDropAtivo : FillDropInativo;
    }

    public void ProcessarDropFerramenta(string ferramentaId, string nomeFerramenta, Point posicaoLocal)
    {
        _conversor.NivelZoom = NivelZoom;
        _conversor.CentroTemporal = CentroTemporal;
        _conversor.LarguraViewport = Bounds.Width;

        var tempoAlvo = _conversor.PixelParaTempo(posicaoLocal.X);
        FerramentaSoltaNaEsteira?.Invoke(this, new FerramentaDropEventArgs(ferramentaId, nomeFerramenta, tempoAlvo, EsteiraId));
    }

    public event EventHandler<FerramentaDropEventArgs>? FerramentaSoltaNaEsteira;
}

public sealed class FerramentaDropEventArgs : EventArgs
{
    public string FerramentaId { get; }
    public string NomeFerramenta { get; }
    public DateTime TempoAlvo { get; }
    public int EsteiraId { get; }

    public FerramentaDropEventArgs(string ferramentaId, string nomeFerramenta, DateTime tempoAlvo, int esteiraId)
    {
        FerramentaId = ferramentaId;
        NomeFerramenta = nomeFerramenta;
        TempoAlvo = tempoAlvo;
        EsteiraId = esteiraId;
    }
}

/// <summary>
/// EventArgs para execucao automatica de tarefa.
/// </summary>
public sealed class TarefaExecutadaEventArgs : EventArgs
{
    public int TarefaId { get; }
    public DateTime TempoExecucao { get; }

    public TarefaExecutadaEventArgs(int tarefaId, DateTime tempoExecucao)
    {
        TarefaId = tarefaId;
        TempoExecucao = tempoExecucao;
    }
}
