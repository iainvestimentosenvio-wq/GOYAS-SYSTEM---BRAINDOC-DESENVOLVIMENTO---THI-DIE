#!/usr/bin/env python3
"""
Gera os 4 assets visuais do instalador Inno Setup (Protons).

Saidas:
  windows/ativos/installer/wizard_back_light.png      140x459
  windows/ativos/installer/wizard_back_dark.png       140x459
  windows/ativos/installer/wizard_small_logo_light.png  55x55
  windows/ativos/installer/wizard_small_logo_dark.png   55x55

Uso:
  python3 windows/scripts/make-ux-assets.py

Requer:
  pip install Pillow
"""

import sys
import os
from pathlib import Path

try:
    from PIL import Image, ImageEnhance
except ImportError:
    print("ERRO: Pillow nao instalado. Execute: pip install Pillow", file=sys.stderr)
    sys.exit(1)

# ---------------------------------------------------------------------------
# Caminhos
# ---------------------------------------------------------------------------
SCRIPT_DIR = Path(__file__).resolve().parent
# windows/scripts -> windows -> INSTALADOR
INSTALLER_DIR = SCRIPT_DIR.parent.parent
# INSTALADOR -> PROJETO PROTONS
PROJECT_ROOT = INSTALLER_DIR.parent

LOGIN_BG   = PROJECT_ROOT / "Login" / "Protons.UI" / "Assets" / "Brand" / "login_bg.png"
LOGIN_LOGO = PROJECT_ROOT / "Login" / "Protons.UI" / "Assets" / "Brand" / "logo_protons.png"
OUTPUT_DIR = INSTALLER_DIR / "windows" / "ativos" / "installer"

BACK_W, BACK_H   = 140, 459
LOGO_W, LOGO_H   = 55, 55

# Overlays dark (RGBA)
DARK_OVERLAY_1 = (12, 16, 25, 106)   # azul escuro semi-opaco
DARK_OVERLAY_2 = (8, 12, 18, 32)     # segunda camada de profundidade

# Ajuste de brilho para logo dark (equivale ao ColorMatrix do PS1)
LOGO_DARK_GAIN   = 1.12
LOGO_DARK_OFFSET = int(0.05 * 255)   # ≈ 12.75 -> 12


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def assert_exists(path: Path, label: str) -> None:
    if not path.exists():
        print(f"ERRO: {label} nao encontrado: {path}", file=sys.stderr)
        sys.exit(1)


def cover_crop(src: Image.Image, target_w: int, target_h: int) -> Image.Image:
    """Cover-crop centralizado: escala para cobrir target e corta o excesso."""
    src_w, src_h = src.size
    target_ratio  = target_w / target_h
    source_ratio  = src_w / src_h

    if source_ratio > target_ratio:
        # fonte mais larga — escala pela altura, corta largura
        scale     = target_h / src_h
        new_w     = round(src_w * scale)
        new_h     = target_h
        resized   = src.resize((new_w, new_h), Image.LANCZOS)
        left      = (new_w - target_w) // 2
        box       = (left, 0, left + target_w, target_h)
    else:
        # fonte mais alta — escala pela largura, corta altura
        scale     = target_w / src_w
        new_w     = target_w
        new_h     = round(src_h * scale)
        resized   = src.resize((new_w, new_h), Image.LANCZOS)
        top       = (new_h - target_h) // 2
        box       = (0, top, target_w, top + target_h)

    return resized.crop(box).convert("RGBA")


def contain_fit(src: Image.Image, target_w: int, target_h: int) -> Image.Image:
    """Contain-fit: redimensiona sem distorcer, centraliza em canvas transparente."""
    src_w, src_h = src.size
    scale    = min(target_w / src_w, target_h / src_h)
    draw_w   = max(1, round(src_w * scale))
    draw_h   = max(1, round(src_h * scale))
    offset_x = (target_w - draw_w) // 2
    offset_y = (target_h - draw_h) // 2

    canvas = Image.new("RGBA", (target_w, target_h), (0, 0, 0, 0))
    resized = src.resize((draw_w, draw_h), Image.LANCZOS).convert("RGBA")
    canvas.paste(resized, (offset_x, offset_y), resized)
    return canvas


