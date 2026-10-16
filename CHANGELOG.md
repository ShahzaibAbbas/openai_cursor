# Changelog

## v4: Lively

### Added
- **Lively style (new default):** five new animated pointers built from the existing
  artwork at every size, all on the Arrow's 3.2-second rhythm:
  - Help: the **?** badge tilts.
  - Link: glow pulse.
  - Location: the pin hops.
  - Person: presence highlight.
  - Unavailable: damped shake.
- **Styles:** Lively, Classic (the v3 set) and Static, chosen with
  `Install.ps1 -Style`, `Install.cmd <style>`, the new tray **Animation** submenu, or
  `CursorKeeper.exe --style`. The choice is saved in `keeper.ini` and survives reinstalls.
- **Reduced motion:** Static pointers are used while Windows *Animation effects* is
  off. This can be toggled from the tray.
- `CursorKeeper.exe --check` loads every pointer privately without changing anything.
  The installer now runs it as a pre-flight step before switching over.
- Tray icon drawn from the Arrow artwork (it was the generic application icon).
  Double-clicking it shows what is applied.
- `Install.cmd`, `Restore.cmd`, `Stop-Protection.cmd`: double-click launchers that keep
  the window open, so results and errors can be read.
- `docs/index.html`: an interactive test drive of every pointer, with style, size
  and background switches. It works locally or on GitHub Pages.
- `tools/`: CursorForge, the generator/verifier, built with the C# compiler that ships with
  Windows, plus `Build.ps1` to rebuild everything and `Serve-Docs.ps1` for a local
  preview. The artwork pipeline is now reproducible from this repository.
- `lively-preview.gif`, and a `scheme-preview.png` regenerated with per-style labels.
- `README.md` (replaces `README.txt`), `.gitattributes`, `.gitignore`.

### Changed
- `Arrow.ani` is stored with identical frames once and replayed through a step table:
  5.06 MB down to 2.74 MB, with the same pixels at every step.
- `validation.json` now covers all 25 files. Each is loaded through Windows at every
  stored size, with every animation step rendered over light and dark backgrounds
  (3,444 renders). Hotspots and step timings are checked against Windows' own reading.
- Registry watchers in `CursorKeeper` re-open their key if it is missing or recreated,
  instead of silently stopping; they also start after the first apply, so the
  `Schemes` key exists on a fresh profile.

### Fixed
- `Install-Static.ps1` always failed: it passed `-Static`, which `Install.ps1` did not accept.
- `Install.ps1` assigned to `$matches`, PowerShell's automatic regex variable.
- Installing while a helper copy from the download folder was running failed with
  the misleading "exited early: 0". That copy is now stopped too, and the message
  explains the situation if another one is still running.

## v3
- Persistent scheme with `CursorKeeper.exe`, registry backup/restore, theme and colour-mode
  protection, explicit size handling. See `Review.md`.
