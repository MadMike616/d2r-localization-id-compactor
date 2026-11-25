param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\build-localization-id-compactor-1.6.1\release')
)
$ErrorActionPreference = 'Stop'
$output = [IO.Path]::GetFullPath($OutputDirectory)
$buildRoot = Split-Path -Parent $output
$env:DOTNET_CLI_HOME = Join-Path $buildRoot '.dotnet-home'
if ([string]::IsNullOrWhiteSpace($env:NUGET_PACKAGES)) { $env:NUGET_PACKAGES = Join-Path $buildRoot '.nuget' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$null = New-Item -ItemType Directory -Path $output -Force
& dotnet publish (Join-Path $PSScriptRoot 'D2RLocalizationCompactor.csproj') -c Release -r win-x64 --self-contained true `
    --artifacts-path (Join-Path $buildRoot 'artifacts') -o $output `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:UseSharedCompilation=false -nodeReuse:false
if ($LASTEXITCODE -ne 0) { throw 'Release publish failed. .NET 10 SDK and the Windows x64 runtime packs are required to build.' }
$executable = Join-Path $output 'D2R-Localization-ID-Compactor.exe'
foreach ($name in @('README.txt', 'LICENSE', 'THIRD-PARTY-NOTICES.txt', 'CHANGELOG.txt')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $output $name) -Force
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ThirdParty\DotNet\LICENSE.txt') -Destination (Join-Path $output 'DOTNET-LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ThirdParty\DotNet\THIRD-PARTY-NOTICES.txt') -Destination (Join-Path $output 'DOTNET-THIRD-PARTY-NOTICES.txt') -Force
$hash = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $output 'SHA256SUMS.txt'), ($hash + '  D2R-Localization-ID-Compactor.exe' + [Environment]::NewLine), [Text.Encoding]::ASCII)
Write-Output ('Release executable: ' + $executable)
Write-Output ('SHA-256: ' + $hash)
