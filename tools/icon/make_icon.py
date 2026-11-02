"""Draws the KikiMAIster icon (a tilted red game card: energy orb, rising gold bars in the art window, "KIKI" on the
name banner) as a PNG. Pure Python (no image library). Usage: python3 make_icon.py <size> <output.png>"""
import math
import struct
import sys
import zlib

def lerp(a, b, t):
    t = max(0.0, min(1.0, t))
    return tuple(round(a[i] + (b[i] - a[i]) * t) for i in range(3))

def rrect(x0, y0, x1, y1, r):
    def f(x, y):
        if x < x0 or x > x1 or y < y0 or y > y1: return False
        cx = min(max(x, x0 + r), x1 - r); cy = min(max(y, y0 + r), y1 - r)
        return (x - cx) ** 2 + (y - cy) ** 2 <= r * r
    return f

def circle(cx, cy, r):
    return lambda x, y: (x - cx) ** 2 + (y - cy) ** 2 <= r * r

def ring(cx, cy, r0, r1):
    return lambda x, y: r0 * r0 <= (x - cx) ** 2 + (y - cy) ** 2 <= r1 * r1

def seg(ax, ay, bx, by, w):
    dx, dy = bx - ax, by - ay; L = dx * dx + dy * dy
    def f(px, py):
        t = max(0.0, min(1.0, ((px - ax) * dx + (py - ay) * dy) / L))
        return (px - ax - t * dx) ** 2 + (py - ay - t * dy) ** 2 <= (w / 2) ** 2
    return f

def poly(pts):
    xs = [p[0] for p in pts]; ys = [p[1] for p in pts]
    bx0, bx1, by0, by1 = min(xs), max(xs), min(ys), max(ys)
    n = len(pts)
    def f(x, y):
        if x < bx0 or x > bx1 or y < by0 or y > by1: return False
        c = False; j = n - 1
        for i in range(n):
            xi, yi = pts[i]; xj, yj = pts[j]
            if (yi > y) != (yj > y) and x < (xj - xi) * (y - yi) / (yj - yi) + xi: c = not c
            j = i
        return c
    return f

def scale_pts(pts, s, cx, cy):
    return [(cx + (x - cx) * s, cy + (y - cy) * s) for x, y in pts]

def rotated(test, ang, cx, cy):
    c, s = math.cos(-ang), math.sin(-ang)
    def f(x, y):
        dx, dy = x - cx, y - cy
        return test(cx + dx * c - dy * s, cy + dx * s + dy * c)
    return f

def both(a, b): return lambda x, y: a(x, y) and b(x, y)
def minus(a, b): return lambda x, y: a(x, y) and not b(x, y)

def render(layers, size, ss=2, mask=None):
    """layers: list of (test, colour or fn(u,v)->rgb), topmost FIRST."""
    out = bytearray()
    for py in range(size):
        out.append(0)
        for px in range(size):
            r = g = b = a = 0
            for sy in range(ss):
                for sx in range(ss):
                    u = (px + (sx + 0.5) / ss) / size; v = (py + (sy + 0.5) / ss) / size
                    if mask and not mask(u, v): continue
                    for test, col in layers:
                        if test(u, v):
                            c = col(u, v) if callable(col) else col
                            r += c[0]; g += c[1]; b += c[2]; a += 1
                            break
            n = ss * ss
            if a: out += bytes((r // a, g // a, b // a, round(255 * a / n)))
            else: out += b"\0\0\0\0"
    def chunk(k, body):
        return struct.pack(">I", len(body)) + k + body + struct.pack(">I", zlib.crc32(k + body) & 0xFFFFFFFF)
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(bytes(out), 9)) + chunk(b"IEND", b""))

ALL = lambda x, y: True

GOLD = (236, 190, 86); GOLD_D = (160, 112, 40); GOLD_L = (255, 228, 150)
INK = (18, 12, 20)
FRAME = rrect(0, 0, 1, 1, 0.14)
INNER = rrect(0.035, 0.035, 0.965, 0.965, 0.115)

def bars(x0, base, w, gap, heights, col, r=0.012):
    return [(rrect(x0 + i * (w + gap), base - h, x0 + i * (w + gap) + w, base, r), col) for i, h in enumerate(heights)]

