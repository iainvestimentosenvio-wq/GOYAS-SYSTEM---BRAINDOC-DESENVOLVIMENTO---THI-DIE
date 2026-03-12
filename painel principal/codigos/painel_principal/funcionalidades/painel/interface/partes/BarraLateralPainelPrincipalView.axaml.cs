using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Protons.UI.Painel.ViewModels;

namespace Protons.UI.Painel.Views.PainelPrincipal.Partes;

/// <summary>
/// Sidebar do painel com lista de ferramentas arrastáveis.
///
/// ═══════════════════════════════════════════════════════════════════════════
/// SISTEMA DE DRAG (CAMADA 1: ORIGEM DO ARRASTE)
/// ═══════════════════════════════════════════════════════════════════════════
///
/// FLUXO DE EVENTOS:
///   1. PointerPressed → OnFerramentaItemPointerPressed()
///      - Captura ponteiro: e.Pointer.Capture(this)
///      - Armazena ponto inicial, visual origem, ferramenta
///      - _aguardandoInicioDrag = true
///
///   2. PointerMoved → OnPointerMovedGlobal()
///      - Calcula delta desde ponto inicial
///      - Se delta < 6px: aguarda (tolerancia a tremor de mao)
///      - Se delta >= 6px: _dragAtivo = true
///      - Dispara: FerramentaDragStarted (com VisualOrigem)
///
///   3. PointerReleased → OnPointerReleasedGlobal()
///      - Libera captura de ponteiro
///      - Dispara: FerramentaDragEnded
///
///   4. PointerCaptureLost → OnPointerCaptureLostGlobal()
///      - Sistema tirou captura (janela inativa, etc)
///      - Dispara: FerramentaDragCanceled
///
/// COORDENACAO COM OUTRAS CAMADAS:
///   - PainelView.axaml.cs (CAMADA 2): Recebe eventos, coordena ciclo completo
///   - EsteiraTarefasControl.cs (CAMADA 3): Recebe drop, cria tarefa
///
/// ═══════════════════════════════════════════════════════════════════════════
/// ⚠️  REGRA CRITICA 1: Background="Transparent" NO GRID DO CARD  ⚠️
/// ═══════════════════════════════════════════════════════════════════════════
///
/// LOCALIZACAO: BarraLateralPainelPrincipalView.axaml, linha ~156
///
/// O Grid de cada ferramenta no AXAML DEVE ter Background="Transparent".
///
/// POR QUE?
///   - Avalonia so dispara eventos de ponteiro em elementos com Background nao-nulo
///   - Sem Background="Transparent", areas vazias do Grid sao "invisiveis" ao mouse
///   - Clique so funciona ao acertar EXATAMENTE Image/TextBlock
///   - Clicar em margens/cantos = NADA acontece
///
/// NAO REMOVER Background="Transparent" NUNCA!
///
/// TESTE PRATICO:
///   1. Remova Background="Transparent" do Grid no AXAML
///   2. Tente clicar nas bordas/cantos do botao de ferramenta
///   3. Resultado: drag NAO inicia (areas vazias nao capturam evento)
///
/// ═══════════════════════════════════════════════════════════════════════════
/// ⚠️  REGRA CRITICA 2: VisualOrigem (RenderTargetBitmap)  ⚠️
/// ═══════════════════════════════════════════════════════════════════════════
///
/// VisualOrigem = Referencia ao Grid do card clicado
///
/// POR QUE PRECISAMOS DELE?
///   - PainelView usa RenderTargetBitmap para capturar screenshot do card
///   - Screenshot vira imagem ghost que segue o cursor
///   - Ferramentas sem PNG proprio (ImagemGhostUri) usam screenshot
///   - Garante que ghost sempre corresponde ao visual do botao
///
/// FLUXO:
///   1. Usuario clica no card "Ferramenta X"
///   2. OnFerramentaItemPointerPressed() captura sender (Grid do card)
///   3. Armazena em _visualOrigem
///   4. FerramentaDragStarted passa _visualOrigem para PainelView
///   5. PainelView.CarregarGhostImagem() chama RenderTargetBitmap(_visualOrigem)
///   6. Ghost exibe visual exato do botao clicado
///
/// NAO remover _visualOrigem ou VisualOrigem do evento!
///
/// ═══════════════════════════════════════════════════════════════════════════
/// DOCUMENTACAO COMPLETA: documentacao/painel/drag_drop/PAINEL_DRAG_DROP.md
/// ═══════════════════════════════════════════════════════════════════════════
/// </summary>
public partial class BarraLateralPainelPrincipalView : UserControl
{
    /// <summary>
    /// Deslocamento minimo em pixels para considerar inicio de drag.
    ///
    /// POR QUE 6 PIXELS?
    ///   - Evita que cliques normais sejam interpretados como drag
    ///   - Tremores naturais da mao ao clicar movem cursor 2-3 pixels
    ///   - 6px e suficiente para distinguir clique de drag intencional
    ///   - Muito baixo (2px): cliques viram drags acidentalmente
    ///   - Muito alto (15px): precisa arrastar muito para iniciar drag
    ///
    /// NAO alterar sem testar extensivamente com usuarios reais.
    /// </summary>
    private const double DistanciaMinimaDrag = 6;

