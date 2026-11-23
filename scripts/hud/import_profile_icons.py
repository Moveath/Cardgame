"""Import the supplied 1448x1086 profile sheet as five Unity sprites, without changing its pixels.

Usage: python scripts/hud/import_profile_icons.py path/to/profile-sheet.png
Rectangles below use the source image's top-left origin; Unity uses the bottom-left.
"""
import re
import shutil
import sys
import uuid
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
HUD = ROOT / "unity/CardGameClient/Assets/Resources/UI/Hub/Hud"
PARTS = {
    "profile-level": (28, 9, 457, 516),
    "profile-played": (475, 106, 965, 459),
    "profile-wins": (966, 106, 1434, 498),
    "profile-rating": (50, 567, 429, 1042),
    "profile-achievements": (521, 570, 904, 1034),
}


def stable_id(name):
    return uuid.uuid5(uuid.NAMESPACE_URL, "cardgame/profile-icons/" + name).hex


def main():
    source = Path(sys.argv[1]).resolve()
    with Image.open(source) as sheet:
        if sheet.size != (1448, 1086) or sheet.mode != "RGBA":
            raise ValueError("Expected the supplied 1448x1086 RGBA profile sheet")
    target = HUD / "profile-icons.png"
    if source != target.resolve():
        shutil.copyfile(source, target)
    template = (HUD / "task-hard.png.meta").read_text(encoding="utf-8")
    prefix = template.split("  spriteSheet:\n")[0]
    prefix = re.sub(r"(?m)^guid: .*", "guid: " + stable_id("texture"), prefix)
    ids = {name: 21300000 + i * 2 for i, name in enumerate(PARTS)}
    table = "  internalIDToNameTable:\n" + "".join(
        f"  - first:\n      213: {ids[name]}\n    second: {name}\n" for name in PARTS
    )
    prefix = re.sub(r"  internalIDToNameTable:.*?(?=  externalObjects:)", table, prefix, flags=re.S)
    prefix = prefix.replace("  spriteMode: 1", "  spriteMode: 2")
    metadata = prefix + "  spriteSheet:\n    serializedVersion: 2\n    sprites:\n"
    for name, (x, top, right, bottom) in PARTS.items():
        metadata += f"""    - serializedVersion: 2
      name: {name}
      rect:
        serializedVersion: 2
        x: {x}
        y: {1086 - bottom}
        width: {right - x}
        height: {bottom - top}
      alignment: 0
      pivot: {{x: 0.5, y: 0.5}}
      border: {{x: 0, y: 0, z: 0, w: 0}}
      customData:
      outline: []
      physicsShape: []
      tessellationDetail: -1
      bones: []
      spriteID: {stable_id(name)}
      internalID: {ids[name]}
      vertices: []
      indices:
      edges: []
      weights: []
"""
    metadata += f"""    outline: []
    customData:
    physicsShape: []
    bones: []
    spriteID: {stable_id('sheet')}
    internalID: 0
    vertices: []
    indices:
    edges: []
    weights: []
    secondaryTextures: []
    spriteCustomMetadata:
      entries: []
    nameFileIdTable:
"""
    metadata += "".join(f"      {name}: {ids[name]}\n" for name in PARTS)
    metadata += """  mipmapLimitGroupName:
  pSDRemoveMatte: 0
  userData: "User supplied profile artwork; five sprite rectangles, original PNG preserved."
  assetBundleName:
  assetBundleVariant:
"""
    target.with_suffix(".png.meta").write_text(metadata, encoding="utf-8")
    print("Imported five sprite regions:", target)


if __name__ == "__main__":
    main()
