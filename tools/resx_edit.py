"""Add/remove keys in a .resx, keeping everything else byte-identical.

Usage: resx_edit.py <resx-path> <adds.json> <remove.txt>

adds.json   : {"Key": "value", ...}   (values are XML-escaped here)
remove.txt  : one key per line, ignored if the key is absent

Why a script and not the editor: there are 19 satellite files that must stay in
lockstep, and the "remove the 4 dead section keys" half has to happen in all of
them too. A hand edit across 19 files is exactly how they drift.
"""
import json
import sys
from xml.sax.saxutils import escape


def main():
    path, adds_path, remove_path = sys.argv[1], sys.argv[2], sys.argv[3]
    adds = json.load(open(adds_path, encoding="utf-8"))
    removes = [x.strip() for x in open(remove_path, encoding="utf-8") if x.strip()]

    src = open(path, encoding="utf-8-sig").read()
    bom = open(path, "rb").read()[:3] == b"\xef\xbb\xbf"

    def block(key, value):
        return f'  <data name="{key}" xml:space="preserve">{escape(value)}</data>'

    # Drop the dead keys. Tolerate both resx line shapes (some entries are
    # one-liners, some span three) so a reformat upstream cannot silently
    # leave one behind.
    for k in removes:
        one = f'  <data name="{k}" xml:space="preserve">'
        i = src.find(one)
        if i < 0:
            continue
        j = src.find("</data>", i)
        if j < 0:
            sys.exit(f"{path}: {k} has no closing </data>")
        src = src[:i] + src[j + len("</data>"):]
        src = src.replace("\r\n\r\n", "\r\n")

    # Add only what is not already there. --force rewrites the value of keys that
    # already exist, which is what you want when the *wording* changed rather than
    # the key being new (a stale hint is worse than a missing one: it is read, believed,
    # and acted on).
    force = "--force" in sys.argv
    added = 0
    for k, v in adds.items():
        one = f'  <data name="{k}" xml:space="preserve">'
        i = src.find(one)
        if i >= 0:
            if not force:
                continue
            j = src.find("</data>", i)
            if j < 0:
                sys.exit(f"{path}: {k} has no closing </data>")
            src = src[:i] + block(k, v) + src[j + len("</data>"):]
            continue
        idx = src.rfind("</root>")
        if idx < 0:
            sys.exit(f"{path}: no </root>")
        nl = "\r\n" if "\r\n" in src else "\n"
        src = src[:idx] + block(k, v) + nl + src[idx:]
        added += 1

    out = src.encode("utf-8")
    open(path, "wb").write((b"\xef\xbb\xbf" if bom else b"") + out)
    print(f"{path}: +{added}, -{len(removes)} (dead keys absent are fine)")


main()
