#!/usr/bin/env python3
"""Checks that the perfect sync template matches its definition.

Usage:
  scripts/check-perfect-sync.py <package dir> [--log <Unity log of Cli.Check>]

Reads the table in <package dir>/Documentation~/perfect-sync.md (ARKit name, region, tracking keys) and "neutral"
(region -> tracking key -> part) of <package dir>/Templates/expression.template.jsonc, and prints every row that differs.
With --log, also compares the Blendshapes Set rows that Cli.Check printed for a generated avatar
("[FTG] check: face tracking row FTG/neutral/<region> <key> -> <blendshape> x<multiplier>"): each row of the table
must be there, with a blendshape whose name equals the ARKit name without regard to capitals.
Exit code 0 when everything matches, 1 otherwise.
"""
import json
import re
import sys
from pathlib import Path


def strip_comments(text):
    out, i, n, in_str = [], 0, len(text), False
    while i < n:
        c = text[i]
        if in_str:
            out.append(c)
            if c == "\\" and i + 1 < n:
                out.append(text[i + 1])
                i += 2
                continue
            if c == '"':
                in_str = False
        elif c == '"':
            in_str = True
            out.append(c)
        elif text.startswith("//", i):
            while i < n and text[i] != "\n":
                i += 1
            continue
        elif text.startswith("/*", i):
            i = text.index("*/", i) + 2
            continue
        else:
            out.append(c)
        i += 1
    return re.sub(r",(\s*[}\]])", r"\1", "".join(out))


def definition(md):
    rows = {}
    for line in md.read_text(encoding="utf-8").splitlines():
        m = re.match(r"^\| `(\w+)` \| `(\w+)` \| (.+) \|$", line)
        if m:
            rows[m.group(1)] = (m.group(2), re.findall(r"`(\w+)`", m.group(3)))
    return rows


def template(path):
    data = json.loads(strip_comments(path.read_text(encoding="utf-8")))
    rows = {}
    for region, keys in data["neutral"].items():
        for key, value in keys.items():
            part = value if isinstance(value, str) else value["part"]
            if part.split(".")[0] != region:
                rows.setdefault("(region of " + part + ")", (region, []))[1].append(key)
                continue
            name = part.split(".")[-1]
            rows.setdefault(name, (region, []))[1].append(key)
    return rows


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 2
    pkg = Path(argv[1])
    log = Path(argv[argv.index("--log") + 1]) if "--log" in argv else None
    want = definition(pkg / "Documentation~" / "perfect-sync.md")
    have = template(pkg / "Templates" / "expression.template.jsonc")
    bad = []
    for name in sorted(set(want) | set(have)):
        if want.get(name) != have.get(name):
            bad.append(f"template: {name}: definition {want.get(name)} template {have.get(name)}")
    if log is not None:
        got = {}
        for line in log.read_text(encoding="utf-8", errors="replace").splitlines():
            m = re.search(r"\[FTG\] check: face tracking row FTG/neutral/(\w+) (\w+) -> (\S+) x", line)
            if m:
                got[(m.group(1), m.group(2))] = m.group(3)
        for name, (region, keys) in want.items():
            for key in keys:
                shape = got.get((region, key))
                if shape is None or shape.lower() != name.lower():
                    bad.append(f"generated: {region} {key}: expected {name}, found {shape}")
    for b in bad:
        print(b)
    rows = sum(len(k) for _, k in want.values())
    if bad:
        print(f"{len(bad)} rows differ")
        return 1
    print(f"{len(want)} names, {rows} rows match" + (" (template and generated)" if log else " (template)"))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
