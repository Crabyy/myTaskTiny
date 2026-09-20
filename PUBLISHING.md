# Publishing an update

The app checks stable, published releases on [GitHub](https://github.com/Crabyy/myTaskTiny/releases). A source commit or Git tag alone does not trigger an update popup. Release tags must be `vMAJOR.MINOR.PATCH`, and a release must include `myTaskTiny.exe` and `SHA256SUMS.txt` for automatic installation. ZIP-only releases offer manual downloading. The executable product version must match the release tag, and the checksum must name `myTaskTiny.exe`.

1. Change the application and update `VERSION`, `CHANGELOG.md`, and any affected instructions in `README.md`. Use a higher version each time.
2. Build and test locally with antivirus protection enabled. Review any security detection before proceeding; passing functional tests does not resolve an antivirus finding. Run the full `test.cmd` on an interactive desktop to check real playback; `--hotkey-test` covers non-injecting regression checks, including the updater.
3. Summarize the changes and obtain the owner’s approval before committing, pushing, or publishing. Once approved, commit the changes, create the matching tag, and push both (example for v1.11.0):

   ```powershell
   git add AGENTS.md src assets tools build.cmd build.ps1 test.cmd VERSION README.md CHANGELOG.md PUBLISHING.md .gitignore
   git commit -m "Release 1.11.0"
   git tag v1.11.0
   git push origin main
   git push origin v1.11.0
   ```

4. With GitHub CLI installed and signed in, run:

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\publish-release.ps1
   ```

The script verifies the clean checkout and pushed tag, builds the release package, runs regression checks, extracts the matching changelog entry as release notes, uploads the EXE, ZIP and checksum to a draft release, then publishes it as latest. If uploading fails, inspect the draft before retrying. Do not replace an existing release's binaries; publish a new version.

Users of version 1.9.0 or later will see the update on their next launch unless they disabled startup checks or skipped that version. They can always use Menu > Check for updates. Versions through v1.10.0 open the release page for manual installation. Versions from v1.11.0 offer **Update now** to download, verify, replace the executable, and restart. Users must install v1.11.0 or later once before they can use automatic installation.

Installation requires the user to choose **Update now**. The updater uses a temporary PowerShell helper, preserves user files, and keeps a backup until the new app confirms successful initialization. Windows Attachment Services applies download security policy before installation. A release without a valid executable and matching checksum is not installed. Exported macro EXEs do not check for recorder updates. Startup failures are silent; manual checks show a connection error. Update preferences live in `%LOCALAPPDATA%\myTaskTiny\updates.json`; existing recordings and shortcut settings are independent.

## Rename release

The repository is `Crabyy/myTaskTiny`. Keep origin set to `https://github.com/Crabyy/myTaskTiny.git`. Existing GitHub links redirect to the renamed repository; the app also accepts legacy release asset names for compatibility. Commit, push, and publish only after the owner approves the changes.

The publishing script includes a compatibility ZIP under the previous app name in every release. Its contents use the new name. The asset label and release notes identify it as legacy updater compatibility; users need only one ZIP. Keep this asset so installed v1.9 clients can discover the latest release even if they skip v1.10.0. The internal legacy name is retained solely for data migration, update compatibility, and coordinating the single-instance lock with old versions.
