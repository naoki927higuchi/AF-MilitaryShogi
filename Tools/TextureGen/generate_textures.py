"""AF-MilitaryShogi piece/board texture generator.

Inputs (never modified): Reference/UserProvided/Images/
  - 軍人将棋無地駒.png        plain wooden piece (RGB, fake checker background)
  - 軍人将棋アイコン素材集.png  icon sheet; icons live in the alpha channel
  - 濃紺木目.png              dark wood for the board
Font for the piece names and the 総司令部 label: 昭和書体「闘龍」(KSO闘龍, family KsoTouryu),
used ONLY as a font installed in the OS of the licensed development PC (1 licence / 1 PC).
The font file is never read from, copied into or shipped with the project; only the rendered,
finished textures are committed. Licence: https://designpocket.jp/font/detail/23984

Without KSO闘龍 (anyone else who clones the repository) the script does not stop: it logs that
KSO闘龍 is not installed and renders with a Japanese system font (Yu Gothic, Meiryo, … / Noto
Sans CJK, Hiragino, …), checking that every character really has a glyph. Such output differs
from the distributed textures. Building or running the game never needs this script or the
font: the finished textures are committed.

Outputs (overwritten deterministically): Assets/Generated/Resources/Textures/
  Pieces/piece_<type>.png, Pieces/piece_back.png, Pieces/piece_side.png,
  Board/board_wood.png, Board/table_wood.png, Board/label_hq.png, UI/title_logo.png;
application icons in Assets/Generated/Icons/app_icon_<size>.png; a contact sheet for review in
Generated/Previews/; the generation record Generated/texture_generation_manifest.json.

Run:  python Tools/TextureGen/generate_textures.py
      python Tools/TextureGen/generate_textures.py --out <dir>   (everything into <dir>; the
            committed textures are not touched)
      --simulate-no-kso   behave as if KSO闘龍 were not installed (tests the fallback)
"""
import argparse
import hashlib
import json
import os
import sys

import numpy as np
from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SRC = os.path.join(ROOT, "Reference", "UserProvided", "Images")
OUT = os.path.join(ROOT, "Assets", "Generated", "Resources", "Textures")
PREVIEW = os.path.join(ROOT, "Generated", "Previews")

PLAIN_PIECE = "軍人将棋無地駒.png"
ICON_SHEET = "軍人将棋アイコン素材集.png"
BOARD_WOOD = "濃紺木目.png"
KSO_FAMILY = "KsoTouryu"          # family name of KSO闘龍 (the JIS90 font; not KsoTouryuN)
KSO_DISPLAY = "昭和書体「闘龍」（KSO闘龍）"
LOGO = "ロゴ タイトルとコピーのみ.png"
APP_ICON = "アイコン画像.png"
ICON_SIZES = (16, 24, 32, 40, 48, 64, 96, 128, 256, 512, 1024)

# Face texture size. Aspect equals the plain-piece bounding box (995 x 1113).
W, H = 512, 572
# Piece outline in normalized face coordinates (u right, v down from top).
# Shared with PieceMeshBuilder.cs (Outline) so geometry and texture line up.
OUTLINE = [(0.5, 0.0), (0.856, 0.137), (1.0, 1.0), (0.0, 1.0), (0.144, 0.137)]

# Uniform layout boxes (fractions of W/H) for every piece.
ICON_BOX = (0.19, 0.20, 0.81, 0.52)     # x0, y0, x1, y1
TEXT_CENTER_Y = 0.72
TEXT_CHAR_H = 0.255                      # glyph em height as fraction of H
TEXT_MAX_W = 0.80                        # max text width as fraction of W
INK = (8, 6, 4)          # 1.1.0: darker ink so names/stars/icons stay black under the key light
BURN = (78, 42, 18)

# Icon cells in the icon sheet (pixel boxes inside the frame lines).
ICON_CELLS = {
    "airplane": (42, 106, 356, 424), "tank": (424, 106, 736, 424),
    "cavalry": (802, 106, 1116, 424), "engineer": (1182, 106, 1496, 424),
    "spy": (42, 529, 356, 844), "mine": (424, 529, 736, 844),
    "flag": (802, 529, 1116, 844), "back_emblem": (1182, 529, 1496, 844),
}

