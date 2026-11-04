# Compile and stage the release application only; never runs the app or test tools.
[CmdletBinding()]
param([string]$BuildDir = 'build\release')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$build = if ([IO.Path]::IsPathRooted($BuildDir)) { [IO.Path]::GetFullPath($BuildDir) } else { [IO.Path]::GetFullPath((Join-Path $root $BuildDir)) }
Push-Location $root
try {
    & python (Join-Path $PSScriptRoot 'prepare-web.py') --output $build
    if ($LASTEXITCODE -ne 0) { throw 'Offline web asset preparation failed.' }
    $csc = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    if (-not (Test-Path -LiteralPath $csc)) { throw '.NET Framework x64 compiler not found.' }
    # Compile every application module, including recovery; exclude generated and test sources.
    $sources = @(Get-ChildItem -LiteralPath (Join-Path $root 'src/DesktopTidy') -Recurse -Filter *.cs |
        Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' } | ForEach-Object { $_.FullName })
    $arguments = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/warn:4', '/warnaserror+',
        "/out:$build\DesktopTidy.exe", '/win32manifest:src/DesktopTidy/app.manifest',
        '/win32icon:src/DesktopTidy/Assets/desktop-tidy.ico',
        '/resource:src/DesktopTidy/Assets/desktop-tidy.ico,DesktopTidy.AppIcon',
        '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll',
        '/r:System.Runtime.Serialization.dll', '/r:System.Web.Extensions.dll', '/r:Accessibility.dll',
        '/r:build/web-deps/sdk/Microsoft.Web.WebView2.Core.dll',
        '/r:build/web-deps/sdk/Microsoft.Web.WebView2.WinForms.dll')
    & $csc @arguments @sources
    if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }
    Copy-Item -LiteralPath (Join-Path $root 'src/DesktopTidy/App.config') -Destination (Join-Path $build 'DesktopTidy.exe.config')
    Write-Output "Release x64 built: $build\DesktopTidy.exe"
}
finally { Pop-Location }
