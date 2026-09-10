# Validation

## Completed in this environment

The source was cross-compiled on Linux using Microsoft's .NET SDK 10.0.100. This checks the real C# and WPF XAML compiler, not only syntax matching. A self-contained single-file `win-x64` publish also completed. The resulting application has a Windows PE x64 header. No Windows desktop, physical monitor, display driver, or Rust/EAC session was available here, so those behaviors have not been run or certified.

The published verification build is a temporary build artifact. This distribution supplies source so you can build with the current .NET 10 SDK and runtime patches on Windows.

| Check | Result |
| --- | --- |
| WPF project compilation | Passed |
| Self-contained Windows x64 single-file publish | Passed |
| Core automated tests | 20 of 20 passed |
| Native Win32 structure sizes | Passed against Windows x64 ABI sizes |
| ADL structure size guards | Present before the driver library is loaded |
| Source scan for forbidden direct calls | No matches |
| Windows UI execution and visual inspection | Not run |
| Actual GDI/AMD driver application and readback | Not run |
| Actual crash recovery on Windows | Not run; simulated owner-loss recovery passed |
| Rust/EAC compatibility or approval | Not established |

The source scan covered direct calls to `OpenProcess`, process-memory functions, remote-thread/allocation functions, window/event hooks, full-process-image lookup, and module filename lookup. The only directly imported native libraries are `user32.dll`, `kernel32.dll`, `gdi32.dll`, and `dwmapi.dll`. AMD ADL exports are resolved dynamically from the Windows-installed driver. Scanning source is useful evidence of the implementation boundary, not a guarantee about vendor enforcement or all behavior inside operating-system/runtime libraries.

The one suppressed compiler analyzer is `WFO0003`. It is a WinForms recommendation about DPI startup configuration. This application is WPF and intentionally sets per-monitor DPI awareness in its application manifest; WinForms is used for the tray only. The suppression and reason are next to each other in the project file.

## Automated test coverage

Run from the source root:

```powershell
dotnet run --project .\tests\ColorShade.Tests\ColorShade.Tests.csproj -c Release
```

The executable test runner returns a nonzero exit code on failure. It uses a simulated display backend and a separate serialized journal copy, so abruptly replacing the engine really reconstructs recovery from saved data.

1. Exact preservation of a non-identity, per-channel calibrated baseline at neutral settings.
2. Monotonic curves and preserved endpoints at slider extremes.
3. Failed durable save prevents every hardware write.
4. Journal durability precedes saturation and gamma mutation.
5. Alt-tab restores both original gamma and saturation.
6. A monitor switch restores the original target before touching the next.
7. A replacement guardian recovers from the previous owner's saved journal.
8. Disconnection retains the original baseline and blocks new capture.
9. A reused `DISPLAY1` name does not receive another monitor's values.
10. A failed saturation restore remains pending, with gamma scheduled for restoration again.
11. A rejected gamma write rolls back saturation already applied.
12. A silently ignored gamma write is detected by readback.
13. An unsupported target is never mutated.
14. Missing ADL selects contrast fallback without saturation calls.
15. Unchanged heartbeats do not repeat display writes.
16. An initially neutral preset is a hardware no-op.
17. Saturation mapping respects baseline, limits and step.
18. Corrupted recovery data fails closed.
19. JSON presets and quoted custom names round-trip.
20. Win32 display, monitor and process-snapshot structure sizes match the x64 ABI.

## Windows acceptance procedure

Run these checks on an SDR desktop before relying on automation. Begin with a modest non-neutral preset and keep `restore.cmd` available. These are checks of ColorShade itself; no game debugging or process inspection is needed.

| Scenario | Steps | Expected result |
| --- | --- | --- |
| Tray startup | Launch the built EXE; open from tray; close with X; minimize; reopen | No startup settings window unless `--open` is used; app remains in tray after X/minimize |
| Preview | Set vibrance 60 and gamma 60; start Test; then repeat and cancel early | Selected window monitor changes, status says Preview, and captured desktop values return after ten seconds or cancellation |
| Built-ins/custom presets | Save a built-in as a copy; rename/save it; restart; delete it | Built-ins remain immutable; custom values/names survive restart; Delete affects only custom presets |
| Normal foreground behavior | Enable a non-neutral preset; focus the configured foreground application; switch to another app; close the target application | Automatic status follows focus; captured desktop values return after focus loss or target exit, subject to poll and driver latency |
| Disable/exit | Disable during an active profile; repeat with Exit | Original values return; Exit removes the tray icon and guardian stops after cleanup |
| Two extended monitors | Preview on A, restore, move settings to B and preview; test a target-window move between them | Only the chosen monitor changes; old monitor restores before the new one is modified |
| Hot unplug | Unplug the changed monitor during a preview; reconnect to the original port | Pending baseline remains in the journal; adjustment is blocked until restoration succeeds |
| HDR | Enable HDR on a test display, then request a preview | Unsupported/Advanced Color message; no new gamma/saturation adjustment |
| Clone mode | Switch to Duplicate displays and request a preview | Mirrored-output message; no attempted independent gamma change |
| Missing/unsupported ADL | Test on a system without available AMD saturation | Backend explicitly reports contrast fallback; gamma preview still works if GDI is supported |
| Calibrated desktop | With the app idle, apply a known custom desktop calibration; preview and restore | Captured calibration is restored rather than replaced by a linear ramp |
| Competing gamma owner | While active, change gamma in another display utility | Readback mismatch pauses adjustment and triggers restoration instead of an endless write loop |
| Session and sleep | Lock/unlock Windows; separately sleep/resume with a preview active | No stale preview remains; restoration is requested, or retained for retry if the driver/session is unavailable |
| Startup option | Enable Start with Windows; sign out and back in; disable it | Current-user startup entry works and is removed when disabled |

For an owner-crash test, first use a desktop preview. In Task Manager's Details tab, enable the Command line column. There will normally be two `ColorShade.exe` processes: the UI and one whose command line contains `--guardian`. Their PIDs are also in the local log. End only the UI process. The guardian should restore when the pipe closes, or at its roughly four-second lease deadline if communication stalls. This must be verified with the actual monitor, not just a log entry.

Then repeat with the guardian process terminated instead. The surviving UI should disable automation and request a recovery-only instance. To test next-launch recovery, terminate both ColorShade processes during a preview and launch ColorShade again; it should read the journal before a new adjustment. Restore the original desktop between cases. A Task Manager operation that ends an entire process tree can terminate the guardian too and therefore exercises next-launch recovery rather than immediate watchdog recovery.

Finally verify `ColorShade.exe --restore` after a pending recovery. It must refuse to compete with a running guardian, report a still-disconnected target as pending, and retain original values until successful readback. Do not interpret deleting the journal or a successful setter return value alone as successful restoration.
