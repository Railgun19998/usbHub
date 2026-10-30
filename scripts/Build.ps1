param([switch]$SelfContained)
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
[xml]$buildProps = Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props') -Raw
$version = [string]$buildProps.Project.PropertyGroup.Version
if ($version -notmatch '^1\.\d+\.[1-9]\d*$') { throw "Invalid release version: $version" }
[xml]$manifest = Get-Content -LiteralPath (Join-Path $projectRoot 'src\UsbHub.App\app.manifest') -Raw
if ($manifest.assembly.assemblyIdentity.version -ne "$version.0") { throw 'Manifest version must match Directory.Build.props' }
$packageName = "CHUB-ver$version-win-x64"
if (!$SelfContained) { $packageName += '-framework-dependent' }
$artifacts = Join-Path $projectRoot 'artifacts'
$outDir = Join-Path $artifacts ('.publish-' + [Guid]::NewGuid().ToString('N'))
$zipPath = Join-Path $artifacts "$packageName.zip"
& $sdk build (Join-Path $projectRoot 'UsbHub.slnx') -c Release --nologo -m:1 /nodeReuse:false
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
try {
    & $sdk publish (Join-Path $projectRoot 'src\UsbHub.App') -c Release -r win-x64 --self-contained $SelfContained.IsPresent.ToString().ToLowerInvariant() -p:PublishProfile=Portable -o $outDir --nologo -m:1 /nodeReuse:false
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
    $runtimeNote = if ($SelfContained) { '已内置 .NET 运行库，无需另行安装。' } else { '需要安装 Microsoft .NET 10 Desktop Runtime x64。' }
    @"
CHUB ver$version

Windows 11 x64 无线外设电量监控
$runtimeNote

解压后双击 CHUB.exe。单文件版可单独复制 EXE；首次启动会将原生运行库解压到系统临时目录。
关闭窗口默认继续在托盘运行；右键托盘图标可完全退出。
右键点击托盘图标展开玻璃电量面板，可打开主窗口、刷新或退出；点击外部或按 Esc 收起，悬停不会展开。
设置 settings.json 和扫描缓存 scan-state.json 保存在 CHUB.exe 同目录；移动程序时请一并移动这两个文件。
点击刷新会立即读取设备和电量；自动查询每 30 分钟一轮，未知或缓存电量不会触发低电量提醒。
支持范围取决于设备协议；NuPhy Air75 V3 的 2.4 GHz 电量目前未支持。
默认不设置开机自启，不安装服务，不修改外设配置，不上传数据。
"@ | Set-Content -LiteralPath (Join-Path $outDir '使用说明.txt') -Encoding UTF8
    $unexpected = Get-ChildItem -LiteralPath $outDir -Force | Where-Object { $_.Name -notin @('CHUB.exe', '使用说明.txt') }
    if ($unexpected) { throw "Unexpected package content: $($unexpected.Name -join ', ')" }
    $fileVersion = (Get-Item -LiteralPath (Join-Path $outDir 'CHUB.exe')).VersionInfo
    if ($fileVersion.ProductVersion -ne $version -or $fileVersion.FileVersion -ne "$version.0") { throw 'Published EXE version mismatch' }
    $stagedZip = Join-Path $outDir "$packageName.zip"
    Compress-Archive -LiteralPath (Join-Path $outDir 'CHUB.exe'),(Join-Path $outDir '使用说明.txt') -DestinationPath $stagedZip -CompressionLevel Optimal
    Move-Item -LiteralPath $stagedZip -Destination $zipPath -Force
    Get-Item -LiteralPath $zipPath | Select-Object FullName,Length
}
finally {
    $resolvedOutput = [IO.Path]::GetFullPath($outDir)
    $resolvedArtifacts = [IO.Path]::GetFullPath($artifacts) + [IO.Path]::DirectorySeparatorChar
    if (!$resolvedOutput.StartsWith($resolvedArtifacts, [StringComparison]::OrdinalIgnoreCase)) { throw 'Publish cleanup path is outside artifacts' }
    if (Test-Path -LiteralPath $resolvedOutput) { Remove-Item -LiteralPath $resolvedOutput -Recurse -Force }
}
