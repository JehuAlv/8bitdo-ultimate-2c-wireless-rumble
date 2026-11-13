# 8BitDo Ultimate 2C Wireless — Bluetooth Rumble for Windows

Give the **8BitDo Ultimate 2C Wireless** real **rumble over Bluetooth** on **Windows 11**, and make Windows see it as a **native Xbox controller** instead of a generic HID device.

Out of the box, over Bluetooth the Ultimate 2C Wireless is a plain HID gamepad on Windows: no rumble, and many games don't recognise it. This project flashes 8BitDo's **own** firmware with two small tweaks, so the controller presents itself as an **Xbox Series X|S** controller and **vibrates over Bluetooth**. No drivers, no dongle, no background software, and no virtual pad — the change lives inside the controller.

> **Use at your own risk.** Flashing modified firmware probably voids the controller's warranty. It is reversible (your backup restores the original firmware), but it is not risk-free. Read the [disclaimer](#safety-and-disclaimer) before you start. This project is **not affiliated with or endorsed by 8BitDo**.

## What it does

| Feature | What it changes |
| --- | --- |
| **Native Xbox over Bluetooth** | The controller reports as an **Xbox Series X\|S** pad (`045e:0b13`), so Windows and games see it as a real Xbox controller through **XInput**. |
| **Rumble over Bluetooth** | Games can make it vibrate over Bluetooth, which the stock firmware does not allow. Both motors work. |
| **Fewer Bluetooth interruptions** | The Bluetooth connection interval is tuned so the pad stays responsive. |

Everything else stays as it was. The tweaks only touch the firmware's application area; the bootloader, the calibration and the Bluetooth pairings are never modified.

### Why the flashing is needed

The tweaks are small edits to the firmware that runs **inside the controller**. That is why, once flashed, the controller works on **any** Windows PC — you only need this tool to flash it once. The stock firmware simply has no code path that accepts rumble commands over Bluetooth.

## Requirements

- **8BitDo Ultimate 2C Wireless** controller (the PC SKU with the 2.4 GHz dongle) on **firmware 1.09**.
- **Windows 11** (x64).
- **.NET 10 SDK** to build and run the app.
- **Python 3.12+** to run the flashing core.
- A **USB-C data cable** for the one-time flash. Bluetooth cannot flash the controller.

## How to use

1. **Back up.** Connect the controller **by cable** and make a backup. The backup is the safety net: it contains your unit's bootloader check, calibration and Bluetooth keys, and the tool refuses to write anything without one.
2. **Install the tweaks.** With the controller still **by cable**, install *Xbox over Bluetooth* + *Rumble over Bluetooth*. The tool puts the controller into update mode, writes the changes, verifies them and restarts it.
3. **Pair again.** Remove the controller in **Settings → Bluetooth & devices** and pair it again, so Windows re-reads the new description.
4. **Play.** Turn the controller on over Bluetooth. Windows shows it as an Xbox controller and games can make it vibrate.

To go back, **restore your backup** (or 8BitDo's original 1.09) the same way.

### Recovery

The bootloader is never written, so a failed or interrupted write is always recoverable:

> Turn the controller fully off, hold **L1 + R1**, and plug in the USB cable. It starts in update mode (the LED blinks white). From there you can restore your backup. This works **only over USB**.

## Build and run the app

```sh
git clone https://github.com/JehuAlv/8bitdo-ultimate-2c-wireless-rumble
cd 8bitdo-ultimate-2c-wireless-rumble

# build the Windows app
dotnet build app/U2cRumble/U2cRumble.csproj -c Release
dotnet run --project app/U2cRumble/U2cRumble.csproj
```

The app drives the flashing core (`core/`), which is the **U2C Firmware Toolkit** by Carlos Browne F. (see [Credits](#credits)).

You can also use the core directly:

```sh
cd core
python -m u2c_customfw devices
python -m u2c_customfw backup --write
python -m u2c_customfw patch --tweaks bt_xbox,bt_rumble --write --accept-disclaimer
python -m u2c_customfw restore backup --write
```

## Safety and disclaimer

- **Warranty:** installing modified firmware probably voids the controller's warranty.
- **Reversible:** the bootloader is never touched, so recovery is always possible over USB.
- **Tested on one unit:** the tweaks were validated on one Ultimate 2C Wireless. Edge cases may exist.
- **Reported risk:** making the motors run during an active Bluetooth link has been reported to hang the controller in one instance; the toolkit author ran both motors at full strength for 30 seconds without reproducing it. This is a behaviour risk, not a brick risk.
- See [DISCLAIMER.md](DISCLAIMER.md) for the full text and [core/SAFEGUARDS.md](core/SAFEGUARDS.md) for every check the tool makes before writing.

## Credits

This project stands on other people's work. See [CREDITS.md](CREDITS.md) for the full list.

- **Carlos Browne F.** — [carlosbrownef-source/8bitdo-ultimate-2c-toolkit](https://github.com/carlosbrownef-source/8bitdo-ultimate-2c-toolkit) (GPL-3.0-or-later): the U2C Firmware Toolkit, its flashing core, the `bt_xbox` and `bt_rumble` tweaks and all the reverse engineering behind them. The `core/` directory is that project.
- **jgus-org/8bitdo-2c-re-flake** (MIT): reverse engineering of the update and bootloader protocols and the flash layout.
- **xpadneo**, **supercypok/ultimate2c-ring**, **Crazyawk/8bitdo-ultimate2c-macos-rumble**, **vomba/8bitdo-ultimate-2c-apple**, **MoridaDE/8bitdo-2c-battery-tray**: descriptor and technique references.

## License

**GPL-3.0-or-later**. The flashing core in `core/` is a copy of the U2C Firmware Toolkit and keeps its original license and copyright. See [LICENSE](LICENSE).

Not affiliated with or endorsed by 8BitDo.
