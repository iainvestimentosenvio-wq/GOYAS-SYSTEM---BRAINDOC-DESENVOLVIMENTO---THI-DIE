using System;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using Protons.UI.Common;
using Protons.UI.Painel.ViewModels;
using Protons.UI.Painel.Views.PainelPrincipal.Controles;
using Protons.UI.Painel.Views.PainelPrincipal.Partes;

namespace Protons.UI.Painel.Views;

/// <summary>
/// View raiz do painel principal. Coordena o ciclo completo de drag-and-drop
/// entre a sidebar (BarraLateralPainelPrincipalView) e as esteiras de tarefas.
///
/// ═══════════════════════════════════════════════════════════════════════════
/// ARQUITETURA DE 3 CAMADAS (DRAG-AND-DROP)
/// ═══════════════════════════════════════════════════════════════════════════
///
///   BarraLateral (CAMADA 1)         PainelView (CAMADA 2)              EsteiraTarefas (CAMADA 3)
///   ───────────────────────         ─────────────────────              ─────────────────────────
///   PointerPressed                  OnFerramentaDragStarted            DefinirDropHover(true)
///     → captura pointer               → CarregarGhostImagem              → destaque visual
///   PointerMoved (> 6px)            OnFerramentaDragMoved              ProcessarDropFerramenta
///     → FerramentaDragStarted         → AtualizarDragGhostPosicao        → FerramentaSoltaNaEsteira
///     → FerramentaDragMoved           → AtualizarHoverEsteira
///   PointerReleased                 OnFerramentaDragEnded
///     → FerramentaDragEnded           → EncerrarSessaoDragInterna
///
/// ═══════════════════════════════════════════════════════════════════════════
/// ⚠️  REGRAS CRITICAS (NAO ALTERAR SEM ENTENDER COMPLETAMENTE)  ⚠️
/// ═══════════════════════════════════════════════════════════════════════════
///
/// REGRA 1: Imagem Ghost em Code-Behind (NAO em Binding XAML)
/// ───────────────────────────────────────────────────────────────────────────
///   PROBLEMA:
///     - Avalonia 11 NAO converte string → IImage em bindings
///     - TypeConverter de string → IImage so funciona com valores literais XAML
///     - Binding <Image Source="{Binding ImagemGhostUri}" /> = FALHA
///
///   SOLUCAO:
///     - Carregar em code-behind via AssetLoader.Open + Bitmap
///     - Ver: CarregarGhostImagem(), linhas 276-326
///
///   TESTE PRATICO:
///     1. Tente usar <Image Source="{Binding ImagemGhostUri}" /> no XAML
///     2. Execute drag de ferramenta
///     3. Resultado: Ghost aparece SEM imagem (Source = null)
///
/// ───────────────────────────────────────────────────────────────────────────
/// REGRA 2: RenderTargetBitmap (Screenshot do Card)
/// ───────────────────────────────────────────────────────────────────────────
///   OBJETIVO:
///     - Ferramentas sem PNG proprio (ImagemGhostUri) ainda precisam de ghost
///     - Solucao: capturar screenshot do card visual clicado na sidebar
///
///   COMO FUNCIONA:
///     1. BarraLateral passa VisualOrigem (Grid do card) no evento DragStarted
///     2. CarregarGhostImagem() chama RenderTargetBitmap(visualOrigem)
///     3. Avalonia renderiza Grid em bitmap off-screen
///     4. Bitmap vira GhostImagem.Source
///     5. Ghost exibe visual EXATO do botao clicado
///
///   VANTAGENS:
///     - Ferramentas novas nao precisam de PNG separado
///     - Mudancas de estilo automaticamente refletem no ghost
///     - Ghost sempre corresponde ao visual atual do botao
///
///   NAO remover parametro VisualOrigem do evento FerramentaDragSidebarEventArgs!
///
/// ───────────────────────────────────────────────────────────────────────────
/// REGRA 3: Protecao Contra Reentrada (Captura de Variaveis Locais)
/// ───────────────────────────────────────────────────────────────────────────
///   PROBLEMA:
///     - ProcessarDropFerramenta() pode abrir modal (configuracao de tarefa)
///     - Modal pode disparar eventos (Window.Deactivated)
///     - Eventos chamam CancelarSessaoDragGlobal()
///     - CancelarSessaoDragGlobal() limpa _esteiraHoverDrop, _ferramentaEmDrag
///     - Usar campos diretamente = NullReferenceException
///
///   SOLUCAO:
///     - Capturar referencias locais ANTES de chamar ProcessarDropFerramenta
///     - Ver: OnFerramentaDragEnded(), linhas 205-209
///
///   CODIGO CORRETO:
///     var esteiraDrop = _esteiraHoverDrop;  // Captura local
///     var ferramentaDrag = _ferramentaEmDrag;
///     esteiraDrop.ProcessarDropFerramenta(...);  // Usa local, nao campo
///
///   CODIGO ERRADO:
///     _esteiraHoverDrop.ProcessarDropFerramenta(...);  // Pode ser null!
///
/// ───────────────────────────────────────────────────────────────────────────
/// REGRA 4: Fallback de Coordenadas Retorna pontoTopLevel (NAO Point(0,0))
/// ───────────────────────────────────────────────────────────────────────────
///   PROBLEMA:
///     - TranslatePoint() pode falhar (retorna null)
///     - Se fallback retornar Point(0,0) e ghost tem offset de -metade:
///       Ghost fica em (-56, -56) com ghost 112x112 = INVISIVEL, fora da tela
///
///   SOLUCAO:
///     - Fallback retorna pontoTopLevel (coordenadas aproximadas, mas visiveis)
///     - Ver: ConverterTopLevelParaVisual(), linhas 377-383
///
///   CODIGO CORRETO:
///     return topoVisual.TranslatePoint(pontoTopLevel, destino) ?? pontoTopLevel;
///
///   CODIGO ERRADO:
///     return topoVisual.TranslatePoint(pontoTopLevel, destino) ?? new Point(0, 0);
///
/// ═══════════════════════════════════════════════════════════════════════════
/// DOCUMENTACAO COMPLETA: documentacao/painel/drag_drop/PAINEL_DRAG_DROP.md
/// ═══════════════════════════════════════════════════════════════════════════
/// </summary>
public partial class PainelView : UserControl
{
    private static readonly TimeSpan IntervaloLogDragMove = TimeSpan.FromMilliseconds(250);
    private const double LarguraOrelhaPainelKpis = 24d;
    private const double LarguraOrelhaPainelEsteiras = 24d;

