# U2cRumble (Windows app)

A small Windows desktop app (WPF, .NET 10) that gives the **8BitDo Ultimate 2C Wireless** real
**rumble over Bluetooth** and a **native Xbox identity** on Windows 11. It is the graphical front end
for the C# flashing core in [`../U2cRumble.Core`](../U2cRumble.Core).

## What it does

| Button | Core operation | Writes? |
| --- | --- | --- |
| **Refresh** | list devices | no |
| **Back up controller** | full read of the flash | no (reads the controller) |
| **Check (no write)** | plan `bt_xbox` + `bt_rumble` | no |
| **Install Bluetooth rumble** | plan and apply the tweaks | **yes** |
| **Restore backup** | plan and apply your backup | **yes** |

The two write buttons stay disabled until you tick the disclaimer box. Progress, warnings and errors
appear in the log at the bottom.

Flashing is done once and only over the **USB cable**. After that the controller works on any Windows
PC over Bluetooth, as an Xbox Series X|S pad with rumble.

## Build and run

Requires only the **.NET 10 SDK**.

```sh
# from the repository root
dotnet build app/U2cRumble/U2cRumble.csproj -c Release
dotnet run --project app/U2cRumble/U2cRumble.csproj
```

## Files

- `U2cRumble.csproj` - the WPF project (net10.0-windows).
- `App.xaml`, `App.xaml.cs` - application entry point.
- `MainWindow.xaml`, `MainWindow.xaml.cs` - the Setup and About tabs.
- `../U2cRumble.Core` - the flashing core, a separate class library.

## License

GPL-3.0-or-later. The flashing core is a C# port of the U2C Firmware Toolkit by Carlos Browne F.
See [`../CREDITS.md`](../CREDITS.md) and [`../LICENSE`](../LICENSE).
