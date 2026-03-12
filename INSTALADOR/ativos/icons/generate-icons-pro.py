#!/usr/bin/env python3
import argparse
import os
import sys

from PIL import Image, ImageChops, ImageDraw, ImageFilter


def parse_color(value):
    value = value.strip()
    if value.startswith("#"):
        hexv = value.lstrip("#")
        if len(hexv) == 6:
            r = int(hexv[0:2], 16)
            g = int(hexv[2:4], 16)
            b = int(hexv[4:6], 16)
            return (r, g, b, 255)
        if len(hexv) == 8:
            r = int(hexv[0:2], 16)
            g = int(hexv[2:4], 16)
            b = int(hexv[4:6], 16)
            a = int(hexv[6:8], 16)
            return (r, g, b, a)
        raise ValueError(f"Invalid hex color: {value}")
    if value.lower().startswith("rgba(") and value.endswith(")"):
        parts = value[5:-1].split(",")
        if len(parts) != 4:
            raise ValueError(f"Invalid rgba color: {value}")
        r = int(parts[0].strip())
        g = int(parts[1].strip())
        b = int(parts[2].strip())
        a_raw = parts[3].strip()
        if "." in a_raw:
            a = int(float(a_raw) * 255)
        else:
            a = int(a_raw)
            if a <= 1:
                a = int(a * 255)
        return (r, g, b, max(0, min(255, a)))
    if value.lower().startswith("rgb(") and value.endswith(")"):
        parts = value[4:-1].split(",")
        if len(parts) != 3:
            raise ValueError(f"Invalid rgb color: {value}")
        r = int(parts[0].strip())
        g = int(parts[1].strip())
        b = int(parts[2].strip())
        return (r, g, b, 255)
    raise ValueError(f"Unsupported color format: {value}")


def make_gradient(size, color_start, color_end):
    width, height = size
    img = Image.new("RGBA", (width, height), color_start)
    draw = ImageDraw.Draw(img)
    for y in range(height):
        t = y / (height - 1)
        r = int(color_start[0] + (color_end[0] - color_start[0]) * t)
        g = int(color_start[1] + (color_end[1] - color_start[1]) * t)
        b = int(color_start[2] + (color_end[2] - color_start[2]) * t)
        draw.line([(0, y), (width, y)], fill=(r, g, b, 255))
    return img


def screen_composite(base, overlay):
    base_rgb = base.convert("RGB")
    overlay_rgb = overlay.convert("RGB")
    screen_rgb = ImageChops.screen(base_rgb, overlay_rgb)
    mask = overlay.split()[3]
    blended = Image.composite(screen_rgb, base_rgb, mask)
    return blended.convert("RGBA")


def center_paste(canvas, image):
    x = (canvas.width - image.width) // 2
    y = (canvas.height - image.height) // 2
    canvas.paste(image, (x, y), image)


def build_logo_square(source_path, size):
    logo = Image.open(source_path).convert("RGBA")
    width, height = logo.size
    max_dim = max(width, height) or 1
    scale = size / max_dim
    new_width = max(1, round(width * scale))
    new_height = max(1, round(height * scale))
    if scale != 1:
        logo = logo.resize((new_width, new_height), Image.LANCZOS)
        if scale > 1.0:
            logo = logo.filter(ImageFilter.UnsharpMask(radius=2, percent=140, threshold=3))
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    center_paste(canvas, logo)
    return canvas, scale, max_dim


def rounded_mask(size, radius):
    mask = Image.new("L", (size, size), 0)
    draw = ImageDraw.Draw(mask)
    draw.rounded_rectangle((0, 0, size - 1, size - 1), radius=radius, fill=255)
    return mask


