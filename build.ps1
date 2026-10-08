param(
    [string]$OutputPath,
    [switch]$Console,
    [string]$TestSource,
    [string]$MainType,
    [string]$DotnetPath,
    [ValidateSet('win-x64','win-arm64')][string]$Runtime = 'win-x64'
)
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
if (-not $DotnetPath) {
    $taskLocalSdk = Join-Path (Split-Path -Parent $taskRoot) '_tools\dotnet10\dotnet.exe'
    if (Test-Path -LiteralPath $taskLocalSdk) { $DotnetPath = $taskLocalSdk }
    else { $DotnetPath = (Get-Command dotnet -ErrorAction Stop).Source }
}
$taskVersion = & $DotnetPath --version
if ($LASTEXITCODE -ne 0 -or [int]($taskVersion.Split('.')[0]) -lt 10) { throw 'Для сборки нужен .NET SDK 10 или новее.' }
if ([bool]$TestSource -ne [bool]$MainType) { throw 'Для проверки нужны оба параметра TestSource и MainType.' }
$taskOutput = if ($OutputPath) { [System.IO.Path]::GetFullPath($OutputPath) } else { Join-Path $taskRoot "artifacts\$Runtime\Kitsu.exe" }
$taskDirectory = Split-Path -Parent $taskOutput
$taskName = [System.IO.Path]::GetFileNameWithoutExtension($taskOutput)
$taskArguments = @((Join-Path $taskRoot 'Kitsu.csproj'),'-c','Release','-r',$Runtime,'-o',$taskDirectory,"-p:AssemblyName=$taskName")
if ($Console) {
    $taskArguments += @('-p:ConsoleBuild=true','-p:SelfContained=false','-p:PublishSingleFile=false')
    if ($TestSource) { $taskArguments += @("-p:TestSource=$TestSource","-p:TestMain=$MainType") }
    & $DotnetPath build @taskArguments
} else {
    $taskArguments += @('--self-contained','true')
    & $DotnetPath publish @taskArguments
}
if ($LASTEXITCODE -ne 0) { throw 'Не удалось собрать Кицу на .NET 10.' }
if (-not (Test-Path -LiteralPath $taskOutput)) { throw "Сборка не создала $taskOutput" }
Write-Output "Готово: $taskOutput"

