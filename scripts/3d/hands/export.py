"""Выгружает скелет, обе пары рук и позы: art3d/hands/CardCity_Hands.fbx. Скульптура и проверочная карта не выгружаются.
Запуск: blender art3d/hands/CardCity_Hands.blend --background --disable-autoexec --python scripts/3d/hands/export.py
HANDS_FBX — выгрузить в другой файл (проверочная копия), иначе art3d/hands/CardCity_Hands.fbx.

В Unity модель ставится с разворотом на 180° вокруг вертикали, как стол и стулья: тогда +Y Blender смотрит вперёд."""
import os
import sys

import bpy

sys.path.insert(0, os.path.dirname(__file__))
import fist_smooth
import source

OUT = os.environ.get("HANDS_FBX") or os.path.join(source.ROOT, "art3d", "hands", "CardCity_Hands.fbx")
EXPORTED = ("Rig_Hands", "Arm_Male_L", "Arm_Male_R", "Arm_Female_L", "Arm_Female_R", "Sleeve_L", "Sleeve_R")


# Свойства скелета — веса корректирующих форм: кулака (fist_smooth.py, клип Take_TestClench) и кисти «фу» и «класса»
# (gesture_smooth.py: ThumbsSmooth — клипы Take_TestThumbsDown, ThumbsUpSmooth — Take_TestThumbsUp, с 2026-11-23; по свойству на руку).
WEIGHTS = (fist_smooth.PROP, "thumbs_smooth_L", "thumbs_smooth_R", "thumbsup_smooth_L", "thumbsup_smooth_R")


def zero_fist_smooth(armature, prop=fist_smooth.PROP):
    """Вес формы кулака — свойство скелета, его ключи есть только в клипе проверки. Выгрузка FBX ставит клипы скелету
    по очереди и между ними свойство не сбрасывает: клип без своих ключей получил бы вес, оставшийся от предыдущего.
    Поэтому каждому клипу скелета без ключей веса — один ключ 0 (только в памяти, .blend не сохраняется).
    prop — другое свойство-вес (WEIGHTS)."""
    if prop not in armature:
        return 0
    path = '["%s"]' % prop
    armature[prop] = 0.
    added = 0
    for action in bpy.data.actions:
        for layer in action.layers:
            for strip in layer.strips:
                for bag in strip.channelbags:
                    paths = [fc.data_path for fc in bag.fcurves]
                    if not paths or path in paths:
                        continue
                    try:
                        for p in paths:
                            armature.path_resolve(p)
                    except ValueError:
                        continue
                    bag.fcurves.new(path).keyframe_points.insert(action.frame_range[0], 0.)
                    added += 1
    return added

for collection in bpy.context.view_layer.layer_collection.children:
    collection.exclude = False
bpy.ops.object.select_all(action="DESELECT")
for name in EXPORTED:
    ob = bpy.data.objects[name]
    ob.hide_set(False)
    ob.select_set(True)
armature = bpy.data.objects["Rig_Hands"]
bpy.context.view_layer.objects.active = armature
armature.data.pose_position = "POSE"
for bone in armature.pose.bones:
    bone.rotation_euler = (0, 0, 0)
armature.animation_data.action = None
for prop in WEIGHTS:
    print("=== EXPORT вес формы %s: ключ 0 дописан клипам:" % prop, zero_fist_smooth(armature, prop))
bpy.context.view_layer.update()
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, object_types={"ARMATURE", "MESH"}, axis_forward="-Z", axis_up="Y",
                         apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL", bake_space_transform=False,
                         use_mesh_modifiers=False, mesh_smooth_type="FACE", use_tspace=True, add_leaf_bones=False,
                         primary_bone_axis="Y", secondary_bone_axis="X", armature_nodetype="NULL",
                         bake_anim=True, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False, bake_anim_use_all_actions=True,
                         bake_anim_force_startend_keying=True, bake_anim_step=1., bake_anim_simplify_factor=0.,
                         path_mode="RELATIVE", embed_textures=False)
print("EXPORTED", OUT, round(os.path.getsize(OUT) / 1024 / 1024, 1), "MB")
print("=== EXPORT DONE")
