# Luffy Desktop: guide for AI coding agents (Codex / ChatGPT / Claude)

A custom Windows 11 desktop theme for Raj's PC: a Straw Hat (One Piece-inspired) sunset look, a Mac-style
dock taskbar, macOS-style desktop widgets and a gently animated wallpaper. Everything is plain C# (.NET
Framework 4, compiled with the built-in `csc.exe`) plus Python/Pillow scripts for the artwork.
There are no installers or third-party apps; Raj asked for everything to be "made with coding".

## Layout

| Path | What it is |
|---|---|
| `apps/TaskbarGlass/TaskbarGlass.cs` | Tray app that reshapes the real Windows taskbar (dock / icons-only), auto-hide, pop-in animation, folder-icon self-repair |
| `apps/DeskWidgets/DeskWidgets.cs` | Desktop widgets (folders, to-do, calendar, music, photos, weather, clock, system) and the animated scene |
| `apps/tools/ArrangeDesktop.cs` | One-off tool: turns on desktop auto-arrange through the shell view |
| `art/*.py` | Artwork generators (Pillow + numpy): wallpaper, scene cut-outs, folder icons, widget photos |
| `scripts/build.ps1` | Builds both apps from this folder into `%LOCALAPPDATA%\<App>\` and restarts them |
| `scripts/*.ps1` | Test helpers used while developing (screen capture, dock reveal test, pop timing test) |
| `assets/` | Copies of the generated artwork as it is on the PC: wallpapers, scene cut-outs, folder icons, widget photos. Regenerate with `art/*.py`, then copy here when they change |

## Build and run

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build.ps1                 # both apps
powershell -ExecutionPolicy Bypass -File scripts\build.ps1 -Only DeskWidgets
```

* Compiler is `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`, so **C# 5 only**: no `$"..."`,
  no `?.`, no `nameof`, no expression-bodied members, no tuples.
* Both apps auto-start through `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` (`TaskbarGlass`,
  `DeskWidgets`) and the Startup-folder shortcut `Taskbar Glass.lnk`.
* `DeskWidgets.exe --snapshot out.png` renders wallpaper + scene + widgets into one image and exits. Use it
  to preview changes, because widgets sit behind normal windows and are usually covered.
* `TaskbarGlass.exe --debug` writes per-tick state to `%TEMP%\TaskbarGlass-debug.log`.

## Where things live on the PC

| Thing | Location |
|---|---|
| Wallpapers | `%USERPROFILE%\Pictures\Wallpapers\` (`Straw-Hat-Sunset-4K.jpg` full, `-Base-4K.jpg` without hat/ship for the animated scene) |
| Scene cut-outs | `%USERPROFILE%\Pictures\Wallpapers\Scene\` (`hat.png`, `ship.png`, `scene.json`); user's own ship image goes in `Scene\Your ship\` |
| Crew scene | `%USERPROFILE%\Pictures\Wallpapers\Crew\crew-4K.jpg`: the user's own crew picture, upscaled. **Stays local, never commit it** (it shows copyrighted characters). `settings.ini` `sceneMode=crew` animates petals, twinkling stars, water glints and gulls over it; `sceneMode=sunset` is the original drawn scene |
| Folder icons | `%USERPROFILE%\Pictures\Wallpapers\FolderIcons\` (`*.ico` for Explorer, `*.png` for the widget, `icons.txt` = `Folder name|icon.ico`) |
| Widget photos | `%USERPROFILE%\Pictures\Widgets\photo1.jpg … photo5.jpg` (hot-reloaded) |
| Widget settings | `%LOCALAPPDATA%\DeskWidgets\settings.ini` (city/lat/lon, `pos.<id>`, `scene=1`), `todo.txt` |
| Taskbar settings | `HKCU\Software\TaskbarGlass` (`Floating`, `AutoHide`, `IconsOnly`, `Theme`, …) |
| Shell tweaks | `HKCU\...\Explorer\Advanced`: `HideIcons=1` (desktop icons hidden), `TaskbarAnimations=0` |

## How the tricky parts work (read before changing)

* **Windows 11 ignores taskbar tint.** `SetWindowCompositionAttribute` accent colours have no visible effect
  on this build. Any glass/colour is painted on a separate per-pixel-alpha, click-through, topmost layered
  window (`Overlay`) laid exactly over the taskbar.
* **Taskbar shape = window region.** `FindGroups` reads taskbar items with UI Automation and `SetWindowRgn`
  cuts the real `Shell_TrayWnd` down to a dock pill + tray pill, or (icons-only) one rounded square per icon.
* **UI Automation is slow (~0.8 s) and blind while hidden.** The scan runs on a thread-pool thread every 4 s,
  only while the bar is on screen; results are applied on the UI thread via `Bar.Pending`. While hidden, the
  last good shape is kept.
* **Auto-hide needs the bottom strip.** When hidden only ~2 px of the bar is on screen and Windows watches it
  to bring the bar back. While hidden the region must be a thin full-width strip, never the dock shape,
  otherwise the taskbar can never reappear.
* **Pop-in animation** (`ApplyPopRegion`): when the bar appears, each icon's region grows from its centre with
  an ease-out-back curve, staggered left to right. Frame rate is limited by Explorer repainting.
* **Explorer restarts re-enable auto-hide / reset things.** `EnsureAutoHide` re-applies the setting every 3 s.
* **Widgets** are layered windows forced to the bottom of the z-order (`WM_WINDOWPOSCHANGING` →
  `HWND_BOTTOM`); they redraw only when content changes. Scene sprites do the same, run at 15 fps and pause
  while a maximised/full-screen window covers the desktop.
* **Folder icons**: .NET can't read PNG-compressed `.ico` files, so the widget uses the `.png` twins.
  OneDrive/Explorer sometimes rewrite `desktop.ini`; TaskbarGlass re-applies icons from `icons.txt` every 60 s.
* **DPI**: both apps call `SetProcessDpiAwarenessContext(-4)` (per-monitor v2); all window maths is in
  physical pixels. The PC runs 1920×1080 at 125%.

## Rules from Raj

* Windows-friendly first: no top bar that covers window buttons, Search must stay on the taskbar.
* Keep the Luffy / Straw Hat theme (red, straw gold, ocean blue, sunset orange).
* Don't add third-party desktop apps; build it in code.
* No copyrighted One Piece characters or official artwork may be drawn or downloaded. Only original
  art and the colour palette are allowed. The user may supply their own images.
* Keep CPU low: each app should stay around ≤2% of one core while idle.

## Verifying changes

* `scripts\cap.ps1 -Out x.png -Y 990 -H 90` captures the taskbar strip from the real screen (DPI aware).
* `scripts\docktest.ps1` hides/reveals the taskbar with real mouse input and checks that clicks reach `Shell_TrayWnd`.
  It cannot reveal the bar while a window is full-screen (Windows' own rule).
* `scripts\poptest.ps1` captures frames of the pop-in animation.