    private readonly Stopwatch _firstRenderStopwatch = Stopwatch.StartNew();

    private bool _firstRenderRegistrado;
    private PainelViewModel? _vm;
    private TopLevel? _topLevel;
    private EsteiraTarefasControl? _esteiraHoverDrop;  // Esteira sob o cursor durante drag
    private FerramentaTarefaLateralItem? _ferramentaEmDrag;  // Ferramenta sendo arrastada
    private bool _sessaoDragAtiva;  // True entre DragStarted e DragEnded/Canceled
    private DateTime _ultimoLogMoveUtc = DateTime.MinValue;

    public PainelView()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, OnPainelPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);

        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        DataContextChanged += OnDataContextChanged;
        PainelKpisInlineSlot.SizeChanged += OnPainelKpisInlineSlotSizeChanged;
        PainelEsteirasInlineSlot.SizeChanged += OnPainelEsteirasInlineSlotSizeChanged;
        BarraLateralPainel.FerramentaDragStarted += OnFerramentaDragStarted;
        BarraLateralPainel.FerramentaDragMoved += OnFerramentaDragMoved;
        BarraLateralPainel.FerramentaDragEnded += OnFerramentaDragEnded;
        BarraLateralPainel.FerramentaDragCanceled += OnFerramentaDragCanceled;

        if (!_firstRenderRegistrado)
        {
            _firstRenderRegistrado = true;
            _firstRenderStopwatch.Stop();
            OpsLogger.WriteInfo($"painel_metrica id=first_render_ms valor={_firstRenderStopwatch.ElapsedMilliseconds} unidade=ms");
        }

        TrocarTopLevel(TopLevel.GetTopLevel(this));

        // Conecta o FilePicker real (StorageProvider) agora que o TopLevel está disponível.
        // Se DataContext já estiver definido (ex.: binding do pai), aplicar agora.
        if (DataContext != null)
            OnDataContextChanged(this, EventArgs.Empty);

        _vm?.ConfigurarFilePickerAncorarPdf(() => _topLevel);
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        DataContextChanged -= OnDataContextChanged;
        PainelKpisInlineSlot.SizeChanged -= OnPainelKpisInlineSlotSizeChanged;
        PainelEsteirasInlineSlot.SizeChanged -= OnPainelEsteirasInlineSlotSizeChanged;
        BarraLateralPainel.FerramentaDragStarted -= OnFerramentaDragStarted;
        BarraLateralPainel.FerramentaDragMoved -= OnFerramentaDragMoved;
        BarraLateralPainel.FerramentaDragEnded -= OnFerramentaDragEnded;
        BarraLateralPainel.FerramentaDragCanceled -= OnFerramentaDragCanceled;
        if (_vm is not null)
            _vm.PropertyChanged -= OnVmPropertyChanged;

        CancelarSessaoDragGlobal("window_deactivated");
        TrocarTopLevel(null);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnVmPropertyChanged;

        _vm = DataContext as PainelViewModel;

        if (_vm is not null)
            _vm.PropertyChanged += OnVmPropertyChanged;
        else
            CancelarSessaoDragGlobal("capture_lost");

        AtualizarPainelKpisInline(animar: false);
        AtualizarPainelEsteirasInline(animar: false);
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_vm is null)
            return;

        if (e.PropertyName == nameof(PainelViewModel.ListaTarefasLateralAberta) && !_vm.ListaTarefasLateralAberta)
            CancelarSessaoDragGlobal("list_closed");

        if (e.PropertyName == nameof(PainelViewModel.ConfiguracaoTarefaAberta) && _vm.ConfiguracaoTarefaAberta)
            CancelarSessaoDragGlobal("modal_opened");

        if (e.PropertyName == nameof(PainelViewModel.PainelKpisDockAberto))
            AtualizarPainelKpisInline(animar: true);

        if (e.PropertyName is nameof(PainelViewModel.PainelEsteirasDockAberto)
            or nameof(PainelViewModel.PodeUsarEsteiras))
            AtualizarPainelEsteirasInline(animar: true);
    }

    private void OnPainelKpisInlineSlotSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_vm?.PainelKpisDockAberto != true)
            return;

        PainelKpisInlineHost.Width = ObterLarguraExpandidaDockHorizontal(PainelKpisInlineSlot.Bounds.Width, LarguraOrelhaPainelKpis);
    }

    private void OnPainelEsteirasInlineSlotSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_vm?.PodeUsarEsteiras != true || _vm.PainelEsteirasDockAberto != true)
            return;

        PainelEsteirasInlineHost.Width = ObterLarguraExpandidaPainelEsteirasInline();
    }

    private void AtualizarPainelKpisInline(bool animar)
    {
        _ = animar;
        AtualizarDockHorizontal(PainelKpisInlineHost, PainelKpisInlineSlot.Bounds.Width, _vm?.PainelKpisDockAberto == true, LarguraOrelhaPainelKpis);
    }

    private void AtualizarPainelEsteirasInline(bool animar)
    {
        _ = animar;
        if (_vm?.PodeUsarEsteiras != true)
        {
            PainelEsteirasInlineHost.IsVisible = false;
            PainelEsteirasInlineHost.IsHitTestVisible = false;
            PainelEsteirasInlineHost.Width = 0;
            return;
        }

        AtualizarDockHorizontal(PainelEsteirasInlineHost, PainelEsteirasInlineSlot.Bounds.Width, _vm?.PainelEsteirasDockAberto == true, LarguraOrelhaPainelEsteiras);
    }

    private double ObterLarguraExpandidaPainelEsteirasInline()
    {
        return ObterLarguraExpandidaDockHorizontal(PainelEsteirasInlineSlot.Bounds.Width, LarguraOrelhaPainelEsteiras);
    }

    private static void AtualizarDockHorizontal(Border host, double larguraSlot, bool painelAberto, double larguraOrelha)
    {
        host.IsVisible = true;
        host.IsHitTestVisible = true;
        host.Width = painelAberto
            ? ObterLarguraExpandidaDockHorizontal(larguraSlot, larguraOrelha)
            : larguraOrelha;
    }

    private static double ObterLarguraExpandidaDockHorizontal(double larguraSlot, double larguraOrelha)
    {
        return Math.Max(larguraOrelha, larguraSlot + larguraOrelha);
    }

    private void TrocarTopLevel(TopLevel? novoTopLevel)
    {
        if (ReferenceEquals(_topLevel, novoTopLevel))
            return;

        if (_topLevel is not null)
        {
            _topLevel.RemoveHandler(KeyDownEvent, OnTopLevelKeyDown);
            if (_topLevel is Window janelaAtual)
                janelaAtual.Deactivated -= OnJanelaDeactivated;
        }

        _topLevel = novoTopLevel;

        if (_topLevel is not null)
        {
            _topLevel.AddHandler(KeyDownEvent, OnTopLevelKeyDown, RoutingStrategies.Tunnel);
            if (_topLevel is Window novaJanela)
                novaJanela.Deactivated += OnJanelaDeactivated;
        }
    }

    private void OnTopLevelKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm is not null && _vm.SeletorClientesAberto && e.Key == Key.Escape)
        {
            _vm.FecharSeletorClientesCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (!_sessaoDragAtiva)
            return;

        if (e.Key != Key.Escape)
            return;

        CancelarSessaoDragGlobal("esc");
        e.Handled = true;
    }

    private void OnJanelaDeactivated(object? sender, EventArgs e)
    {
        CancelarSessaoDragGlobal("window_deactivated");
        _vm?.FecharSeletorClientesCommand.Execute(null);
    }

    private void OnBuscaClientePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm is null || _vm.SeletorClientesAberto)
            return;

        _vm.AbrirSeletorClientesCommand.Execute(null);
    }

    /// <summary>
    /// Permite arrastar a janela do painel (ex.: para mover entre monitores).
    /// </summary>
    private void OnTopBarDragRegionPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        var window = (sender as Visual)?.GetVisualRoot() as Window;
        window?.BeginMoveDrag(e);
    }

    private void OnPainelPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm is null || !_vm.SeletorClientesAberto)
            return;

        if (e.Source is not Visual origemVisual)
            return;

        if (EstaDentroDoSeletorClientes(origemVisual))
            return;

        _vm.FecharSeletorClientesCommand.Execute(null);
    }

    private bool EstaDentroDoSeletorClientes(Visual visualOrigem)
    {
        Visual? atual = visualOrigem;
        while (atual is not null)
        {
            if (ReferenceEquals(atual, SeletorClientesContainer) || ReferenceEquals(atual, BuscaClienteTextBox))
                return true;

            atual = atual.GetVisualParent();
        }

        return false;
    }

    private void OnFerramentaDragStarted(object? sender, FerramentaDragSidebarEventArgs e)
    {
        if (_vm is null)
            return;

        if (!_vm.ListaTarefasLateralAberta || _vm.ConfiguracaoTarefaAberta)
        {
            BarraLateralPainel.CancelarDragAtivo(notificarEvento: false);
            return;
        }

        _sessaoDragAtiva = true;
        _ferramentaEmDrag = e.Ferramenta;
        _ultimoLogMoveUtc = DateTime.MinValue;

        var pontoLocalPainel = ConverterTopLevelParaVisual(e.PosicaoTopLevel, this);
        _vm.IniciarDragGhost(e.Ferramenta, pontoLocalPainel.X, pontoLocalPainel.Y);
        CarregarGhostImagem(e.Ferramenta, e.VisualOrigem);
        _vm.RegistrarEventoDragFerramenta("drag_tool_start", $"ferramenta_id={SanitizarValor(e.Ferramenta.FerramentaId)}");
    }

    private void OnFerramentaDragMoved(object? sender, FerramentaDragSidebarEventArgs e)
    {
        if (!_sessaoDragAtiva || _vm is null)
            return;

        var pontoLocalPainel = ConverterTopLevelParaVisual(e.PosicaoTopLevel, this);
        _vm.AtualizarDragGhostPosicao(pontoLocalPainel.X, pontoLocalPainel.Y);
        AtualizarHoverEsteira(e.PosicaoTopLevel);

        var agora = DateTime.UtcNow;
        if (agora - _ultimoLogMoveUtc >= IntervaloLogDragMove)
        {
            _ultimoLogMoveUtc = agora;
            _vm.RegistrarEventoDragFerramenta("drag_tool_move", $"x={(int)pontoLocalPainel.X} y={(int)pontoLocalPainel.Y}");
        }
    }

    private void OnFerramentaDragEnded(object? sender, FerramentaDragSidebarEventArgs e)
    {
        if (!_sessaoDragAtiva || _vm is null)
            return;

        AtualizarHoverEsteira(e.PosicaoTopLevel);

        // ═══════════════════════════════════════════════════════════════════════════
        // ⚠️  PROTECAO CONTRA REENTRADA - NAO REMOVER  ⚠️
        // ═══════════════════════════════════════════════════════════════════════════
        //
        // Capturar referencias locais ANTES de ProcessarDropFerramenta.
        //
        // POR QUE?
        //   ProcessarDropFerramenta() pode abrir modal (configuracao de tarefa).
        //   Modal pode disparar Window.Deactivated ou outros eventos.
        //   Eventos chamam CancelarSessaoDragGlobal().
        //   CancelarSessaoDragGlobal() limpa _esteiraHoverDrop, _ferramentaEmDrag.
        //   Se usarmos os campos diretamente = NullReferenceException.
        //
        // SOLUCAO:
        //   Capturar em variaveis locais ANTES de chamar handler externo.
        //   Mesmo que os campos sejam limpos, as variaveis locais permanecem validas.
        //
        // NAO ALTERAR ESTA LOGICA SEM ENTENDER O PROBLEMA DE REENTRADA!
        //
        // ═══════════════════════════════════════════════════════════════════════════
        var esteiraDrop = _esteiraHoverDrop ?? EncontrarEsteiraNoPonto(e.PosicaoTopLevel);
        var ferramentaDrag = _ferramentaEmDrag;
        var vm = _vm;

        if (esteiraDrop is not null && ferramentaDrag is not null)
        {
            var pontoNaEsteira = ConverterTopLevelParaVisual(e.PosicaoTopLevel, esteiraDrop);
            esteiraDrop.ProcessarDropFerramenta(ferramentaDrag.FerramentaId, ferramentaDrag.Nome, pontoNaEsteira);
            vm?.RegistrarEventoDragFerramenta(
                "drag_tool_drop_success",
                $"ferramenta={SanitizarValor(ferramentaDrag.FerramentaId)} esteira_id={esteiraDrop.EsteiraId}");
        }
        else
        {
            vm?.RegistrarEventoDragFerramenta("drag_tool_drop_cancel", "motivo=outside_lane");
        }

        EncerrarSessaoDragInterna();
    }

    private void OnFerramentaDragCanceled(object? sender, FerramentaDragSidebarEventArgs e)
    {
        if (!_sessaoDragAtiva && _ferramentaEmDrag is null)
            return;

        var reason = string.IsNullOrWhiteSpace(e.CancellationReason) ? "capture_lost" : e.CancellationReason;
        _vm?.RegistrarEventoDragFerramenta("drag_tool_drop_cancel", $"motivo={reason}");
        EncerrarSessaoDragInterna();
    }

    private void CancelarSessaoDragGlobal(string motivo)
    {
        if (!_sessaoDragAtiva && _ferramentaEmDrag is null)
            return;

        _vm?.RegistrarEventoDragFerramenta("drag_tool_drop_cancel", $"motivo={motivo}");
        BarraLateralPainel.CancelarDragAtivo(notificarEvento: false);
        EncerrarSessaoDragInterna();
    }

    private void EncerrarSessaoDragInterna()
    {
        if (_esteiraHoverDrop is not null)
        {
            _esteiraHoverDrop.DefinirDropHover(false);
            _esteiraHoverDrop = null;
        }

        _sessaoDragAtiva = false;
        _ferramentaEmDrag = null;
        _ultimoLogMoveUtc = DateTime.MinValue;
        GhostImagem.Source = null;
        _vm?.EncerrarDragGhost();
    }

    /// <summary>
    /// Carrega a imagem do ghost para a ferramenta sendo arrastada.
    ///
    /// ═══════════════════════════════════════════════════════════════════════════
    /// ⚠️  POR QUE NAO USAR BINDING XAML  ⚠️
    /// ═══════════════════════════════════════════════════════════════════════════
    ///
    /// PROBLEMA:
    ///   - Avalonia 11: Image.Source espera IImage
    ///   - ViewModel expoe string (URI avares://)
    ///   - TypeConverter string→IImage so funciona com valores LITERAIS no XAML
    ///   - NAO funciona com bindings (nem com x:CompileBindings="False")
    ///
    /// TESTE QUE FALHA:
    ///   <Image Source="{Binding ImagemGhostUri}" />
    ///   Resultado: Source = null (string nao convertida)
    ///
    /// SOLUCAO:
    ///   - Carregar em code-behind via AssetLoader.Open + Bitmap
    ///   - GhostImagem.Source = new Bitmap(stream)
    ///
    /// NAO tentar "consertar" usando binding XAML!
    ///
    /// ═══════════════════════════════════════════════════════════════════════════
    /// ESTRATEGIA DE RESOLUCAO (3 NIVEIS DE FALLBACK)
    /// ═══════════════════════════════════════════════════════════════════════════
    ///
    /// NIVEL 1: ImagemGhostUri da ferramenta (PNG customizado)
    /// ───────────────────────────────────────────────────────────────────────────
    ///   - Ferramenta define ImagemGhostUri no ViewModel
    ///   - Exemplo: "avares://Protons.UI/Assets/UI/Buttons/ancora_pdf_normal.png"
    ///   - Usado por: Extrator PDF (botao com PNG proprio)
    ///   - Vantagem: Controle total sobre aparencia do ghost
    ///
    /// NIVEL 2: RenderTargetBitmap (Screenshot do card visual)
    /// ───────────────────────────────────────────────────────────────────────────
    ///   - Captura screenshot do Grid clicado na sidebar (visualOrigem)
    ///   - Avalonia renderiza controle em bitmap off-screen
    ///   - Usado por: Ferramentas genericas (sem PNG proprio)
    ///   - Vantagem: Ghost = visual exato do botao clicado
    ///   - Vantagem: Mudancas de estilo automaticamente refletem no ghost
    ///   - Vantagem: Ferramentas novas nao precisam de PNG separado
    ///
    /// NIVEL 3: Fallback absoluto (ancora_pdf_normal.png hardcoded)
    /// ───────────────────────────────────────────────────────────────────────────
    ///   - Usado se Nivel 1 e 2 falharem
    ///   - Garante que ghost NUNCA fica vazio
    ///   - Experiencia ruim (imagem errada), mas melhor que nada
    ///
    /// ═══════════════════════════════════════════════════════════════════════════
    /// FLUXO DE RENDERIZACAO (Nivel 2 - RenderTargetBitmap)
    /// ═══════════════════════════════════════════════════════════════════════════
    ///
    ///   1. Usuario clica no card "Ferramenta X" na sidebar
    ///   2. BarraLateral captura visualOrigem (Grid do card)
    ///   3. BarraLateral dispara FerramentaDragStarted(visualOrigem)
    ///   4. PainelView.CarregarGhostImagem() recebe visualOrigem
    ///   5. new RenderTargetBitmap(pixelSize)
    ///   6. rtb.Render(visualOrigem)  ← Screenshot aqui
    ///   7. GhostImagem.Source = rtb
    ///   8. Ghost exibe visual identico ao botao clicado
    ///
    /// ═══════════════════════════════════════════════════════════════════════════
    /// </summary>
    private void CarregarGhostImagem(FerramentaTarefaLateralItem ferramenta, Control? visualOrigem)
    {
        // Nivel 1: Imagem customizada do asset (ex: ancora_pdf_normal.png para Extrator PDF)
        if (!string.IsNullOrWhiteSpace(ferramenta.ImagemGhostUri))
        {
            try
            {
                using var stream = AssetLoader.Open(new Uri(ferramenta.ImagemGhostUri));
                GhostImagem.Source = new Bitmap(stream);
                DefinirTamanhoGhost(112, 112);
                return;
            }
            catch (Exception ex) { OpsLogger.WriteError("painel_evento id=ghost_imagem_asset_falha", ex); }
        }

        // Nivel 2: Screenshot do card visual via RenderTargetBitmap.
        // Cada ferramenta generica (sem PNG proprio) mostra seu card real como ghost,
        // garantindo que o ghost sempre corresponda visualmente ao botao clicado.
        if (visualOrigem is not null &&
            visualOrigem.Bounds.Width > 0 &&
            visualOrigem.Bounds.Height > 0)
        {
            try
            {
                var w = visualOrigem.Bounds.Width;
                var h = visualOrigem.Bounds.Height;
                var pixelSize = new PixelSize(
                    (int)Math.Ceiling(w),
                    (int)Math.Ceiling(h));
                var rtb = new RenderTargetBitmap(pixelSize);
                rtb.Render(visualOrigem);
                GhostImagem.Source = rtb;
                DefinirTamanhoGhost(w, h);
                return;
            }
            catch (Exception ex) { OpsLogger.WriteError("painel_evento id=ghost_imagem_rtb_falha", ex); }
        }

        // Nivel 3: Fallback absoluto - nunca deixar ghost sem imagem
        try
        {
            using var stream = AssetLoader.Open(
                new Uri(PainelViewModel.AssetAncoraPdfUri));
            GhostImagem.Source = new Bitmap(stream);
            DefinirTamanhoGhost(112, 112);
        }
        catch (Exception ex)
        {
            OpsLogger.WriteError("painel_evento id=ghost_imagem_fallback_falha", ex);
            GhostImagem.Source = null;
        }
    }

    /// <summary>
    /// Ajusta o tamanho do ghost (Border + Image) e sincroniza o offset de
    /// centralizacao no ViewModel para que o ghost fique centrado no cursor.
    /// </summary>
    private void DefinirTamanhoGhost(double largura, double altura)
    {
        GhostBorda.Width = largura;
        GhostBorda.Height = altura;
        GhostImagem.Width = largura;
        GhostImagem.Height = altura;
        _vm?.DefinirTamanhoGhost(largura, altura);
    }

    private void AtualizarHoverEsteira(Point pontoTopLevel)
    {
        var novaEsteira = EncontrarEsteiraNoPonto(pontoTopLevel);
        if (ReferenceEquals(novaEsteira, _esteiraHoverDrop))
            return;

        if (_esteiraHoverDrop is not null)
        {
            _vm?.RegistrarEventoDragFerramenta("drag_tool_hover_leave", $"esteira_id={_esteiraHoverDrop.EsteiraId}");
            _esteiraHoverDrop.DefinirDropHover(false);
        }

        _esteiraHoverDrop = novaEsteira;

        if (_esteiraHoverDrop is not null)
        {
            _esteiraHoverDrop.DefinirDropHover(true);
            _vm?.RegistrarEventoDragFerramenta("drag_tool_hover_enter", $"esteira_id={_esteiraHoverDrop.EsteiraId}");
        }
    }

    private EsteiraTarefasControl? EncontrarEsteiraNoPonto(Point pontoTopLevel)
    {
        if (_topLevel is not IInputElement inputRoot)
            return null;

        var hit = inputRoot.InputHitTest(pontoTopLevel) as Visual;
        var porCaminhoVisual = EncontrarEsteiraNoCaminhoVisual(hit);
        if (porCaminhoVisual is not null)
            return porCaminhoVisual;

        return EncontrarEsteiraPorAreaMapeada(pontoTopLevel);
    }

    private EsteiraTarefasControl? EncontrarEsteiraPorAreaMapeada(Point pontoTopLevel)
    {
        if (_topLevel is not Visual topoVisual)
            return null;

        foreach (var visual in this.GetVisualDescendants())
        {
            if (visual is not Control control || !control.IsVisible)
                continue;

            var esteiraMapeada = EsteiraTarefasControl.GetDropTargetEsteira(control);
            if (esteiraMapeada is null)
                continue;

            var pontoLocal = topoVisual.TranslatePoint(pontoTopLevel, control);
            if (pontoLocal is null)
                continue;

            var bounds = control.Bounds;
            if (pontoLocal.Value.X < 0 || pontoLocal.Value.Y < 0 ||
                pontoLocal.Value.X > bounds.Width || pontoLocal.Value.Y > bounds.Height)
                continue;

            return esteiraMapeada;
        }

        return null;
    }

    /// <summary>
    /// Converte coordenadas do TopLevel para o espaco local de um Visual.
    ///
    /// ═══════════════════════════════════════════════════════════════════════════
    /// ⚠️  FALLBACK DEVE RETORNAR pontoTopLevel (NAO Point(0,0))  ⚠️
    /// ═══════════════════════════════════════════════════════════════════════════
    ///
    /// PROBLEMA COM Point(0,0):
    ///   - TranslatePoint() pode falhar (retorna null)
    ///   - Se fallback retornar Point(0,0):
    ///     • Ghost tem offset de -metade (para centralizar no cursor)
    ///     • Exemplo: Ghost 112x112 → offset (-56, -56)
    ///     • Ghost posicionado em (0, 0) + offset = (-56, -56)
    ///     • Resultado: Ghost INVISIVEL, fora da tela (canto superior esquerdo)
    ///
    /// SOLUCAO COM pontoTopLevel:
    ///   - Fallback retorna pontoTopLevel (coordenadas originais)
    ///   - Mesmo sem conversao perfeita, coordenadas sao aproximadas
    ///   - Ghost fica visivel, proximo ao cursor
    ///   - Experiencia: ghost "trava" se conversao falhar, mas ainda visivel
    ///   - Melhor que ghost desaparecer completamente
    ///
    /// TESTE PRATICO:
    ///   1. Force fallback: return new Point(0, 0);
    ///   2. Execute drag de ferramenta
    ///   3. Resultado: Ghost desaparece (fica em coordenadas negativas)
    ///
    /// NAO ALTERAR FALLBACK PARA Point(0,0)!
    ///
    /// ═══════════════════════════════════════════════════════════════════════════
    /// </summary>
    private Point ConverterTopLevelParaVisual(Point pontoTopLevel, Visual destino)
    {
        if (_topLevel is not Visual topoVisual)
            return pontoTopLevel;  // ← NAO retornar Point(0,0)!

        return topoVisual.TranslatePoint(pontoTopLevel, destino) ?? pontoTopLevel;  // ← NAO usar ?? new Point(0,0)!
    }

    private static EsteiraTarefasControl? EncontrarEsteiraNoCaminhoVisual(Visual? visual)
    {
        var atual = visual;
        while (atual is not null)
        {
            if (atual is EsteiraTarefasControl esteira)
                return esteira;

            if (atual is AvaloniaObject objetoVisual)
            {
                var esteiraMapeada = EsteiraTarefasControl.GetDropTargetEsteira(objetoVisual);
                if (esteiraMapeada is not null)
                    return esteiraMapeada;
            }

            atual = atual.GetVisualParent();
        }

        return null;
    }

    private static string SanitizarValor(string valor)
    {
        return valor.Trim().Replace(' ', '_');
    }
}
