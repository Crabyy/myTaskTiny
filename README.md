# myTinyTask

A portable Windows app for recording and replaying mouse and keyboard actions. Built by [Craby](https://github.com/Crabyy), inspired by TinyTask.

[Download the latest release](https://github.com/Crabyy/myTinyTask/releases/latest) · [Changelog](CHANGELOG.md) · [Report an issue](https://github.com/Crabyy/myTinyTask/issues)

## Download and run

Download **myTinyTask.exe** from the release page and place it in a writable folder. No installation is required. The ZIP includes the executable, source code, and documentation.

**Requirements:** Windows with .NET Framework 4.5 or later.

### Windows SmartScreen

The current release is **not digitally signed**. Windows may display **“Windows protected your PC”** and identify the publisher as **“Unknown publisher”** when you open it. This is a SmartScreen reputation warning, not by itself a malware detection or a guarantee that a file is safe. Hosting a file on GitHub does not give it a trusted publisher signature.

Download only from [this repository’s releases](https://github.com/Crabyy/myTinyTask/releases). Each release includes `SHA256SUMS.txt`, which you can use to check that your download matches the published executable:

```powershell
Get-FileHash .\myTinyTask.exe -Algorithm SHA256
```

If you have verified the source and choose to proceed, select **More info → Run anyway**, where Windows permits it. Do not disable SmartScreen or antivirus protection. If Windows reports a specific threat rather than an unrecognized app, stop and investigate that warning separately.

See [Microsoft’s explanation of SmartScreen reputation](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation). Code signing can identify the publisher, but does not guarantee that a new release will avoid reputation warnings.

## Record your first task

1. Open the application you want to use.
2. Press **F8** (or your custom Record key) to start recording, then perform your actions.
3. Press **F8** again to finish.
4. Press **F9** (or your custom Play key) to replay. You have two seconds to focus the target window.
5. Press **F12** (or your custom Stop key) to stop playback or cancel a countdown.

The Record, Play, and Stop buttons perform the same actions. Recording captures mouse movement, button presses, scrolling, and keystrokes. Input in the recorder’s own window is excluded. Use F8 to finish without moving the mouse back to the recorder.

### Keyboard shortcuts

The following are the default assignments:

| Key | Action |
| --- | --- |
| F8 | Start or finish recording |
| F9 | Start playback, or stop an active playback |
| F12 | Stop recording, playback, or a countdown |

Use **Menu → Customize keys** to assign a single key to each action. A custom key replaces that action’s default; the buttons and tooltips show the active assignments. Choose **Restore defaults**, then **Save**, to return to F8, F9, and F12. Each action must have a different key. Modifier combinations such as Ctrl+R are not supported. Existing saved custom keys are kept when you update.

Click **Menu** to open it; click **Menu** again to close it.

These shortcuts are reserved while myTinyTask is open. Close the app to return them to other applications.

## Playback

Choose one of two modes:

| Mode | Behavior |
| --- | --- |
| **Speed** | Replay at 0.25×, 0.5×, 1×, 2×, 4×, or 8× the recorded speed. |
| **Run every** | Replay the entire recording at a set interval in seconds, minutes, or hours, using its original speed. |

The inactive mode’s controls are grayed out. **Repeat** sets the total number of runs; **Loop until stopped** continues until you stop it. **Completed loops** counts finished runs, remains visible after stopping, and resets on the next playback.

For example, to repeat a task every five minutes, select **Run every → 5 minutes**, enable **Loop until stopped**, and press Play. The first run starts after the two-second countdown. The interval is measured between the starts of subsequent runs. If a recording takes longer than the interval, it finishes before the next run begins. Runs do not overlap.

Keep the app running and the computer awake. Playback mode, speed, interval, and repeat settings apply to the current session; they are not included in saved recordings or exported executables.

## Save, open, and export

- **Menu → Save** saves a recording as an `.mtt` file.
- **Menu → Open** loads a recording from disk.
- **Menu → Saved recordings** lists recently saved or opened files, plus `.mtt` files in the app folder and its `Recordings` subfolder. It remembers up to 50 recently used paths. Use **Browse** to add a file saved elsewhere.
- **Menu → Export EXE** creates an executable with the recording embedded. Open it and press Play to run it. Exported macros require the same Windows/.NET environment as the recorder.

Only one recorder or exported macro from the current version can be open in a Windows session. Close the running app before opening an exported macro. Older exports may not enforce this restriction.

## Updates

The app checks GitHub Releases on startup. When a newer stable version is available, it shows the release notes with **Download update**, **Later**, and **Skip this version**. The popup waits until the app is idle and focused.

**Download update** opens the release page in your browser. Save your work, close myTinyTask, and replace its executable with the new download. Keep your recordings and settings files. Updates are not installed automatically.

Use **Menu → Check for updates** to check manually, including versions you previously skipped. To disable startup checks, uncheck **Menu → Check for updates on startup**. Exported macro executables do not check for recorder updates.

## Files and privacy

Recordings stay on your computer. The update checker contacts GitHub for release metadata; it does not upload recordings or keystrokes.

| File | Purpose |
| --- | --- |
| `*.mtt` | Saved mouse and keyboard recordings |
| `myTinyTask.settings.json` | Active keyboard shortcuts, stored beside the app |
| `myTinyTask.recordings.json` | Saved-recordings menu history, stored beside the app |
| `%LOCALAPPDATA%\myTinyTask\updates.json` | Startup update preference and skipped version |

Recordings contain the keys you type. Avoid recording passwords or other sensitive information, and review recordings before sharing them.

## Limitations

- Playback uses screen coordinates. Keep target windows, display scaling, and monitor positions consistent with the recording.
- Timing is approximate and depends on Windows scheduling and the target application’s response time.
- Windows can prevent input from reaching an application running with higher privileges. Some games and protected applications also reject simulated input.
- TinyTask `.rec` files are not supported. myTinyTask is an independent implementation.

## Build and test

Close the app, then run `build.cmd` to compile it with the Windows .NET Framework C# compiler. Source code is in `src/Program.cs`; the icon is in `assets/app.ico`.

| Command | Purpose |
| --- | --- |
| `build.cmd` | Build the main executable |
| `build.cmd -Package` | Build the executable and a versioned release ZIP |
| `build.cmd -Package -NoDeploy` | Create a release package without replacing the main executable |
| `test.cmd` | Run regression checks, including keyboard playback in a temporary test window |
| `myTinyTask.exe --hotkey-test` | Run checks without injecting playback input |

The full test opens a temporary window and types into it; leave that window focused until it finishes. Tests write their results and UI previews to the executable’s folder. They do not replace practical testing in your target applications.

`VERSION` controls executable version metadata. See [PUBLISHING.md](PUBLISHING.md) for the release process.