    private bool _aguardandoInicioDrag;      // True apos PointerPressed, antes de confirmar drag
    private bool _dragAtivo;                 // True apos deslocamento > 6px
    private Point _pontoInicioTopLevel;      // Ponto inicial do clique (coordenadas TopLevel)
    private Point _ultimaPosicaoTopLevel;    // Ultima posicao conhecida do cursor
    private bool _temUltimaPosicaoTopLevel;  // Guard contra uso de posicao nao-inicializada
    private FerramentaTarefaLateralItem? _ferramentaEmDrag;  // Ferramenta sendo arrastada
    private Control? _visualOrigem;          // Grid do card clicado (para RenderTargetBitmap)
    private IPointer? _ponteiroCapturado;    // Referencia ao pointer capturado

    public event EventHandler<FerramentaDragSidebarEventArgs>? FerramentaDragStarted;
    public event EventHandler<FerramentaDragSidebarEventArgs>? FerramentaDragMoved;
    public event EventHandler<FerramentaDragSidebarEventArgs>? FerramentaDragEnded;
    public event EventHandler<FerramentaDragSidebarEventArgs>? FerramentaDragCanceled;

    public BarraLateralPainelPrincipalView()
    {
        InitializeComponent();

        AddHandler(PointerMovedEvent, OnPointerMovedGlobal, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnPointerReleasedGlobal, RoutingStrategies.Tunnel);
        AddHandler(PointerCaptureLostEvent, OnPointerCaptureLostGlobal, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// Permite arrastar a janela do painel (ex.: para mover entre monitores).
    /// </summary>
    private void OnLogoDragRegionPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        var window = (sender as Visual)?.GetVisualRoot() as Window;
        window?.BeginMoveDrag(e);
    }

    private void OnFerramentaItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (DataContext is not PainelViewModel vm || !vm.ListaTarefasLateralAberta)
            return;

        if (sender is not StyledElement element ||
            element.DataContext is not FerramentaTarefaLateralItem ferramenta)
        {
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        vm.FerramentaLateralSelecionada = ferramenta;

        _ferramentaEmDrag = ferramenta;
        _visualOrigem = sender as Control;
        _pontoInicioTopLevel = e.GetPosition(topLevel);
        _ultimaPosicaoTopLevel = _pontoInicioTopLevel;
        _temUltimaPosicaoTopLevel = true;
        _aguardandoInicioDrag = true;
        _dragAtivo = false;
        _ponteiroCapturado = e.Pointer;

        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void OnPointerMovedGlobal(object? sender, PointerEventArgs e)
    {
        if (_ferramentaEmDrag is null)
            return;

        if (e.Pointer.Captured != this)
            return;

        if (DataContext is not PainelViewModel vm || !vm.ListaTarefasLateralAberta)
        {
            CancelarDragAtivo();
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            CancelarDragAtivo();
            return;
        }

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            CancelarDragAtivo();
            return;
        }

        var posicaoTopLevel = e.GetPosition(topLevel);
        _ultimaPosicaoTopLevel = posicaoTopLevel;
        _temUltimaPosicaoTopLevel = true;

        if (_aguardandoInicioDrag && !_dragAtivo)
        {
            var delta = posicaoTopLevel - _pontoInicioTopLevel;
            if (Math.Abs(delta.X) < DistanciaMinimaDrag &&
                Math.Abs(delta.Y) < DistanciaMinimaDrag)
            {
                return;
            }

            _dragAtivo = true;
            FerramentaDragStarted?.Invoke(this, new FerramentaDragSidebarEventArgs(_ferramentaEmDrag, posicaoTopLevel, visualOrigem: _visualOrigem));
        }

        if (_dragAtivo)
            FerramentaDragMoved?.Invoke(this, new FerramentaDragSidebarEventArgs(_ferramentaEmDrag, posicaoTopLevel));

        e.Handled = true;
    }

    private void OnPointerReleasedGlobal(object? sender, PointerReleasedEventArgs e)
    {
        if (!_aguardandoInicioDrag && !_dragAtivo)
            return;

        if (e.Pointer.Captured == this)
            e.Pointer.Capture(null);

        if (_ferramentaEmDrag is null)
        {
            LimparEstadoDrag();
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is not null)
        {
            _ultimaPosicaoTopLevel = e.GetPosition(topLevel);
            _temUltimaPosicaoTopLevel = true;
        }

        var ferramenta = _ferramentaEmDrag;
        var posicao = _ultimaPosicaoTopLevel;
        var tinhaPosicao = _temUltimaPosicaoTopLevel;
        var tinhaDragAtivo = _dragAtivo;

        LimparEstadoDrag();

        if (tinhaDragAtivo && tinhaPosicao)
            FerramentaDragEnded?.Invoke(this, new FerramentaDragSidebarEventArgs(ferramenta, posicao));

        e.Handled = true;
    }

    private void OnPointerCaptureLostGlobal(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!_aguardandoInicioDrag && !_dragAtivo)
            return;

        CancelarDragAtivo(cancellationReason: "capture_lost");
    }

