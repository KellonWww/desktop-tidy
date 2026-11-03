# Build the current source and package a per-user Windows installer. Never runs the application.
[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')]
    [string]$Version = '1.0.0',
    [string]$BuildDir = 'build\notes-interaction-fix',
    [string]$OutputDir = 'dist',
    [string]$IsccPath = '',
    [switch]$SkipBuild,
    [switch]$PrepareOnly
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# Resolve caller-supplied paths relative to this repository, independently of the shell location.
function Get-RepositoryPath([string]$Path) {
    if ([IO.Path]::IsPathRooted($Path)) { return [IO.Path]::GetFullPath($Path) }
    return [IO.Path]::GetFullPath((Join-Path $root $Path))
}

# Resolve an existing compiler only; do not install tools or modify machine-wide configuration.
function Find-InnoCompiler {
    if ($IsccPath) {
        if (-not (Test-Path -LiteralPath $IsccPath -PathType Leaf)) { throw "ISCC not found: $IsccPath" }
        return (Resolve-Path -LiteralPath $IsccPath).Path
    }
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    foreach ($directory in @(${env:ProgramFiles(x86)}, $env:ProgramFiles, "$env:LOCALAPPDATA\Programs")) {
        foreach ($name in @('Inno Setup 7', 'Inno Setup 6')) {
            $candidate = Join-Path $directory "$name\ISCC.exe"
            if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
        }
    }
    throw 'Install Inno Setup 6.3+ from https://jrsoftware.org/isdl.php, or pass -IsccPath. Use -PrepareOnly to prepare without ISCC.'
}

# Verify cached and downloaded executables before they can be embedded in an installer.
function Assert-MicrosoftSignature([string]$Path) {
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch '(?:^|,\s*)O=Microsoft Corporation(?:,|$)') {
        throw "Invalid Microsoft Authenticode signature: $Path ($($signature.Status))"
    }
}

$build = Get-RepositoryPath $BuildDir
$output = Get-RepositoryPath $OutputDir
$installer = Join-Path $output "DesktopTidy-Setup-$Version-win-x64.exe"
if (-not $PrepareOnly -and (Test-Path -LiteralPath $installer)) {
    throw "Refusing to overwrite an existing release: $installer. Use a new version or output directory."
}
$compiler = $null
if (-not $PrepareOnly) { $compiler = Find-InnoCompiler }
Push-Location $root
try {
    if (-not $SkipBuild) {
        # Never replace a running build or terminate a process that may own unsaved drafts.
        foreach ($process in @(Get-Process DesktopTidy -ErrorAction SilentlyContinue)) {
            if ($process.Path -and [IO.Path]::GetFullPath($process.Path) -eq (Join-Path $build 'DesktopTidy.exe')) {
                throw 'Exit DesktopTidy from its tray menu before rebuilding this directory.'
            }
        }
        & python (Join-Path $PSScriptRoot 'prepare-web.py') --output $build
        if ($LASTEXITCODE -ne 0) { throw 'Offline web asset preparation failed.' }
        $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
        if (-not (Test-Path -LiteralPath $csc)) { throw '.NET Framework x64 compiler not found.' }
        # Include current source files, including data-recovery modules, without compiling test tools.
        $sources = @(Get-ChildItem -LiteralPath (Join-Path $root 'src\DesktopTidy') -Recurse -Filter *.cs |
            Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' } | ForEach-Object { $_.FullName })
        $arguments = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/warn:4', '/warnaserror+',
            "/out:$build\DesktopTidy.exe", '/win32manifest:src/DesktopTidy/app.manifest',
            '/win32icon:src/DesktopTidy/Assets/desktop-tidy.ico',
            '/resource:src/DesktopTidy/Assets/desktop-tidy.ico,DesktopTidy.AppIcon',
            '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll',
            '/r:System.Runtime.Serialization.dll', '/r:System.Web.Extensions.dll',
            '/r:build/web-deps/sdk/Microsoft.Web.WebView2.Core.dll',
            '/r:build/web-deps/sdk/Microsoft.Web.WebView2.WinForms.dll')
        & $csc @arguments @sources
        if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }
        Copy-Item -LiteralPath (Join-Path $root 'src\DesktopTidy\App.config') -Destination (Join-Path $build 'DesktopTidy.exe.config')
    }
    $required = @('DesktopTidy.exe', 'DesktopTidy.exe.config', 'Microsoft.Web.WebView2.Core.dll',
        'Microsoft.Web.WebView2.WinForms.dll', 'WebView2Loader.dll', 'WebView2-LICENSE.txt',
        'Web\index.html', 'Web\workspace.js', 'Web\workspace.css', 'Web\notes.js',
        'Web\vendor\gsap.min.js', 'Web\vendor\ScrollTrigger.min.js', 'Web\vendor\Geist.woff2')
    foreach ($file in $required) {
        if (-not (Test-Path -LiteralPath (Join-Path $build $file) -PathType Leaf)) { throw "Missing installer input: $file" }
    }
    foreach ($file in @('index.html', 'workspace.js', 'workspace.css', 'notes.js')) {
        $sourceHash = (Get-FileHash -LiteralPath (Join-Path $root "src\DesktopTidy\UI\Web\$file") -Algorithm SHA256).Hash
        $buildHash = (Get-FileHash -LiteralPath (Join-Path $build "Web\$file") -Algorithm SHA256).Hash
        if ($sourceHash -ne $buildHash) { throw "Stale packaged web asset: $file. Rebuild without -SkipBuild." }
    }
    $dependencies = Join-Path $root 'build\installer-deps'
    New-Item -ItemType Directory -Path $dependencies -Force | Out-Null
    $bootstrapper = Join-Path $dependencies 'MicrosoftEdgeWebview2Setup.exe'
    if (-not (Test-Path -LiteralPath $bootstrapper)) {
        # The helper downloads the Evergreen runtime at installation time; this is not an offline runtime bundle.
        Invoke-WebRequest -UseBasicParsing -Uri 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile $bootstrapper
    }
    Assert-MicrosoftSignature $bootstrapper
    Write-Output "Application and signed WebView2 bootstrapper prepared: $build"
    if (-not $PrepareOnly) {
        New-Item -ItemType Directory -Path $output -Force | Out-Null
        & $compiler "/DAppVersion=$Version" "/DBuildDir=$build" "/DBootstrapper=$bootstrapper" "/DReleaseDir=$output" (Join-Path $root 'installer\DesktopTidy.iss')
        if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }
        if (-not (Test-Path -LiteralPath $installer -PathType Leaf)) { throw 'Installer compiler did not produce the expected file.' }
        $hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
        $manifest = "$installer.sha256"
        [IO.File]::WriteAllText($manifest, "$hash  $([IO.Path]::GetFileName($installer))`n", (New-Object Text.UTF8Encoding($false)))
        Write-Output "Installer (unsigned): $installer"
        Write-Output "SHA256: $manifest"
    }
}
finally { Pop-Location }
