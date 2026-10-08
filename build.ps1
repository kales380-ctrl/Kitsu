param([string]$OutputPath, [switch]$Console, [string]$TestSource, [string]$MainType)
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskOutput = if ($OutputPath) { $OutputPath } else { Join-Path $taskRoot 'Кицу.exe' }
$taskResources = @(
    "/resource:$taskRoot\assets\kitsu-atlas.png,Kitsu.Atlas",
    "/resource:$taskRoot\assets\kitsu-gait.png,Kitsu.Gait",
    "/resource:$taskRoot\assets\kitsu-spin.png,Kitsu.Spin",
    "/resource:$taskRoot\assets\kitsu-tail.png,Kitsu.Tail",
    "/resource:$taskRoot\assets\kitsu-transitions.png,Kitsu.Transitions",
    "/resource:$taskRoot\assets\kitsu-comfort.png,Kitsu.Comfort",
    "/resource:$taskRoot\assets\kitsu-chew.png,Kitsu.Chew",
    "/resource:$taskRoot\assets\kitsu-ground.png,Kitsu.Ground",
    "/resource:$taskRoot\assets\kitsu-posture.png,Kitsu.Posture",
    "/resource:$taskRoot\assets\kitsu-paws.png,Kitsu.Paws",
    "/resource:$taskRoot\assets\kitsu-ambient.png,Kitsu.Ambient",
    "/resource:$taskRoot\assets\kitsu-rest-voice.png,Kitsu.RestVoice",
    "/resource:$taskRoot\assets\kitsu-ball-play.png,Kitsu.BallPlay",
    "/resource:$taskRoot\assets\kitsu-boar-play.png,Kitsu.BoarPlay",
    "/resource:$taskRoot\assets\kitsu-bone-play.png,Kitsu.BonePlay",
    "/resource:$taskRoot\assets\kitsu-toy-chew.png,Kitsu.ToyChew",
    "/resource:$taskRoot\assets\kitsu-ball-shake.png,Kitsu.BallShake",
    "/resource:$taskRoot\assets\kitsu-boar-shake.png,Kitsu.BoarShake"
)
$taskSources = Get-ChildItem -LiteralPath "$taskRoot\src" -Filter '*.cs' | ForEach-Object { $_.FullName }
$taskEntry = @()
if ($TestSource -and $MainType) { $taskSources += $TestSource; $taskEntry = @("/main:$MainType") }
elseif ($TestSource -or $MainType) { throw 'Для проверки нужны оба параметра TestSource и MainType.' }
$taskTarget = if ($Console) { '/target:exe' } else { '/target:winexe' }
& $compiler /nologo $taskTarget /platform:x64 /optimize+ /codepage:65001 "/out:$taskOutput" @taskResources @taskEntry /reference:System.Windows.Forms.dll /reference:System.Drawing.dll @taskSources
if ($LASTEXITCODE -ne 0) { throw 'Не удалось собрать Кицу.' }
Write-Output "Готово: $taskOutput"
