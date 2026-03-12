using System;
using System.ComponentModel;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Protons.UI.Common;
using Protons.UI.Login.ViewModels;
using Protons.UI.Painel.ViewModels;

namespace Protons.UI.Login.Views;

public partial class MainWindow : Window
{
    private enum WindowMode
    {
        Login,
        Painel
    }

    private MainWindowViewModel? _viewModel;
    private Border? _dragRegion;
    private Button? _closeButton;
    private bool _isPainelActive;
    private bool _initialPositionSet;
    private WindowMode? _lastCenteredMode;
    private int _centerRequestId;
    private readonly bool _isPureWaylandSession;
    private bool _manualPositioningDisabled;
    private bool _recheckScheduled;
    private bool _painelTransitionActive;
    private CancellationTokenSource? _painelTransitionCts;
    private Window? _painelWindow;
    private bool _closingPainelForLogout;
    private bool _loginRecenterScheduled;
    private readonly bool _diagEnabled;
    private long _lastDiagTick;

    public MainWindow()
    {
        // IMPORTANTE: Iniciar COM decorações para permitir posicionamento
        // As decorações serão removidas após a janela ser posicionada
        InitializeComponent();

        _dragRegion = this.FindControl<Border>("DragRegion");
        _closeButton = this.FindControl<Button>("CloseButton");

        // Esconder controles customizados inicialmente
        if (_dragRegion is not null) _dragRegion.IsVisible = false;
        if (_closeButton is not null) _closeButton.IsVisible = false;

        // Login inicia direto com chrome customizado (ajustado por plataforma)
        ApplyWindowChrome(useSystemChrome: false);

        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _isPureWaylandSession = IsPureWaylandSession();

        _diagEnabled = IsWindowDiagEnabled();
        if (_diagEnabled)
        {
            SizeChanged += (_, __) => LogWindowMetricsThrottled("size_changed");
            LayoutUpdated += (_, __) => LogWindowMetricsThrottled("layout_updated");
            OpsLogger.WriteInfo($"window_startup: startupLocation={WindowStartupLocation} pureWayland={_isPureWaylandSession}");
        }

        DataContextChanged += OnDataContextChanged;

        // Quando a janela for mostrada, aplicar o chrome correto
        Opened += OnWindowOpened;
        LogWindowMetrics("ctor_after_init");
    }

    private async void OnWindowOpened(object? sender, EventArgs e)
    {
        if (!_initialPositionSet)
        {
            _initialPositionSet = true;

            LogWindowMetrics("opened_before_delay");

            // Aguardar um frame para garantir que a janela está visível
            await System.Threading.Tasks.Task.Delay(50);

            LogWindowMetrics("opened_ready");
            ScheduleLoginRecenter();

            OpsLogger.WriteInfo($"OnWindowOpened: Position after chrome = ({Position.X},{Position.Y})");
        }
    }

