"""Dibuja el ícono de Hysteria Meter y lo guarda como .ico con todos los tamaños de Windows.

El rombo es el del encabezado del overlay (12 x 14, borde Gold #E8B04A) con el fondo de la ventana (#0C0E13) y
tres barras de daño adentro. Cada tamaño se dibuja aparte a 8 veces y se achica: los bordes de las barras caen
justo en un píxel, así en 16 px se leen nítidas. De 16 a 64 px van como BMP (lo que mejor lee Windows en la
bandeja); 128 y 256, como PNG.

Uso: python -I tools/icono/icono.py [--muestra archivo.png]
"""

import argparse
import io
import os
import struct

from PIL import Image, ImageChops, ImageDraw

SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
SUPERSAMPLE = 8

GOLD = (0xE8, 0xB0, 0x4A)
GOLD_LIGHT = (0xF6, 0xCF, 0x72)
GOLD_DARK = (0xC9, 0x8F, 0x2E)
BG_TOP = (0x1E, 0x23, 0x2E)
BG_BOTTOM = (0x0C, 0x0E, 0x13)

# Rombo del encabezado: 12 de ancho por 14 de alto, centrado.
TOP, BOTTOM = 0.035, 0.965
HALF_H = (BOTTOM - TOP) / 2
HALF_W = HALF_H * 12 / 14
CX, CY = 0.5, 0.5

# Barras (fracción del lienzo): la del medio es la más alta, como el rombo.
BAR_BASE = 0.665
BARS = [0.17, 0.33, 0.24]
BAR_WIDTH = 0.092
BAR_GAP = 0.046


def diamond(scale, k=1.0):
    hw, hh = HALF_W * k * scale, HALF_H * k * scale
    cx, cy = CX * scale, CY * scale
    return [(cx, cy - hh), (cx + hw, cy), (cx, cy + hh), (cx - hw, cy)]


def gradient(size, top, bottom):
    """Degradé vertical de top a bottom, del tamaño pedido."""
    column = Image.new("RGB", (1, size))
    for y in range(size):
        t = y / max(size - 1, 1)
        column.putpixel((0, y), tuple(round(a + (b - a) * t) for a, b in zip(top, bottom)))
    return column.resize((size, size))


def stroke_width(size):
    """Grosor del borde en píxeles del tamaño final: fino en grande y nunca menos de 1,25 px."""
    return max(1.25, size * 0.052)


def render(size):
    big = size * SUPERSAMPLE
    image = Image.new("RGBA", (big, big), (0, 0, 0, 0))

    # Borde: el rombo entero en dorado y, adentro, el rombo achicado con el fondo.
    outer = Image.new("L", (big, big), 0)
    ImageDraw.Draw(outer).polygon(diamond(big), fill=255)
    image.paste(gradient(big, GOLD_LIGHT, GOLD_DARK), (0, 0), outer)

    inradius = HALF_W * HALF_H / ((HALF_W ** 2 + HALF_H ** 2) ** 0.5)
    k = (inradius - stroke_width(size) / size) / inradius
    inner = Image.new("L", (big, big), 0)
    ImageDraw.Draw(inner).polygon(diamond(big, k), fill=255)
    image.paste(gradient(big, BG_TOP, BG_BOTTOM), (0, 0), inner)

    # Barras con los bordes en píxeles enteros del tamaño final.
    def snap(value):
        return round(value * size) * SUPERSAMPLE

    # En los tamaños chicos el rombo se angosta antes: las barras suben un poco y son algo más bajas.
    small = size <= 24
    base = BAR_BASE - (0.045 if small else 0)
    heights = [h * (0.85 if small else 1) for h in BARS]
    width = max(BAR_WIDTH * size, 2 if small else 0) / size
    gap = max(BAR_GAP * size, 1) / size
    total = len(heights) * width + (len(heights) - 1) * gap
    left = CX - total / 2
    bars = Image.new("L", (big, big), 0)
    draw = ImageDraw.Draw(bars)
    for i, height in enumerate(heights):
        x0 = left + i * (width + gap)
        draw.rectangle([snap(x0), snap(base - height), snap(x0 + width) - 1, snap(base) - 1], fill=255)

    # Las barras nunca pisan el borde: se recortan con el interior, dejando medio píxel de aire.
    k_bars = (inradius - (stroke_width(size) + 0.6) / size) / inradius
    room = Image.new("L", (big, big), 0)
    ImageDraw.Draw(room).polygon(diamond(big, k_bars), fill=255)
    bars = ImageChops.multiply(bars, room)
    image.paste(gradient(big, GOLD_LIGHT, GOLD), (0, 0), bars)

    return image.resize((size, size), Image.LANCZOS)


def bmp_entry(image):
    """Imagen de 32 bits para el .ico: BITMAPINFOHEADER con el doble de alto, filas de abajo arriba y máscara AND vacía."""
    w, h = image.size
    header = struct.pack("<IiiHHIIiiII", 40, w, h * 2, 1, 32, 0, 0, 0, 0, 0, 0)
    pixels = bytearray()
    for y in range(h - 1, -1, -1):
        for x in range(w):
            r, g, b, a = image.getpixel((x, y))
            pixels += bytes((b, g, r, a))
    mask_row = ((w + 31) // 32) * 4
    return header + bytes(pixels) + bytes(mask_row * h)


def png_entry(image):
    buffer = io.BytesIO()
    image.save(buffer, format="PNG", optimize=True)
    return buffer.getvalue()


def write_ico(path, images):
    entries = [(img, png_entry(img) if img.size[0] >= 128 else bmp_entry(img)) for img in images]
    offset = 6 + 16 * len(entries)
    out = bytearray(struct.pack("<HHH", 0, 1, len(entries)))
    for img, data in entries:
        w, h = img.size
        out += struct.pack("<BBBBHHII", w % 256, h % 256, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
    for _, data in entries:
        out += data
    with open(path, "wb") as f:
        f.write(out)


def contact_sheet(path, images):
    """Cada tamaño en su tamaño real y ampliado, sobre fondo claro y oscuro, para mirarlo antes de guardarlo."""
    zoom = 4
    cell = max(s for s in SIZES if s <= 64) * zoom + 16
    width = sum(max(s * zoom, s) + 16 for s in SIZES if s <= 64) + 256 + 32
    sheet = Image.new("RGB", (width, cell * 2 + 280), (240, 240, 240))
    dark = Image.new("RGB", (width, cell + 140), (32, 32, 36))
    sheet.paste(dark, (0, cell + 140))
    for row, y0 in enumerate([8, cell + 148]):
        x = 8
        for img in images:
            s = img.size[0]
            if s > 64:
                continue
            sheet.paste(img, (x, y0), img)
            big = img.resize((s * zoom, s * zoom), Image.NEAREST)
            sheet.paste(big, (x, y0 + s + 8), big)
            x += s * zoom + 16
        largest = images[-1]
        sheet.paste(largest, (x + 8, y0), largest)
    sheet.save(path)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--muestra", help="guarda también una hoja con todos los tamaños")
    args = parser.parse_args()

    root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    target = os.path.join(root, "src", "HysteriaMeter.App", "Assets", "HysteriaMeter.ico")
    images = [render(size) for size in SIZES]
    write_ico(target, images)
    print(f"{target}: {', '.join(str(s) for s in SIZES)} px")
    if args.muestra:
        contact_sheet(args.muestra, images)
        print(f"muestra: {args.muestra}")


if __name__ == "__main__":
    main()
