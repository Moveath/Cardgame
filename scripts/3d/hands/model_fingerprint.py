"""Отпечаток модели рук: что в файле лежит, числами, чтобы сравнить два .blend и доказать, что правка тронула только то,
что должна. Пишет JSON: по каждой сетке — число вершин и граней, сумма координат и хэш координат, хэш весов, UV и
материалы; по каждой кости — начало, конец и крен; картинки с размерами; ключи форм.

Запуск: blender <файл.blend> --background --disable-autoexec --python scripts/3d/hands/model_fingerprint.py -- <куда.json>
Сравнение двух отпечатков: tools/mocap/.venv/Scripts/python.exe -I scripts/3d/hands/model_fingerprint.py <a.json> <b.json>"""
import hashlib
import json
import sys


def digest(values):
    return hashlib.md5(",".join("%.6f" % v for v in values).encode()).hexdigest()


def collect():
    import bpy
    result = {"meshes": {}, "bones": {}, "images": {}, "materials": {}}
    for ob in bpy.data.objects:
        if ob.type == "MESH":
            mesh = ob.data
            co = [c for v in mesh.vertices for c in v.co]
            names = {g.index: g.name for g in ob.vertex_groups}
            weights = hashlib.md5()
            for v in mesh.vertices:
                weights.update(";".join("%s:%.5f" % (names[g.group], g.weight) for g in sorted(v.groups, key=lambda g: names[g.group])).encode())
            uv = [c for layer in mesh.uv_layers for d in layer.data for c in d.uv]
            keys = [k.name for k in mesh.shape_keys.key_blocks] if mesh.shape_keys else []
            result["meshes"][ob.name] = {
                "verts": len(mesh.vertices), "faces": len(mesh.polygons), "co": digest(co), "weights": weights.hexdigest(),
                "uv": digest(uv), "materials": [m.name if m else None for m in mesh.materials], "shape_keys": keys,
                "matrix": digest([c for row in ob.matrix_world for c in row]),
                "modifiers": [m.type for m in ob.modifiers],
            }
        if ob.type == "ARMATURE":
            for bone in ob.data.bones:
                # Крен: угол оси X кости вокруг её оси Y — он один и меняется при правке осей сгиба.
                x = bone.matrix_local.col[0].xyz
                result["bones"][bone.name] = {"head": digest(bone.head_local), "tail": digest(bone.tail_local), "x": [round(c, 5) for c in x],
                                              "parent": bone.parent.name if bone.parent else None, "deform": bone.use_deform}
    for image in bpy.data.images:
        result["images"][image.name] = [image.size[0], image.size[1], image.filepath]
    for material in bpy.data.materials:
        nodes = sorted(n.bl_idname for n in material.node_tree.nodes) if material.node_tree else []
        result["materials"][material.name] = hashlib.md5(json.dumps(nodes).encode()).hexdigest()
    return result


def compare(a, b):
    same = True
    for section in ("meshes", "images", "materials"):
        for name in sorted(set(a[section]) | set(b[section])):
            if a[section].get(name) != b[section].get(name):
                same = False
                print("ОТЛИЧИЕ %s %s: %s -> %s" % (section, name, a[section].get(name), b[section].get(name)))
    moved, rolled = [], []
    for name in sorted(set(a["bones"]) | set(b["bones"])):
        x, y = a["bones"].get(name), b["bones"].get(name)
        if x is None or y is None or x["head"] != y["head"] or x["tail"] != y["tail"] or x["parent"] != y["parent"] or x["deform"] != y["deform"]:
            moved.append(name)
        elif x["x"] != y["x"]:
            rolled.append(name)
    print("сетки, материалы, картинки:", "совпадают" if same else "ОТЛИЧАЮТСЯ")
    print("кости с другим положением, длиной или родителем:", moved or "нет")
    print("кости, у которых изменился только крен (%d): %s" % (len(rolled), ", ".join(rolled)))
    return same and not moved


if __name__ == "__main__":
    if "--" in sys.argv:
        out = sys.argv[sys.argv.index("--") + 1]
        with open(out, "w", encoding="utf-8") as f:
            json.dump(collect(), f, ensure_ascii=False, indent=1)
        print("=== FINGERPRINT", out)
    else:
        with open(sys.argv[1], encoding="utf-8") as fa, open(sys.argv[2], encoding="utf-8") as fb:
            ok = compare(json.load(fa), json.load(fb))
        sys.exit(0 if ok else 1)
