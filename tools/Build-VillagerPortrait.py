from __future__ import annotations

import argparse
from pathlib import Path
from zipfile import ZipFile

from PIL import Image


TEXTURE_PREFIX = "assets/minecraft/textures/entity/villager/"
SOURCE_TEXTURES = {
    "villager_base.png": TEXTURE_PREFIX + "villager.png",
    "villager_type_plains.png": TEXTURE_PREFIX + "type/plains.png",
    "villager_profession_farmer.png": TEXTURE_PREFIX + "profession/farmer.png",
}
SCALE = 8
CANVAS_SIZE = (288, 211)
CENTER_X = CANVAS_SIZE[0] // 2
MODEL_TOP = -10
MODEL_BOTTOM = 12
TOP_MARGIN = CANVAS_SIZE[1] - (MODEL_BOTTOM - MODEL_TOP) * SCALE


def extract_source_textures(jar_path: Path, output_directory: Path) -> list[Image.Image]:
    output_directory.mkdir(parents=True, exist_ok=True)
    textures = []
    with ZipFile(jar_path) as jar:
        for output_name, archive_name in SOURCE_TEXTURES.items():
            texture_data = jar.read(archive_name)
            output_path = output_directory / output_name
            output_path.write_bytes(texture_data)
            with Image.open(output_path) as image:
                texture = image.convert("RGBA")
            if texture.size != (64, 64):
                raise ValueError(f"Expected a 64x64 Minecraft texture: {archive_name}")
            textures.append(texture)
    return textures


def build_portrait(textures: list[Image.Image]) -> Image.Image:
    base, plains_type, farmer_profession = textures
    skin = Image.alpha_composite(Image.alpha_composite(base, plains_type), farmer_profession)
    portrait = Image.new("RGBA", CANVAS_SIZE, (0, 0, 0, 0))
    center_x = CENTER_X

    def draw_front_face(uv: tuple[int, int, int, int], x: int, model_y: int, mirrored: bool = False) -> None:
        face = skin.crop(uv)
        if mirrored:
            face = face.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
        face = face.resize((face.width * SCALE, face.height * SCALE), Image.Resampling.NEAREST)
        portrait.alpha_composite(face, (center_x + x * SCALE, TOP_MARGIN + (model_y - MODEL_TOP) * SCALE))

    # Match the game's 288x211, 100-PPU customer portrait frame. The original
    # customer artwork is a close upper-body portrait, so omit the villager legs.
    # All regions below are front-face UVs from Java 1.20.1 VillagerModel.
    draw_front_face((22, 26, 30, 38), -4, 0)
    draw_front_face((6, 44, 14, 56), -4, 0)
    draw_front_face((48, 26, 52, 34), -8, 1)
    draw_front_face((48, 26, 52, 34), 4, 1, mirrored=True)
    draw_front_face((44, 42, 52, 46), -4, 5)
    draw_front_face((8, 8, 16, 18), -4, -10)
    draw_front_face((40, 8, 48, 18), -4, -10)
    draw_front_face((26, 2, 28, 6), -1, -3)
    draw_front_face((30, 47, 46, 48), -8, -10)
    return portrait


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Extract the original Java 1.20.1 villager textures and build a pixel-exact front portrait."
    )
    parser.add_argument("minecraft_jar", type=Path, help="Path to the Minecraft Java 1.20.1 client JAR")
    args = parser.parse_args()

    project_root = Path(__file__).resolve().parents[1]
    source_directory = project_root / "tools" / "OriginalTextures" / "Villager"
    output_path = project_root / "Textures" / "villager_farmer.png"
    textures = extract_source_textures(args.minecraft_jar, source_directory)
    build_portrait(textures).save(output_path)
    print(f"Saved original source textures to {source_directory}")
    print(f"Saved villager portrait to {output_path}")


if __name__ == "__main__":
    main()
