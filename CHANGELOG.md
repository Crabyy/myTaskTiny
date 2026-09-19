# Changelog

Application changes, newest first. Versions use `MAJOR.MINOR.PATCH`: breaking changes, features, and fixes respectively.

Version 1.9.0 is the first public GitHub release. Earlier entries document development builds; 1.0.0 and 1.1.0 were assigned retrospectively to builds that did not yet include version metadata.

## 1.9.1 — 2026-09-19

- Custom shortcuts now replace their defaults. Buttons and tooltips show the active keys.
- Added **Restore defaults** to reset recording, playback, and stop to F8, F9, and F12.
- Fixed the Menu button so clicking it again closes the menu.

## 1.9.0 — 2026-09-19

- Added startup update checks and **Menu → Check for updates**.
- Added an update dialog with release notes, **Download update**, **Later**, and **Skip this version**.
- Saved startup-check preferences and skipped versions. Manual checks can still show a skipped release.
- Deferred update dialogs until the app is idle and focused. Failed startup checks do not interrupt the user.
- Update downloads open the GitHub release page for manual installation. Exported macro executables do not check for recorder updates.

## 1.8.0 — 2026-09-19

- Limited the app to one instance per Windows session. Opening it again restores and focuses the existing window.
- Applied the same restriction to exported macros, preventing them from running alongside the recorder or another current-version export.

## 1.7.0 — 2026-09-19

- Added checkmarks for **Always on top** and the selected saved recording.
- Added an About dialog with the app version and developer credit.
- Added developer details to Windows file properties for the recorder and exported macros.

## 1.6.2 — 2026-09-19

- Reduced the window to 400 × 128 pixels by moving the loop counter beside **Run every**.
- Aligned playback controls and kept large loop counts on one line.
- Removed the duplicate completed-loop count from the waiting status.

## 1.6.1 — 2026-09-19

- Made **Speed** and **Run every** mutually exclusive, with inactive controls grayed out.
- Set interval playback to the recording’s original speed. Returning to Speed restores the previous speed selection.

## 1.6.0 — 2026-09-19

- Added a completed-loop counter that remains visible during playback, between runs, and after stopping.
- Counted only finished runs and reset the counter when a new playback starts.

## 1.5.0 — 2026-09-19

- Added **Run every** for replaying whole recordings at intervals measured in seconds, minutes, or hours.
- Measured intervals between run starts and prevented overlapping runs.
- Added a countdown between runs, with support for repeat limits, continuous looping, and immediate cancellation.

## 1.4.0 — 2026-09-19

- Added **Menu → Saved recordings**, including a marker for the loaded file.
- Remembered up to 50 saved or opened recordings across sessions.
- Listed `.mtt` files from the app folder and its `Recordings` subfolder; omitted missing files.
- Validated selected files and prompted before discarding unsaved work.

## 1.3.0 — 2026-09-19

- Replaced the larger window with a compact toolbar and playback controls.
- Moved file actions, key settings, and **Always on top** into the menu.
- Added an application icon to the recorder and exported macros.
- Displayed default shortcuts on the buttons and additional shortcuts in tooltips.

## 1.2.0 — 2026-09-19

- Redesigned the recording controls, shortcut labels, and status display.
- Added separate visual states for recording, countdown, and playback.
- Added **None** for removing an additional shortcut and improved display scaling.
- Disabled the repeat-count input while continuous looping is selected.

## 1.1.2 — 2026-09-19

- Kept F8, F9, and F12 active alongside additional custom shortcuts.
- Prevented custom shortcuts from using another action’s default key.
- Made release packaging optional and removed obsolete generated files.

## 1.1.1 — 2026-09-19

- Introduced the `VERSION` file and consistent version metadata in the app and exported macros.
- Added versioned release packages containing source, documentation, and an executable checksum.
- Added a backup of the previous executable during deployment.

## 1.1.0 — 2026-09-19

- Added configurable recording, playback, and stop keys.
- Saved shortcut preferences and rejected duplicate assignments.
- Handled held shortcuts independently and suppressed repeated activation.

## 1.0.0 — 2026-09-19

- Added mouse and keyboard recording, playback speeds, repeat counts, and continuous looping.
- Added recording files and standalone executable export.
- Added global shortcuts and an emergency stop.
