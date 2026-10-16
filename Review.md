# Cursor persistence review

The custom files were present, but the previous installation did not protect the
active cursor state from later Windows settings changes.

## Findings

1. Windows' colored-pointer mode was still enabled: `CursorType=3`, while the
   active scheme paths pointed to v3. That was a competing appearance setting.
2. `ThemeChangesMousePointers=1` allowed theme applications to change pointers.
   The active theme also referenced a different v3 asset generation from the
   active registry paths.
3. The old installer reloaded the scheme once. It had no sign-in or settings-change
   handler to reapply it when Windows replaced the runtime cursors.
4. Default-size native loads returned 32-pixel cursors even with
   `CursorBaseSize=48`. Explicit size handling is needed to retain the selected size.

## Installed correction

- Stable assets in `%LOCALAPPDATA%\CustomCursors\SoftBlackArrowPermanent`.
- All 17 registry roles and native system cursors use the custom set.
- `CursorType=0` lets the file-based scheme supply the appearance.
- Theme pointer replacement is disabled.
- `CursorKeeper.exe` starts for this user after sign-in and reacts to cursor,
  accessibility, display, theme and session notifications. It sleeps when idle.
- The current `CursorBaseSize=48`, `CursorSize=2` and stored color were preserved.
- Original settings are backed up once. Stop and restore scripts are included.

## Verification performed

- All 17 active native cursor images and hotspots matched their installed files.
- Simulated a default-pointer/color-mode reset while selecting a 64-pixel size.
  The helper recovered all 17 roles and preserved 64 pixels.
- Returned to the original 48-pixel setting; all 17 roles still matched.
- Stopped the helper, introduced a default-pointer reset, and launched the exact
  saved startup command. All 17 roles recovered at 48 pixels.
- Executed Restore and reinstalled. The original backup hash was unchanged, and
  the user's current size was preserved.
- A full PC restart was not performed.

## Scope and limitations

Writing `C:\Windows\Cursors\SoftBlackArrow` was denied because this process is
not elevated. No protected Windows cursor originals were overwritten. The
installation replaces the standard cursors in the signed-in user's session;
the secure sign-in desktop and application-defined cursors can still differ.

Windows controls when startup commands run after sign-in. This is not a guarantee
of the custom pointer before sign-in. Stop protection before intentionally
switching to a different cursor scheme or pointer color mode.

## v4 review

### Defects found and fixed

1. `Install-Static.ps1` invoked `Install.ps1 -Static`, but `Install.ps1` declared only
   `-Restore`, so the static install always stopped with a parameter error. `-Style`
   (Lively, Classic, Static) now exists, and `-Static` is accepted as its alias.
2. `Assert-Snapshot` stored results in `$matches`, which is PowerShell's automatic
   variable for regex captures. Renamed.
3. The installer waited only for the helper at the installed path. A copy started
   from the download folder holds the same per-user monitor mutex. The new helper then
   exited with code 0, and the install rolled back with "exited early: 0".
   Both locations are now stopped and awaited, and the message explains the
   remaining case.
4. `RegistryWatch` gave up for good if its key was missing at start-up or was later
   deleted and recreated. It also started before the first apply, so on a fresh profile
   `Control Panel\Cursors\Schemes` was never watched. It now retries, and starts after
   the first apply.
5. "Run with PowerShell" closes the console the moment a script ends, so success
   messages and errors were never readable. The `.cmd` launchers pause.
6. The artwork had no generator in the repository: the `.ani` files could not be
   rebuilt or extended. `tools/CursorForge` decodes and re-encodes all 20 v3 files
   byte for byte, which proves the format handling, and generates the new animations.

### Animation design

- Every new effect is derived per size from that size's own static artwork, so the
  small sizes keep their pixel hinting. The glow model (Gaussian blur of the solid
  shape, sigma = 5% of the size, strength 0.15, raised-cosine pulse) was fitted to the
  v3 `Arrow.ani` peak frame. Its mean error there is under 0.25/255, so the new pulses
  match the original one.
- All looping effects peak at the half-way step of the same 48-step cycle. Steps k and
  48−k are therefore identical and are stored once (`seq ` table). Unavailable holds its
  rest pose with a single long `rate` entry. Windows' own `GetCursorFrameInfo` reports
  exactly the authored step counts and rates.
- The person badge is knocked out of the hand outline. Moving it would expose that
  knockout (verified visually), so Person brightens in place instead of moving.
  The halo fades before the canvas edge so it never ends in a hard line.

### Verification performed for v4

- All 25 files, at every stored size, were loaded with `LoadImage` and every step was
  drawn with `DrawIconEx` into a 32-bit DIB over white and `#1A222E`. That is 3,444
  renders, and none differed from the stored pixels by more than 1/255. All hotspots
  match `cursor-manifest.json`. See `validation.json`.
- `CursorKeeper.exe --check` passed for each style, and failed as intended when a file
  was removed. Unknown styles and arguments are rejected without side effects.
- Not performed in this pass: applying the scheme to the live session, sign-in, or
  restart. These change system pointer settings, so they are for the owner to run
  with `Install.cmd`. The v3 results below still describe that path.

## Implementation references

The helper uses Windows' documented [settings notifications](https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-settingchange)
and [registry change notifications](https://learn.microsoft.com/en-us/windows/win32/api/winreg/nf-winreg-regnotifychangekeyvalue)
to react to changes. Native replacement uses [SetSystemCursor](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setsystemcursor),
with [cursor creation scaling](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setthreadcursorcreationscaling)
specified for the chosen logical size. Windows documents startup timing under
[Run and RunOnce keys](https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys).