def add_dark_overlay(img: Image.Image, rgba: tuple) -> Image.Image:
    """Aplica overlay semi-transparente sobre img (RGBA)."""
    overlay = Image.new("RGBA", img.size, rgba)
    return Image.alpha_composite(img, overlay)


def brighten_logo(img: Image.Image, gain: float, offset: int) -> Image.Image:
    """
    Ajusta brilho/contraste pixel a pixel:
        out = clamp(in * gain + offset, 0, 255)
    Preserva canal alpha intacto.
    """
    r, g, b, a = img.split()

    def adjust_channel(ch):
        lut = [min(255, max(0, round(i * gain + offset))) for i in range(256)]
        return ch.point(lut)

    r = adjust_channel(r)
    g = adjust_channel(g)
    b = adjust_channel(b)
    return Image.merge("RGBA", (r, g, b, a))


def save_png(img: Image.Image, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    img.save(str(path), "PNG")
    size_kb = path.stat().st_size // 1024
    w, h    = img.size
    print(f"  OK  {path.name:<40} {w}x{h}  ({size_kb} KB)")


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

def main() -> None:
    print("Protons Installer UX Assets — gerador Python/Pillow")
    print(f"  login_bg  : {LOGIN_BG}")
    print(f"  logo      : {LOGIN_LOGO}")
    print(f"  saida     : {OUTPUT_DIR}")
    print()

    assert_exists(LOGIN_BG,   "Imagem de fundo do login (login_bg.png)")
    assert_exists(LOGIN_LOGO, "Logo do login (logo_protons.png)")

    # --- Carregar fontes ---
    bg_src   = Image.open(str(LOGIN_BG)).convert("RGBA")
    logo_src = Image.open(str(LOGIN_LOGO)).convert("RGBA")

    # --- wizard_back_light (cover-crop puro) ---
    back_light = cover_crop(bg_src, BACK_W, BACK_H)

    # --- wizard_back_dark (cover-crop + 2 overlays escuros) ---
    back_dark = cover_crop(bg_src, BACK_W, BACK_H)
    back_dark = add_dark_overlay(back_dark, DARK_OVERLAY_1)
    back_dark = add_dark_overlay(back_dark, DARK_OVERLAY_2)

    # --- wizard_small_logo_light (contain-fit, fundo transparente) ---
    logo_light = contain_fit(logo_src, LOGO_W, LOGO_H)

    # --- wizard_small_logo_dark (contain-fit + ajuste de brilho) ---
    logo_dark_base = contain_fit(logo_src, LOGO_W, LOGO_H)
    logo_dark      = brighten_logo(logo_dark_base, LOGO_DARK_GAIN, LOGO_DARK_OFFSET)

    # --- Salvar ---
    save_png(back_light, OUTPUT_DIR / "wizard_back_light.png")
    save_png(back_dark,  OUTPUT_DIR / "wizard_back_dark.png")
    save_png(logo_light, OUTPUT_DIR / "wizard_small_logo_light.png")
    save_png(logo_dark,  OUTPUT_DIR / "wizard_small_logo_dark.png")

    # --- Validar dimensoes ---
    errors = []
    for name, expected_w, expected_h in [
        ("wizard_back_light.png",      BACK_W, BACK_H),
        ("wizard_back_dark.png",       BACK_W, BACK_H),
        ("wizard_small_logo_light.png", LOGO_W, LOGO_H),
        ("wizard_small_logo_dark.png",  LOGO_W, LOGO_H),
    ]:
        path = OUTPUT_DIR / name
        if not path.exists():
            errors.append(f"AUSENTE: {name}")
            continue
        w, h = Image.open(str(path)).size
        if (w, h) != (expected_w, expected_h):
            errors.append(f"DIMENSAO ERRADA: {name} -> {w}x{h} (esperado {expected_w}x{expected_h})")

    if errors:
        print("\nERROS DE VALIDACAO:", file=sys.stderr)
        for e in errors:
            print(f"  {e}", file=sys.stderr)
        sys.exit(1)

    print("\nTodos os assets gerados e validados com sucesso.")


if __name__ == "__main__":
    main()
