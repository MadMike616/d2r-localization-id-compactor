param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\build-localization-id-compactor-1.0.1\release')
)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x C# compiler was not found.' }
$profile = Join-Path $PSScriptRoot 'vanilla-3.4.json.gz'
if (-not (Test-Path -LiteralPath $profile)) { throw 'The bundled vanilla ID/key reference is missing.' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
$null = New-Item -ItemType Directory -Path $output -Force
$executable = Join-Path $output 'D2R-Localization-ID-Compactor.exe'
$framework = Split-Path -Parent $compiler
$arguments = @('/nologo', '/target:exe', '/platform:anycpu', '/optimize+', '/debug-', '/warn:4', '/warnaserror+',
    ('/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')),
    ('/out:' + $executable), ('/resource:' + $profile + ',vanilla-3.4.json.gz'),
    ('/reference:' + (Join-Path $framework 'System.Core.dll')),
    ('/reference:' + (Join-Path $framework 'System.Web.Extensions.dll')),
    (Join-Path $PSScriptRoot 'Compactor.cs'))
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Release compilation failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.txt') -Destination (Join-Path $output 'README.txt') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Destination (Join-Path $output 'LICENSE') -Force
$hash = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $output 'SHA256SUMS.txt'), ($hash + '  D2R-Localization-ID-Compactor.exe' + [Environment]::NewLine), [Text.Encoding]::ASCII)
Write-Output ('Release executable: ' + $executable)
Write-Output ('SHA-256: ' + $hash)
