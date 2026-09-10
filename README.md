# ColorShade

ColorShade is a lightweight Windows 11 tray application that automatically adjusts display vibrance and brightness while playing Rust, then restores your normal desktop settings when you alt-tab or close the game.

## Installation

### Option 1: Download the Release

1. Go to the **Releases** section of this GitHub repository.
2. Download the latest `ColorShade.exe`.
3. Move it to a permanent folder, for example:

```text
C:\Tools\ColorShade
```

4. Run `ColorShade.exe`.
5. If Windows SmartScreen appears, choose **More info → Run anyway** if you trust the release.
6. ColorShade will start in the Windows system tray.
7. Open it by double-clicking the tray icon or right-clicking it and selecting **Open**.

No additional software is required when using the prebuilt self-contained release.

---

### Option 2: Build From Source

Requirements:

* Windows 11 x64
* [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)

Clone or download the repository, then run:

```text
build.cmd
```

Or build manually with PowerShell:

```powershell
dotnet publish .\src\ColorShade\ColorShade.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o .\artifacts\win-x64
```

The finished application will be located at:

```text
artifacts\win-x64\ColorShade.exe
```

## How to Use

1. Open ColorShade.
2. Select a preset or adjust the vibrance and brightness sliders.
3. Use **Test for 10 seconds** to make sure the settings look correct.
4. Leave **Enabled** turned on.
5. Start Rust.

When Rust is the active window, ColorShade applies your selected display settings.

When you alt-tab, minimize Rust, or close the game, ColorShade restores your previous desktop display settings.

Closing the settings window does not close ColorShade. It continues running in the system tray.

Use **Exit** when you want to completely close the application.

## Presets

ColorShade includes several default presets:

| Preset                  | Vibrance | Brightness / Gamma |
| ----------------------- | -------: | -----------------: |
| Default / Balanced      |       50 |                 50 |
| Rust Day Visibility     |       65 |                 55 |
| Rust Night / High Gamma |       60 |                 72 |

You can also create and save your own presets.

## GPU Support

### AMD

Supported AMD GPUs can use AMD ADL display controls for actual saturation adjustment.

### NVIDIA

ColorShade does **not** currently use NVIDIA's native Digital Vibrance Control.

On NVIDIA GPUs, ColorShade uses gamma and contrast adjustment instead.

This means the vibrance slider will not behave exactly like the **Digital Vibrance** setting inside NVIDIA Control Panel.

### Intel and Unsupported GPUs

Intel GPUs and unsupported AMD configurations also use the gamma/contrast fallback.

## Brightness

The brightness slider adjusts the Windows display gamma ramp.

It changes the appearance of brightness and midtones but does **not** change your monitor's physical backlight brightness.

## Automatic Rust Detection

ColorShade checks whether the active foreground application is:

```text
RustClient.exe
```

or

```text
Rust.exe
```

When Rust gains focus, your selected display profile is applied.

When Rust loses focus, your original display settings are restored.

## Anti-Cheat Safety Design

ColorShade works entirely outside of the game.

It does not:

* Inject DLLs
* Read or write Rust's memory
* Hook DirectX, Vulkan, or Direct3D
* Create a game overlay
* Modify game files
* Modify Easy Anti-Cheat
* Open handles to Rust for memory access
* Install a driver

It only checks which application is currently in the foreground and changes Windows/GPU display settings.

This design is intended to avoid interacting with the game itself, but this project is **not officially approved or certified by Facepunch or Easy Anti-Cheat**.

Use third-party software with online games at your own discretion.

## Restore and Recovery

ColorShade saves your original display settings before applying changes.

It normally restores them when:

* You alt-tab from Rust
* Rust closes
* ColorShade is disabled
* A test preview finishes
* You exit ColorShade

If the display does not restore correctly, open ColorShade and select:

**Restore / Retry**

You can also run:

```powershell
ColorShade.exe --restore
```

or use:

```text
restore.cmd
```

ColorShade cannot guarantee automatic restoration after situations such as:

* Power loss
* Windows crashes
* GPU driver crashes
* Both ColorShade processes being forcibly terminated
* Monitor disconnection
* Hardware failure

If recovery fails, restore your normal display settings through Windows or your GPU control panel.

## Multiple Monitors

ColorShade supports normal extended SDR monitor configurations.

Some configurations may not work correctly, including:

* HDR / Advanced Color
* Mirrored displays
* Remote Desktop
* DisplayLink / USB graphics
* Some hybrid-GPU laptops
* Driver-specific display configurations

If you experience problems, try disabling HDR before using ColorShade.

## Start With Windows

Enable **Start with Windows** inside ColorShade to automatically launch it when you sign in.

Move `ColorShade.exe` to its permanent location before enabling this option.

If you move the executable later, disable and re-enable **Start with Windows**.

## Settings Location

ColorShade stores its settings in:

```text
%LOCALAPPDATA%\ColorShade
```

This includes:

```text
presets.json
settings.json
recovery.json
colorshade.log
```

## Uninstall

1. Disable **Start with Windows**.
2. Select **Restore / Retry**.
3. Exit ColorShade.
4. Delete the ColorShade folder.

After confirming your display settings have been restored, you can optionally delete:

```text
%LOCALAPPDATA%\ColorShade
```

## License

See the repository's `LICENSE` file for licensing information.
