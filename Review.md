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

## Implementation references

The helper uses Windows' documented [settings notifications](https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-settingchange)
and [registry change notifications](https://learn.microsoft.com/en-us/windows/win32/api/winreg/nf-winreg-regnotifychangekeyvalue)
to react to changes. Native replacement uses [SetSystemCursor](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setsystemcursor),
with [cursor creation scaling](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setthreadcursorcreationscaling)
specified for the chosen logical size. Windows documents startup timing under
[Run and RunOnce keys](https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys).
