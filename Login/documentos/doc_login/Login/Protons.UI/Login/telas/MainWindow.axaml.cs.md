# MainWindow.axaml.cs

## Objetivo
Gerenciar a janela de Login e abrir o Painel em **uma janela separada**, eliminando flicker causado por swap de chrome e resize tardio.

## Responsabilidades
- Manter o Login centralizado usando `WindowStartupLocation=CenterScreen` e **sem `Position` manual no Linux**.
- Aplicar chrome do Login por plataforma:
  - Linux: `SystemDecorations=BorderOnly` (WM respeita o centro).
  - Windows/macOS: chrome customizado (sem borda, transparente).
- Abrir o Painel em **outra janela**:
  - Linux: `SystemDecorations=None` + `WindowState=FullScreen` desde o inicio.
  - Windows: `SystemDecorations.Full` + `WindowState=Maximized`.
- Controlar o ciclo Login → Painel → Login (via `_navigate`).
- Registrar logs de diagnóstico (quando `PROTONS_WINDOW_DIAG=1`).

## Fluxo do Login
- A janela de Login nasce com `WindowStartupLocation=CenterScreen`.
- No Linux, **nao** tenta recenter manual (o WM ignora `Position`).
- `ApplyWindowChrome(useSystemChrome: false)` aplica o chrome adequado por plataforma.

## Fluxo do Painel (duas janelas)
Quando `CurrentViewModel` muda para `PainelViewModel`:
1. A janela de Login é **ocultada**.
2. Uma nova janela é criada:
   - Linux: `SystemDecorations = None` + `WindowState = FullScreen`
   - Windows: `SystemDecorations = Full` + `WindowState = Maximized`
   - `SizeToContent = Manual`
   - Reforco em `EnsurePainelFullscreenAsync` (Linux usa `screen.Bounds`)
3. O `Content` recebe o `PainelViewModel` (ViewLocator resolve a View).
4. Se o usuário fechar o Painel sem “Sair”, o app é encerrado.
5. Se o usuário clicar em “Sair”, o Painel é fechado e o Login reaparece.

## Diagnóstico
Ativar com:
```bash
PROTONS_WINDOW_DIAG=1 /usr/bin/protons
```

Logs relevantes:
- `window_diag: phase=painel_wait_fullscreen` (antes de abrir Painel)
- `window_diag: phase=painel_shown` (Painel exibido)
- `window_diag: phase=painel_fullscreen_forced` (Linux precisou forcar fullscreen)
- `app_start` / `app_env` (versão e ambiente)

## Causa raiz (documentado)
- Flicker era causado por **swap de chrome** + **resize tardio** numa única janela.
- Centralização falhava porque o WM ignorava `Position` manual (Wayland/XWayland).

## Prevenção
- Não reintroduzir posicionamento manual no Login em Linux.
- Manter Painel em janela separada.
- Para fullscreen no Linux: manter `SystemDecorations=None` + `WindowState=FullScreen`.
- Se precisar medir regressões: usar `PROTONS_WINDOW_DIAG=1` e comparar `window_diag`.