def card():
    ang = math.radians(-7); C = (0.5, 0.52)
    R = lambda t: rotated(t, ang, *C)
    L = []
    # energy orb (top-left of the card), drawn on top
    ox, oy = 0.27, 0.19
    orb = lambda u, v: lerp((255, 140, 60), (150, 20, 20), math.hypot(u - ox + 0.025, v - oy + 0.025) / 0.11)
    L += [(R(circle(ox - 0.02, oy - 0.02, 0.022)), (255, 225, 170)),
          (R(circle(ox, oy, 0.075)), orb), (R(circle(ox, oy, 0.095)), GOLD), (R(circle(ox, oy, 0.105)), INK)]
    # card name "KIKI" on the banner: cream serif letters, dark outline and drop shadow, as on game cards
    top, bot, sw = 0.574, 0.636, 0.016; mid = (top + bot) / 2; sf = sw * 0.75
    strokes = []
    x = 0.392
    for ch in "KIKI":
        if ch == "K":
            strokes += [(x, top, x, bot, sw), (x - 0.012, top + 0.002, x + 0.012, top + 0.002, sf), (x - 0.012, bot - 0.002, x + 0.012, bot - 0.002, sf),
                        (x + 0.044, top + 0.002, x + 0.006, mid + 0.004, sw * 0.85), (x + 0.034, top + 0.002, x + 0.056, top + 0.002, sf),
                        (x + 0.014, mid - 0.004, x + 0.048, bot - 0.002, sw), (x + 0.036, bot - 0.002, x + 0.060, bot - 0.002, sf)]
            x += 0.050 + 0.020
        else:
            c = x + 0.004
            strokes += [(c, top, c, bot, sw), (c - 0.014, top + 0.002, c + 0.014, top + 0.002, sf), (c - 0.014, bot - 0.002, c + 0.014, bot - 0.002, sf)]
            x += 0.008 + 0.034
    fill = lambda u, v: lerp((255, 250, 235), (232, 214, 180), (v - top) / (bot - top))
    L += [(R(seg(*p[:4], p[4])), fill) for p in strokes]
    L += [(R(seg(*p[:4], p[4] + 0.013)), (40, 20, 12)) for p in strokes]
    L += [(R(seg(p[0] + 0.004, p[1] + 0.006, p[2] + 0.004, p[3] + 0.006, p[4] + 0.013)), (95, 60, 25)) for p in strokes]
    # name banner with notched ends
    bn = [(0.15, 0.565), (0.85, 0.565), (0.81, 0.605), (0.85, 0.645), (0.15, 0.645), (0.19, 0.605)]
    L += [(R(poly(bn)), lambda u, v: lerp((205, 160, 70), (140, 95, 35), (v - 0.56) / 0.09)),
          (R(poly(scale_pts(bn, 1.06, 0.5, 0.605))), INK),
          (R(poly([(0.19, 0.645), (0.25, 0.645), (0.25, 0.675)])), (95, 60, 25)),
          (R(poly([(0.81, 0.645), (0.75, 0.645), (0.75, 0.675)])), (95, 60, 25))]
    # art window: dusk sky, rising bars
    for i, h in enumerate((0.11, 0.18, 0.26)):
        x0 = 0.335 + i * 0.115
        L += [(R(rrect(x0, 0.53 - h, x0 + 0.085, 0.53, 0.008)), lambda u, v, h=h: lerp(GOLD_L, GOLD_D, (v - (0.53 - h)) / h))]
    sky = lambda u, v: lerp((70, 30, 60), (190, 70, 50), (v - 0.24) / 0.31)
    L += [(R(rrect(0.30, 0.24, 0.70, 0.55, 0.01)), sky), (R(rrect(0.285, 0.225, 0.715, 0.565, 0.015)), INK)]
    # description area
    L += [(R(rrect(0.36, 0.71, 0.64, 0.725, 0.007)), (120, 95, 70)), (R(rrect(0.40, 0.75, 0.60, 0.765, 0.007)), (120, 95, 70)),
          (R(rrect(0.28, 0.67, 0.72, 0.83, 0.02)), (226, 208, 170))]
    # card body
    body = lambda u, v: lerp((175, 38, 40), (95, 15, 25), v)
    L += [(R(rrect(0.24, 0.17, 0.76, 0.88, 0.035)), body), (R(rrect(0.215, 0.145, 0.785, 0.905, 0.05)), GOLD),
          (R(rrect(0.20, 0.13, 0.80, 0.92, 0.06)), INK)]
    bg = lambda u, v: lerp((52, 34, 62), (14, 10, 20), math.hypot(u - 0.5, v - 0.45) / 0.7)
    L += [(INNER, bg), (FRAME, GOLD)]
    return L


if __name__ == "__main__":
    size = int(sys.argv[1])
    with open(sys.argv[2], "wb") as f:
        f.write(render(card(), size, ss=3))
