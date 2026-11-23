# Тестовая сцена базовых поз кисти в Unity: собирает, проверяет и снимает кадры в отдельном проекте
# (screens/qa/shop-project), затем кладёт сцену и тестовую модель в игру, в Assets/HandGestureTest.
# Игровая сцена, игровой CardCity_Hands.fbx и аниматор рук не меняются.
# Перед запуском: blender art3d/hands/CardCity_Hands.blend --background --disable-autoexec --python scripts/3d/hands/export_test.py
# Запуск из корня репозитория: powershell -ExecutionPolicy Bypass -File scripts/3d/run_hand_gesture_test.ps1
$root = (Get-Location).Path
$main = "$root\unity\CardGameClient"
$qa = "$root\screens\qa\shop-project"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.6.1f1\Editor\Unity.exe"
robocopy "$main\Assets\Scripts" "$qa\Assets\Scripts" *.cs *.cs.meta /S /XD Editor /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
robocopy "$main\Assets\Resources\ThreeD" "$qa\Assets\Resources\ThreeD" /E /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
$folder = "$qa\Assets\HandGestureTest"
New-Item -ItemType Directory -Force $folder | Out-Null
Copy-Item "$root\art3d\hands\test\CardCity_Hands_Test.fbx" $folder -Force
Copy-Item "$root\art3d\hands\test\CardCity_Hands_Test.json" $folder -Force
# Тестовая молодая кожа (scripts/3d/hands/skin_smooth.py): импорт как у игровых карт кожи — их .meta копируются
# с новым guid один раз, дальше guid не меняется.
foreach ($kind in "BaseColor", "Normal") {
    $source = "$root\art3d\hands\textures\T_Hand_Male_Smooth_$kind.png"
    if (-not (Test-Path $source)) { continue }
    Copy-Item $source $folder -Force
    $meta = "$folder\T_Hand_Male_Smooth_$kind.png.meta"
    if (-not (Test-Path $meta)) {
        $game = [IO.File]::ReadAllText("$main\Assets\Resources\ThreeD\Hands\T_Hand_Male_Young_$kind.png.meta")
        [IO.File]::WriteAllText($meta, ($game -replace 'guid: [0-9a-f]{32}', ("guid: " + [guid]::NewGuid().ToString("N"))))
    }
}
Copy-Item "$root\scripts\3d\HandGestureTestBuilder.cs" "$qa\Assets\Scripts\Editor\HandGestureTestBuilder.cs" -Force
$out = "$root\screens\3d-hands\stage2\unity"
New-Item -ItemType Directory -Force $out | Out-Null
Remove-Item "$out\unity-check.txt" -ErrorAction SilentlyContinue
$env:HAND_TEST_OUTPUT = $out
$log = "$root\screens\qa\hand-gesture-test-log.txt"
$p = Start-Process -FilePath $unity -ArgumentList "-batchmode", "-force-d3d11", "-burst-disable-compilation", "-projectPath", "`"$qa`"", "-executeMethod", "HandGestureTestBuilder.Run", "-logFile", "`"$log`"" -Wait -PassThru
Write-Output "exit $($p.ExitCode)"
if (Test-Path "$out\unity-check.txt") { Get-Content "$out\unity-check.txt" -Encoding UTF8 | ForEach-Object { $_.Substring(0, [Math]::Min(260, $_.Length)) } }
Select-String -Path $log -Pattern "error CS|Crash!!!" | Select-Object -First 8 | ForEach-Object { $_.Line.Substring(0, [Math]::Min(220, $_.Line.Length)) }
if ($p.ExitCode -eq 0) {
    # В игру идут сцена, тестовая модель, контроллер и материал рукава с их .meta; файл замеров нужен только проверке.
    $target = "$main\Assets\HandGestureTest"
    New-Item -ItemType Directory -Force $target | Out-Null
    robocopy $folder $target /E /XF "CardCity_Hands_Test.json" "CardCity_Hands_Test.json.meta" /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
    Copy-Item "$qa\Assets\HandGestureTest.meta" "$main\Assets\HandGestureTest.meta" -Force
    Write-Output "copied to the game: Assets/HandGestureTest"
}
exit 0