# (file id, display name, stars or icon key)
PIECES = [
    ("General", "大将", 3), ("LieutenantGeneral", "中将", 3), ("MajorGeneral", "少将", 3),
    ("Colonel", "大佐", 2), ("LieutenantColonel", "中佐", 2), ("Major", "少佐", 2),
    ("Captain", "大尉", 1), ("FirstLieutenant", "中尉", 1), ("SecondLieutenant", "少尉", 1),
    ("Airplane", "飛行機", "airplane"), ("Tank", "タンク", "tank"), ("Cavalry", "騎兵", "cavalry"),
    ("Engineer", "工兵", "engineer"), ("Spy", "スパイ", "spy"), ("Mine", "地雷", "mine"),
    ("Flag", "軍旗", "flag"),
]


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def installed_font_dirs():
    """Font folders of the OS (the project tree is never searched)."""
    home = os.path.expanduser("~")
    dirs = [
        os.path.join(os.environ.get("LOCALAPPDATA", ""), "Microsoft", "Windows", "Fonts"),   # Windows, per user
        os.path.join(os.environ.get("WINDIR", r"C:\Windows"), "Fonts"),                      # Windows, all users
        "/System/Library/Fonts", "/Library/Fonts", os.path.join(home, "Library", "Fonts"),   # macOS
        "/usr/share/fonts", "/usr/local/share/fonts", os.path.join(home, ".fonts"),           # Linux
        os.path.join(home, ".local", "share", "fonts"),
    ]
    return [d for d in dirs if d and os.path.isdir(d)]


def installed_font_files():
    for d in installed_font_dirs():
        for root, _, names in os.walk(d):
            for n in names:
                if n.lower().endswith((".otf", ".ttf", ".ttc")):
                    yield os.path.join(root, n)


def registry_fonts():
    """Windows: font files registered for the user and the machine (may live outside the font folders)."""
    try:
        import winreg
    except ImportError:
        return []
    found = []
    key = r"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts"
    for hive, base in ((winreg.HKEY_CURRENT_USER, ""), (winreg.HKEY_LOCAL_MACHINE, os.path.join(os.environ.get("WINDIR", r"C:\Windows"), "Fonts"))):
        try:
            with winreg.OpenKey(hive, key) as k:
                i = 0
                while True:
                    try:
                        name, value, _ = winreg.EnumValue(k, i)
                    except OSError:
                        break
                    i += 1
                    path = value if os.path.isabs(value) else os.path.join(base, value)
                    found.append((name, path))
        except OSError:
            pass
    return found


def family(path, index=0):
    try:
        return ImageFont.truetype(path, 10, index=index).getname()[0]
    except OSError:
        return None


def find_kso():
    """KSO闘龍 installed in the OS, or None. Matched by the font's own family name."""
    candidates = [path for name, path in registry_fonts() if "kso" in name.lower() or "touryu" in name.lower()]
    candidates += [p for p in installed_font_files() if "kso" in os.path.basename(p).lower() or "touryu" in os.path.basename(p).lower()]
    for path in candidates:
        if os.path.isfile(path) and family(path) == KSO_FAMILY:
            return path
    return None


# Japanese system fonts tried in this order when KSO闘龍 is not installed: (file name, index in a .ttc).
FALLBACK_FONTS = [
    ("YuGothM.ttc", 0), ("YuGothR.ttc", 0), ("YuGothB.ttc", 0),          # Windows: Yu Gothic
    ("meiryo.ttc", 0), ("BIZ-UDGothicR.ttc", 0), ("msgothic.ttc", 0),     # Windows: Meiryo, BIZ UD, MS Gothic
    ("ヒラギノ角ゴシック W3.ttc", 0), ("Hiragino Sans GB.ttc", 0),          # macOS
    ("NotoSansCJK-Regular.ttc", 0), ("NotoSansCJKjp-Regular.otf", 0),     # Linux
    ("NotoSansJP-Regular.otf", 0), ("ipagp.ttf", 0), ("fonts-japanese-gothic.ttf", 0),
]


