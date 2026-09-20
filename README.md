# myTaskTiny

A portable Windows app for recording and replaying mouse and keyboard actions. Built by [Craby](https://github.com/Crabyy), inspired by TinyTask.

[Download the latest release](https://github.com/Crabyy/myTaskTiny/releases/latest) · [Changelog](CHANGELOG.md) · [Report an issue](https://github.com/Crabyy/myTaskTiny/issues)

## Download and run

Download **myTaskTiny.exe** from the release page and place it in a writable folder. No installation is required. The ZIP includes the executable, source code, and documentation.

**Requirements:** Windows with .NET Framework 4.5 or later. Automatic updates and uninstall require Windows PowerShell 4.0 or later; building release ZIPs requires Windows PowerShell 5.0 or later.

### Windows SmartScreen

The current release is **not digitally signed**. Windows may display **“Windows protected your PC”** and identify the publisher as **“Unknown publisher”** when you open it. This is a SmartScreen reputation warning, not by itself a malware detection or a guarantee that a file is safe. Hosting a file on GitHub does not give it a trusted publisher signature.

Download only from [this repository’s releases](https://github.com/Crabyy/myTaskTiny/releases). Each release includes `SHA256SUMS.txt`, which you can use to check that your download matches the published executable:

```powershell
Get-FileHash .\myTaskTiny.exe -Algorithm SHA256
```

If you have verified the source and choose to proceed, select **More info → Run anyway**, where Windows permits it. Do not disable SmartScreen or antivirus protection. If Windows reports a specific threat rather than an unrecognized app, stop and investigate that warning separately.

See [Microsoft’s explanation of SmartScreen reputation](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation). Code signing can identify the publisher, but does not guarantee that a new release will avoid reputation warnings.

## Upgrading from the previous name

Close the old app and place `myTaskTiny.exe` in the same folder as your existing settings. On first launch, it copies the previous shortcut settings, recording history, file-tracking list, and update preferences if the corresponding new files do not already exist. Existing `.mtt` recordings still work. Your old settings are retained; remove the old executable after confirming the new app works.

## Record your first task

1. Open the application you want to use.
2. Press **F8** (or your custom Record key) to start recording, then perform your actions.
3. Press **F8** again to finish.
4. Press **F9** (or your custom Play key) to replay. You have two seconds to focus the target window.
5. Press **F12** (or your custom Stop key) to stop playback or cancel a countdown.

The Record, Play, and Stop buttons perform the same actions. Recording captures mouse movement, button presses, scrolling, and keystrokes. Input in the recorder’s own window is excluded. Use your Record key to finish without moving the mouse back to the recorder.

### Keyboard shortcuts

The following are the default assignments:

| Key | Action |
| --- | --- |
| F8 | Start or finish recording |
| F9 | Start playback, or stop an active playback |
| F12 | Stop recording, playback, or a countdown |

Use **Menu → Customize keys** to assign a single key to each action. A custom key replaces that action’s default; the buttons and tooltips show the active assignments. Choose **Restore defaults**, then **Save**, to return to F8, F9, and F12. Each action must have a different key. Modifier combinations such as Ctrl+R are not supported. Existing saved custom keys are kept when you update.

Click **Menu** to open it; click **Menu** again to close it.

These shortcuts are reserved while myTaskTiny is open, except while an app dialog is active. Close the app to return them to other applications.

## Playback

Choose one of two modes:

| Mode | Behavior |
| --- | --- |
| **Speed** | Replay at 0.25×, 0.5×, 1×, 2×, 4×, or 8× the recorded speed. |
| **Run every** | Replay the entire recording at a set interval in seconds, minutes, or hours, using its original speed. |

The inactive mode’s controls are grayed out. **Repeat** sets the total number of runs; **Loop until stopped** continues until you stop it. **Completed loops** counts finished runs, remains visible after stopping, and resets on the next playback.

For example, to repeat a task every five minutes, select **Run every → 5 minutes**, enable **Loop until stopped**, and press Play. The first run starts after the two-second countdown. The interval is measured between the starts of subsequent runs. If a recording takes longer than the interval, it finishes before the next run begins. Runs do not overlap.

Keep the app running and the computer awake. Playback mode, speed, interval, and repeat settings apply to the current session; they are not included in saved recordings or exported executables.

The **Play** button turns green while a recording is playing and amber during a countdown or an interval wait. **Stop** turns red whenever recording, playback, or a countdown is active; it is disabled when idle. The status line and window title identify the current activity. After playback ends, the status shows **Stopped** or **Finished**.

## Save, open, and export

- **Menu → Save** saves a recording as an `.mtt` file.
- **Menu → Open** loads a recording from disk.
- **Menu → Saved recordings** lists recently saved or opened files, plus `.mtt` files in the app folder and its `Recordings` subfolder. It remembers up to 50 recently used paths. Use **Browse** to add a file saved elsewhere.
- **Menu → Export EXE** creates an executable with the recording embedded. Open it and press Play to run it. Exported macros require the same Windows/.NET environment as the recorder.

Only one recorder or exported macro from the current version can be open in a Windows session. Close the running app before opening an exported macro. Older exports may not enforce this restriction.

## Updates

The app checks GitHub Releases on startup. When a newer stable version is available, it shows the release notes with **Update now**, **Later**, and **Skip this version**. The popup waits until the app is idle and focused.

**Update now** downloads the new executable, checks its SHA-256 checksum and version, then closes and restarts the app. If you have an unsaved recording, it prompts you to save it first. The download shows progress and can be cancelled before installation. Recordings, shortcut settings, recording history, and update preferences are kept. Playback options that apply only to the current session reset after restart.

Automatic installation requires a writable app folder and a release containing both `myTaskTiny.exe` and `SHA256SUMS.txt`. A failed download or verification leaves the current app unchanged. The installer keeps a temporary backup until the restarted app confirms that its window and input hooks are ready. If startup fails, it attempts to restore the previous executable. If recovery cannot finish, it reports the backup location. Downloaded updates are passed to Windows Attachment Services for security policy and download-origin handling. A security failure stops installation. Checksum verification does not provide a publisher signature or guarantee that Windows will allow the app to run.

**Open release page** remains available for manual downloads. Users on v1.10.0 or earlier must update manually once to receive the automatic updater introduced in v1.11.0.

Use **Menu → Check for updates** to check manually, including versions you previously skipped. To disable startup checks, uncheck **Menu → Check for updates on startup**. An update is installed only after you select **Update now**. Exported macro executables do not check for recorder updates.

## Uninstall

Choose **Menu → Uninstall** while recording and playback are stopped. The review lists the executable and settings to remove. Recordings and exported macros are kept unless you select them; **Select all** includes all listed data files. Use **Add files** for older exports or files saved elsewhere that are not listed. Confirming Uninstall permanently deletes the selected files after the app closes.

The app tracks recordings and exports saved from v1.10.0 onward in a `.created-files.json` file beside the executable. Earlier versions did not track every output, so uninstall cannot discover all historical files automatically. Settings and tracked outputs from exported macros are also listed for optional removal. Source code, unrelated files, and nonempty folders remain. Files changed after the review are left in place, and any deletion failures are reported.

## Files and privacy

Recordings stay on your computer. The update checker contacts GitHub for release metadata and, when you choose Update now, downloads the executable and checksum; it does not upload recordings or keystrokes.

| File | Purpose |
| --- | --- |
| `*.mtt` | Saved mouse and keyboard recordings |
| `myTaskTiny.settings.json` | Active keyboard shortcuts, stored beside the app |
| `myTaskTiny.created-files.json` | Paths of saved recordings and exported macros, used for uninstall |
| `myTaskTiny.recordings.json` | Saved-recordings menu history, stored beside the app |
| `%LOCALAPPDATA%\myTaskTiny\updates.json` | Startup update preference and skipped version |

Recordings contain the keys you type. Avoid recording passwords or other sensitive information, and review recordings before sharing them.

## Limitations

- Playback uses screen coordinates. Keep target windows, display scaling, and monitor positions consistent with the recording.
- A recording is limited to 24 hours and 500,000 input events. Recording stops at either limit. Version 1.11.0 raises the file-size limit to 64 MB so recordings within these limits can be saved and reopened.
- Timing is approximate and depends on Windows scheduling and the target application’s response time.
- Windows can prevent input from reaching an application running with higher privileges. Some games and protected applications also reject simulated input.
- TinyTask `.rec` files are not supported. myTaskTiny is an independent implementation.

## Build and test

Close the app, then run `build.cmd` to compile it with the Windows .NET Framework C# compiler. Source code is in `src/Program.cs`; the icon is in `assets/app.ico`.

| Command | Purpose |
| --- | --- |
| `build.cmd` | Build the main executable |
| `build.cmd -Package` | Build the executable and a versioned release ZIP |
| `build.cmd -Package -NoDeploy` | Create a release package without replacing the main executable |
| `test.cmd` | Run regression checks, including keyboard playback in a temporary test window |
| `myTaskTiny.exe --hotkey-test` | Run checks without injecting playback input |

The full test opens a temporary window and types into it; leave that window focused until it finishes. Tests write their results and UI previews to the executable’s folder. They do not replace practical testing in your target applications.

`VERSION` controls executable version metadata. See [PUBLISHING.md](PUBLISHING.md) for the release process.