    public void CancelarDragAtivo(bool notificarEvento = true, string cancellationReason = "capture_lost")
    {
        if (!_aguardandoInicioDrag && !_dragAtivo)
            return;

        if (_ponteiroCapturado is not null && _ponteiroCapturado.Captured == this)
            _ponteiroCapturado.Capture(null);

        var ferramenta = _ferramentaEmDrag;
        var posicao = _ultimaPosicaoTopLevel;
        var podeNotificar = _temUltimaPosicaoTopLevel && ferramenta is not null;

        LimparEstadoDrag();

        if (notificarEvento && podeNotificar)
            FerramentaDragCanceled?.Invoke(this, new FerramentaDragSidebarEventArgs(ferramenta!, posicao, cancellationReason));
    }

    private void LimparEstadoDrag()
    {
        _aguardandoInicioDrag = false;
        _dragAtivo = false;
        _ferramentaEmDrag = null;
        _visualOrigem = null;
        _ponteiroCapturado = null;
        _temUltimaPosicaoTopLevel = false;
    }
}

/// <summary>
/// Dados do evento de drag de ferramenta da sidebar.
/// VisualOrigem carrega a referencia ao Control do card clicado para
/// que PainelView possa capturar seu visual via RenderTargetBitmap.
/// </summary>
public sealed class FerramentaDragSidebarEventArgs : EventArgs
{
    public FerramentaTarefaLateralItem Ferramenta { get; }
    public Point PosicaoTopLevel { get; }
    public string? CancellationReason { get; }

    /// <summary>
    /// Grid do card de ferramenta na sidebar. Usado por PainelView.CarregarGhostImagem()
    /// para capturar screenshot via RenderTargetBitmap quando a ferramenta nao tem
    /// ImagemGhostUri propria. NAO remover este campo.
    /// </summary>
    public Control? VisualOrigem { get; }

    public FerramentaDragSidebarEventArgs(
        FerramentaTarefaLateralItem ferramenta,
        Point posicaoTopLevel,
        string? cancellationReason = null,
        Control? visualOrigem = null)
    {
        Ferramenta = ferramenta;
        PosicaoTopLevel = posicaoTopLevel;
        CancellationReason = cancellationReason;
        VisualOrigem = visualOrigem;
    }
}
