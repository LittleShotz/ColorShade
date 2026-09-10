# ColorShade

A Windows 11 x64 tray utility written in C# and WPF. It applies a display profile while a foreground window belongs to `RustClient.exe` or `Rust.exe`, then restores the monitor's captured desktop settings when focus moves away. The full implementation is included; there are no placeholder backends or paid dependencies.

**Support boundary:** AMD saturation uses documented ADL2 APIs when the installed driver exposes them. NVIDIA, Intel, and unsupported AMD configurations use gamma plus a clearly identified **contrast fallback**. Native NVIDIA Digital Vibrance is not implemented. A one-dimensional RGB gamma ramp cannot implement true saturation.

**Recovery boundary:** ColorShade restores the actual pre-activation values, including an existing gamma calibration, rather than assuming factory defaults. A separate guardian and a persistent recovery journal cover ordinary UI crashes and later recovery. No application can guarantee restoration after power loss, a GPU/OS failure, both processes being terminated, or a disconnected monitor. This project does not claim EAC approval or a guarantee against anti-cheat enforcement.

## Build and run on Windows 11

1. Extract this archive to a writable folder, for example `C:\Tools\ColorShade-source`.
2. Install the current **.NET 10 SDK, Windows x64** from [Microsoft's .NET 10 download page](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). Choose the SDK, not only the runtime. Restart Terminal after installation. Visual Studio is optional.
3. Open Terminal in the extracted `ColorShade` folder, the folder containing `build.cmd`. Confirm `dotnet --version` reports a .NET 10 SDK or newer SDK capable of targeting .NET 10.
4. Double-click `build.cmd`. It runs the tests and publishes a self-contained Windows executable. Alternatively, run these commands in PowerShell:

   ```powershell
   dotnet run --project .\tests\ColorShade.Tests\ColorShade.Tests.csproj -c Release
   dotnet publish .\src\ColorShade\ColorShade.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o .\artifacts\win-x64
   ```

5. Run `artifacts\win-x64\ColorShade.exe`. It starts in the system tray. Open the tray overflow area if its blue C icon is hidden. Double-click the icon, or right-click it and choose **Open**.
6. Select a preset or adjust the sliders. Use **Test for 10 seconds** on the desktop first. The preview affects the monitor containing ColorShade's settings window and restores automatically. Press the button again to stop early.
7. Leave **Enabled** on for foreground automation. Closing or minimizing the settings window puts it back in the tray. **Exit**, available in the window and tray, stops the app and requests restoration.

The published executable includes the .NET runtime. The target PC does not need the SDK, Visual Studio, Python, or a separate .NET installation. The executable is relatively large because it bundles WPF and the runtime; a self-contained build made during validation was about 165 MiB. The actual application source is small. Native runtime components are extracted by .NET during startup.

For a smaller deployment that requires the .NET 10 Desktop Runtime on the destination PC:

```powershell
dotnet publish .\src\ColorShade\ColorShade.csproj -c Release -r win-x64 --self-contained false -o .\artifacts\framework-dependent
```

Keep that entire output folder together. For either build, launch `ColorShade.exe` directly, because it launches a second copy of itself as its guardian. Do not use `dotnet ColorShade.dll` as the launcher. `ColorShade.exe --open` opens the settings window at startup; `--tray` explicitly requests normal tray startup.

To debug in an IDE, open `ColorShade.slnx` with an IDE that supports .NET 10 and select `src/ColorShade` as the startup project. No designer or external UI package is needed.

## Dependencies and project structure

| Path | Purpose |
| --- | --- |
| `src/ColorShade/ColorShade.csproj` | Windows x64 WPF app; WinForms is used for the tray menu only |
| `src/ColorShade/MainWindow.xaml` and `.xaml.cs` | Dark UI, sliders, preset editing, tray operation and focus polling |
| `src/ColorShade/Themes/Dark.xaml` | Shared dark styles |
| `src/ColorShade/Native/ForegroundDetector.cs` | Read-only foreground PID/name matching |
| `src/ColorShade/Native/Win32.cs` | Win32 display/window/process-snapshot declarations |
| `src/ColorShade/Native/DisplayInventory.cs` | Display paths, monitor identity, HDR checks and clone rejection |
| `src/ColorShade/Native/AmdAdl.cs` | Optional driver-provided AMD ADL2 saturation |
| `src/ColorShade/Native/WindowsDisplaySystem.cs` | Per-monitor GDI gamma capture, application and readback |
| `src/ColorShade/Recovery/` | Guardian process, same-user named pipe and heartbeat timeout |
| `src/ColorShade/StartupRegistration.cs` | Current-user Windows startup option |
| `src/ColorShade.Core/` | Testable display state machine, gamma math, presets and recovery journal |
| `tests/ColorShade.Tests/` | Dependency-free test runner with simulated hardware |
| `build.cmd` | Tests and self-contained publish |
| `restore.cmd` | Recovery launcher for a built application |
| `VALIDATION.md` | Actual validation results and Windows hardware test procedure |

The project has no third-party NuGet package references. Windows Desktop framework/targeting/runtime packs are supplied by Microsoft's SDK and may be downloaded during restore and publish. ADL is loaded only from the installed AMD driver in Windows System32. No driver DLL or proprietary SDK binary is redistributed in this source archive. An unsupported or missing ADL library selects the contrast fallback automatically.

## Controls and presets

| Control | Behavior |
| --- | --- |
| Digital Vibrance, 0–100 | On supported AMD displays: maps the reported saturation range, with 50 equal to the captured desktop value. Otherwise: mild contrast adjustment, with 50 neutral. |
| Display Brightness / Gamma, 0–100 | Changes midtones by composing a gamma curve with the original per-channel ramp. 50 is neutral. It does not change the monitor backlight. |
| Default / Balanced | Vibrance 50, gamma 50. Leaves desktop levels unchanged. |
| Rust Day Visibility | Vibrance 65, gamma 55. A starting point for testing, not a calibrated recommendation. |
| Rust Night / High Gamma | Vibrance 60, gamma 72. A starting point with brighter midtones. |
| Save | Saves slider values and lets you rename a custom preset. For built-ins, creates a named copy. |
| New | Creates a named custom preset from the current slider values. |
| Delete | Deletes the selected custom preset. Built-ins are immutable. |
| Test for 10 seconds | Temporarily applies settings on the monitor containing this window, even when automatic adjustment is disabled. |
| Restore / Retry | Disables automatic adjustment, stops a preview, retries restoration and reconnects a stopped guardian. Enable automation again when ready. |
| Start with Windows | Adds or removes `ColorShade` in the current user's `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. No administrator rights required. |

Move the published executable to its intended permanent folder before enabling startup. If it moves later, toggle **Start with Windows** off and on to update the registered path. Sliders take effect while a preview or foreground profile is active; use Save to persist them across restarts. Switching presets replaces unsaved slider edits.

Settings are written under `%LOCALAPPDATA%\ColorShade`:

| File | Contents |
| --- | --- |
| `presets.json` | Built-in and custom presets in schema version 1 |
| `settings.json` | Enabled state and selected preset identifier |
| `recovery.json` | Original per-monitor gamma/saturation values still needed for restoration |
| `display-owner.lock` | Exclusive ownership handle; an empty leftover file is harmless |
| `colorshade.log` | Local status and error log, with one rotated copy |

A sample preset file is in `examples/presets.json`. Files use readable JSON with atomic replacement. A malformed recovery file blocks adjustment and is retained; ColorShade never replaces it with guessed desktop values. A malformed presets file is retained on load and built-ins remain available in memory. Explicitly saving a preset writes a new valid file. Limit: 200 presets, including built-ins.

## Foreground detection and process boundaries

Every 250 ms, the UI reads `GetForegroundWindow`, obtains its PID with `GetWindowThreadProcessId`, and looks up the executable basename in a `TH32CS_SNAPPROCESS` snapshot using `Process32FirstW` / `Process32NextW`. It checks that foreground window and PID still agree before accepting the result. A failed lookup, focus loss, or missing window requests restoration. The monitor is selected with `MonitorFromWindow`, which chooses the monitor with the largest intersection for a spanning window.

There are no game process handles, memory reads or writes, module queries, DLL injections, rendering hooks, overlays, services, or custom drivers. The only child executable launched is ColorShade's own guardian. The ordinary settings window is neither topmost nor attached to the game. The application does not inspect EAC state, try to hide itself, or change the game.

The implementation uses documented AMD exports. It deliberately omits the private NVIDIA DVC interface identifiers found in some third-party utilities: `NvAPI_SetDVCLevelEx` is absent from the public header inspected for this project. NVIDIA output therefore uses the explicit contrast fallback. The public [NVIDIA NVAPI header](https://github.com/NVIDIA/nvapi/blob/main/nvapi.h) is the reference for that scope decision.

These boundaries describe the implementation. They are not a certification by Facepunch, Epic, NVIDIA, AMD, or Microsoft.

## Restoration and crash handling

The guardian process is the sole owner of display mutations. A same-user named pipe carries settings and acts as a renewable lease. An exclusive file handle prevents two guardians or a recovery command from modifying displays concurrently.

1. On startup, read and validate the recovery journal and restore any connected, identifiable targets before allowing a new adjustment.
2. Before a first adjustment, capture the current gamma and supported saturation. Flush the recovery record to disk before calling any display setter.
3. Apply saturation first, then gamma, and check driver readback. Saturation may reset a driver's gamma state, so restoration follows the same order.
4. On focus loss, a disable action, preview completion, exit, or a monitor switch, restore the exact captured values. When switching monitors, finish restoring the old target before touching the new one.
5. On pipe disconnection or roughly four seconds without a command, the guardian restores. This also handles a hung UI. If a driver call itself blocks, this timing cannot be guaranteed.
6. If the guardian fails, the living UI disables automation and launches a recovery-only instance. If both processes disappear, the next normal launch or manual recovery reads the journal.
7. Failed or disconnected targets remain in the journal. New adjustments are blocked until pending recovery succeeds. After disconnect, the guardian retries briefly; a later launch can resume recovery.

The guardian independently caps each preview at ten seconds. Session lock/disconnect and suspend events request restoration; shutdown may give the process only a limited time to complete. An ongoing driver call, an OS termination deadline, or a hardware fault can prevent immediate completion. Readback checks cannot prove the image physically displayed by a monitor.

Manual recovery, after exiting ColorShade:

```powershell
.\artifacts\win-x64\ColorShade.exe --restore
```

Or double-click `restore.cmd`. If you copied only the executable to another folder, put `restore.cmd` beside it. Reconnect the original monitor, use the same connection where possible, and return it to SDR first. Keep `recovery.json` until recovery completes. A changed monitor connection can change the device identity; ColorShade will retain the old record instead of risking another display.

For a power loss, OS crash, both-process termination, permanently disconnected monitor, corrupted recovery data, or changed monitor identity, immediate automatic restoration cannot be promised. Use recovery when the original display is available. If its identity cannot be recovered, restore your known desktop settings in the vendor control panel and reapply your saved Windows calibration manually; preserve the journal for reference. Do not delete a pending journal and assume that doing so restored the display.

## Multiple monitors and display limitations

Extended SDR displays are addressed independently using their current GDI name plus the stable monitor device path from `QueryDisplayConfig`. The device path, not a reusable `DISPLAY1` number, identifies the saved baseline. Every native read/write rechecks the target's identity and supported state. Hotplugging can still occur between an OS query and the driver call, so errors are retained for recovery rather than treated as success.

HDR / Advanced Color, unknown color state, missing identity, and mirrored displays are blocked. Mirrored outputs share a gamma source and cannot be adjusted independently by this design. An ambiguous ADL mapping uses contrast fallback rather than selecting an arbitrary output. Hybrid-GPU, remote-desktop, USB/DisplayLink, or driver-specific outputs may be unsupported.

Microsoft documents that GDI gamma can fail silently, be overwritten by other applications or display events, and behave unpredictably with HDR or calibration systems. ColorShade uses bounded curves, readback checks, and a two-second conflict check, then pauses on a detected mismatch. It does not continuously rewrite the ramp to fight another owner. See Microsoft's [SetDeviceGammaRamp limitations](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-setdevicegammaramp).

Exclusive-fullscreen rendering, a game-owned gamma ramp, Night Light, automatic color management, ICC loaders, or other color utilities may override these settings. Borderless SDR is a useful compatibility test. The app cannot force effectiveness without using techniques explicitly excluded by this project's scope. At focus transitions, the 250 ms poll plus driver latency means a brief display transition is possible.

## Engineering references

- [AMD ADL2 display color APIs](https://gpuopen-librariesandsdks.github.io/adl/group__COLORAPI.html): saturation capability, range, current-value and set calls. ColorShade does not flush temporary changes into persistent driver settings.
- [AMD display color constants](https://gpuopen-librariesandsdks.github.io/adl/group__define__color__type.html): `ADL_DISPLAY_COLOR_SATURATION`.
- [AMD's public SDK source](https://github.com/GPUOpen-LibrariesAndSDKs/display-library): native structure layouts and adapter/display mapping.
- [Microsoft Tool Help snapshot API](https://learn.microsoft.com/en-us/windows/win32/api/tlhelp32/nf-tlhelp32-createtoolhelp32snapshot): read-only process listing.
- [Microsoft display-path structure](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ns-wingdi-displayconfig_path_info): source/target topology.
- [Microsoft's Windows SDK headers](https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/um/wingdi.h): native display structure sizes and Advanced Color flags.
- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core): .NET 10 is an LTS release. Rebuild self-contained distributions with current SDK/runtime patches when updating the app.

## Uninstall

Turn off **Start with Windows**, choose **Restore / Retry**, and then **Exit**. Remove the executable folder. Once recovery is confirmed complete, `%LOCALAPPDATA%\ColorShade` can be removed if you also want to delete custom presets and logs. Removing files alone does not revert driver settings.
