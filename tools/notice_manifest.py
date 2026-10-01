"""Verify the exact upstream license texts retained by this release."""

from __future__ import annotations

import hashlib
import json
from pathlib import Path


def verified_notices(root: Path) -> tuple[set[str], list[dict]]:
    manifest = root / "packaging/third-party-notices.json"
    if not manifest.is_file():
        return set(), [{"file": "packaging/third-party-notices.json", "categories": ["missing-notice-manifest"]}]
    entries = json.loads(manifest.read_text(encoding="utf-8"))["files"]
    trusted, findings = set(), []
    for entry in entries:
        relative = entry["file"]
        path = (root / relative).resolve()
        if (path != root / "LICENSE" and root / "licenses" not in path.parents) or relative in trusted:
            raise ValueError("Invalid or duplicate license path in notice manifest")
        if not path.is_file() or hashlib.sha256(path.read_bytes()).hexdigest() != entry["sha256"]:
            findings.append({"file": relative, "categories": ["notice-integrity"]})
        else:
            trusted.add(relative)
    return trusted, findings


if __name__ == "__main__":
    _, findings = verified_notices(Path(__file__).resolve().parents[1])
    print(json.dumps({"findings": findings}))
    raise SystemExit(bool(findings))
