#!/usr/bin/env python3
"""
Генерує графіку для вікна інсталятора Inno Setup із FamilyTree.App/Resources/app-icon-source.png.

Результат (тека installer/assets):
    wizard-image.bmp            164x314   боковий баннер (Welcome/Finished), 100% DPI
    wizard-image-125.bmp        205x393   125% DPI
    wizard-image-150.bmp        246x471   150% DPI
    wizard-image-200.bmp        328x628   200% DPI
    wizard-small.bmp             55x58    логотип у шапці, 100% DPI
    wizard-small-125/150/200.bmp          відповідні масштаби

Запуск:
    python installer/make-wizard-images.py
Вимоги: Pillow (pip install pillow)

Палітра: #82C596 (світло-зелений) → #15556B (глибокий синій).
"""

from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / "FamilyTree.App" / "Resources" / "app-icon-source.png"
OUT_DIR = Path(__file__).resolve().parent / "assets"

GREEN = (0x82, 0xC5, 0x96)
BLUE = (0x15, 0x55, 0x6B)

# Розміри баннера: (суфікс файлу, ширина, висота)
BANNER_SIZES = [("", 164, 314), ("-125", 205, 393), ("-150", 246, 471), ("-200", 328, 628)]
SMALL_SIZES = [("", 55, 58), ("-125", 69, 73), ("-150", 83, 87), ("-200", 110, 116)]

FONT_BOLD = "/usr/share/fonts/truetype/lato/Lato-Bold.ttf"
FONT_REG = "/usr/share/fonts/truetype/lato/Lato-Regular.ttf"
FONT_FALLBACK_BOLD = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
FONT_FALLBACK_REG = "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"


def load_font(path: str, fallback: str, size: int) -> ImageFont.FreeTypeFont:
    for candidate in (path, fallback):
        try:
            return ImageFont.truetype(candidate, size)
        except OSError:
            continue
    return ImageFont.load_default()


def tree_alpha() -> Image.Image:
    """Витягує силуетну маску дерева з вихідного PNG (темний малюнок на білому тлі)."""
    src = Image.open(SOURCE).convert("L")
    # Чим темніший піксель — тим щільніша маска. Легкий поріг прибирає сірий вініьєтний фон.
    alpha = src.point(lambda v: 0 if v > 236 else min(255, int((236 - v) * 255 / 200)))
    bbox = alpha.getbbox()
    return alpha.crop(bbox) if bbox else alpha


def tree_color() -> Image.Image:
    """Кольорове дерево з прозорим тлом (для маленького логотипа)."""
    src = Image.open(SOURCE).convert("RGB")
    alpha = tree_alpha()
    bbox = Image.open(SOURCE).convert("L").point(lambda v: 0 if v > 236 else 255).getbbox()
    rgba = src.crop(bbox).convert("RGBA")
    rgba.putalpha(alpha.resize(rgba.size, Image.LANCZOS))
    return rgba


def vertical_gradient(size: tuple[int, int], top: tuple, bottom: tuple) -> Image.Image:
    w, h = size
    grad = Image.new("RGB", (1, h))
    px = grad.load()
    for y in range(h):
        t = min(1.0, (y / max(1, h - 1)) / 0.82)  # повний перехід завершується на 82% висоти
        t = t * t * (3 - 2 * t)                    # ease-in-out
        px[0, y] = tuple(round(top[i] + (bottom[i] - top[i]) * t) for i in range(3))
    return grad.resize((w, h), Image.BILINEAR)


def build_banner(width: int, height: int) -> Image.Image:
    ss = 4  # суперсемплінг для гладких країв
    w, h = width * ss, height * ss
    img = vertical_gradient((w, h), GREEN, BLUE).convert("RGBA")

    mask = tree_alpha()

    # 1. Велике напівпрозоре дерево як водяний знак у нижній частині.
    big = mask.resize((int(w * 1.25), int(w * 1.25)), Image.LANCZOS)
    ghost = Image.new("RGBA", (big.width, big.height), (255, 255, 255, 255))
    ghost.putalpha(big.point(lambda v: int(v * 0.10)))
    img.alpha_composite(ghost, (int((w - big.width) / 2), int(h * 0.46)))

    # 2. Основне дерево — біле, у верхній третині.
    main_w = int(w * 0.60)
    main = mask.resize((main_w, main_w), Image.LANCZOS)
    solid = Image.new("RGBA", (main_w, main_w), (255, 255, 255, 255))
    solid.putalpha(main.point(lambda v: int(v * 0.95)))
    img.alpha_composite(solid, (int((w - main_w) / 2), int(h * 0.09)))

    # 3. М'яке затемнення внизу, щоб підпис завжди читався.
    shade_h = int(h * 0.26)
    shade = Image.new("RGBA", (w, shade_h), BLUE + (0,))
    spx = shade.load()
    for y in range(shade_h):
        a = int(150 * (y / max(1, shade_h - 1)) ** 1.4)
        for x in range(w):
            spx[x, y] = BLUE + (a,)
    img.alpha_composite(shade, (0, h - shade_h))

    draw = ImageDraw.Draw(img)

    # 4. Тонка вертикальна акцентна лінія праворуч.
    draw.rectangle([w - 3 * ss, 0, w - 1, h], fill=(255, 255, 255, 55))

    # 5. Підпис унизу.
    title = load_font(FONT_BOLD, FONT_FALLBACK_BOLD, int(17 * ss))
    subtitle = load_font(FONT_REG, FONT_FALLBACK_REG, int(8 * ss))
    pad = int(13 * ss)
    draw.line([pad, h - int(46 * ss), pad + int(24 * ss), h - int(46 * ss)],
              fill=GREEN + (235,), width=max(1, int(2 * ss)))
    draw.text((pad, h - int(38 * ss)), "Family Tree", font=title, fill=(255, 255, 255, 255))
    draw.text((pad, h - int(16 * ss)), "РОДИННЕ ДЕРЕВО", font=subtitle, fill=GREEN + (200,))

    return img.convert("RGB").resize((width, height), Image.LANCZOS)


def build_small(width: int, height: int) -> Image.Image:
    ss = 4
    canvas = Image.new("RGBA", (width * ss, height * ss), (255, 255, 255, 255))
    tree = tree_color()
    side = int(min(width, height) * ss * 0.92)
    tree = tree.resize((side, side), Image.LANCZOS)
    canvas.alpha_composite(tree, ((canvas.width - side) // 2, (canvas.height - side) // 2))
    return canvas.convert("RGB").resize((width, height), Image.LANCZOS)


def main() -> None:
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    for suffix, w, h in BANNER_SIZES:
        path = OUT_DIR / f"wizard-image{suffix}.bmp"
        build_banner(w, h).save(path, "BMP")
        print(f"{path.name}: {w}x{h}")
    for suffix, w, h in SMALL_SIZES:
        path = OUT_DIR / f"wizard-small{suffix}.bmp"
        build_small(w, h).save(path, "BMP")
        print(f"{path.name}: {w}x{h}")


if __name__ == "__main__":
    main()
