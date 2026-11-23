#!/usr/bin/env bash
# Один и тот же тест candidate_check.py для четырёх вариантов основы рук (2026-11-15). Пути к моделям — как их положил автор.
# Запуск из корня репозитория: bash scripts/3d/hands/candidates_run.sh
# Вывод — screens/3d-hands/candidates/<имя>.log и кадры в screens/3d-hands/candidates/<имя>/.
set -u
BLENDER="${BLENDER:-/c/Program Files/Blender Foundation/Blender 5.2/blender.exe}"
C=art3d/hands/source/candidates
OUT=screens/3d-hands/candidates
mkdir -p "$OUT"

run() {
  local name="$1" file="$2" config="$3"
  CANDIDATE_CONFIG="$config" timeout 900 "$BLENDER" --background --factory-startup --disable-autoexec \
    --python scripts/3d/hands/candidate_check.py -- "$file" "$name" > "$OUT/$name.log" 2>&1
  echo "######## $name"; grep -E "=== (MESH|ARMATURE|ACTION|SIDE|POSE)|Error|Traceback" "$OUT/$name.log" | grep -v "WGT-" | head -40
}

OURS_CHAINS='"chains": {"R": {"thumb": ["Thumb01_R", "Thumb02_R", "Thumb03_R"], "index": ["Index01_R", "Index02_R", "Index03_R"],
  "middle": ["Middle01_R", "Middle02_R", "Middle03_R"], "ring": ["Ring01_R", "Ring02_R", "Ring03_R"], "pinky": ["Pinky01_R", "Pinky02_R", "Pinky03_R"]}}'

# 1. Наша рука как есть.
run card-city art3d/hands/CardCity_Hands.blend "{\"armature\": \"Rig_Hands\", \"mesh\": \"Arm_Male_R\", \"hand\": \"Hand_R\", $OURS_CHAINS}"
# 2. Наша рука с осью сгиба вдоль линии костяшек (flex_converge.py).
run card-city-flex art3d/hands/test/CardCity_Hands_flex.blend "{\"armature\": \"Rig_Hands\", \"mesh\": \"Arm_Male_R\", \"hand\": \"Hand_R\", $OURS_CHAINS}"
# 3. Rigged hand (elenaferfor, CC BY-SA): деформирующие кости *_01–03.R, у каждой есть дубль-контроллер *_Ctrl_*.
run elena "$C/rigged-hand-elena/source/handRig_02.fbx" '{"armature": "Armature", "mesh": "handSmooth.001", "hand": "hand.R",
  "chains": {"R": {"thumb": ["thumb_01.R", "thumb_02.R", "thumb_03.R"], "index": ["index_01.R", "index_02.R", "index_03.R"],
  "middle": ["middle_01.R", "middle_02.R", "middle_03.R"], "ring": ["ring_01.R", "ring_02.R", "ring_03.R"], "pinky": ["pinky_01.R", "pinky_02.R", "pinky_03.R"]}}}'
# 4. Hand Rig (CreativeMachine, CC BY): кости без имён, сетка не привязана к скелету (веса есть, модификатора Armature нет);
#    пальцы в файле гнёт контроллер на весь палец через ограничения — для теста ограничения выключены, фаланги гнутся сами.
run hand-rig "$C/hand-rig/unpacked/Rigged Hands.blend" '{"armature": "Hand_Right", "mesh": "Hand.R", "hand": "Bone.016", "mute": true, "bind": true,
  "chains": {"R": {"thumb": ["Bone.017", "Bone.018", "Bone.019"], "index": ["Bone", "Bone.001", "Bone.002"],
  "middle": ["Bone.003", "Bone.004", "Bone.005"], "ring": ["Bone.006", "Bone.007", "Bone.008"], "pinky": ["Bone.009", "Bone.010", "Bone.011"]}}}'
# 5. Handz Free (ThreeDee): риг Rigify, пальцы гнутся FK-контроллерами, деформируют кости DEF-*. Основная фаланга среднего
#    пальца в риге названа f_pinky.01.R.002 (ошибка имени в исходнике).
run handz-free "$C/handz-free/1. Free Version/Source files/Free Hands.blend" '{"armature": "Rig-Hand", "mesh": "geo_hand_male", "hand": "DEF-hand.R",
  "chains": {"R": {"thumb": ["thumb.01.R", "thumb.02.R", "thumb.03.R"], "index": ["f_index.01.R", "f_index.02.R", "f_index.03.R"],
  "middle": ["f_pinky.01.R.002", "f_middle.02.R", "f_middle.03.R"], "ring": ["f_ring.01.R", "f_ring.02.R", "f_ring.03.R"], "pinky": ["f_pinky.01.R", "f_pinky.02.R", "f_pinky.03.R"]}}}'
