"""Draws the KikiMAIster icon (dark rounded square, gold frame, three rising bars, a sword) as a PNG.
Pure Python (no image library). Usage: python3 make_icon.py <size> <output.png>"""
import math
import struct
import sys
import zlib

SS = 3  # supersampling per axis, for smooth edges

BG = (15, 14, 22)
GOLD = (242, 199, 89)
GOLD_DARK = (176, 132, 44)
STEEL = (222, 226, 236)
STEEL_DARK = (140, 146, 160)
GRIP = (110, 62, 40)


def inside_rounded(x, y, x0, y0, x1, y1, r):
    if x < x0 or x > x1 or y < y0 or y > y1:
        return False
    cx = min(max(x, x0 + r), x1 - r)
    cy = min(max(y, y0 + r), y1 - r)
    return (x - cx) ** 2 + (y - cy) ** 2 <= r * r


def in_segment(px, py, ax, ay, bx, by, w):
    """Within w/2 of segment AB."""
    dx, dy = bx - ax, by - ay
    t = max(0.0, min(1.0, ((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy)))
    qx, qy = ax + t * dx, ay + t * dy
    return (px - qx) ** 2 + (py - qy) ** 2 <= (w / 2) ** 2


def color_at(u, v):
    """u, v in [0, 1]: the colour (RGBA) of that point."""
    if not inside_rounded(u, v, 0.0, 0.0, 1.0, 1.0, 0.14):
        return (0, 0, 0, 0)
    if not inside_rounded(u, v, 0.035, 0.035, 0.965, 0.965, 0.115):
        return GOLD + (255,)
    # Sword, from bottom-left to top-right, drawn over the bars.
    ax, ay, bx, by = 0.20, 0.82, 0.80, 0.20  # pommel side -> tip
    def along(t):
        return ax + (bx - ax) * t, ay + (by - ay) * t
    gx, gy = along(0.22)  # cross-guard centre
    nx, ny = (by - ay), -(bx - ax)
    n = math.hypot(nx, ny)
    nx, ny = nx / n, ny / n
    if in_segment(u, v, gx - nx * 0.11, gy - ny * 0.11, gx + nx * 0.11, gy + ny * 0.11, 0.035):
        return GOLD_DARK + (255,)
    px, py = along(0.04)
    if (u - px) ** 2 + (v - py) ** 2 <= 0.028 ** 2:
        return GOLD_DARK + (255,)
    if in_segment(u, v, *along(0.04), *along(0.22), 0.04):
        return GRIP + (255,)
    tx, ty = along(1.0)
    if in_segment(u, v, *along(0.22), *along(0.93), 0.06):
        # Blade, with a darker edge line along its middle.
        return (STEEL_DARK if in_segment(u, v, *along(0.24), *along(0.92), 0.012) else STEEL) + (255,)
    if in_segment(u, v, *along(0.92), tx, ty, 0.035):
        return STEEL + (255,)
    # Three rising bars (a damage meter).
    for i, h in enumerate((0.26, 0.42, 0.60)):
        x0 = 0.17 + i * 0.235
        if inside_rounded(u, v, x0, 0.80 - h, x0 + 0.17, 0.80, 0.02):
            return GOLD + (255,)
    # Base line.
    if 0.12 <= u <= 0.88 and 0.815 <= v <= 0.84:
        return GOLD_DARK + (255,)
    return BG + (255,)


def render(size):
    rows = []
    for y in range(size):
        row = bytearray([0])
        for x in range(size):
            acc = [0, 0, 0, 0]
            for sy in range(SS):
                for sx in range(SS):
                    c = color_at((x + (sx + 0.5) / SS) / size, (y + (sy + 0.5) / SS) / size)
                    a = c[3]
                    acc[0] += c[0] * a
                    acc[1] += c[1] * a
                    acc[2] += c[2] * a
                    acc[3] += a
            n = SS * SS
            alpha = acc[3] / n
            if acc[3] > 0:
                row += bytes((round(acc[0] / acc[3]), round(acc[1] / acc[3]), round(acc[2] / acc[3]), round(alpha)))
            else:
                row += bytes((0, 0, 0, 0))
        rows.append(bytes(row))
    return b"".join(rows)


def png(size, data):
    def chunk(kind, body):
        return struct.pack(">I", len(body)) + kind + body + struct.pack(">I", zlib.crc32(kind + body) & 0xFFFFFFFF)
    header = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header) + chunk(b"IDAT", zlib.compress(data, 9)) + chunk(b"IEND", b"")


if __name__ == "__main__":
    size = int(sys.argv[1])
    with open(sys.argv[2], "wb") as f:
        f.write(png(size, render(size)))
