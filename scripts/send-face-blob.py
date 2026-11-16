#!/usr/bin/env python3
"""Send VRChat's native face tracking input (/tracking/face/v1) to a UDP port, as VRCFaceTracking does.

Used to check the settings editor's Live input without VRCFaceTracking (it does not run on macOS).

  scripts/send-face-blob.py --port 9000 --seconds 60 --set "JawOpen=1,EyeClosedLeft=0.5" [--rate 60] [--form bundle]

The payload is one OSC blob of 102 bytes, one byte per tracking key, value = round(v * 255), keys not given are 0.
Key order (0-101):
  https://github.com/vrchat-community/creator-docs/blob/16fb16cfb4541fddcf35884e811ce8db16ff6493/Docs/docs/avatars/face-tracking/osc.md
VRCFaceTracking puts its messages in an OSC bundle (OscSendService.Send, create_osc_bundle):
  https://github.com/benaclejames/VRCFaceTracking/blob/4d8ca2245465e8a07c3e7a6f494db1783616cd3c/VRCFaceTracking.Core/Services/OscSendService.cs
--form: bundle (default, as VRCFaceTracking), plain (the message alone) or nested (a bundle in a bundle).
Prints "send-face-blob: sent <n> packets to 127.0.0.1:<port>" when done. Stop it early with Ctrl+C.
"""
import argparse
import socket
import struct
import sys
import time

KEYS = [
    "EyeLookOutRight", "EyeLookInRight", "EyeLookUpRight", "EyeLookDownRight", "EyeLookOutLeft", "EyeLookInLeft",
    "EyeLookUpLeft", "EyeLookDownLeft", "EyeClosedRight", "EyeClosedLeft", "EyeDilationRight", "EyeDilationLeft",
    "EyeConstrictRight", "EyeConstrictLeft", "EyeSquintRight", "EyeSquintLeft", "EyeWideRight", "EyeWideLeft",
    "BrowPinchRight", "BrowPinchLeft", "BrowLowererRight", "BrowLowererLeft", "BrowInnerUpRight", "BrowInnerUpLeft",
    "BrowOuterUpRight", "BrowOuterUpLeft", "NasalDilationRight", "NasalDilationLeft", "NasalConstrictRight", "NasalConstrictLeft",
    "CheekSquintRight", "CheekSquintLeft", "CheekPuffRight", "CheekPuffLeft", "CheekSuckRight", "CheekSuckLeft",
    "JawOpen", "JawRight", "JawLeft", "JawForward", "JawBackward", "JawClench",
    "JawMandibleRaise", "MouthClosed", "LipSuckUpperRight", "LipSuckUpperLeft", "LipSuckLowerRight", "LipSuckLowerLeft",
    "LipSuckCornerRight", "LipSuckCornerLeft", "LipFunnelUpperRight", "LipFunnelUpperLeft", "LipFunnelLowerRight", "LipFunnelLowerLeft",
    "LipPuckerUpperRight", "LipPuckerUpperLeft", "LipPuckerLowerRight", "LipPuckerLowerLeft", "MouthUpperUpRight", "MouthUpperUpLeft",
    "MouthUpperDeepenRight", "MouthUpperDeepenLeft", "NoseSneerRight", "NoseSneerLeft", "MouthLowerDownRight", "MouthLowerDownLeft",
    "MouthUpperRight", "MouthUpperLeft", "MouthLowerRight", "MouthLowerLeft", "MouthCornerPullRight", "MouthCornerPullLeft",
    "MouthCornerSlantRight", "MouthCornerSlantLeft", "MouthFrownRight", "MouthFrownLeft", "MouthStretchRight", "MouthStretchLeft",
    "MouthDimpleRight", "MouthDimpleLeft", "MouthRaiserUpper", "MouthRaiserLower", "MouthPressRight", "MouthPressLeft",
    "MouthTightenerRight", "MouthTightenerLeft", "TongueOut", "TongueUp", "TongueDown", "TongueRight",
    "TongueLeft", "TongueRoll", "TongueBendDown", "TongueCurlUp", "TongueSquish", "TongueFlat",
    "TongueTwistRight", "TongueTwistLeft", "SoftPalateClose", "ThroatSwallow", "NeckFlexRight", "NeckFlexLeft",
]
assert len(KEYS) == 102


def osc_string(s):
    b = s.encode("ascii") + b"\0"
    return b + b"\0" * (-len(b) % 4)


def message(payload):
    return osc_string("/tracking/face/v1") + osc_string(",b") + struct.pack(">i", len(payload)) + payload + b"\0" * (-len(payload) % 4)


def bundle(*elements):
    out = osc_string("#bundle") + struct.pack(">Q", 1)
    for e in elements:
        out += struct.pack(">i", len(e)) + e
    return out


def payload(values):
    data = bytearray(102)
    for name, v in values.items():
        data[KEYS.index(name)] = max(0, min(255, round(v * 255)))
    return bytes(data)


def parse_set(text):
    values = {}
    for pair in filter(None, (p.strip() for p in text.split(","))):
        name, v = pair.split("=")
        if name.strip() not in KEYS:
            sys.exit(f"send-face-blob: unknown key {name.strip()}")
        values[name.strip()] = float(v)
    return values


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--port", type=int, default=9000)
    ap.add_argument("--seconds", type=float, default=30)
    ap.add_argument("--rate", type=float, default=60, help="packets per second")
    ap.add_argument("--set", default="", help='"Key=value,..." (0-1)')
    ap.add_argument("--form", choices=["bundle", "plain", "nested"], default="bundle")
    a = ap.parse_args()
    msg = message(payload(parse_set(a.set)))
    packet = {"bundle": lambda: bundle(msg), "plain": lambda: msg, "nested": lambda: bundle(bundle(msg))}[a.form]()
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sent = 0
    end = time.monotonic() + a.seconds
    try:
        while time.monotonic() < end:
            try:
                sock.sendto(packet, ("127.0.0.1", a.port))
                sent += 1
            except ConnectionRefusedError:
                pass  # nobody listens yet (ICMP port unreachable)
            time.sleep(1 / a.rate)
    except KeyboardInterrupt:
        pass
    print(f"send-face-blob: sent {sent} packets to 127.0.0.1:{a.port}")


if __name__ == "__main__":
    main()
