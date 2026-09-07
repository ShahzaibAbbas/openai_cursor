SOFT BLACK ARROW v3

A complete Windows cursor scheme inspired by your original black arrow.

IMPROVEMENTS
Smooth redrawn contours and rounded hand shapes.
Corrected transparency encoding for clean edges and soft shadows.
Correct diagonal resize directions and precise click points at every size.
The arrow keeps the same size and click point during help and working states.
Static cursors embed 32, 40, 48, 64, 96 and 128 pixel sizes.
Animations embed 32, 40, 48, 64 and 128 pixels; Windows scales intermediate sizes.

EFFECTS
Normal pointer: a gentle blue glow pulse over 3.2 seconds.
Busy and working: a rotating blue highlight at 30 frames per second.
The pointer itself stays still within each animation frame.

INSTALL
Right-click Install.ps1 and choose Run with PowerShell.
It installs all 17 standard Windows cursor roles for your current user.
The active scheme is named Soft Black Arrow v3.

STATIC NORMAL POINTER
Run Install-Static.ps1 to disable the normal pointer's glow animation.
Busy and working remain animated.

RESTORE
Run Restore.ps1 to restore the cursor settings from before the first v3 install.
Keep Install.ps1 alongside it. A persistent copy is also installed under
%LOCALAPPDATA%\CustomCursors\SoftBlackArrowV3.
Your previous cursor files and the backup are retained.

PREVIEWS
scheme-preview.png: all 17 roles on light and dark backgrounds.
effects-preview.gif: an enlarged preview of the three animated states.
windows-render-preview.png: actual Windows rendering at 32 and 64 pixels.

Some applications and websites supply their own cursor graphics, which can
override the standard Windows scheme.
