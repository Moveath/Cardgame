"""Тестовая версия рук для проверки базовых поз в Unity: art3d/hands/test/CardCity_Hands_Test.fbx.
В файле те же сетки и скелет, что в игровом CardCity_Hands.fbx, но клипы только тестовые (gestures.py):
  Pose_Base — поза покоя (Unity берёт из первого клипа положение костей в покое);
  Test_Idle — расслабленная кисть, Test_Fist — кулак (по два одинаковых кадра);
  Test_IdleFistIdle — переход: расслабленная кисть — кулак — расслабленная кисть.
Обе руки в одной позе, сами руки стоят как в покое скелета. Основной CardCity_Hands.blend не сохраняется и игровые
клипы в нём не меняются: копия с тестовыми клипами — art3d/hands/test/CardCity_Hands_Test.blend.
Рядом пишется CardCity_Hands_Test.json — расстояния от запястья до суставов пальцев в отмеченных кадрах: по ним
сцена проверки в Unity (HandGestureTestBuilder.cs) сверяет, что кости встали так же, как в Blender.

Запуск: blender art3d/hands/CardCity_Hands.blend --background --disable-autoexec --python scripts/3d/hands/export_test.py"""
import json
import os
import sys

import bpy

sys.path.insert(0, os.path.dirname(__file__))
import gestures
import hand_anatomy
import poses
import source

FOLDER = os.path.join(source.ROOT, "art3d", "hands", "test")
OUT = os.path.join(FOLDER, "CardCity_Hands_Test.fbx")
EXPORTED = ("Rig_Hands", "Arm_Male_L", "Arm_Male_R", "Arm_Female_L", "Arm_Female_R", "Sleeve_L", "Sleeve_R")
PREFIX = "Test_"
SIDES = ("_L", "_R")
MARKED = [f + n for f in ("Thumb", "Index", "Middle", "Ring", "Pinky") for n in ("01", "02", "03")] + ["Tip" + f for f in ("Thumb", "Index", "Middle", "Ring", "Pinky")]


def reset(armature):
    for bone in armature.pose.bones:
        bone.rotation_mode = "XYZ"
        bone.rotation_euler = (0, 0, 0)
        bone.location = (0, 0, 0)
    bpy.context.view_layer.update()


def distances(armature):
    """Расстояния от запястья до начала каждой кости пальцев, миллиметры: от осей и разворота модели не зависят."""
    bones = armature.pose.bones
    result = {}
    for side in SIDES:
        wrist = bones["Hand" + side].head
        # Кости кончиков есть не у всех пальцев: берутся те, что есть.
        result[side] = {name: round((bones[name + side].head - wrist).length * 1000, 3) for name in MARKED if name + side in bones}
    return result


def bake(armature, name, frames):
    """Клип: ключ на каждый кадр углами Эйлера и положением костей, как в animations.bake."""
    action = bpy.data.actions.new(PREFIX + name)
    action.use_fake_user = True
    armature.animation_data.action = action
    previous, marks = {}, {}
    for number, pose in enumerate(frames):
        reset(armature)
        for side in SIDES:
            hand_anatomy.apply(armature, pose, side)
        bpy.context.view_layer.update()
        marks[number] = distances(armature)
        for bone in armature.pose.bones:
            if bone.name == "Root" or "Socket" in bone.name:
                continue
            euler = bone.matrix_basis.to_euler("XYZ", previous[bone.name]) if bone.name in previous else bone.matrix_basis.to_euler("XYZ")
            previous[bone.name] = euler
            bone.rotation_euler = euler
            bone.keyframe_insert("rotation_euler", frame=number + 1)
            bone.keyframe_insert("location", frame=number + 1)
    armature.animation_data.action = None
    return marks


def main():
    armature = bpy.data.objects["Rig_Hands"]
    armature.animation_data_create()
    armature.animation_data.action = None
    armature.matrix_world.identity()
    bpy.context.scene.render.fps = gestures.FPS
    # В тестовом файле только поза покоя и тестовые клипы: игровые клипы остаются в основном файле.
    for action in [a for a in bpy.data.actions if a.name != poses.ZERO_POSE]:
        bpy.data.actions.remove(action)
    clips, marks = [], []

    def note(name, frames, measured):
        # Плоский список: его читает JsonUtility в Unity.
        clips.append({"name": PREFIX + name, "frames": frames})
        for frame, sides in measured.items():
            for side, bones in sides.items():
                marks.extend({"clip": PREFIX + name, "frame": frame, "side": side, "bone": bone, "mm": mm} for bone, mm in bones.items())

    for name, pose in gestures.POSES.items():
        note(name, 2, {0: bake(armature, name, [pose, pose])[0]})
    for name, clip in gestures.CLIPS.items():
        frames = clip()
        every = bake(armature, name, frames)
        note(name, len(frames), {f: every[f] for f in gestures.CLIP_MARKS[name]})
    reset(armature)
    if hand_anatomy.OVER_SWING:
        print("=== TEST ПРЕДУПРЕЖДЕНИЕ основание большого пальца отведено дальше предела:", hand_anatomy.OVER_SWING)
    os.makedirs(FOLDER, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(FOLDER, "CardCity_Hands_Test.blend"), copy=True)
    with open(os.path.join(FOLDER, "CardCity_Hands_Test.json"), "w", encoding="utf-8") as f:
        json.dump({"fps": gestures.FPS, "clips": clips, "marks": marks}, f)

    for collection in bpy.context.view_layer.layer_collection.children:
        collection.exclude = False
    bpy.ops.object.select_all(action="DESELECT")
    for name in EXPORTED:
        ob = bpy.data.objects[name]
        ob.hide_set(False)
        ob.select_set(True)
    bpy.context.view_layer.objects.active = armature
    armature.data.pose_position = "POSE"
    bpy.context.view_layer.update()
    # Те же настройки, что у игровой выгрузки (export.py).
    bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, object_types={"ARMATURE", "MESH"}, axis_forward="-Z", axis_up="Y",
                             apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL", bake_space_transform=False,
                             use_mesh_modifiers=False, mesh_smooth_type="FACE", use_tspace=True, add_leaf_bones=False,
                             primary_bone_axis="Y", secondary_bone_axis="X", armature_nodetype="NULL",
                             bake_anim=True, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False, bake_anim_use_all_actions=True,
                             bake_anim_force_startend_keying=True, bake_anim_step=1., bake_anim_simplify_factor=0.,
                             path_mode="RELATIVE", embed_textures=False)
    print("=== TEST EXPORTED %s %.1f МБ, клипы %s" % (OUT, os.path.getsize(OUT) / 1024 / 1024, sorted(a.name for a in bpy.data.actions)))


main()
