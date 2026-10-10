#!/bin/sh
# Пробный APK для телефона (2026-10-10). Копия клиента — C:/CardCityBuild/project: инструменты Android не работают
# с путями, где есть не латинские буквы («Пользователь», «учеба»), поэтому вне репозитория. Открытый редактор основного
# проекта не мешает, его платформа и настройки не меняются. Первый раз долго — Unity пережимает картинки под Android;
# дальше быстрее. Готовый файл — C:/CardCityBuild/CardCity-test.apk, журнал — build.log там же.
#   sh scripts/android/build-test-apk.sh
set -e
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
UNITY="C:/Program Files/Unity/Hub/Editor/6000.6.1f1/Editor/Unity.exe"
SRC="$ROOT/unity/CardGameClient"
OUT="C:/CardCityBuild"
DST="$OUT/project"
mkdir -p "$DST"
# Зеркало Assets, Packages и ProjectSettings; архив аватаров и аварийные бэкапы сцены — не нужны.
for part in Assets Packages ProjectSettings; do
  set +e
  robocopy "$(cygpath -w "$SRC/$part")" "$(cygpath -w "$DST/$part")" //MIR //XD "AvatarsArchive~" _Recovery //NFL //NDL //NJH //NJS //NP >/dev/null
  code=$?
  set -e
  [ $code -lt 8 ] || { echo "robocopy $part failed: $code"; exit 1; }
done
mkdir -p "$DST/Assets/Editor"
cp "$ROOT/scripts/android/AndroidTestBuild.cs" "$DST/Assets/Editor/"
# Диагностика кадров (FrameProbe.cs) — только по просьбе: ANDROID_PROBE=1 sh scripts/android/build-test-apk.sh
[ "$ANDROID_PROBE" = "1" ] && cp "$ROOT/scripts/android/FrameProbe.cs" "$DST/Assets/Scripts/"
rm -f "$OUT/CardCity-test.apk"
# Кэш Gradle и папка Android (отладочная подпись) — тоже по латинским путям: в профиле пользователя они не латинские.
# Ключ adb — тот же, что у пользователя: Unity при сборке поднимает свой adb, и с чужим ключом телефон «отваливается».
mkdir -p "$OUT/android-home"
cp "$USERPROFILE/.android/adbkey" "$USERPROFILE/.android/adbkey.pub" "$OUT/android-home/" 2>/dev/null || true
ANDROID_USER_HOME="$(cygpath -w "$OUT/android-home")" GRADLE_USER_HOME="$(cygpath -w "$OUT/gradle")" ANDROID_APK="$(cygpath -w "$OUT/CardCity-test.apk")" "$UNITY" -batchmode -nographics -projectPath "$(cygpath -w "$DST")" \
  -buildTarget Android -executeMethod AndroidTestBuild.Run -logFile "$(cygpath -w "$OUT/build.log")" || true
grep -E "ANDROID TEST BUILD|error CS|BuildFailedException" "$OUT/build.log" | head -20
[ -f "$OUT/CardCity-test.apk" ] && ls -la "$OUT/CardCity-test.apk"
