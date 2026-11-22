from __future__ import annotations

import argparse
from pathlib import Path
from zipfile import ZipFile

from PIL import Image


TEXTURE_PREFIX = "assets/minecraft/textures/entity/enderman/"
SOURCE_TEXTURES = {
    "enderman.png": TEXTURE_PREFIX + "enderman.png",
    "enderman_eyes.png": TEXTURE_PREFIX + "enderman_eyes.png",
}
SCALE = 8
CANVAS_SIZE = (288, 211)
CENTER_X = CANVAS_SIZE[0] // 2
MODEL_TOP = -8
MODEL_BOTTOM = 12
TOP_MARGIN = CANVAS_SIZE[1] - (MODEL_BOTTOM - MODEL_TOP) * SCALE


def extract_source_textures(jar_path: Path, output_directory: Path) -> dict[str, Image.Image]:
    output_directory.mkdir(parents=True, exist_ok=True)
    textures = {}
    with ZipFile(jar_path) as jar:
        for output_name, archive_name in SOURCE_TEXTURES.items():
            output_path = output_directory / output_name
            output_path.write_bytes(jar.read(archive_name))
            with Image.open(output_path) as image:
                texture = image.convert("RGBA")
            if texture.size != (64, 32):
                raise ValueError(f"Expected a 64x32 Minecraft texture: {archive_name}")
            textures[output_name] = texture
    return textures


def build_portrait(textures: dict[str, Image.Image]) -> Image.Image:
    skin = textures["enderman.png"]
    eyes = textures["enderman_eyes.png"]
    portrait = Image.new("RGBA", CANVAS_SIZE, (0, 0, 0, 0))

    def draw_front_face(
        texture: Image.Image,
        uv: tuple[int, int, int, int],
        x: int,
        model_y: int,
        mirrored: bool = False,
    ) -> None:
        face = texture.crop(uv)
        if mirrored:
            face = face.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
        face = face.resize((face.width * SCALE, face.height * SCALE), Image.Resampling.NEAREST)
        pixel_x = CENTER_X + x * SCALE
        pixel_y = TOP_MARGIN + (model_y - MODEL_TOP) * SCALE
        portrait.alpha_composite(face, (pixel_x, pixel_y))

    # These are front-facing regions from Minecraft Java 1.20.1 EndermanModel.
    draw_front_face(skin, (58, 2, 60, 20), -5, 0)
    draw_front_face(skin, (58, 2, 60, 20), 3, 0, mirrored=True)
    draw_front_face(skin, (36, 20, 44, 32), -4, 0)
    draw_front_face(skin, (8, 8, 16, 16), -4, -8)
    draw_front_face(eyes, (8, 8, 16, 16), -4, -8)
    return portrait


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Extract original Java 1.20.1 Enderman textures and build a customer portrait."
    )
    parser.add_argument("minecraft_jar", type=Path, help="Path to the Minecraft Java 1.20.1 client JAR")
    args = parser.parse_args()

    project_root = Path(__file__).resolve().parents[1]
    source_directory = project_root / "tools" / "OriginalTextures" / "Enderman"
    output_path = project_root / "Textures" / "enderman_customer.png"
    textures = extract_source_textures(args.minecraft_jar, source_directory)
    build_portrait(textures).save(output_path)
    print(f"Saved original source textures to {source_directory}")
    print(f"Saved enderman customer portrait to {output_path}")


if __name__ == "__main__":
    main()
