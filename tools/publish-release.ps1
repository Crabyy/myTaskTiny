# Run after committing, tagging v<VERSION>, and pushing the commit and tag.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
$notesFile = Join-Path ([IO.Path]::GetTempPath()) ('myTaskTiny-notes-' + [Guid]::NewGuid().ToString('N') + '.md')
try {
    $version = (Get-Content VERSION -Raw).Trim()
    $tag = "v$version"
    $dirty = & git status --porcelain
    if ($LASTEXITCODE -ne 0 -or $dirty) { throw 'Commit all source changes before publishing.' }
    $head = & git rev-parse HEAD
    $tagCommit = & git rev-parse "$tag^{commit}"
    if ($LASTEXITCODE -ne 0 -or $head -ne $tagCommit) { throw 'The version tag must point to the current commit.' }
    $remoteTag = & git ls-remote origin "refs/tags/$tag" "refs/tags/$tag^{}"
    if ($LASTEXITCODE -ne 0 -or -not (($remoteTag | Out-String).Contains($head))) { throw 'Push the version tag to origin before publishing.' }
    $changelog = Get-Content CHANGELOG.md -Raw -Encoding UTF8
    $pattern = '(?ms)^## ' + [regex]::Escape($version) + ' [^\r\n]*\r?\n(.*?)(?=^## |\z)'
    $entry = [regex]::Match($changelog,$pattern)
    if (!$entry.Success) { throw 'Add a changelog entry for this version before publishing.' }
    $notes = $entry.Groups[1].Value.Trim() + "`n`nDownload myTaskTiny.exe for the portable Windows app, or the ZIP for the app plus source and documentation. Save your work and close myTaskTiny before replacing its EXE. Keep your .mtt recordings and settings files."
    Set-Content -LiteralPath $notesFile -Value $notes -Encoding UTF8
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Package -NoDeploy
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    $exe = Join-Path $root "releases\$version\myTaskTiny.exe"
    $test = Start-Process -FilePath $exe -ArgumentList '--hotkey-test' -PassThru -Wait
    if ($test.ExitCode -ne 0) { throw 'Regression checks failed; release was not published.' }
    $assets = @($exe,(Join-Path $root "releases\myTaskTiny-$version.zip"),(Join-Path $root "releases\$version\SHA256SUMS.txt"))
    # Keep this alias on every release so v1.9 clients can skip intermediate updates.
    $compatibilityZip = Join-Path $root "releases\myTinyTask-$version.zip"
    Copy-Item -LiteralPath $assets[1] -Destination $compatibilityZip -Force
    $assets += $compatibilityZip
    & gh release create $tag @assets --repo Crabyy/myTaskTiny --verify-tag --draft --title "myTaskTiny $version" --notes-file $notesFile
    if ($LASTEXITCODE -ne 0) { throw 'Release creation failed. Check GitHub before retrying.' }
    & gh release edit $tag --repo Crabyy/myTaskTiny --draft=false --latest
    if ($LASTEXITCODE -ne 0) { throw 'Release exists as a draft. Inspect it on GitHub and publish when ready.' }
} finally { if (Test-Path $notesFile) { Remove-Item -LiteralPath $notesFile }; Pop-Location }
