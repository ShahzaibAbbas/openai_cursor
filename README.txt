SOFT BLACK ARROW - PERMANENT WINDOWS SCHEME

This package keeps the refined v3 artwork and animated effects, and adds
persistence after sign-in and pointer-size changes.

PERMANENT FOLDER
%LOCALAPPDATA%\CustomCursors\SoftBlackArrowPermanent

All active cursor paths use this fixed folder. Downloading, extracting or moving
this package later does not change the installed files.

WHAT CHANGED
All 17 standard Windows cursor roles point to the custom files.
Windows themes are prevented from replacing the scheme.
Windows' built-in colored-pointer mode is changed to the file-based scheme.
Your chosen cursor size and stored color value are preserved.
A small event-driven CursorKeeper.exe starts after you sign in and reapplies the
scheme when pointer settings, display settings, or the session change.
The helper has a tray menu for reapplying the scheme, opening the folder, and
pausing protection until the next sign-in. It does not poll continuously.

INSTALL
Right-click Install.ps1 and choose Run with PowerShell.
Administrator privileges are not needed for the current-user installation.

STOP AUTOMATIC PROTECTION
Run Stop-Protection.ps1. This stops the helper and removes its startup entry.
The current cursor remains selected, and you can choose another scheme freely.
Run Install.ps1 to enable protection again.

RESTORE PREVIOUS SETTINGS
Run Restore.ps1. It stops the helper, removes this installation's startup entry,
and restores the exact saved cursor, theme-pointer and pointer-style settings.
Your current size is kept. The backup and files are retained for recovery.

LIMITS
This replaces the standard cursors in your signed-in Windows session.
Windows' protected built-in files are not overwritten. The sign-in/secure desktop
and applications or websites with their own cursor graphics can still differ.
Windows controls startup timing, so reapplication happens after sign-in.
Cursor sizes above 128 pixels are scaled from the supplied artwork.

DIAGNOSTICS
state.json reports the most recent application, size and affected native roles.
keeper.log records event-driven reapplication and errors; its size is bounded.
The full source for the small helper is included as CursorKeeper.cs.
