param([switch]$NoDeploy, [switch]$Package)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$version = (Get-Content -LiteralPath (Join-Path $root 'VERSION') -Raw).Trim()
if ($version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') { throw 'VERSION must contain MAJOR.MINOR.PATCH (for example 1.1.1).' }
foreach ($part in $version.Split('.')) { if ([long]$part -gt 65534) { throw 'Each version component must be at most 65534 for Windows executable metadata.' } }
$stage = Join-Path ([IO.Path]::GetTempPath()) ('myTaskTiny-build-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
try {
    $metadata = @"
using System.Reflection;
[assembly: AssemblyTitle("myTaskTiny")]
[assembly: AssemblyProduct("myTaskTiny")]
[assembly: AssemblyCompany("Craby")]
[assembly: AssemblyCopyright("Developed by Craby")]
[assembly: AssemblyDescription("Portable mouse and keyboard macro recorder")]
[assembly: AssemblyVersion("$version.0")]
[assembly: AssemblyFileVersion("$version.0")]
[assembly: AssemblyInformationalVersion("$version")]
"@
    $info = Join-Path $stage 'AssemblyInfo.cs'
    Set-Content -LiteralPath $info -Value $metadata -Encoding UTF8
    $source = Join-Path $root 'src\Program.cs'
    $exe = Join-Path $stage 'myTaskTiny.exe'
    $icon = Join-Path $root 'assets\app.ico'
    if (-not (Test-Path -LiteralPath $icon)) { throw 'assets\app.ico is missing. Run tools\make-icon.ps1 to regenerate it.' }
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    & $compiler /nologo /target:winexe /platform:anycpu /optimize+ "/out:$exe" "/resource:$source,Program.cs" "/resource:$info,AssemblyInfo.cs" "/resource:$icon,app.ico" "/win32icon:$icon" /r:Microsoft.CSharp.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll $source $info
    if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
    $actual = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
    if ($actual.ProductVersion -ne $version -or $actual.FileVersion -ne "$version.0") { throw 'Executable version verification failed.' }
    if ($Package) {
    $release = Join-Path $root "releases\$version"
    if (Test-Path -LiteralPath $release) { Remove-Item -LiteralPath $release -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $release | Out-Null
    Copy-Item -LiteralPath $exe -Destination (Join-Path $release 'myTaskTiny.exe') -Force
    foreach ($name in @('VERSION','README.md','CHANGELOG.md','PUBLISHING.md','AGENTS.md','.gitignore','build.cmd','build.ps1','test.cmd')) { Copy-Item -LiteralPath (Join-Path $root $name) -Destination $release -Force }
    foreach ($name in @('assets','tools','src')) { New-Item -ItemType Directory -Force -Path (Join-Path $release $name) | Out-Null }
    # Only package declared project files; do not sweep in unrelated local tools or signing files.
    foreach ($name in @('assets\app.ico','tools\make-icon.ps1','tools\publish-release.ps1','src\Program.cs')) { Copy-Item -LiteralPath (Join-Path $root $name) -Destination (Join-Path $release $name) -Force }
    $hash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath (Join-Path $release 'SHA256SUMS.txt') -Value "$hash  myTaskTiny.exe" -Encoding ASCII
    $archive = Join-Path $root "releases\myTaskTiny-$version.zip"
    Compress-Archive -Path (Join-Path $release '*') -DestinationPath $archive -Force
    }
    if (!$NoDeploy) {
        $installed = Join-Path $root 'myTaskTiny.exe'
        # Renaming does not replace an old executable that is currently in use.
        $legacyInstalled = Join-Path $root 'myTinyTask.exe'
        $activeLegacy = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $legacyInstalled }
        if ($activeLegacy) { throw 'Close the previous app before building the renamed version.' }
        $backup = $null
        if (Test-Path -LiteralPath $installed) {
            $active = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $installed }
            if ($active) { throw 'Save your recording and close myTaskTiny before rebuilding.' }
            $backup = Join-Path $stage 'previous.exe'
            Move-Item -LiteralPath $installed -Destination $backup
        }
        try { Copy-Item -LiteralPath $exe -Destination $installed }
        catch { if ($backup) { Move-Item -LiteralPath $backup -Destination $installed -Force }; throw }
    }
    Write-Host "Built myTaskTiny v$version."
    if ($Package) { Write-Host "Release: $archive" }
    if (!$NoDeploy) { Write-Host 'The updated app is ready.' }
} finally { Remove-Item -LiteralPath $stage -Recurse -Force }
