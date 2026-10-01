# Собирает логику игры и кладёт её в Unity-проект.
# Запускать после любых правок в CardGame.Core или CardGame.Bots.

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$target = Join-Path $root "unity\CardGameClient\Assets\Plugins\CardGame"

dotnet build "$root\src\CardGame.Core\CardGame.Core.csproj" -c Release -v q --nologo
if ($LASTEXITCODE -ne 0) { throw "CardGame.Core build failed; Unity DLLs were not changed." }
dotnet build "$root\src\CardGame.Bots\CardGame.Bots.csproj" -c Release -v q --nologo
if ($LASTEXITCODE -ne 0) { throw "CardGame.Bots build failed; Unity DLLs were not changed." }

New-Item -ItemType Directory -Force -Path $target | Out-Null

Copy-Item "$root\src\CardGame.Core\bin\Release\netstandard2.1\CardGame.Core.dll" $target -Force
Copy-Item "$root\src\CardGame.Bots\bin\Release\netstandard2.1\CardGame.Bots.dll" $target -Force

Write-Host "Логика собрана и скопирована в $target"

