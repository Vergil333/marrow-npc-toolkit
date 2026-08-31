#!/usr/bin/env python3
"""Create normalized Unity Package Manager tarballs and checksums."""

from __future__ import annotations

import gzip
import hashlib
import json
import shutil
import tarfile
from pathlib import Path

import validate_release


ROOT = Path(__file__).resolve().parents[1]
ARTIFACTS = ROOT / "Artifacts"
PACKAGES = (
    ROOT / "Packages" / "com.vergil333.marrow-npc-toolkit",
    ROOT / "Packages" / "com.vergil333.marrow-npc-toolkit.patch6",
)
def normalized_info(info: tarfile.TarInfo) -> tarfile.TarInfo:
    info.uid = 0
    info.gid = 0
    info.uname = ""
    info.gname = ""
    info.mtime = 0
    if info.isdir():
        info.mode = 0o755
    elif info.isfile():
        info.mode = 0o644
    return info


def build(package: Path) -> Path:
    manifest = json.loads((package / "package.json").read_text(encoding="utf-8"))
    name = manifest["name"]
    version = manifest["version"]
    output = ARTIFACTS / f"{name}-{version}.tgz"

    with output.open("wb") as raw:
        with gzip.GzipFile(filename="", mode="wb", fileobj=raw, mtime=0) as compressed:
            with tarfile.open(fileobj=compressed, mode="w", format=tarfile.PAX_FORMAT) as archive:
                for path in sorted(package.rglob("*")):
                    if path.is_symlink():
                        raise RuntimeError(f"Symlinks are not allowed: {path}")
                    relative = path.relative_to(package)
                    arcname = Path("package") / relative
                    info = normalized_info(archive.gettarinfo(str(path), str(arcname)))
                    if path.is_file():
                        with path.open("rb") as source:
                            archive.addfile(info, source)
                    else:
                        archive.addfile(info)
    return output


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for block in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def main() -> int:
    if validate_release.main() != 0:
        return 1

    if ARTIFACTS.exists():
        shutil.rmtree(ARTIFACTS)
    ARTIFACTS.mkdir(parents=True)

    outputs = [build(package) for package in PACKAGES]
    version = json.loads(
        (PACKAGES[0] / "package.json").read_text(encoding="utf-8")
    )["version"]
    checksum_documents = (
        ROOT / "Documentation" / "VALIDATION_EVIDENCE.md",
        ROOT / "Documentation" / f"RELEASE_NOTES_{version}.md",
    )
    checksum_lines = [f"{sha256(path)}  {path.name}" for path in outputs]
    checksums = ARTIFACTS / "SHA256SUMS.txt"
    checksums.write_text("\n".join(checksum_lines) + "\n", encoding="utf-8")
    for document in checksum_documents:
        text = document.read_text(encoding="utf-8")
        missing = [line for line in checksum_lines if line not in text]
        if missing:
            raise RuntimeError(
                "Documented release checksums are stale in "
                f"{document.relative_to(ROOT)}:\n  " + "\n  ".join(missing)
            )
    for path in outputs:
        print(path.relative_to(ROOT))
    print(checksums.relative_to(ROOT))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
