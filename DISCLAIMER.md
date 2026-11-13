# Disclaimer

**Installing modified firmware probably voids your controller's warranty.**

This project flashes 8BitDo's own firmware with small edits. It is reversible — your backup restores the controller — but it is not risk-free, and you do it at your own risk.

What is known and what is not:

- **Tested on one unit.** The tweaks were validated on one 8BitDo Ultimate 2C Wireless on firmware 1.09, on Windows 11. Edge cases may exist on other units or other firmware versions.
- **The bootloader is never written.** The tool writes nothing below `0x6000` or at or above `0x73000`, so recovery over USB is always possible: turn the controller off, hold **L1 + R1**, plug in the cable, and restore your backup.
- **One reported behaviour risk.** Making the motors run during an active Bluetooth link has been reported to hang the controller. The toolkit author ran both motors at full strength for 30 seconds without reproducing it. This is a behaviour risk, not a brick risk.
- **No warranty.** This project makes a lot of assumptions and comes with no warranty of any kind. See the [GPL-3.0 license](LICENSE), sections 15 and 16.

The app enforces its own safeguards before writing; see [SAFEGUARDS.md](SAFEGUARDS.md). Keep your backup: this app restores the file you made, it does not download firmware.

Not affiliated with or endorsed by 8BitDo.
