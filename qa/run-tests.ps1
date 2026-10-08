$ErrorActionPreference = 'Stop'
$taskProject = Split-Path -Parent $PSScriptRoot
$taskWorkspace = Split-Path -Parent $taskProject
$taskSpriteCheck = Join-Path $PSScriptRoot 'sprite-check.exe'
$taskBehaviorCheck = Join-Path $PSScriptRoot 'behavior-check.exe'
Push-Location -LiteralPath $taskWorkspace
try {
    & "$taskProject\build.ps1" -OutputPath $taskSpriteCheck -Console
    & $taskSpriteCheck --self-test
    if ($LASTEXITCODE -ne 0) { throw 'Sprite/physics checks failed.' }
    & "$taskProject\build.ps1" -OutputPath $taskBehaviorCheck -Console -TestSource "$PSScriptRoot\ToyPlayTest.cs" -MainType KitsuDesktop.ToyPlayTest
    & $taskBehaviorCheck
    if ($LASTEXITCODE -ne 0) { throw 'Toy behavior checks failed.' }
}
finally {
    Pop-Location
    if (Test-Path -LiteralPath $taskSpriteCheck) { Remove-Item -LiteralPath $taskSpriteCheck -Force }
    if (Test-Path -LiteralPath $taskBehaviorCheck) { Remove-Item -LiteralPath $taskBehaviorCheck -Force }
}
