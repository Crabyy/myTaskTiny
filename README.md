# myTinyTask

Your own small, portable Windows mouse and keyboard recorder, inspired by TinyTask.

Developed by Craby. The version and credit are shown under **Menu > About myTinyTask…**.

## Start

Double-click **myTinyTask.exe**. Download it from [GitHub Releases](https://github.com/Crabyy/myTinyTask/releases/latest). No installer is needed; keep the app in a writable folder. Windows with .NET Framework 4.5 or newer is required. Only one copy runs at a time; opening it again brings the running window to the front.

1. Open the application you want to automate.
2. Press **F8** to start recording, perform the steps, then press **F8** again to finish.
3. Press **F9** to play. You have two seconds to focus the target application.
4. Press **F12** at any time to stop recording, cancel the countdown, or stop playback.

The window has the same controls. Open **Menu > Customize keys…** to add an extra single key for each action. **F8 (Record), F9 (Play), and F12 (Stop) always remain active outside the settings dialog.** Choose **None**, or click **Reset extras**, to remove additional shortcuts. Click **Save** to remember your choices. Additional keys must be distinct and cannot use another action's default key. Function keys, letters, digits, numpad digits, and selected navigation keys are available. Modifier combinations are not supported. Default and additional keys are reserved while the app is open; close the app to return them to other applications. Keyboard input in the recorder itself and mouse events over its visible window are excluded. For clean recordings, use F8 to finish without moving the mouse back to the toolbar.

## Features

- Records keyboard presses/releases, mouse movement, left/right/middle/side buttons, and vertical/horizontal scrolling.
- Replays with recorded timing and absolute desktop coordinates, including multiple monitors.
- Playback speeds: 0.25x, 0.5x, 1x, 2x, 4x, and 8x.
- Repeat a specific number of times or loop until stopped.
- Save and open `.mtt` recording files.
- Export a standalone EXE with the recording embedded. Open the exported app and press Play; it does not play automatically.
- Compact window with Record, Play, and Stop up front; file actions, Always on top, and key settings live in the Menu button.
- Always-on-top option, unsaved recording prompts, and release of simulated held keys/buttons on stop or completion.

## Practical notes

Keep the target window, display scaling, and monitor arrangement the same as during recording. This uses screen positions, not image recognition. Timing is approximate; mouse movements are sampled at most every 8 ms and playback uses a Windows UI timer.

This is an independent implementation, not a binary or pixel-for-pixel copy. TinyTask `.rec` files are not supported. Shortcut choices are saved beside the executable in `myTinyTask.settings.json` and restored on launch. Exported apps have their own settings file and initially use the default keys. Exported apps require Windows with .NET Framework 4.x, available on supported Windows systems. Elevated apps can block playback from a non-elevated recorder, and some games or protected applications may not accept simulated input. See Microsoft's [SendInput documentation](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput).

Recordings stay local. Input is retained only while recording is enabled, and written only when you save or export. Saved recordings contain the keys you recorded, so avoid including passwords or other secrets.

## Build and verify

Source: `src/Program.cs`. The app icon is `assets/app.ico`; regenerate it with `tools/make-icon.ps1`. Double-click `build.cmd` to rebuild using the Windows .NET Framework C# compiler. Close the running app before rebuilding.

Run `test.cmd` to execute the built-in checks. Tests briefly open a temporary text window and simulate the letter A. Let the test finish without switching windows. Results are saved in `test-results.txt`; a UI rendering is saved as `preview.png`.

Checks cover serialization, malformed timing and invalid-key rejection, native structure sizing, input conversion, Windows hook installation, window lifecycle, real keyboard playback into a temporary text box, repeat count, stop cleanup, countdown cancellation, and standalone EXE compilation. Manual recording across your chosen applications, side mouse buttons, mixed-DPI monitor arrangements, and elevated applications still need practical testing.

Shortcut regression checks: run `myTinyTask.exe --hotkey-test`. These test custom key dispatch, repeat suppression, using Stop while Play is still held, suspension in settings, duplicate/unsupported key validation, and saving/reloading settings without changing your settings file.

## Versioning and releases

The current release is **1.9.0**. `VERSION` is the single source for the build's version; the build generates assembly metadata from it. The window title shows the same version. Windows Properties > Details shows product version `1.9.0` and file version `1.9.0.0`. Exported macro EXEs retain the app version that created them. Recording file schema version remains separately managed as version 1.

For future changes, update `VERSION` and add a dated entry to `CHANGELOG.md`, then run `build.cmd`:

- **Patch** (`1.9.1`): compatible fixes or maintenance.
- **Minor** (`1.10.0`): compatible new features.
- **Major** (`2.0.0`): incompatible changes.

The default build updates only `myTinyTask.exe`. Close the app before rebuilding. The previous EXE is kept temporarily during deployment for rollback on failure, then removed.

Use `build.cmd -Package` when you need a distributable release: it creates `releases/<version>/` and `releases/myTinyTask-<version>.zip` with source, documentation, and a SHA-256 checksum. Add `-NoDeploy` to package without updating the main EXE. Bump VERSION before publishing changed releases. Settings and recordings are excluded; nothing is uploaded. Test the release before distributing it.

The initial 1.0.0 and 1.1.0 entries in the changelog are retrospective labels for the earlier unversioned builds.

## Saved recordings (v1.4.0)

Choose **Menu → Saved recordings → recording name** to load a macro directly, then press **Play** or F9. The menu remembers up to 50 files you save or open, and discovers `.mtt` files beside the app or inside a `Recordings` subfolder. For recordings already saved elsewhere, use **Browse…** once to add them. Missing files are hidden; hover over an entry to see its full path. Unsaved work is protected by the usual Save prompt.

The list persists in `myTinyTask.recordings.json` beside the executable. This is app data used by the menu; keep it to retain your list. Selecting a recording only loads it and does not start playback.

## Run every (v1.5.0)

To replay a recording once every five minutes, load it, select **Run every**, choose **5 minutes**, then check **Loop until stopped** (or set Repeat to a specific number of runs). Press Play. The first run starts after the usual two-second countdown; subsequent runs start five minutes apart. Recorded delays within each run still apply at their original speed (1x). Speed and Run every are mutually exclusive modes: select either radio button and the other mode's inputs are grayed out. Returning to Speed restores your previous speed selection.

The interval applies to the entire recording, including multiple clicks and keystrokes. If a run lasts longer than the interval, the next run starts after it finishes; runs never overlap or accumulate missed runs. F12 stops playback or the waiting countdown. Interval controls are session settings and are not stored in recording files or exported EXEs. Keep the app running and the computer awake for regular timing.

**Completed loops** shows how many full replays finished in the last playback session. It stays visible while waiting and after stopping, and resets when you start playback again. Stopping midway through a replay does not count that incomplete loop.

## Updates

Starting with v1.9.0, the app checks the official GitHub Releases page in the background on launch. When a newer downloadable stable release exists, a popup shows the version and release notes, with **Download update**, **Later**, and **Skip this version**. It waits until the app is idle and focused. Later dismisses the popup until a future launch; Skip remembers that specific version. Use **Menu > Check for updates** to check manually, including skipped versions. Uncheck **Menu > Check for updates on startup** to turn off automatic checks.

Download update opens the official release page. Download the new EXE, save your work, close the app, then replace only `myTinyTask.exe`. Keep your recordings and settings. This version does not install updates automatically. Update preferences are stored in `%LOCALAPPDATA%\myTinyTask\updates.json`. The check sends an HTTPS request to GitHub for release metadata; no recordings or keystrokes are uploaded. Exported macro EXEs do not check for updates.

For maintainers, see [PUBLISHING.md](PUBLISHING.md) for publishing subsequent versions and their release notes.
