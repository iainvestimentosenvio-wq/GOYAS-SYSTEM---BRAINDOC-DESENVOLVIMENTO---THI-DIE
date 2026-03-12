#!/usr/bin/env python3
"""
Valida assets da ancora PDF:
- existencia
- dimensoes
- alpha com transparencia real
- peso maximo recomendado
"""

from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
import sys

import gi
import numpy as np

gi.require_version("GdkPixbuf", "2.0")
from gi.repository import GdkPixbuf  # type: ignore


@dataclass(frozen=True)
class AssetRule:
    name: str
    width: int
    height: int
    require_transparency: bool = True
    max_bytes: int = 400_000


def load_rgba(path: Path) -> np.ndarray:
    pixbuf = GdkPixbuf.Pixbuf.new_from_file(str(path))
    w, h = pixbuf.get_width(), pixbuf.get_height()
    ch = pixbuf.get_n_channels()
    rs = pixbuf.get_rowstride()
    arr = np.frombuffer(pixbuf.get_pixels(), dtype=np.uint8).reshape((h, rs))[:, : w * ch].reshape((h, w, ch))
    if ch == 4:
        return arr
    rgba = np.empty((h, w, 4), dtype=np.uint8)
    rgba[:, :, :3] = arr[:, :, :3]
    rgba[:, :, 3] = 255
    return rgba


def check_rule(base: Path, rule: AssetRule) -> list[str]:
    problems: list[str] = []
    path = base / rule.name
    if not path.exists():
        return [f"[erro] arquivo ausente: {rule.name}"]

    stat = path.stat()
    if stat.st_size > rule.max_bytes:
        problems.append(f"[erro] {rule.name} acima do peso: {stat.st_size} > {rule.max_bytes} bytes")

    rgba = load_rgba(path)
    h, w = rgba.shape[:2]
    if w != rule.width or h != rule.height:
        problems.append(f"[erro] {rule.name} dimensao invalida: {w}x{h}, esperado {rule.width}x{rule.height}")

    alpha = rgba[:, :, 3]
    if rule.require_transparency:
        if np.all(alpha == 255):
            problems.append(f"[erro] {rule.name} sem transparencia real (alpha 100% opaco)")
        if not np.any(alpha == 0):
            problems.append(f"[erro] {rule.name} sem fundo removido (alpha nao possui 0)")

    return problems


def main() -> int:
    repo_login = Path(__file__).resolve().parents[1]
    assets_dir = repo_login / "Protons.UI" / "Assets" / "UI" / "Buttons"

    rules = [
        AssetRule("ancora_pdf_128.png", 128, 128),
        AssetRule("ancora_pdf_192.png", 192, 192),
        AssetRule("ancora_pdf_256.png", 256, 256),
        AssetRule("ancora_pdf_normal.png", 192, 192),
        AssetRule("ancora_pdf_hover.png", 192, 192),
        AssetRule("ancora_pdf_pressed.png", 192, 192),
        AssetRule("ancora_pdf_focus.png", 192, 192),
    ]

    all_problems: list[str] = []
    for rule in rules:
        all_problems.extend(check_rule(assets_dir, rule))

    if all_problems:
        print("\n".join(all_problems))
        return 1

    print("[ok] todos os assets da ancora PDF estao validos.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
