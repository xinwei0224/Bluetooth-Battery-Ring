from PIL import Image, ImageDraw
import os, math

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "extension", "icons")
os.makedirs(OUT, exist_ok=True)

GREEN = (34, 197, 94, 255)
TRACK = (128, 128, 128, 90)
WHITE = (255, 255, 255, 255)

def make(size):
    S = size * 8  # supersample
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    pad = S * 0.12
    lw = int(S * 0.11)
    box = [pad, pad, S - pad, S - pad]
    # track ring
    d.ellipse(box, outline=TRACK, width=lw)
    # green ring (full)
    d.ellipse(box, outline=GREEN, width=lw)
    # bluetooth glyph
    cx = S / 2
    top = S * 0.24
    bottom = S * 0.76
    mid = S * 0.50
    rx = S * 0.66
    glw = max(3, int(S * 0.10))
    # vertical spine
    d.line([(cx, top), (cx, bottom)], fill=GREEN, width=glw, joint="curve")
    # upper triangle
    d.line([(cx, top), (rx, S * 0.37), (cx, mid)], fill=GREEN, width=glw, joint="curve")
    # lower triangle
    d.line([(cx, bottom), (rx, S * 0.63), (cx, mid)], fill=GREEN, width=glw, joint="curve")
    img = img.resize((size, size), Image.LANCZOS)
    img.save(os.path.join(OUT, "icon%d.png" % size))

for s in (16, 48, 128):
    make(s)
    print("icon%d.png" % s)
print("done ->", OUT)
