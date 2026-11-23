# Руки в игре: новая модель (оси сгиба, клип проверки Take_TestClench, клипы новых жестов Take_TestThumbsUp/Down), аниматор
# со слоями PoseTest и GestureTest и проверка партии.
# Всё собирается и проверяется в отдельном проекте screens/qa/shop-project; в игру (unity/CardGameClient) модель, её .meta,
# аниматор и маски копируются, только если сборка аниматора и проверка партии прошли.
# Перед запуском: blender art3d/hands/CardCity_Hands.blend --background --disable-autoexec --python scripts/3d/hands/game_test.py -- store
#                 blender art3d/hands/CardCity_Hands.blend --background --disable-autoexec --python scripts/3d/hands/gesture_smooth.py -- store   (формы кисти жестов, если менялись их позы; GS_KEYS=ThumbsUpSmooth — только «класс»)
#                 blender art3d/hands/CardCity_Hands.blend --background --disable-autoexec --python scripts/3d/hands/gesture_test.py -- store
#                 blender art3d/hands/CardCity_Hands.blend --background --disable-autoexec --python scripts/3d/hands/update_motion.py   (клипы меню «ЖЕСТЫ», если менялись жесты)
#                 blender art3d/hands/CardCity_Hands.blend --background --disable-autoexec --python scripts/3d/hands/export.py
# Запуск из корня репозитория: powershell -ExecutionPolicy Bypass -File scripts/3d/run_hands_in_game.ps1
# Кадры и отчёт — screens/3d-qa (verification.txt, runtime-errors.txt), журналы — screens/qa/hands-animator-log.txt и 3d-log.txt.
# -Fbx <путь от корня> — проверить другую выгрузку (проверочную копию); -NoCopy — только проверить, в игру ничего не копировать.
param([string]$Fbx = "art3d\hands\CardCity_Hands.fbx", [switch]$NoCopy)
$root = (Get-Location).Path
$main = "$root\unity\CardGameClient"
$qa = "$root\screens\qa\shop-project"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.6.1f1\Editor\Unity.exe"
$hands = "Assets\Resources\ThreeD\Hands"

robocopy "$main\Assets\Scripts" "$qa\Assets\Scripts" *.cs *.cs.meta /S /XD Editor /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
foreach ($f in "DurakClubQa.cs", "DurakClubRuntimeQa.cs", "DurakPhoneLayoutQa.cs", "MobileLayoutQa.cs") { Copy-Item "$root\scripts\android\$f" "$qa\Assets\Scripts\Editor\$f" -Force }
Copy-Item "$root\scripts\3d\Table3DQa.cs" "$qa\Assets\Scripts\Editor\Table3DQa.cs" -Force
Copy-Item "$root\scripts\3d\HandsAnimatorBuilder.cs" "$qa\Assets\Scripts\Editor\HandsAnimatorBuilder.cs" -Force
Copy-Item "$root\scripts\3d\Table3DMaterialsBuilder.cs" "$qa\Assets\Scripts\Editor\Table3DMaterialsBuilder.cs" -Force
foreach ($d in "UI\Hub\Hud", "UI\Hub\Durak", "ThreeD") { robocopy "$main\Assets\Resources\$d" "$qa\Assets\Resources\$d" /E /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null }
Copy-Item "$main\Assets\Plugins\CardGame\*.dll" "$qa\Assets\Plugins\CardGame\" -Force
foreach ($f in "PC_RPAsset.asset", "Mobile_RPAsset.asset", "PC_Renderer.asset", "Mobile_Renderer.asset") { Copy-Item "$main\Assets\Settings\$f" "$qa\Assets\Settings\$f" -Force }
# Новая модель — из art3d; её .meta остаётся игровой (тот же guid), список клипов в ней обновит сборка аниматора.
Copy-Item "$root\$Fbx" "$qa\$hands\CardCity_Hands.fbx" -Force
Write-Output "model under test: $Fbx"

$log = "$root\screens\qa\hands-animator-log.txt"
$p = Start-Process -FilePath $unity -ArgumentList "-batchmode", "-force-d3d11", "-burst-disable-compilation", "-projectPath", "`"$qa`"", "-executeMethod", "HandsAnimatorBuilder.Build", "-logFile", "`"$log`"" -Wait -PassThru
Write-Output "animator exit $($p.ExitCode)"
Select-String -Path $log -Pattern "MASK |HANDS ANIMATOR|error CS|Exception|Crash!!!" | Select-Object -First 12 | ForEach-Object { $_.Line.Substring(0, [Math]::Min(1200, $_.Line.Length)) }
if ($p.ExitCode -ne 0) { exit 1 }

$out = "3d-qa"
Remove-Item "$root\screens\$out\verification.txt" -ErrorAction SilentlyContinue
$env:MOBILE_LAYOUT_QA_OUTPUT = $out
$log = "$root\screens\qa\3d-log.txt"
$p = Start-Process -FilePath $unity -ArgumentList "-batchmode", "-force-d3d11", "-burst-disable-compilation", "-projectPath", "`"$qa`"", "-executeMethod", "MobileLayoutQa.RunTable3D", "-logFile", "`"$log`"" -Wait -PassThru
Write-Output "table exit $($p.ExitCode)"
if (Test-Path "$root\screens\$out\verification.txt") { Get-Content "$root\screens\$out\verification.txt" -Encoding UTF8 | Select-Object -First 30 | ForEach-Object { $_.Substring(0, [Math]::Min(300, $_.Length)) } }
if (Test-Path "$root\screens\$out\runtime-errors.txt") { Get-Content "$root\screens\$out\runtime-errors.txt" -Encoding UTF8 | Select-Object -First 8 | ForEach-Object { $_.Substring(0, [Math]::Min(220, $_.Length)) } }
Select-String -Path $log -Pattern "error CS" | Select-Object -First 10 | ForEach-Object { $_.Line.Substring(0, [Math]::Min(220, $_.Line.Length)) }
if ($p.ExitCode -ne 0) { exit 1 }
if ($NoCopy) { Write-Output "checked only, nothing copied to the game (-NoCopy)"; exit 0 }

foreach ($f in "CardCity_Hands.fbx", "CardCity_Hands.fbx.meta", "HandsAnimator.controller", "HandsAnimator.controller.meta", "Mask_FanArm.mask", "Mask_FanArm.mask.meta", "Mask_FreeHand.mask", "Mask_FreeHand.mask.meta") {
    Copy-Item "$qa\$hands\$f" "$main\$hands\$f" -Force
}
Write-Output "copied to the game: CardCity_Hands.fbx, its .meta, HandsAnimator.controller, masks"
exit 0