    private void CloseButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }

    private void TitleBar_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.SetDisplayedViewModel(_viewModel.CurrentViewModel);
            ApplyWindowSizing(_viewModel.CurrentViewModel);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.CurrentViewModel))
        {
            // CORREÇÃO 3.1: Proteção contra crashes durante transição
            try
            {
                ApplyWindowSizing(_viewModel?.CurrentViewModel);
            }
            catch (Exception ex)
            {
                OpsLogger.WriteError("OnViewModelPropertyChanged: erro ao aplicar sizing", ex);

                // ESTADO SEGURO: maximizar sem animação
                try
                {
                    if (_viewModel?.CurrentViewModel is PainelViewModel)
                    {
                        WindowState = WindowState.Maximized;
                        _viewModel.SetDisplayedViewModel(_viewModel.CurrentViewModel);
                        EndPainelTransition("error_recovery");
                    }
                }
                catch
                {
                    // Último recurso: não crashar
                }
            }
        }
    }

    private void ApplyWindowSizing(ViewModelBase? viewModel)
    {
        LogWindowMetrics("sizing_before");

        if (viewModel is PainelViewModel painelVm)
        {
            OpsLogger.WriteInfo("ApplyWindowSizing: opening Painel window");
            OpenPainelWindow(painelVm);
            return;
        }

        OpsLogger.WriteInfo("ApplyWindowSizing: switching to Login mode");
        _isPainelActive = false;
        _painelTransitionCts?.Cancel();
        _painelTransitionCts = null;
        EndPainelTransition("back_to_login");

        // Definir tamanho
        SizeToContent = SizeToContent.Manual;
        Width = 460;
        Height = 580;
        MinWidth = 460;
        MinHeight = 580;
        CanResize = false;
        WindowState = WindowState.Normal;

        LogWindowMetrics("sizing_after");
        if (viewModel is not null)
        {
            _viewModel?.SetDisplayedViewModel(viewModel);
        }

        ClosePainelWindowForLogout();
        ShowLoginWindow();

        // Se já foi posicionada inicialmente, aplicar chrome sem decoração
        if (_initialPositionSet)
        {
            ApplyWindowChrome(useSystemChrome: false);
        }
    }

    private void EnsureVisible(string reason)
    {
        if (!_isPainelActive)
            return;

        if (!IsVisible)
        {
            Show();
        }

        Activate();
        LogPainelTransition(reason);
    }

    private void ShowLoginWindow()
    {
        if (!IsVisible)
        {
            Show();
        }

        WindowState = WindowState.Normal;
        Activate();

        // Só tentar recentralizar se ainda não foi feito
        // No Linux, o WindowStartupLocation=CenterScreen já funciona com BorderOnly
        if (!_loginRecenterScheduled)
        {
            ScheduleLoginRecenter();
        }
    }

    private void ScheduleLoginRecenter()
    {
        if (_loginRecenterScheduled || _isPureWaylandSession || _isPainelActive)
            return;

        _loginRecenterScheduled = true;

        // No Linux, usar posicionamento manual após janela estar pronta
        if (OperatingSystem.IsLinux())
        {
            ScheduleLinuxLoginCenter();
            return;
        }

        ScheduleLoginRecenterAfter(200, "login_recenter");
        ScheduleLoginRecenterAfter(1200, "login_recenter_late");
    }

    private void ScheduleLinuxLoginCenter()
    {
        DispatcherTimer.RunOnce(() =>
        {
            if (_isPainelActive) return;

            if (IsLoginApproximatelyCentered())
            {
                LogWindowMetrics("linux_already_centered");
                return;
            }

            ForceLoginCenterLinux("linux_center_force");
        }, TimeSpan.FromMilliseconds(200), DispatcherPriority.Render);
    }

    private bool IsLoginApproximatelyCentered()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return true; // Assume centered if no screen

        var workArea = screen.WorkingArea;
        if (workArea.Width <= 0 || workArea.Height <= 0)
            workArea = screen.Bounds;

        var targetX = (int)workArea.X + (int)((workArea.Width - Width) / 2);
        var targetY = (int)workArea.Y + (int)((workArea.Height - Height) / 2);

        var dx = Math.Abs(Position.X - targetX);
        var dy = Math.Abs(Position.Y - targetY);

        // Tolerância de 50px para considerar "centralizado"
        return dx <= 50 && dy <= 50;
    }

    private void ForceLoginCenterLinux(string phase)
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return;

        var workArea = screen.WorkingArea;
        if (workArea.Width <= 0 || workArea.Height <= 0)
            workArea = screen.Bounds;

        var targetX = (int)workArea.X + (int)((workArea.Width - Width) / 2);
        var targetY = (int)workArea.Y + (int)((workArea.Height - Height) / 2);

        Position = new PixelPoint(targetX, targetY);
        LogWindowMetrics(phase);
    }

    private void ScheduleLoginRecenterAfter(int delayMs, string phase)
    {
        DispatcherTimer.RunOnce(() =>
        {
            if (_isPainelActive)
                return;

            TryRecenterLogin(phase);
        }, TimeSpan.FromMilliseconds(delayMs), DispatcherPriority.Render);
    }

    private void TryRecenterLogin(string phase)
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null)
            return;

        var workArea = screen.WorkingArea;
        if (workArea.Width <= 0 || workArea.Height <= 0)
        {
            workArea = screen.Bounds;
        }

        var targetX = (int)workArea.X + (int)Math.Round((workArea.Width - Width) / 2);
        var targetY = (int)workArea.Y + (int)Math.Round((workArea.Height - Height) / 2);
        var dx = Math.Abs(Position.X - targetX);
        var dy = Math.Abs(Position.Y - targetY);

        if (dx > 8 || dy > 8)
        {
            Position = new PixelPoint(targetX, targetY);
            LogWindowMetrics(phase);
        }
    }

    private void OpenPainelWindow(PainelViewModel painelVm)
    {
        LogWindowMetrics("painel_wait_fullscreen");

        var isLinux = OperatingSystem.IsLinux();

        // Obter dimensões da tela ANTES de criar a janela
        var screen = Screens.Primary;
        var screenBounds = screen?.Bounds ?? new PixelRect(0, 0, 1920, 1080);

        if (_painelWindow is not null)
        {
            _painelWindow.Content = painelVm;

            if (isLinux)
            {
                // Forçar fullscreen real no Linux
                _painelWindow.Position = new PixelPoint(0, 0);
                _painelWindow.Width = screenBounds.Width;
                _painelWindow.Height = screenBounds.Height;
                _painelWindow.WindowState = WindowState.FullScreen;
            }

            _painelWindow.Show();
            _painelWindow.Activate();
            DispatcherHelper.PostAsyncSafe(
                () => EnsurePainelFullscreenAsync(_painelWindow),
                "mainwindow_painel_fullscreen_falha",
                DispatcherPriority.Render);
            Hide();
            return;
        }

        _isPainelActive = true;
        var painelWindow = new Window
        {
            Title = "Protons - Painel",
            WindowStartupLocation = WindowStartupLocation.Manual,
            SystemDecorations = isLinux ? SystemDecorations.None : SystemDecorations.Full,
            ExtendClientAreaToDecorationsHint = isLinux,
            ExtendClientAreaChromeHints = isLinux ? ExtendClientAreaChromeHints.NoChrome : ExtendClientAreaChromeHints.Default,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.None },
            Background = new SolidColorBrush(Color.Parse("#0B1118")),
            SizeToContent = SizeToContent.Manual,
            CanResize = !isLinux,
            Content = painelVm
        };

        // No Linux: definir posição e tamanho ANTES do Show()
        if (isLinux)
        {
            painelWindow.Position = new PixelPoint(0, 0);
            painelWindow.Width = screenBounds.Width;
            painelWindow.Height = screenBounds.Height;
        }

        painelWindow.Closed += OnPainelWindowClosed;
        painelWindow.Opened += (_, __) =>
        {
            DispatcherHelper.PostAsyncSafe(
                () => EnsurePainelFullscreenAsync(painelWindow),
                "mainwindow_painel_fullscreen_falha",
                DispatcherPriority.Render);
        };

        _painelWindow = painelWindow;
        painelWindow.WindowState = isLinux ? WindowState.FullScreen : WindowState.Maximized;
        painelWindow.Show();
        painelWindow.Activate();
        Hide();
    }

    private async System.Threading.Tasks.Task EnsurePainelFullscreenAsync(Window painelWindow)
    {
        var screen = painelWindow.Screens.ScreenFromWindow(painelWindow) ?? painelWindow.Screens.Primary;
        if (screen is null)
        {
            LogPainelWindowMetrics(painelWindow, "painel_shown");
            return;
        }

        var isLinux = OperatingSystem.IsLinux();
        var boundsTarget = screen.Bounds;
        var workArea = screen.WorkingArea;
        if (workArea.Width <= 0 || workArea.Height <= 0)
        {
            workArea = boundsTarget;
        }

        if (isLinux)
        {
            // Forçar fullscreen real no Wayland/XWayland
            // 1. Garantir decorações removidas
            painelWindow.SystemDecorations = SystemDecorations.None;
            painelWindow.ExtendClientAreaToDecorationsHint = true;
            painelWindow.ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome;

            // 2. Forçar posição (0,0) - CRÍTICO para cobrir barra do sistema
            painelWindow.Position = new PixelPoint(0, 0);

            // 3. Forçar tamanho exato da tela (usando Bounds, não WorkArea)
            painelWindow.Width = boundsTarget.Width;
            painelWindow.Height = boundsTarget.Height;

            // 4. Definir WindowState após dimensões
            painelWindow.WindowState = WindowState.FullScreen;

            await System.Threading.Tasks.Task.Delay(80);

            // 5. Verificar e reforçar se necessário
            if (!IsWindowFullscreenReal(painelWindow, boundsTarget))
            {
                painelWindow.Position = new PixelPoint(0, 0);
                painelWindow.Width = boundsTarget.Width;
                painelWindow.Height = boundsTarget.Height;
                painelWindow.WindowState = WindowState.FullScreen;
                await System.Threading.Tasks.Task.Delay(80);
                LogPainelWindowMetrics(painelWindow, "painel_fullscreen_forced");
            }

            LogPainelWindowMetrics(painelWindow, "painel_shown");
            return;
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            painelWindow.WindowState = WindowState.Maximized;
            await System.Threading.Tasks.Task.Delay(120);
            if (IsWindowFullscreenLike(painelWindow, workArea))
                break;
        }

        if (!IsWindowFullscreenLike(painelWindow, workArea))
        {
            LogPainelWindowMetrics(painelWindow, "painel_fullscreen_forced");
        }

        LogPainelWindowMetrics(painelWindow, "painel_shown");
    }

    private static bool IsWindowFullscreenReal(Window window, PixelRect screenBounds)
    {
        // Verificar se a janela cobre toda a tela (posição + tamanho)
        var posOk = window.Position.X <= 2 && window.Position.Y <= 2;
        var widthOk = Math.Abs(window.Bounds.Width - screenBounds.Width) <= 4;
        var heightOk = Math.Abs(window.Bounds.Height - screenBounds.Height) <= 4;
        return posOk && widthOk && heightOk;
    }

    private static bool IsWindowFullscreenLike(Window window, PixelRect workArea)
    {
        var widthOk = Math.Abs(window.Bounds.Width - workArea.Width) <= 2;
        var heightOk = Math.Abs(window.Bounds.Height - workArea.Height) <= 2;
        return widthOk && heightOk;
    }

    private void ClosePainelWindowForLogout()
    {
        if (_painelWindow is null)
            return;

        _closingPainelForLogout = true;
        _painelWindow.Close();
    }

    private void OnPainelWindowClosed(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, _painelWindow))
            return;

        _painelWindow = null;
        if (_closingPainelForLogout)
        {
            _closingPainelForLogout = false;
            return;
        }

        Close();
    }

    private void LogPainelWindowMetrics(Window window, string phase)
    {
        if (!_diagEnabled)
            return;

        var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
        var workArea = screen?.WorkingArea;
        var screenBounds = screen?.Bounds;
        var scale = screen?.Scaling;
        var transparency = window.TransparencyLevelHint is null
            ? "none"
            : string.Join(",", window.TransparencyLevelHint);

        OpsLogger.WriteInfo(
            "window_diag: " +
            $"phase={phase} " +
            $"pos=({window.Position.X},{window.Position.Y}) " +
            $"bounds=({window.Bounds.X:0.##},{window.Bounds.Y:0.##},{window.Bounds.Width:0.##},{window.Bounds.Height:0.##}) " +
            $"client=({window.ClientSize.Width:0.##},{window.ClientSize.Height:0.##}) " +
            $"state={window.WindowState} " +
            $"deco={window.SystemDecorations} " +
            $"extend={window.ExtendClientAreaToDecorationsHint} " +
            $"chrome={window.ExtendClientAreaChromeHints} " +
            $"transparency={transparency} " +
            $"workarea=({workArea?.X},{workArea?.Y},{workArea?.Width},{workArea?.Height}) " +
            $"screen=({screenBounds?.X},{screenBounds?.Y},{screenBounds?.Width},{screenBounds?.Height}) " +
            $"scale={scale:0.##}");
    }

    private void BeginPainelTransition(PainelViewModel painelVm)
    {
        _painelTransitionCts?.Cancel();
        _painelTransitionCts = new CancellationTokenSource();
        _painelTransitionActive = true;

        LogPainelTransition("painel_switch");
        ApplyWindowChrome(useSystemChrome: true);
        LogWindowMetrics("painel_wait_fullscreen");
        ForcePainelFullscreen("painel_apply");
        LogPainelTransition("painel_wait_fullscreen");

        _ = CompletePainelTransitionAsync(painelVm, _painelTransitionCts.Token);
    }

    private void EndPainelTransition(string reason)
    {
        if (!_painelTransitionActive)
            return;

        _painelTransitionActive = false;
        // CORREÇÃO 1.1: Remover restauração de opacity
        LogPainelTransition(reason);
    }

    private void ForcePainelFullscreen(string reason)
    {
        if (!_isPainelActive)
            return;

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        // CORREÇÃO 1.3: Ir direto para Maximized (melhor compatibilidade que FullScreen)
        WindowState = WindowState.Maximized;
        EnsureVisible(reason);
    }

    private bool IsPainelFullscreen()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null)
            return false;

        var workArea = screen.WorkingArea;
        if (workArea.Width <= 0 || workArea.Height <= 0)
        {
            workArea = screen.Bounds;
        }

        if (Bounds.Width <= 0 || Bounds.Height <= 0)
            return false;

        return Bounds.Width >= workArea.Width - 2
               && Bounds.Height >= workArea.Height - 2;
    }

    private void ApplyFullscreenBoundsFallback(string reason)
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null)
            return;

        var workArea = screen.WorkingArea;
        if (workArea.Width <= 0 || workArea.Height <= 0)
        {
            workArea = screen.Bounds;
        }

        WindowState = WindowState.Normal;
        Width = workArea.Width;
        Height = workArea.Height;

        if (!_isPureWaylandSession && !_manualPositioningDisabled)
        {
            Position = new PixelPoint((int)workArea.X, (int)workArea.Y);
        }

        EnsureVisible(reason);
    }

    private async System.Threading.Tasks.Task CompletePainelTransitionAsync(
        PainelViewModel painelVm,
        CancellationToken token)
    {
        try
        {
            await WaitForFullscreenAsync(token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested)
            return;

        if (_viewModel?.CurrentViewModel is not PainelViewModel)
        {
            EndPainelTransition("painel_transition_aborted");
            return;
        }

        if (!IsPainelFullscreen())
        {
            ApplyFullscreenBoundsFallback("painel_fallback");
            try
            {
                await System.Threading.Tasks.Task.Delay(50, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        if (token.IsCancellationRequested)
            return;

        _viewModel?.SetDisplayedViewModel(painelVm);
        LogWindowMetrics("painel_shown");
        LogPainelTransition("painel_shown");
        EndPainelTransition("painel_ready");
    }

    private async System.Threading.Tasks.Task WaitForFullscreenAsync(CancellationToken token)
    {
        var start = Environment.TickCount64;
        while (!token.IsCancellationRequested && Environment.TickCount64 - start < 1200)
        {
            if (IsPainelFullscreen())
                return;

            await System.Threading.Tasks.Task.Delay(50, token);
        }
    }

    private void RequestCenterOnScreen(WindowMode mode, string reason)
    {
        if (_isPureWaylandSession || _manualPositioningDisabled)
        {
            if (_diagEnabled)
            {
                var reasonTag = _manualPositioningDisabled ? "manual_disabled" : "wayland";
                OpsLogger.WriteInfo($"CenterOnScreen: skip ({reasonTag}) reason={reason}");
            }
            return;
        }

        if (_lastCenteredMode == mode)
        {
            if (_diagEnabled)
            {
                OpsLogger.WriteInfo($"CenterOnScreen: skip (already centered) mode={mode} reason={reason}");
            }
            return;
        }

        var requestId = Interlocked.Increment(ref _centerRequestId);
        CenterOnScreen(requestId);
        _lastCenteredMode = mode;
    }

    private void CenterOnScreen(int requestId)
    {
        DispatcherHelper.PostAsyncSafe(async () =>
        {
            if (requestId != _centerRequestId)
                return;

            await System.Threading.Tasks.Task.Delay(50);

            if (requestId != _centerRequestId)
                return;

            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            if (screen is null)
            {
                OpsLogger.WriteWarning("CenterOnScreen: no screen available");
                return;
            }

            var workArea = screen.WorkingArea;
            if (workArea.Width <= 0 || workArea.Height <= 0)
            {
                workArea = screen.Bounds;
            }

            var windowWidth = Math.Max(Bounds.Width, ClientSize.Width);
            var windowHeight = Math.Max(Bounds.Height, ClientSize.Height);
            if (windowWidth <= 0 || double.IsNaN(windowWidth))
            {
                windowWidth = double.IsNaN(Width) ? (_isPainelActive ? 1280 : 460) : Width;
            }
            if (windowHeight <= 0 || double.IsNaN(windowHeight))
            {
                windowHeight = double.IsNaN(Height) ? (_isPainelActive ? 720 : 580) : Height;
            }

            LogWindowMetrics("center_calc");

            var x = (int)workArea.X + (int)Math.Round(((double)workArea.Width - windowWidth) / 2);
            var y = (int)workArea.Y + (int)Math.Round(((double)workArea.Height - windowHeight) / 2);

            var minX = (int)workArea.X;
            var minY = (int)workArea.Y;
            var maxX = (int)(workArea.X + workArea.Width - windowWidth);
            var maxY = (int)(workArea.Y + workArea.Height - windowHeight);
            if (windowWidth > workArea.Width)
            {
                x = minX;
            }
            else
            {
                x = Math.Clamp(x, minX, maxX);
            }

            if (windowHeight > workArea.Height)
            {
                y = minY;
            }
            else
            {
                y = Math.Clamp(y, minY, maxY);
            }

            OpsLogger.WriteInfo($"CenterOnScreen: target=({x},{y}) for {windowWidth:0.##}x{windowHeight:0.##}");

            Position = new PixelPoint(x, y);
            await System.Threading.Tasks.Task.Delay(30);

            var actual = Position;
            var dx = x - actual.X;
            var dy = y - actual.Y;
            if (Math.Abs(dx) > 1 || Math.Abs(dy) > 1)
            {
                var corrected = new PixelPoint(x + dx, y + dy);
                Position = corrected;
                if (_diagEnabled)
                {
                    OpsLogger.WriteInfo($"CenterOnScreen: delta applied dx={dx} dy={dy} corrected=({corrected.X},{corrected.Y})");
                }
                await System.Threading.Tasks.Task.Delay(20);
            }

            var finalPos = Position;
            var finalDx = x - finalPos.X;
            var finalDy = y - finalPos.Y;
            if (Math.Abs(finalDx) > 8 || Math.Abs(finalDy) > 8)
            {
                _manualPositioningDisabled = true;
                if (_diagEnabled)
                {
                    OpsLogger.WriteInfo($"CenterOnScreen: manual disabled (wm override) dx={finalDx} dy={finalDy}");
                }
            }

            OpsLogger.WriteInfo($"CenterOnScreen: actual=({finalPos.X},{finalPos.Y})");
            LogWindowMetrics("center_after_set");
        }, "mainwindow_center_falha", DispatcherPriority.Render);
    }

    private void ApplyWindowChrome(bool useSystemChrome)
    {
        if (useSystemChrome)
        {
            // Painel: usa decorações do sistema
            SystemDecorations = SystemDecorations.Full;
            ExtendClientAreaToDecorationsHint = false;
            ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.Default;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.None };
            Background = new SolidColorBrush(Color.Parse("#0B1118"));
            if (_dragRegion is not null) _dragRegion.IsVisible = false;
            if (_closeButton is not null) _closeButton.IsVisible = false;
            return;
        }

        // Login: usar chrome que o WM respeita no Linux para centralizar corretamente
        if (OperatingSystem.IsLinux())
        {
            SystemDecorations = SystemDecorations.BorderOnly;
            ExtendClientAreaToDecorationsHint = false;
            ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.Default;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.None };
            Background = new SolidColorBrush(Color.Parse("#0B1118"));
            if (_dragRegion is not null) _dragRegion.IsVisible = true;
            if (_closeButton is not null) _closeButton.IsVisible = true;
            return;
        }

        // Login: sem decorações, com transparência (Windows/macOS)
        SystemDecorations = SystemDecorations.None;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        if (_dragRegion is not null) _dragRegion.IsVisible = true;
        if (_closeButton is not null) _closeButton.IsVisible = true;
    }

    private static bool IsWindowDiagEnabled()
    {
        var value = Environment.GetEnvironmentVariable("PROTONS_WINDOW_DIAG");
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
    }

    private async void ScheduleRecheckOnce()
    {
        if (_isPureWaylandSession || _manualPositioningDisabled || _recheckScheduled)
            return;

        _recheckScheduled = true;
        var requestId = _centerRequestId;
        await System.Threading.Tasks.Task.Delay(700);
        if (requestId != _centerRequestId)
            return;

        CenterOnScreen(requestId);
    }

    private static bool IsPureWaylandSession()
    {
        var sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
        var isWaylandSession = string.Equals(sessionType, "wayland", StringComparison.OrdinalIgnoreCase);
        var waylandDisplay = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
        var isWaylandDisplay = !string.IsNullOrWhiteSpace(waylandDisplay);
        var display = Environment.GetEnvironmentVariable("DISPLAY");
        var isXWayland = !string.IsNullOrWhiteSpace(display);
        return (isWaylandSession || isWaylandDisplay) && !isXWayland;
    }

    private void LogWindowMetricsThrottled(string phase)
    {
        if (!_diagEnabled)
            return;

        var now = Environment.TickCount64;
        if (now - _lastDiagTick < 250)
            return;

        _lastDiagTick = now;
        LogWindowMetrics(phase);
    }

    private void LogWindowMetrics(string phase)
    {
        if (!_diagEnabled)
            return;

        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        var workArea = screen?.WorkingArea;
        var screenBounds = screen?.Bounds;
        var scale = screen?.Scaling;
        var transparency = TransparencyLevelHint is null
            ? "none"
            : string.Join(",", TransparencyLevelHint);

        OpsLogger.WriteInfo(
            "window_diag: " +
            $"phase={phase} " +
            $"pos=({Position.X},{Position.Y}) " +
            $"bounds=({Bounds.X:0.##},{Bounds.Y:0.##},{Bounds.Width:0.##},{Bounds.Height:0.##}) " +
            $"client=({ClientSize.Width:0.##},{ClientSize.Height:0.##}) " +
            $"state={WindowState} " +
            $"deco={SystemDecorations} " +
            $"extend={ExtendClientAreaToDecorationsHint} " +
            $"chrome={ExtendClientAreaChromeHints} " +
            $"transparency={transparency} " +
            $"workarea=({workArea?.X},{workArea?.Y},{workArea?.Width},{workArea?.Height}) " +
            $"screen=({screenBounds?.X},{screenBounds?.Y},{screenBounds?.Width},{screenBounds?.Height}) " +
            $"scale={scale:0.##}");
    }

    private void LogPainelTransition(string phase)
    {
        if (!_diagEnabled)
            return;

        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        var workArea = screen?.WorkingArea;
        var screenBounds = screen?.Bounds;
        var scale = screen?.Scaling;

        OpsLogger.WriteInfo(
            "painel_transition: " +
            $"phase={phase} " +
            $"state={WindowState} " +
            $"visible={IsVisible} " +
            $"pos=({Position.X},{Position.Y}) " +
            $"bounds=({Bounds.X:0.##},{Bounds.Y:0.##},{Bounds.Width:0.##},{Bounds.Height:0.##}) " +
            $"client=({ClientSize.Width:0.##},{ClientSize.Height:0.##}) " +
            $"workarea=({workArea?.X},{workArea?.Y},{workArea?.Width},{workArea?.Height}) " +
            $"screen=({screenBounds?.X},{screenBounds?.Y},{screenBounds?.Width},{screenBounds?.Height}) " +
            $"scale={scale:0.##}");
    }
}
