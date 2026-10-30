param([switch]$InteractiveTray)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.local\dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.local\packages'
$sdk = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
if (!(Test-Path -LiteralPath $sdk)) { $sdk = (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_ROOT = Split-Path $sdk -Parent
& $sdk run --project (Join-Path $projectRoot 'tests\UsbHub.Tests') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Core tests failed' }
& $sdk build (Join-Path $projectRoot 'src\UsbHub.App') -c Release --nologo -m:1 /nodeReuse:false
if ($LASTEXITCODE -ne 0) { throw 'App build failed' }
$app = Join-Path $projectRoot 'src\UsbHub.App\bin\Release\net10.0-windows10.0.19041.0\UsbHub.exe'
$preview = Join-Path $projectRoot 'artifacts\preview'
$testData = Join-Path $projectRoot 'artifacts\test-data'
$previewArgs = @('--preview', ('"' + $preview + '"'), '--data-dir', ('"' + $testData + '"'))
if ($InteractiveTray) { $previewArgs += '--interactive-tray-test' }
$task = Start-Process -FilePath $app -ArgumentList $previewArgs -WindowStyle Hidden -PassThru
if (!$task.WaitForExit(30000)) { $task.Kill(); throw 'UI smoke test timed out' }
if ($task.ExitCode -ne 0) { throw "UI smoke failed. See $testData\last-error.log" }
Get-Content -LiteralPath (Join-Path $preview 'ui-smoke.json')
