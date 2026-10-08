param([string]$DotnetPath)
$ErrorActionPreference = 'Stop'
$taskProject = Split-Path -Parent $PSScriptRoot
if (-not $DotnetPath) {
    $taskLocalSdk = Join-Path (Split-Path -Parent $taskProject) '_tools\dotnet10\dotnet.exe'
    $DotnetPath = if (Test-Path -LiteralPath $taskLocalSdk) { $taskLocalSdk } else { (Get-Command dotnet -ErrorAction Stop).Source }
}
$taskTestOutput = Join-Path $taskProject 'artifacts\tests'
$taskSpriteCheck = Join-Path $taskTestOutput 'sprites\sprite-check.exe'
$taskBehaviorCheck = Join-Path $taskTestOutput 'behavior\behavior-check.exe'
& "$taskProject\build.ps1" -OutputPath $taskSpriteCheck -Console -DotnetPath $DotnetPath
& $DotnetPath ([System.IO.Path]::ChangeExtension($taskSpriteCheck,'.dll')) --self-test
if ($LASTEXITCODE -ne 0) { throw 'Sprite/physics checks failed.' }
& "$taskProject\build.ps1" -OutputPath $taskBehaviorCheck -Console -DotnetPath $DotnetPath -TestSource "$PSScriptRoot\ToyPlayTest.cs" -MainType KitsuDesktop.ToyPlayTest
& $DotnetPath ([System.IO.Path]::ChangeExtension($taskBehaviorCheck,'.dll'))
if ($LASTEXITCODE -ne 0) { throw 'Toy behavior checks failed.' }

