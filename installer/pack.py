"""Append a folder to the installer stub, producing the self-extracting installer.

Layout of the output file (must match src/Stub.cs):
    [stub.exe][file data ...][index, UTF-8 text][trailer, 32 bytes]
With stub=None only the part after the stub is written ("Installa NAM.dat", offsets counted from the start of that file):
Windows cannot run an .exe larger than 4 GiB, so the big package sits next to the small stub.
Index lines (tab separated, '/' in paths):
    N <bytes free space the install needs (largest variant)>
    V <variant> <bytes free space that variant needs>      (nvidia | amd | cpu; optional)
    D <relative folder>                 (folders that must exist, incl. empty ones)
    F <size> <absolute offset> <sha256> <relative path>
Trailer: b"NAMINST1" + int64 index offset + int64 index length + 8 reserved bytes.

    python pack.py verify <installer.exe>     re-read the file and check every SHA-256
"""

import hashlib
import struct
import sys
from pathlib import Path

MAGIC = b"NAMINST1"
CHUNK = 8 * 1024 * 1024


def pack(stub, stage: Path, output: Path, need_bytes: int, need_by_variant: dict = None) -> dict:
    stage = stage.resolve()
    files = sorted(p for p in stage.rglob("*") if p.is_file())
    dirs = sorted(p.relative_to(stage).as_posix() for p in stage.rglob("*") if p.is_dir())
    index = [f"N\t{need_bytes}"] + [f"V\t{v}\t{n}" for v, n in sorted((need_by_variant or {}).items())] + [f"D\t{d}" for d in dirs]
    total = 0
    with open(output, "wb") as out:
        if stub is not None:
            out.write(stub.read_bytes())
        for path in files:
            rel = path.relative_to(stage).as_posix()
            if any(part in ("", "..") for part in rel.split("/")) or ":" in rel or "\\" in rel:
                raise ValueError(f"unsafe path in stage: {rel}")
            offset, digest, size = out.tell(), hashlib.sha256(), 0
            with open(path, "rb") as src:
                while chunk := src.read(CHUNK):
                    digest.update(chunk)
                    out.write(chunk)
                    size += len(chunk)
            total += size
            index.append(f"F\t{size}\t{offset}\t{digest.hexdigest()}\t{rel}")
        index_bytes = ("\n".join(index) + "\n").encode("utf-8")
        index_offset = out.tell()
        out.write(index_bytes)
        out.write(MAGIC + struct.pack("<qq", index_offset, len(index_bytes)) + b"\0" * 8)
    return {"files": len(files), "dirs": len(dirs), "payload_bytes": total, "size": output.stat().st_size}


def read_index(path: Path):
    with open(path, "rb") as f:
        f.seek(-32, 2)
        trailer = f.read(32)
        if trailer[:8] != MAGIC:
            raise ValueError("no NAMINST1 trailer")
        offset, length = struct.unpack("<qq", trailer[8:24])
        f.seek(offset)
        text = f.read(length).decode("utf-8")
    files, dirs, need, variants = [], [], 0, {}
    for line in text.splitlines():
        parts = line.split("\t")
        if parts[0] == "F":
            files.append({"size": int(parts[1]), "offset": int(parts[2]), "sha256": parts[3], "path": parts[4]})
        elif parts[0] == "D":
            dirs.append(parts[1])
        elif parts[0] == "N":
            need = int(parts[1])
        elif parts[0] == "V":
            variants[parts[1]] = int(parts[2])
    return files, dirs, need, variants


def verify(path: Path) -> int:
    files, dirs, need, variants = read_index(path)
    bad = 0
    with open(path, "rb") as f:
        for entry in files:
            f.seek(entry["offset"])
            digest, left = hashlib.sha256(), entry["size"]
            while left:
                chunk = f.read(min(CHUNK, left))
                if not chunk:
                    break
                digest.update(chunk)
                left -= len(chunk)
            if left or digest.hexdigest() != entry["sha256"]:
                print("BAD", entry["path"])
                bad += 1
    print(f"{len(files)} files, {len(dirs)} dirs, need={need}, variants={variants}, bad={bad}")
    return bad


if __name__ == "__main__":
    if len(sys.argv) == 3 and sys.argv[1] == "verify":
        sys.exit(1 if verify(Path(sys.argv[2])) else 0)
    print(__doc__)
    sys.exit(2)
