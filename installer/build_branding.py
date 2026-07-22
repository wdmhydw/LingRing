from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
ICON = ROOT / "src" / "GongSiOrganizer.RadialPrototype" / "Assets" / "GongSiOrganizer.png"
OUTPUT = Path(__file__).resolve().parent / "assets"


def gradient(size: tuple[int, int], top: tuple[int, int, int], bottom: tuple[int, int, int]) -> Image.Image:
    width, height = size
    image = Image.new("RGB", size)
    pixels = image.load()
    for y in range(height):
        ratio = y / max(1, height - 1)
        color = tuple(round(a + (b - a) * ratio) for a, b in zip(top, bottom))
        for x in range(width):
            pixels[x, y] = color
    return image


def create_side(icon: Image.Image) -> Image.Image:
    image = gradient((164, 314), (238, 246, 255), (211, 231, 253))
    draw = ImageDraw.Draw(image, "RGBA")
    center = (82, 118)
    for radius, width, alpha in ((112, 9, 42), (84, 8, 55), (58, 7, 72)):
        box = (center[0] - radius, center[1] - radius, center[0] + radius, center[1] + radius)
        draw.arc(box, 205, 510, fill=(49, 132, 226, alpha), width=width)
    resized = icon.resize((108, 108), Image.Resampling.LANCZOS)
    image.paste(resized, (28, 64), resized)
    draw.rounded_rectangle((31, 238, 133, 244), radius=3, fill=(49, 132, 226, 180))
    draw.rounded_rectangle((49, 253, 115, 258), radius=2, fill=(105, 157, 217, 105))
    return image


def create_header(icon: Image.Image) -> Image.Image:
    image = gradient((150, 57), (246, 250, 255), (226, 239, 254))
    draw = ImageDraw.Draw(image, "RGBA")
    for radius, alpha in ((62, 28), (45, 38), (30, 52)):
        draw.arc((80 - radius, 28 - radius, 80 + radius, 28 + radius), 205, 500, fill=(49, 132, 226, alpha), width=5)
    resized = icon.resize((46, 46), Image.Resampling.LANCZOS)
    image.paste(resized, (96, 6), resized)
    return image


def main() -> None:
    OUTPUT.mkdir(parents=True, exist_ok=True)
    icon = Image.open(ICON).convert("RGBA")
    create_side(icon).save(OUTPUT / "wizard-side.bmp")
    create_header(icon).save(OUTPUT / "wizard-header.bmp")


if __name__ == "__main__":
    main()