def main():
    parser = argparse.ArgumentParser(description="Generate professional Protons icons")
    parser.add_argument("--source", required=True, help="Path to source logo")
    parser.add_argument("--style", default="futurista", choices=["futurista", "classic", "profissional", "simbolo", "clean", "only"])
    parser.add_argument("--logo-size", type=int, default=None)
    parser.add_argument("--bg-start", default=None)
    parser.add_argument("--bg-end", default=None)
    parser.add_argument("--accent", default=None)
    parser.add_argument("--accent-glow", default=None)
    parser.add_argument("--glass-fill", default=None)
    parser.add_argument("--glass-stroke", default=None)
    args = parser.parse_args()

    script_dir = os.path.dirname(os.path.abspath(__file__))
    output_dir = os.path.join(script_dir, "png")
    os.makedirs(output_dir, exist_ok=True)

    if not os.path.isfile(args.source):
        print(f"ERROR: Source logo not found: {args.source}")
        return 1

    style = args.style
    if style == "profissional":
        style = "classic"

    if style == "futurista":
        bg_start = args.bg_start or "#0B1020"
        bg_end = args.bg_end or "#123556"
        accent = args.accent or "#4CC9F0"
        accent_glow = args.accent_glow or "rgba(76,201,240,0.55)"
        glass_fill = args.glass_fill or "rgba(255,255,255,0.10)"
        glass_stroke = args.glass_stroke or "rgba(255,255,255,0.20)"
        logo_size = args.logo_size or 200
    elif style in ("simbolo", "clean", "only"):
        bg_start = None
        bg_end = None
        accent = None
        accent_glow = None
        glass_fill = None
        glass_stroke = None
        logo_size = args.logo_size or 248
    else:
        bg_start = args.bg_start or "#E8F4FF"
        bg_end = args.bg_end or "#FFFFFF"
        accent = args.accent or "#2D7BD4"
        accent_glow = args.accent_glow or "rgba(45,123,212,0.35)"
        glass_fill = args.glass_fill or "rgba(255,255,255,0.00)"
        glass_stroke = args.glass_stroke or "rgba(255,255,255,0.00)"
        logo_size = args.logo_size or 192

    base_size = 256
    if style in ("simbolo", "clean", "only"):
        base = Image.new("RGBA", (base_size, base_size), (0, 0, 0, 0))
    else:
        bg_start_rgba = parse_color(bg_start)
        bg_end_rgba = parse_color(bg_end)
        accent_rgba = parse_color(accent)
        accent_glow_rgba = parse_color(accent_glow)
        glass_fill_rgba = parse_color(glass_fill)
        glass_stroke_rgba = parse_color(glass_stroke)
        base = make_gradient((base_size, base_size), bg_start_rgba, bg_end_rgba)

    if style == "futurista":
        glow_layer = Image.new("RGBA", (base_size, base_size), (0, 0, 0, 0))
        draw = ImageDraw.Draw(glow_layer)
        draw.ellipse((140, 20, 240, 120), fill=accent_glow_rgba)
        glow_layer = glow_layer.filter(ImageFilter.GaussianBlur(28))
        base = screen_composite(base, glow_layer)

        ring_layer = Image.new("RGBA", (base_size, base_size), (0, 0, 0, 0))
        draw = ImageDraw.Draw(ring_layer)
        draw.ellipse((30, 30, 226, 226), outline=accent_glow_rgba, width=3)
        base = screen_composite(base, ring_layer)

        glass_layer = Image.new("RGBA", (base_size, base_size), (0, 0, 0, 0))
        draw = ImageDraw.Draw(glass_layer)
        draw.rounded_rectangle((26, 26, 230, 230), radius=28, fill=glass_fill_rgba, outline=glass_stroke_rgba, width=1)
        base = Image.alpha_composite(base, glass_layer)

    logo_square, scale, source_max = build_logo_square(args.source, logo_size)
    if source_max < 200:
        print("AVISO: imagem de origem pequena; o ícone foi ampliado para ficar grande.")
    if scale > 1.0:
        print(f"Ajuste: upscale {scale:.2f}x com nitidez automática.")
    logo_layer = Image.new("RGBA", (base_size, base_size), (0, 0, 0, 0))
    center_paste(logo_layer, logo_square)

    if style == "futurista":
        glow_color = Image.new("RGBA", (base_size, base_size), accent_rgba)
        glow_color.putalpha(logo_layer.split()[3])
        glow = glow_color.filter(ImageFilter.GaussianBlur(14))
        base = screen_composite(base, glow)
    elif style in ("classic", "profissional"):
        shadow = Image.new("RGBA", (base_size, base_size), (0, 0, 0, 120))
        shadow.putalpha(logo_layer.split()[3])
        shadow = shadow.filter(ImageFilter.GaussianBlur(4))
        shadow_offset = Image.new("RGBA", (base_size, base_size), (0, 0, 0, 0))
        shadow_offset.paste(shadow, (0, 2), shadow)
        base = Image.alpha_composite(base, shadow_offset)

    base = Image.alpha_composite(base, logo_layer)

    if style not in ("simbolo", "clean", "only"):
        mask = rounded_mask(base_size, radius=8)
        base.putalpha(mask)

    base_path = os.path.join(output_dir, "256x256.png")
    base.save(base_path, optimize=True)

    for size in (16, 32, 48, 64, 128):
        resized = base.resize((size, size), Image.LANCZOS)
        resized.save(os.path.join(output_dir, f"{size}x{size}.png"), optimize=True)

    print("OK: Icons generated in", output_dir)
    return 0


if __name__ == "__main__":
    sys.exit(main())
