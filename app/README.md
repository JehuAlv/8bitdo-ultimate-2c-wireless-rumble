# U2cRumble (Windows app)

A small Windows desktop app (WPF, .NET 10) that gives the **8BitDo Ultimate 2C Wireless** real
**rumble over Bluetooth** and a **native Xbox identity** on Windows 11. It is the graphical front end
for the flashing core in [`../core`](../core).

## What it does

The app drives the core's command line and shows its output live:

| Button | Core command | Writes? |
| --- | --- | --- |
| **Refresh** | `devices` | no |
| **Back up controller** | `backup` | no (reads the controller) |
| **Check (no write)** | `patch --tweaks bt_xbox,bt_rumble` | no |
| **Install Bluetooth rumble** | `patch --tweaks bt_xbox,bt_rumble --write --accept-disclaimer` | **yes** |
| **Restore backup** | `restore backup --write --accept-disclaimer` | **yes** |

The two write buttons stay disabled until you tick the disclaimer box. Everything the core prints
(progress, warnings, errors) appears in the log at the bottom.

Flashing is done once and only over the **USB cable**. After that the controller works on any Windows
PC over Bluetooth, as an Xbox Series X|S pad with rumble.

## Build and run

Requires the **.NET 10 SDK** and **Python 3.12+** on `PATH`.

```sh
# from the repository root
dotnet build app/U2cRumble/U2cRumble.csproj -c Release
dotnet run --project app/U2cRumble/U2cRumble.csproj
```

The app finds `core/` by walking up from its own folder, so it works when run from the repository
with `dotnet run`. To point it at a different interpreter, set `U2C_PYTHON` to the Python executable.
If a bundled `core/u2c-customfw.exe` (built with PyInstaller) is present, the app uses it instead of
Python.

## Files

- `U2cRumble.csproj` - the WPF project (net10.0-windows).
- `App.xaml`, `App.xaml.cs` - application entry point.
- `MainWindow.xaml`, `MainWindow.xaml.cs` - the Setup and About tabs.
- `CoreRunner.cs` - finds and runs the Python core, streaming its output.

## License

GPL-3.0-or-later. The flashing core in `../core` is the U2C Firmware Toolkit by Carlos Browne F. and
keeps its original license and copyright. See [`../CREDITS.md`](../CREDITS.md) and
[`../LICENSE`](../LICENSE).
