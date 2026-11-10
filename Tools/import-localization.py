#!/usr/bin/env python3
"""Converts a flat VData localization file (KeyValues: "file" { "key" "value" ... }) into the JSON the Unity
project reads, e.g. survivors_difficulty_russian.txt → Assets/Game/Data/Localization/difficulty_ru.json
(design: .kiro/specs/screen-flow/design.md Ф12). Keys and values are copied as they are (markup such as <b> and
<br/> stays; the UI converts it). Exit code 1 if the input has no keys or a line cannot be read.
Usage: python3 Tools/import-localization.py INPUT.txt OUTPUT.json
"""
import json
import re
import sys

PAIR = re.compile(r'^\s*"((?:[^"\\]|\\.)*)"\s+"((?:[^"\\]|\\.)*)"\s*$')
SKIP = re.compile(r'^\s*("(?:[^"\\]|\\.)*"\s*)?[{}]?\s*$|^\s*//')


def unescape(text):
    return re.sub(r'\\(.)', lambda m: {"n": "\n", "t": "\t"}.get(m.group(1), m.group(1)), text)


def parse(text):
    """Pairs of the flat KeyValues block; the first duplicate wins (Source keeps the first token)."""
    result = {}
    for number, line in enumerate(text.lstrip("\ufeff").splitlines(), 1):
        match = PAIR.match(line)
        if match:
            result.setdefault(unescape(match.group(1)), unescape(match.group(2)))
        elif not SKIP.match(line):
            raise ValueError(f"line {number}: cannot read {line.strip()!r}")
    if not result:
        raise ValueError("no localization keys")
    return result


def main(argv):
    if len(argv) != 3:
        print(__doc__.strip().splitlines()[-1], file=sys.stderr)
        return 2
    try:
        with open(argv[1], encoding="utf-8") as f:
            pairs = parse(f.read())
    except (OSError, ValueError) as e:
        print(f"import-localization: {argv[1]}: {e}", file=sys.stderr)
        return 1
    with open(argv[2], "w", encoding="utf-8", newline="\n") as f:
        json.dump(dict(sorted(pairs.items())), f, ensure_ascii=False, indent=2)
        f.write("\n")
    print(f"Wrote {len(pairs)} keys to {argv[2]}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
