#!/usr/bin/env python3
"""
Prepara o asset BOTAO_ANCORA_PDF para uso em runtime:
- copia fonte para Assets/UI/Buttons/ancora_pdf_source.png
- remove fundo conectado a borda com alpha suave
- recorta area util com padding
- exporta tamanhos 128/192/256
- gera estados: normal/hover/pressed/focus
"""

from __future__ import annotations

from pathlib import Path
from typing import Iterable, Tuple
import shutil
import sys

import gi
import numpy as np

gi.require_version("GdkPixbuf", "2.0")
from gi.repository import GdkPixbuf, GLib  # type: ignore


def pixbuf_to_np_rgba(pixbuf: GdkPixbuf.Pixbuf) -> np.ndarray:
    width = pixbuf.get_width()
    height = pixbuf.get_height()
    channels = pixbuf.get_n_channels()
    rowstride = pixbuf.get_rowstride()
    arr = np.frombuffer(pixbuf.get_pixels(), dtype=np.uint8)
    arr = arr.reshape((height, rowstride))[:, : width * channels]
    arr = arr.reshape((height, width, channels))
    if channels == 4:
        return arr.copy()

    # Promove RGB para RGBA opaco.
    rgba = np.empty((height, width, 4), dtype=np.uint8)
    rgba[:, :, :3] = arr[:, :, :3]
    rgba[:, :, 3] = 255
    return rgba


def np_rgba_to_pixbuf(arr: np.ndarray) -> GdkPixbuf.Pixbuf:
    height, width, channels = arr.shape
    if channels != 4:
        raise ValueError("Esperado RGBA com 4 canais.")
    rowstride = width * channels
    data = GLib.Bytes.new(arr.tobytes())
    return GdkPixbuf.Pixbuf.new_from_bytes(
        data,
        GdkPixbuf.Colorspace.RGB,
        True,
        8,
        width,
        height,
        rowstride,
    )


def blend_over(base_rgba: np.ndarray, overlay_rgb: Tuple[int, int, int], overlay_alpha: np.ndarray) -> np.ndarray:
    out = base_rgba.copy().astype(np.float32)
    alpha = np.clip(overlay_alpha.astype(np.float32), 0.0, 1.0)[:, :, None]
    base_rgb = out[:, :, :3]
    base_a = out[:, :, 3:4] / 255.0

    # Alpha efetivo respeita transparencia da imagem base.
    eff = alpha * base_a
    base_rgb = overlay_rgb * eff + base_rgb * (1.0 - eff)
    out[:, :, :3] = base_rgb
    return np.clip(out, 0, 255).astype(np.uint8)


def dilate(mask: np.ndarray, radius: int) -> np.ndarray:
    if radius <= 0:
        return mask.copy()
    h, w = mask.shape
    out = np.zeros((h, w), dtype=bool)
    for dy in range(-radius, radius + 1):
        y_src_start = max(0, -dy)
        y_src_end = min(h, h - dy)
        y_dst_start = max(0, dy)
        y_dst_end = min(h, h + dy)
        if y_src_start >= y_src_end or y_dst_start >= y_dst_end:
            continue
        for dx in range(-radius, radius + 1):
            x_src_start = max(0, -dx)
            x_src_end = min(w, w - dx)
            x_dst_start = max(0, dx)
            x_dst_end = min(w, w + dx)
            if x_src_start >= x_src_end or x_dst_start >= x_dst_end:
                continue
            out[y_dst_start:y_dst_end, x_dst_start:x_dst_end] |= mask[y_src_start:y_src_end, x_src_start:x_src_end]
    return out


def flood_fill_background(similar_bg: np.ndarray) -> np.ndarray:
    h, w = similar_bg.shape
    visited = np.zeros((h, w), dtype=bool)
    stack: list[Tuple[int, int]] = []

    for x in range(w):
        if similar_bg[0, x]:
            stack.append((0, x))
        if similar_bg[h - 1, x]:
            stack.append((h - 1, x))
    for y in range(h):
        if similar_bg[y, 0]:
            stack.append((y, 0))
        if similar_bg[y, w - 1]:
            stack.append((y, w - 1))

    while stack:
        y, x = stack.pop()
        if visited[y, x] or not similar_bg[y, x]:
            continue
        visited[y, x] = True
        if y > 0:
            stack.append((y - 1, x))
        if y < h - 1:
            stack.append((y + 1, x))
        if x > 0:
            stack.append((y, x - 1))
        if x < w - 1:
            stack.append((y, x + 1))

    return visited


def soft_alpha_from_distance(dist: np.ndarray, low: float, high: float) -> np.ndarray:
    if high <= low:
        raise ValueError("high deve ser maior que low")
    alpha = (dist - low) / (high - low)
    return np.clip(alpha, 0.0, 1.0)


