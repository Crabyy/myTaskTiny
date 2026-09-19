# Publishing an update

The app checks stable, published releases at https://github.com/Crabyy/myTinyTask/releases. A source commit or Git tag alone does not trigger an update popup. Release tags must be `vMAJOR.MINOR.PATCH`, and a release must include `myTinyTask.exe` or `myTinyTask-MAJOR.MINOR.PATCH.zip`.

1. Change the application and update `VERSION`, `CHANGELOG.md`, and the version mentioned in README.md. Use a higher version each time.
2. Build and test locally. Run the full `test.cmd` on an interactive desktop to check real playback; `--hotkey-test` covers non-injecting regression checks, including the updater.
3. Commit the changes, create the matching tag, and push both (example for the next patch):

   ```powershell
   git add src assets tools build.cmd build.ps1 test.cmd VERSION README.md CHANGELOG.md PUBLISHING.md .gitignore
   git commit -m "Release 1.9.1"
   git tag v1.9.1
   git push origin main
   git push origin v1.9.1
   ```

4. With GitHub CLI installed and signed in, run:

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\publish-release.ps1
   ```

The script verifies the clean checkout and pushed tag, builds the release package, runs regression checks, extracts the matching changelog entry as release notes, uploads the EXE, ZIP and checksum to a draft release, then publishes it as latest. If uploading fails, inspect the draft before retrying. Do not replace an existing release's binaries; publish a new version.

Users of version 1.9.0 or later will see the update on their next launch unless they disabled startup checks or skipped that version. They can always use Menu > Check for updates. Download update opens the release page in their browser; installation is manual. Older versions must be updated manually once to get the checker.

The app never downloads and executes an installer automatically. Exported macro EXEs do not check for recorder updates. Startup failures are silent; manual checks show a connection error. Update preferences live in `%LOCALAPPDATA%\myTinyTask\updates.json`; existing recordings and shortcut settings are independent.
