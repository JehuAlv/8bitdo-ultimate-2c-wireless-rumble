# Safeguards

These are the checks the app makes before and while it writes to the controller. They are enforced in the code, not just recommended. For the warranty and the risks, see the [disclaimer](DISCLAIMER.md).

## The controller

- **Protected memory.** The app writes nothing below `0x6000`, where the bootloader lives, or at or above `0x73000`, where the configuration, the MAC address, the calibration and the Bluetooth pairings live. The bootloader is what runs update mode, so you can always restore.
- **A backup of the same controller.** A write needs a backup of that very unit: its bootloader must match, and so must its MAC address, allowing for the small change each new pairing makes.
- **The right firmware.** The controller must be running the original firmware 1.09, with or without the tweaks. A different known version is refused; an unknown version is refused too.
- **A pending update.** If the controller has a firmware update pending, the app refuses to install or restore over it, because it could replace what was written at the next start.
- **Never the dongle.** The controller's steps never write to the dongle. The app tells them apart by the bootloader's identity, the USB version and how the flash reads back.
- **Checked edits.** Each tweak is a set of byte edits to the original firmware 1.09, and every edit checks the SHA-256 of the original bytes it replaces before writing.
- **Restoring your backup.** The app picks the newest backup of the same controller that still holds the original firmware 1.09, so restoring really undoes the tweaks. If no backup has it, it uses the newest recognised one.
- **Check first, write second.** *Check (no write)* reads the controller by cable and leaves it running. To write, the app first confirms the device is not the dongle, then puts the controller in update mode and checks everything again there, through the bootloader, before it erases anything.
- **Verified writes.** After writing, the app reads the firmware back and compares it with what it wrote. It only restarts the controller when the comparison matches.
- **An interrupted write.** If a write stops after erasing, the controller stays in update mode, the app says so, and *Restore backup* still works there. Unplugging the cable does not leave update mode; holding Home for about 12 seconds with the cable out does.
- **One unit at a time.** A lock file stops two copies of the app, or the app and the console tool, from interleaving requests to the same controller.

## What this app does not do

- It does not download firmware from 8BitDo's servers. *Restore backup* puts back the backup you made, so keep that file safe.
- It does not write to the 2.4 GHz dongle.
- It does not touch the Home ring, the settings records or the safety features of the original toolkit.
