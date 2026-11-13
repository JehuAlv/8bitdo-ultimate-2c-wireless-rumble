# SPDX-License-Identifier: GPL-3.0-or-later
"""Windows HID transport, through the hidapi Python package.

hidapi hands an input report back with its report ID byte in front and expects the report ID in front of an
output report, which is the buffer the toolkit builds, so reports pass through unchanged. Reads are synchronous
here, unlike IOKit's run loop.
"""
import time

import hid

from . import DeviceInfo, PermissionDenied

BUS = {0: "", 1: "USB", 2: "Bluetooth", 3: "I2C", 4: "SPI"}
REPORT_LEN = 64
READ_LEN = 128   # room for the report ID in front of a 64-byte report
NO_ID = b"\x00"  # hidapi on Windows wants a report ID byte even on devices that declare none


def list_devices():
    return [DeviceInfo(vendor=d["vendor_id"], product=d["product_id"], usage_page=d.get("usage_page", 0),
                       transport=BUS.get(d.get("bus_type", 0), ""), name=d.get("product_string") or "",
                       handle=d["path"], version=d.get("release_number", 0))
            for d in hid.enumerate()]


class WindowsChannel:
    def __init__(self, dev: DeviceInfo, report_id: int):
        self.rid = report_id
        self.stale = False   # the last request timed out, so a late reply may still be waiting
        self.dev = hid.device()
        try:
            self.dev.open_path(dev.handle)
        except OSError as e:
            raise PermissionDenied(f"The device couldn't be opened: {e}") from e

    def xfer(self, report: bytes, timeout: float = 0.5, accept=None) -> bytes | None:
        """The first reply after sending or, given accept, the first one it accepts; a reply that came after an
        earlier request timed out is drained first, so it can't pass for this request's answer."""
        if self.stale:
            for _ in range(32):   # 0 means "no timeout" to hidapi, so a short positive one drains without blocking
                if not self.dev.read(READ_LEN, 1):
                    break
            self.stale = False
        data = bytes(report).ljust(REPORT_LEN, b"\0")[:REPORT_LEN]
        if not self.rid:   # the bootloader declares no report IDs; the update channel's 0x81 is already in the buffer
            data = NO_ID + data
        if self.dev.write(data) < 0:
            self.stale = True
            return None
        end = time.monotonic() + timeout
        while True:
            left = end - time.monotonic()
            if left <= 0:
                self.stale = True
                return None
            r = self.dev.read(READ_LEN, max(1, int(left * 1000)))
            if not r:
                self.stale = True
                return None
            r = bytes(r)
            if accept is None or accept(r):
                return r

    def close(self) -> None:
        self.dev.close()


def open_channel(dev: DeviceInfo, report_id: int) -> WindowsChannel:
    return WindowsChannel(dev, report_id)