def renders_all(path, index, text):
    """True if every character of text has a real glyph (not empty, not the .notdef box)."""
    try:
        font = ImageFont.truetype(path, 64, index=index)
    except OSError:
        return False
    notdef = font.getmask("\uE000")                      # private-use code point → .notdef in fonts without it
    for ch in set(text):
        m = font.getmask(ch)
        if m.getbbox() is None:
            return False
        if m.size == notdef.size and bytes(m) == bytes(notdef):
            return False
    return True


def find_fallback(text):
    by_name = {}
    for p in installed_font_files():
        by_name.setdefault(os.path.basename(p), p)
    for name, index in FALLBACK_FONTS:
        path = by_name.get(name)
        if path and renders_all(path, index, text):
            return path, index
    # Last resort: any installed font that renders every character.
    for p in sorted(by_name.values()):
        if renders_all(p, 0, text):
            return p, 0
    return None, 0


def choose_font(simulate_no_kso):
    """(path, index, family, fallback). Never stops because KSO闘龍 is missing."""
    needed = "".join(name for _, name, _ in PIECES) + "総司令部"
    kso = None if simulate_no_kso else find_kso()
    if kso:
        print("KSO闘龍を検出しました：%s" % kso)
        print("KSO闘龍を使用して駒テクスチャを生成します。")
        return kso, 0, KSO_FAMILY, False
    path, index = find_fallback(needed)
    if path is None:
        sys.exit("日本語を描画できるフォントがOSに見つかりません（KSO闘龍も代替フォントもなし）。日本語フォントをインストールしてください。")
    name = " ".join(ImageFont.truetype(path, 10, index=index).getname())
    print("KSO闘龍はインストールされていません%s。" % ("（--simulate-no-kso）" if simulate_no_kso else ""))
    print("代替フォント「%s」（%s）を使用して駒テクスチャを生成します。" % (name, os.path.basename(path)))
    print("この生成結果は配布版と外観が異なります。ゲームのビルド・実行にはKSO闘龍は必要ありません。")
    return path, index, ImageFont.truetype(path, 10, index=index).getname()[0], True


def outline_mask(scale=4):
    """Anti-aliased alpha mask of the piece outline at face resolution."""
    big = Image.new("L", (W * scale, H * scale), 0)
    ImageDraw.Draw(big).polygon([(u * W * scale, v * H * scale) for u, v in OUTLINE], fill=255)
    return big.resize((W, H), Image.LANCZOS)


def wood_face(mirror=False):
    """Crop the plain piece photo to its outline bounding box."""
    src = Image.open(os.path.join(SRC, PLAIN_PIECE)).convert("RGB")
    a = np.asarray(src).astype(int)
    mask = (a[..., 0] - a[..., 2]) > 40          # orange wood vs gray checker
    ys, xs = np.nonzero(mask)
    box = (int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1)
    face = src.crop(box).resize((W, H), Image.LANCZOS)
    if mirror:
        face = face.transpose(Image.FLIP_LEFT_RIGHT)
        # keep the photographed bevel lighting consistent (light from top-left)
    return face


def extract_icon(key):
    sheet = Image.open(os.path.join(SRC, ICON_SHEET))
    alpha = sheet.getchannel("A").crop(ICON_CELLS[key])
    # Icons are opaque black on a transparent cell: keep coverage only.
    arr = np.asarray(alpha).astype(float)
    arr = np.clip((arr - 40) * (255.0 / 180.0), 0, 255).astype("uint8")
    icon = Image.fromarray(arr, "L")
    bbox = icon.getbbox()
    return icon.crop(bbox)


def fit_into(img, box_w, box_h):
    s = min(box_w / img.width, box_h / img.height)
    return img.resize((max(1, round(img.width * s)), max(1, round(img.height * s))), Image.LANCZOS)


def star_polygon(cx, cy, r_out, r_in, rotation=-90.0):
    import math
    pts = []
    for i in range(10):
        r = r_out if i % 2 == 0 else r_in
        a = math.radians(rotation + i * 36)
        pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts


def stars_layer(count, scale=4):
    """Rank stars, centred in the icon box, same size for every rank."""
    layer = Image.new("L", (W * scale, H * scale), 0)
    d = ImageDraw.Draw(layer)
    r_out = 0.085 * W * scale
    gap = 2.15 * r_out
    x0, y0, x1, y1 = ICON_BOX
    cy = (y0 + 0.72 * (y1 - y0)) * H * scale      # stars sit low in the icon box, close to the name
    cx = 0.5 * W * scale
    for i in range(count):
        x = cx + (i - (count - 1) / 2) * gap
        d.polygon(star_polygon(x, cy, r_out, r_out * 0.40), fill=255)
    return layer.resize((W, H), Image.LANCZOS)


