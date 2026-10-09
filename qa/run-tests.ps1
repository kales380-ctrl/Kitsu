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
$taskMultipleToyCheck = Join-Path $taskTestOutput 'multiple-toys\multiple-toys-check.exe'
& "$taskProject\build.ps1" -OutputPath $taskMultipleToyCheck -Console -DotnetPath $DotnetPath -TestSource "$PSScriptRoot\MultipleToysTest.cs" -MainType KitsuDesktop.MultipleToysTest
& $DotnetPath ([System.IO.Path]::ChangeExtension($taskMultipleToyCheck,'.dll'))
if ($LASTEXITCODE -ne 0) { throw 'Multiple toy checks failed.' }
$taskHomeCheck = Join-Path $taskTestOutput 'home\home-check.exe'
& "$taskProject\build.ps1" -OutputPath $taskHomeCheck -Console -DotnetPath $DotnetPath -TestSource "$PSScriptRoot\HomeBehaviorTest.cs" -MainType KitsuDesktop.HomeBehaviorTest
& $DotnetPath ([System.IO.Path]::ChangeExtension($taskHomeCheck,'.dll'))
if ($LASTEXITCODE -ne 0) { throw 'Home and meal checks failed.' }
$taskFeedingCheck = Join-Path $taskTestOutput 'feeding\feeding-check.exe'
& "$taskProject\build.ps1" -OutputPath $taskFeedingCheck -Console -DotnetPath $DotnetPath -TestSource "$PSScriptRoot\FeedingGeometryTest.cs" -MainType KitsuDesktop.FeedingGeometryTest
& $DotnetPath ([System.IO.Path]::ChangeExtension($taskFeedingCheck,'.dll')) (Join-Path $taskProject 'docs\kitsu-feeding-v6.0.2.png')
if ($LASTEXITCODE -ne 0) { throw 'Feeding geometry checks failed.' }
$taskLayerCheck = Join-Path $taskTestOutput 'layers\layers-check.exe'
& "$taskProject\build.ps1" -OutputPath $taskLayerCheck -Console -DotnetPath $DotnetPath -TestSource "$PSScriptRoot\HomeWindowLayersTest.cs" -MainType KitsuDesktop.HomeWindowLayersTest
& $DotnetPath ([System.IO.Path]::ChangeExtension($taskLayerCheck,'.dll'))
if ($LASTEXITCODE -ne 0) { throw 'Home window layer checks failed.' }

