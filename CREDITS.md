# Credits

This project would not exist without the work of others. Please keep these credits if you fork or redistribute it.

## The flashing core (GPL-3.0-or-later)

The flashing core in [`app/U2cRumble.Core`](app/U2cRumble.Core) is a **C# port** of the **U2C Firmware Toolkit** by **Carlos Browne F.**, used under the **GNU General Public License v3.0 or later**:

- **carlosbrownef-source/8bitdo-ultimate-2c-toolkit** — https://github.com/carlosbrownef-source/8bitdo-ultimate-2c-toolkit

The original toolkit contains the flashing core, the `bt_xbox` (native Xbox over Bluetooth) and `bt_rumble` (rumble over Bluetooth) tweaks, the safeguards, and the reverse engineering that makes the flashing possible. This project ports the core to C#, adds a native Windows HID transport and wraps it in a desktop app.

The port was validated **byte for byte** against the original core: given the same stock 1.09 backup, both produce the same 77,824-byte image and the same SHA-256. See [`tests/ParityCheck`](tests/ParityCheck).

## Reverse engineering

- **jgus-org/8bitdo-2c-re-flake** (MIT) — https://github.com/jgus-org/8bitdo-2c-re-flake — reverse engineering of the Ultimate 2C's update protocol, bootloader protocol and recovery, and of the dual-bank flash layout.

## Descriptor and technique references

- **xpadneo** — https://github.com/atar-axis/xpadneo — the captured Xbox Series X|S Bluetooth HID report descriptor.
- **supercypok/ultimate2c-ring** (MIT) — https://github.com/supercypok/ultimate2c-ring — the Home ring tweak method.
- **Crazyawk/8bitdo-ultimate2c-macos-rumble** (MIT) — https://github.com/Crazyawk/8bitdo-ultimate2c-macos-rumble — early public use of the bootloader identity query and rumble testing.
- **vomba/8bitdo-ultimate-2c-apple** (MIT) — https://github.com/vomba/8bitdo-ultimate-2c-apple — notes on the identity query and the pending-update fields.
- **MoridaDE/8bitdo-2c-battery-tray** (MIT) — https://github.com/MoridaDE/8bitdo-2c-battery-tray — battery reporting notes.
- **8BitDo** — the hardware and the firmware update server.
- **Telink** — the tc32 toolchain used to build the tweak source.

## This project

- **C# port, Windows HID transport and the desktop app** — https://github.com/JehuAlv

Not affiliated with or endorsed by 8BitDo.