def remove_background_and_crop(src_rgba: np.ndarray) -> np.ndarray:
    rgb = src_rgba[:, :, :3].astype(np.float32)
    h, w, _ = rgb.shape

    corners = np.array(
        [
            rgb[0, 0],
            rgb[0, w - 1],
            rgb[h - 1, 0],
            rgb[h - 1, w - 1],
        ],
        dtype=np.float32,
    )
    bg = np.mean(corners, axis=0)
    dist = np.linalg.norm(rgb - bg[None, None, :], axis=2)

    similar_bg = dist <= 34.0
    bg_connected = flood_fill_background(similar_bg)

    base_alpha = src_rgba[:, :, 3].astype(np.float32) / 255.0
    soft = soft_alpha_from_distance(dist, low=22.0, high=78.0)

    # Fundo conectado fica transparente; interior preserva opacidade original.
    out_alpha = np.where(bg_connected, soft, base_alpha)
    out_alpha = np.clip(out_alpha, 0.0, 1.0)

    # Mantem apenas a area util principal (remove ruidos soltos).
    fg_mask = out_alpha > 0.06
    ys, xs = np.where(fg_mask)
    if ys.size == 0 or xs.size == 0:
        raise RuntimeError("Nao foi possivel detectar area util do botao.")

    x0 = max(int(xs.min()) - 36, 0)
    y0 = max(int(ys.min()) - 36, 0)
    x1 = min(int(xs.max()) + 36, w - 1)
    y1 = min(int(ys.max()) + 36, h - 1)

    cropped = src_rgba[y0 : y1 + 1, x0 : x1 + 1].copy()
    alpha_crop = (out_alpha[y0 : y1 + 1, x0 : x1 + 1] * 255.0).astype(np.uint8)
    cropped[:, :, 3] = alpha_crop

    # Remove pixels quase transparentes para evitar halo.
    cropped[:, :, 3] = np.where(cropped[:, :, 3] < 7, 0, cropped[:, :, 3]).astype(np.uint8)
    return cropped


def center_fit_to_square(src_rgba: np.ndarray, target: int) -> np.ndarray:
    pixbuf = np_rgba_to_pixbuf(src_rgba)
    h, w = src_rgba.shape[:2]
    scale = min((target * 0.84) / max(w, 1), (target * 0.84) / max(h, 1))
    out_w = max(1, int(round(w * scale)))
    out_h = max(1, int(round(h * scale)))
    scaled = pixbuf.scale_simple(out_w, out_h, GdkPixbuf.InterpType.BILINEAR)
    if scaled is None:
        raise RuntimeError("Falha ao redimensionar asset.")
    scaled_rgba = pixbuf_to_np_rgba(scaled)

    out = np.zeros((target, target, 4), dtype=np.uint8)
    y0 = (target - out_h) // 2
    x0 = (target - out_w) // 2
    out[y0 : y0 + out_h, x0 : x0 + out_w] = scaled_rgba
    return out


def build_states(base_192: np.ndarray) -> dict[str, np.ndarray]:
    normal = base_192.copy()
    alpha = normal[:, :, 3].astype(np.float32) / 255.0

    # Hover: realce azul com glow leve.
    hover_overlay = np.clip(alpha * 0.28, 0.0, 1.0)
    hover = blend_over(normal, (62, 152, 255), hover_overlay)

    # Pressed: ligeiramente mais escuro.
    pressed = normal.copy().astype(np.float32)
    pressed[:, :, :3] *= 0.9
    pressed = np.clip(pressed, 0, 255).astype(np.uint8)

    # Focus: anel ciano externo para navegação por teclado.
    focus = normal.copy()
    mask = alpha > 0.08
    ring_outer = dilate(mask, 9)
    ring_inner = dilate(mask, 5)
    ring = ring_outer & ~ring_inner
    glow_outer = dilate(mask, 14) & ~ring_outer

    focus = blend_over(focus, (98, 224, 255), ring.astype(np.float32) * 0.85)
    focus = blend_over(focus, (68, 186, 255), glow_outer.astype(np.float32) * 0.25)
    focus[:, :, 3] = np.maximum(focus[:, :, 3], (ring.astype(np.uint8) * 220))

    return {
        "normal": normal,
        "hover": hover,
        "pressed": pressed,
        "focus": focus,
    }


def save_png(arr: np.ndarray, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    pixbuf = np_rgba_to_pixbuf(arr)
    pixbuf.savev(str(path), "png", [], [])


def main(argv: Iterable[str]) -> int:
    args = list(argv)
    repo_login = Path(__file__).resolve().parents[1]
    repo_root = repo_login.parent

    default_source = repo_root / "painel principal" / "BOTAO_ANCORA_PDF"
    src = Path(args[1]).resolve() if len(args) > 1 else default_source
    out_dir = (
        Path(args[2]).resolve()
        if len(args) > 2
        else repo_login / "Protons.UI" / "Assets" / "UI" / "Buttons"
    )

    if not src.exists():
        print(f"ERRO: fonte nao encontrada: {src}", file=sys.stderr)
        return 1

    out_dir.mkdir(parents=True, exist_ok=True)
    source_copy = out_dir / "ancora_pdf_source.png"
    shutil.copy2(src, source_copy)
    print(f"[ok] fonte copiada: {source_copy}")

    pixbuf = GdkPixbuf.Pixbuf.new_from_file(str(src))
    src_rgba = pixbuf_to_np_rgba(pixbuf)
    clean = remove_background_and_crop(src_rgba)

    for size in (128, 192, 256):
        sized = center_fit_to_square(clean, size)
        save_png(sized, out_dir / f"ancora_pdf_{size}.png")
        print(f"[ok] gerado: ancora_pdf_{size}.png")

    base_192_path = out_dir / "ancora_pdf_192.png"
    base_192 = pixbuf_to_np_rgba(GdkPixbuf.Pixbuf.new_from_file(str(base_192_path)))
    states = build_states(base_192)
    for state_name, state_img in states.items():
        save_png(state_img, out_dir / f"ancora_pdf_{state_name}.png")
        print(f"[ok] gerado estado: ancora_pdf_{state_name}.png")

    print("[done] preparo de asset concluido.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
