"""Высота кожи кистей над сукном в каждом кадре анимаций Take_*: рука не должна уходить в стол.
Меряется сетка, а не кости: самая низкая вершина кисти и пальцев (без предплечья) у каждой руки. Касание — около 0 мм,
минус — кожа в столе. Прижатая подушечка пальца уходит в ткань до 1,5 мм, мягкое основание большого пальца и ладонь —
до 3,5 мм: глубже считается провалом. Клипы, которых нет в Animator Controller (HandsAnimatorBuilder.cs), помечены:
их провалы в игре не видны.
Запуск: blender art3d/hands/CardCity_Hands.blend --background --disable-autoexec --python scripts/3d/hands/probe_table.py"""
import os
import sys

import bpy
import numpy as np

sys.path.insert(0, os.path.dirname(__file__))
import animations
import in_game

# Клипы, которые играет Animator Controller. Взятие и ход картой в игре ведёт OwnArms3DView, а не клипы Take_DrawCard и Take_PlayCard.
IN_GAME = ("Idle", "TableRest", "HoldCards", "Point", "TapTable", "ThumbsUp", "ThumbsDown", "Shrug", "Fist")
# Насколько кожа может быть вдавлена в сукно, мм: пальцы и мягкие части кисти (ладонь, основание большого пальца).
PRESSED, PRESSED_SOFT = 1.5, 3.5
SOFT = ("Hand", "JointHand", "Thumb01", "JointThumb01", "MetaIndex", "MetaMiddle", "MetaRing", "MetaPinky")


class Skin:
    """Сетка одной руки: какая вершина к какой кости относится и как высоко она над сукном в текущем кадре."""

    def __init__(self, side):
        self.arm = bpy.data.objects["Arm_Male" + side]
        names = {group.index: group.name.replace(side, "") for group in self.arm.vertex_groups}
        self.part = []
        for vertex in self.arm.data.vertices:
            group = max(vertex.groups, key=lambda g: g.weight, default=None)
            self.part.append(names[group.group] if group else "")
        self.hand = np.array([bool(p) and not p.startswith("Forearm") for p in self.part])
        self.allowed = np.array([PRESSED_SOFT if p in SOFT else PRESSED for p in self.part])
        self.up = np.array(self.arm.matrix_world.to_3x3().transposed() @ in_game.TABLE_UP)
        self.shift = self.arm.matrix_world.translation.dot(in_game.TABLE_UP) + in_game.EYE_ABOVE_TABLE

    def lowest(self):
        evaluated = self.arm.evaluated_get(bpy.context.evaluated_depsgraph_get())
        mesh = evaluated.to_mesh()
        co = np.empty(len(mesh.vertices) * 3)
        mesh.vertices.foreach_get("co", co)
        evaluated.to_mesh_clear()
        heights = (co.reshape(-1, 3) @ self.up + self.shift) * 1000
        heights[~self.hand] = 1e9
        # Самая низкая вершина с поправкой на то, насколько её часть кисти может быть вдавлена.
        index = int((heights + self.allowed).argmin())
        return float(heights[index]), self.part[index], float(heights[index] + self.allowed[index])


def main():
    armature = bpy.data.objects["Rig_Hands"]
    armature.matrix_world.identity()
    sc = bpy.context.scene
    skins = [("свободная", Skin(in_game.FREE_HAND)), ("с веером", Skin(in_game.FAN_HAND))]
    bad = []
    for name, keys in animations.CLIPS.items():
        armature.animation_data.action = bpy.data.actions[animations.PREFIX + name]
        worst = {}
        for frame in range(1, keys[-1][0] + 2):
            sc.frame_set(frame)
            for label, skin in skins:
                height, part, margin = skin.lowest()
                if margin < worst.get(label, (0, "", 0, 1e9))[3]:
                    worst[label] = (height, part, frame, margin)
        used = name in IN_GAME
        shown = "; ".join("%s %.1f мм (%s, кадр %d)" % ((label,) + worst[label][:3]) for label, _ in skins)
        print("=== CLIP %-11s %s%s" % (name, shown, "" if used else "  [в игре не используется]"))
        if used and min(v[3] for v in worst.values()) < 0:
            bad.append(name)
    print("=== TABLE", "в стол уходят: " + ", ".join(bad) if bad else
          "ни один клип игры не уходит в стол: пальцы не глубже %.1f мм, ладонь и основание большого пальца не глубже %.1f мм" % (PRESSED, PRESSED_SOFT))


if __name__ == "__main__":
    main()