def text_layer(text, font_path, font_index=0):
    """Piece name with KSO闘龍. Same glyph height for all; long names are squeezed horizontally."""
    em = int(TEXT_CHAR_H * H * 4)
    font = ImageFont.truetype(font_path, em, index=font_index)
    tracking = 0.02 * em if len(text) == 2 else 0.0
    glyphs = []
    for ch in text:
        l, t, r, b = font.getbbox(ch)
        img = Image.new("L", (int(r - l) + 8, em * 2), 0)
        ImageDraw.Draw(img).text((4 - l, em // 2), ch, font=font, fill=255)
        glyphs.append((img, font.getlength(ch)))
    total = sum(adv for _, adv in glyphs) + tracking * (len(glyphs) - 1)
    canvas = Image.new("L", (int(total) + 16, em * 2), 0)
    x = 8.0
    for img, adv in glyphs:
        canvas.paste(img, (int(x) - 4, 0), img)
        x += adv + tracking
    # Crop horizontally to ink, vertically to the font's em box so every name
    # shares the same baseline/height regardless of which glyphs it contains.
    left, _, right, _ = canvas.getbbox()
    top = em // 2 + int(font.getmetrics()[0] - em * 0.88)
    canvas = canvas.crop((left, top, right, top + em))
    target_h = TEXT_CHAR_H * H
    s = target_h / canvas.height
    new_w = canvas.width * s
    max_w = TEXT_MAX_W * W
    squeeze = min(1.0, max_w / new_w)
    canvas = canvas.resize((max(1, round(new_w * squeeze)), max(1, round(target_h))), Image.LANCZOS)
    layer = Image.new("L", (W, H), 0)
    layer.paste(canvas, (round((W - canvas.width) / 2), round(TEXT_CENTER_Y * H - canvas.height / 2)))
    return layer


def ink(face, coverage, color=INK, engrave=True):
    """Composite a coverage mask as carved, inked lettering.

    1.1.0 legibility: strokes are thickened by about one texel and surrounded by a soft
    dark groove shadow (the former light rim washed out under the key light). The wood
    itself is not darkened.
    """
    out = face.copy()
    bold = ImageChops.lighter(coverage, coverage.filter(ImageFilter.MaxFilter(3)).point(lambda p: int(p * 0.6)))
    if engrave:
        groove = bold.filter(ImageFilter.MaxFilter(5)).filter(ImageFilter.GaussianBlur(1.6))
        groove = ImageChops.subtract(groove, bold)
        shade = Image.new("RGB", face.size, (70, 42, 20))
        out = Image.composite(shade, out, groove.point(lambda p: int(p * 0.45)))
    solid = Image.new("RGB", face.size, color)
    return Image.composite(solid, out, bold)


def finish(face, mask):
    rgba = face.convert("RGBA")
    rgba.putalpha(mask)
    return rgba


def main():
    global OUT, PREVIEW
    ap = argparse.ArgumentParser(description="AF-MilitaryShogi texture generator")
    ap.add_argument("--out", help="write everything into this folder instead of the project (committed textures untouched)")
    ap.add_argument("--simulate-no-kso", action="store_true", help="act as if KSO闘龍 were not installed (fallback test)")
    args = ap.parse_args()
    icon_dir = os.path.join(ROOT, "Assets", "Generated", "Icons")
    manifest_path = os.path.join(ROOT, "Generated", "texture_generation_manifest.json")
    if args.out:
        out_root = os.path.abspath(args.out)
        OUT, PREVIEW = os.path.join(out_root, "Textures"), os.path.join(out_root, "Previews")
        icon_dir = os.path.join(out_root, "Icons")
        manifest_path = os.path.join(out_root, "texture_generation_manifest.json")
    rel_base = os.path.abspath(args.out) if args.out else ROOT   # manifest paths are relative to this
    font_path, font_index, font_family, fallback = choose_font(args.simulate_no_kso)
    if fallback and not args.out:
        print("注意：プロジェクト内の生成済みテクスチャを代替フォントの結果で上書きします（配布版の駒文字とは異なります）。")
    pieces_dir = os.path.join(OUT, "Pieces")
    board_dir = os.path.join(OUT, "Board")
    os.makedirs(pieces_dir, exist_ok=True)
    os.makedirs(board_dir, exist_ok=True)
    os.makedirs(PREVIEW, exist_ok=True)

    mask = outline_mask()
    wood = wood_face()
    x0, y0, x1, y1 = ICON_BOX
    box_w, box_h = (x1 - x0) * W, (y1 - y0) * H
    outputs = []

    for file_id, name, mark in PIECES:
        cover = text_layer(name, font_path, font_index)
        if isinstance(mark, int):
            cover = ImageChops.lighter(cover, stars_layer(mark))
        else:
            icon = fit_into(extract_icon(mark), box_w, box_h)
            layer = Image.new("L", (W, H), 0)
            layer.paste(icon, (round(x0 * W + (box_w - icon.width) / 2), round(y0 * H + (box_h - icon.height) / 2)))
            cover = ImageChops.lighter(cover, layer)
        img = finish(ink(wood, cover), mask)
        path = os.path.join(pieces_dir, "piece_%s.png" % file_id)
        img.save(path, optimize=True)
        outputs.append(path)

    # Back face: same light wood (mirrored grain) with a small burnt-in circle+star emblem.
    back = wood_face(mirror=True)
    emblem = extract_icon("back_emblem")
    size = round(0.25 * W)
    emblem = emblem.resize((size, size), Image.LANCZOS)
    layer = Image.new("L", (W, H), 0)
    cy = 0.58 * H     # visual centre of the pentagon
    layer.paste(emblem, (round((W - size) / 2), round(cy - size / 2)))
    scorch = layer.filter(ImageFilter.GaussianBlur(3)).point(lambda p: p * 0.28)
    back = Image.composite(Image.new("RGB", back.size, (120, 70, 30)), back, scorch)
    back = Image.composite(Image.new("RGB", back.size, BURN), back, layer.filter(ImageFilter.GaussianBlur(0.6)).point(lambda p: p * 0.88))
    path = os.path.join(pieces_dir, "piece_back.png")
    finish(back, mask).save(path, optimize=True)
    outputs.append(path)

    # Side wood: interior strip of the plain piece, tiled for the piece edges.
    side = wood.crop((int(0.3 * W), int(0.3 * H), int(0.7 * W), int(0.9 * H))).resize((128, 256), Image.LANCZOS)
    path = os.path.join(pieces_dir, "piece_side.png")
    side.save(path, optimize=True)
    outputs.append(path)

    # Board / table wood.
    board = Image.open(os.path.join(SRC, BOARD_WOOD)).convert("RGB")
    path = os.path.join(board_dir, "board_wood.png")
    board.resize((1536, 1024), Image.LANCZOS).save(path, optimize=True)
    outputs.append(path)
    table = board.rotate(90, expand=True).resize((1024, 1024), Image.LANCZOS)
    table = Image.blend(table, Image.new("RGB", table.size, (12, 8, 6)), 0.55)
    path = os.path.join(board_dir, "table_wood.png")
    table.save(path, optimize=True)
    outputs.append(path)

    # Engraved "総司令部" label decal for the headquarters (the HQ area itself is Unity geometry).
    font = ImageFont.truetype(font_path, 160, index=font_index)
    text = "総司令部"
    l, t, r, b = font.getbbox(text)
    label = Image.new("L", (r - l + 40, b - t + 40), 0)
    ImageDraw.Draw(label).text((20 - l, 20 - t), text, font=font, fill=255)
    rgba = Image.new("RGBA", label.size, (222, 186, 132, 0))
    rgba.putalpha(label.point(lambda p: int(p * 0.85)))
    path = os.path.join(board_dir, "label_hq.png")
    rgba.save(path, optimize=True)
    outputs.append(path)

    # Title logo (alpha title text only; the banner-with-background variant is not used in game).
    ui_dir = os.path.join(OUT, "UI")
    os.makedirs(ui_dir, exist_ok=True)
    logo = Image.open(os.path.join(SRC, LOGO)).convert("RGBA")
    logo = logo.crop((0, 0, logo.width, 620))  # 1.1.1: keep title + English; exclude bottom copy
    logo = logo.crop(logo.getchannel("A").point(lambda p: 255 if p > 8 else 0).getbbox())
    logo_h = 300
    logo = logo.resize((round(logo.width * logo_h / logo.height), logo_h), Image.LANCZOS)
    path = os.path.join(ui_dir, "title_logo.png")
    logo.save(path, optimize=True)
    outputs.append(path)

    # Application icon: the black outside of the rounded frame becomes transparent (flood fill
    # from the corners), then one PNG per size Unity asks for on Windows.
    icon = Image.open(os.path.join(SRC, APP_ICON)).convert("RGB")
    a = np.asarray(icon).astype(int)
    dark = (a.max(axis=2) < 40)
    outside = np.zeros(dark.shape, bool)
    stack = [(0, 0), (0, dark.shape[1] - 1), (dark.shape[0] - 1, 0), (dark.shape[0] - 1, dark.shape[1] - 1)]
    while stack:
        y, x = stack.pop()
        if y < 0 or x < 0 or y >= dark.shape[0] or x >= dark.shape[1] or outside[y, x] or not dark[y, x]:
            continue
        outside[y, x] = True
        stack.extend(((y + 1, x), (y - 1, x), (y, x + 1), (y, x - 1)))
    alpha = Image.fromarray(np.where(outside, 0, 255).astype("uint8")).filter(ImageFilter.GaussianBlur(1.2))
    icon = icon.convert("RGBA")
    icon.putalpha(alpha)
    icon = icon.crop(alpha.point(lambda p: 255 if p > 8 else 0).getbbox())
    side_px = max(icon.size)
    square = Image.new("RGBA", (side_px, side_px), (0, 0, 0, 0))
    square.paste(icon, ((side_px - icon.width) // 2, (side_px - icon.height) // 2))
    os.makedirs(icon_dir, exist_ok=True)
    for size in ICON_SIZES:
        path = os.path.join(icon_dir, "app_icon_%d.png" % size)
        square.resize((size, size), Image.LANCZOS).save(path, optimize=True)
        outputs.append(path)

    # Contact sheet for human review (outside Assets, not a game resource).
    names = [p for p in outputs if os.path.basename(p).startswith("piece_") and "side" not in p]
    cols = 6
    thumb_w, thumb_h = W // 2, H // 2
    sheet = Image.new("RGB", (cols * (thumb_w + 16) + 16, ((len(names) + cols - 1) // cols) * (thumb_h + 16) + 16), (34, 26, 20))
    for i, p in enumerate(names):
        t = Image.open(p).resize((thumb_w, thumb_h), Image.LANCZOS)
        sheet.paste(t, (16 + (i % cols) * (thumb_w + 16), 16 + (i // cols) * (thumb_h + 16)), t)
    sheet.save(os.path.join(PREVIEW, "piece_contact_sheet.png"))

    manifest = {
        "generator": "Tools/TextureGen/generate_textures.py",
        "inputs": {n: sha256(os.path.join(SRC, n)) for n in (PLAIN_PIECE, ICON_SHEET, BOARD_WOOD, LOGO, APP_ICON)},
        "font": {"requested": KSO_FAMILY, "actual": font_family, "fallback": fallback,
                 "note": ("Fallback system font: the piece lettering differs from the distributed textures."
                          if fallback else
                          "KSO闘龍 installed in the OS of the licensed PC; used for rendering only, never copied or distributed.")},
        "logo_crop": [0, 0, 2172, 620], "logo_note": "Game-only two-line crop; UserProvided original unchanged",
        "face_size": [W, H], "outline_uv": OUTLINE,
        "outputs": {os.path.relpath(p, rel_base).replace("\\", "/"): sha256(p) for p in outputs},
    }
    os.makedirs(os.path.dirname(manifest_path), exist_ok=True)
    with open(manifest_path, "w", encoding="utf-8") as f:
        json.dump(manifest, f, ensure_ascii=False, indent=2)
    print("generated %d files" % len(outputs))


if __name__ == "__main__":
    main()
