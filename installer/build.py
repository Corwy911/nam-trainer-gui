r"""Build "Installa NAM.exe": one self-extracting installer that carries everything needed offline, for three kinds of PC.

Run with the venv's Python (any Python 3.12 works):

    python build.py all          # wheels + redist + stub + stage + pack + verify (+ lock the first time)
    python build.py <step>       # lock | wheels | redist | stub | stage | pack | verify | release
    python build.py release      # output\release\: the files to attach to a GitHub release (data split in parts < 2 GiB)

What goes in (see src/Installa.ps1 for how it is installed):
    app\      NAM\nam-web (no tmp/cache/logs), NAM\trainers, Avvia NAM.bat, Ferma NAM.bat, README.md
    python\   portable copy of Python 3.12 (the one already installed here), without docs/scripts
    setup\    Installa.ps1, requirements.lock (packages common to all PCs), wheelhouse\*.whl,
              one PyTorch build per hardware variant: requirements-<v>.lock + wheelhouse-<v>\*.whl,
              vc_redist.x64.exe, LEGGIMI.txt

Variants (the installer detects the hardware and extracts only the matching one):
    nvidia   torch/torchaudio CUDA 12.8 (versions taken from `pip freeze` of NAM\venv, known to work)
    amd      AMD's official ROCm 7.2.1 preview for Windows (torch 2.9.1, Python 3.12, repo.radeon.com)
    cpu      torch/torchaudio CPU build (same version as the CUDA one)

The common package versions come from `pip freeze` of NAM\venv; the wheels are downloaded once into
cache\wheelhouse[-<v>] and re-used by later builds (delete cache\ to start over).
"""

import hashlib
import os
import re
import shutil
import subprocess
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
NAM = HERE.parent   # the project root: nam-web, trainers, the .bat files; this folder is NAM\installer


def find_base_python() -> Path:
    """The Python 3.12 that gets copied into the installer (a portable copy, without docs/launchers).
    $NAM_BASE_PYTHON (a folder) overrides; else the running Python if it is 3.12 and not a venv; else the usual per-user install."""
    override = os.environ.get("NAM_BASE_PYTHON")
    if override:
        return Path(override)
    if sys.version_info[:2] == (3, 12) and Path(sys.base_prefix) == Path(sys.prefix):
        return Path(sys.prefix)
    return Path(os.environ.get("LOCALAPPDATA", "")) / "Programs" / "Python" / "Python312"


PYTHON_BASE = find_base_python()
# Python used to run pip (freeze / download). The project's venv if it exists (a fresh clone has none: any Python 3.12 works).
VENV_PY = NAM / "venv" / "Scripts" / "python.exe"
if not VENV_PY.exists():
    VENV_PY = Path(sys.executable)
CACHE = HERE / "cache"
WHEELHOUSE = CACHE / "wheelhouse"          # packages common to every variant
VARIANTS = ("nvidia", "amd", "cpu")
TORCH_PACKAGES = ("torch", "torchaudio")
STAGE = CACHE / "stage"
STUB = CACHE / "stub.exe"
REDIST = CACHE / "vc_redist.x64.exe"
LOCK = HERE / "requirements.lock"           # common packages (no torch)


def variant_lock(v: str) -> Path:
    return HERE / f"requirements-{v}.lock"


def variant_wheelhouse(v: str) -> Path:
    return CACHE / f"wheelhouse-{v}"
OUTPUT = HERE / "output" / "Installa NAM.dat"        # the package (Windows cannot run an .exe over 4 GiB)
OUTPUT_EXE = HERE / "output" / "Installa NAM.exe"     # the small stub that finds it
CSC = Path(r"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe")
REDIST_URL = "https://aka.ms/vs/17/release/vc_redist.x64.exe"
# What the venv takes on the target PC (measured, plus a margin), added to the extracted files to get the free space needed.
VENV_ESTIMATE = {"nvidia": 7 * 1024**3, "amd": 9 * 1024**3, "cpu": 3 * 1024**3}
AMD_BASE = "https://repo.radeon.com/rocm/windows/rocm-rel-7.2.1"
AMD_WHEELS = [f"{AMD_BASE}/rocm_sdk_core-7.2.1-py3-none-win_amd64.whl",
              f"{AMD_BASE}/rocm_sdk_devel-7.2.1-py3-none-win_amd64.whl",
              f"{AMD_BASE}/rocm_sdk_libraries_custom-7.2.1-py3-none-win_amd64.whl",
              f"{AMD_BASE}/torch-2.9.1%2Brocm7.2.1-cp312-cp312-win_amd64.whl",
              f"{AMD_BASE}/torchaudio-2.9.1%2Brocm7.2.1-cp312-cp312-win_amd64.whl"]
AMD_META_SDIST = f"{AMD_BASE}/rocm-7.2.1.tar.gz"   # the "rocm" meta package that torch depends on (sdist -> wheel)
AMD_LOCK = ["rocm-sdk-core==7.2.1", "rocm-sdk-devel==7.2.1", "rocm-sdk-libraries-custom==7.2.1", "rocm==7.2.1",
            "torch==2.9.1+rocm7.2.1", "torchaudio==2.9.1+rocm7.2.1"]

sys.path.insert(0, str(HERE))
import pack  # noqa: E402


def say(message: str) -> None:
    print(f"[build] {message}", flush=True)


def normalize(name: str) -> str:
    return re.sub(r"[-_.]+", "-", name).lower()


def step_lock() -> None:
    out = subprocess.run([str(VENV_PY), "-m", "pip", "freeze"], capture_output=True, text=True, check=True).stdout
    lines = sorted((l.strip() for l in out.splitlines() if l.strip() and not l.startswith(("#", "-e", "pip=="))), key=str.lower)
    torch_pins = {l.partition("==")[0].lower(): l.partition("==")[2] for l in lines if l.partition("==")[0].lower() in TORCH_PACKAGES}
    if set(torch_pins) != set(TORCH_PACKAGES) or "+cu" not in torch_pins["torch"]:
        raise SystemExit(f"la venv di sviluppo dovrebbe avere torch/torchaudio CUDA, trovato: {torch_pins}")
    common = [l for l in lines if l.partition("==")[0].lower() not in TORCH_PACKAGES]
    LOCK.write_text("\n".join(common) + "\n", encoding="utf-8")
    base = {name: version.split("+")[0] for name, version in torch_pins.items()}
    variant_lock("nvidia").write_text("\n".join(f"{n}=={torch_pins[n]}" for n in TORCH_PACKAGES) + "\n", encoding="utf-8")
    variant_lock("cpu").write_text("\n".join(f"{n}=={base[n]}+cpu" for n in TORCH_PACKAGES) + "\n", encoding="utf-8")
    variant_lock("amd").write_text("\n".join(AMD_LOCK) + "\n", encoding="utf-8")
    say(f"requirements.lock: {len(common)} pacchetti comuni; PyTorch: nvidia {torch_pins['torch']}, cpu {base['torch']}+cpu, amd 2.9.1+rocm7.2.1")


def read_lock(path: Path = None) -> list:
    pins = []
    for line in (path or LOCK).read_text(encoding="utf-8").splitlines():
        if not line.strip():
            continue
        name, _, version = line.partition("==")
        pins.append((name.strip(), version.strip()))
    return pins


def wheel_index(directory: Path = None) -> dict:
    index = {}
    for w in (directory or WHEELHOUSE).glob("*.whl"):
        parts = w.name[:-4].split("-")
        index[(normalize(parts[0]), parts[1])] = w
    return index


def missing_wheels(lock: Path = None, directory: Path = None) -> list:
    index = wheel_index(directory)
    return [f"{n}=={v}" for n, v in read_lock(lock) if (normalize(n), v) not in index]


def pip(*args: str) -> None:
    subprocess.run([str(VENV_PY), "-m", "pip", *args, "--disable-pip-version-check"], check=True)


def step_wheels() -> None:
    WHEELHOUSE.mkdir(parents=True, exist_ok=True)
    todo = missing_wheels()
    if todo:
        say(f"scarico {len(todo)} wheel comuni...")
        pip("download", "-r", str(LOCK), "-d", str(WHEELHOUSE), "--no-deps", "--only-binary=:all:", "--index-url", "https://pypi.org/simple")
        todo = missing_wheels()
        if todo:
            raise SystemExit(f"wheel mancanti dopo il download: {todo}")
    total = sum(w.stat().st_size for w in WHEELHOUSE.glob("*.whl"))
    say(f"wheelhouse comune: {len(list(WHEELHOUSE.glob('*.whl')))} file, {total / 1e9:.2f} GB")

    for v in VARIANTS:
        directory = variant_wheelhouse(v)
        directory.mkdir(parents=True, exist_ok=True)
        if not missing_wheels(variant_lock(v), directory):
            say(f"wheelhouse {v}: gia completa")
            continue
        say(f"scarico PyTorch per la variante {v} (pesa da centinaia di MB a qualche GB)...")
        if v == "nvidia":
            pip("download", "-r", str(variant_lock(v)), "-d", str(directory), "--no-deps", "--only-binary=:all:",
                "--index-url", "https://pypi.org/simple", "--extra-index-url", "https://download.pytorch.org/whl/cu128")
        elif v == "cpu":
            pip("download", "-r", str(variant_lock(v)), "-d", str(directory), "--no-deps", "--only-binary=:all:",
                "--index-url", "https://download.pytorch.org/whl/cpu")
        else:
            pip("download", "--no-deps", "-d", str(directory), *AMD_WHEELS)
            pip("wheel", "--no-deps", "--no-build-isolation", "-w", str(directory), AMD_META_SDIST)
        todo = missing_wheels(variant_lock(v), directory)
        if todo:
            raise SystemExit(f"wheel {v} mancanti dopo il download: {todo}")
        total = sum(w.stat().st_size for w in directory.glob("*.whl"))
        say(f"wheelhouse {v}: {len(list(directory.glob('*.whl')))} file, {total / 1e9:.2f} GB")

def step_redist() -> None:
    CACHE.mkdir(parents=True, exist_ok=True)
    if not REDIST.exists():
        say(f"scarico il runtime Visual C++ da {REDIST_URL}")
        subprocess.run(["curl.exe", "-sL", "-o", str(REDIST), REDIST_URL], check=True)
    sig = subprocess.run(["powershell", "-NoProfile", "-Command",
                          f"$s = Get-AuthenticodeSignature -LiteralPath '{REDIST}'; \"$($s.Status)|$($s.SignerCertificate.Subject)\""],
                         capture_output=True, text=True).stdout.strip()
    say(f"firma di {REDIST.name}: {sig}")
    if not sig.startswith("Valid|") or "Microsoft Corporation" not in sig:
        REDIST.unlink(missing_ok=True)
        raise SystemExit("il runtime scaricato non ha una firma Microsoft valida: annullato")
    say(f"{REDIST.name}: {REDIST.stat().st_size / 1e6:.1f} MB")


def step_stub() -> None:
    CACHE.mkdir(parents=True, exist_ok=True)
    proc = subprocess.run([str(CSC), "/nologo", "/target:exe", "/platform:x64", "/optimize+", f"/out:{STUB}",
                           f"/win32manifest:{HERE / 'src' / 'app.manifest'}", "/reference:System.dll", "/reference:System.Core.dll", "/reference:System.Management.dll",
                           str(HERE / "src" / "Stub.cs")], capture_output=True, text=True)
    if proc.returncode != 0:
        raise SystemExit("compilazione dello stub fallita:\n" + proc.stdout + proc.stderr)
    say(f"stub compilato: {STUB.stat().st_size / 1024:.0f} KB")


def link_or_copy(src: Path, dst: Path) -> None:
    try:
        os.link(src, dst)
    except OSError:
        shutil.copy2(src, dst)


def step_stage() -> None:
    if STAGE.exists():
        shutil.rmtree(STAGE)
    ignore_common = shutil.ignore_patterns("__pycache__", "*.pyc")
    app = STAGE / "app"
    shutil.copytree(NAM / "nam-web", app / "nam-web", ignore=shutil.ignore_patterns(
        "__pycache__", "*.pyc", "tmp", "cache", "data", "*.log", "settings.json", "hardware.json"))
    shutil.copytree(NAM / "trainers", app / "trainers", ignore=ignore_common)
    for name in ("Avvia NAM.bat", "Ferma NAM.bat", "README.md", "README.it.md"):
        shutil.copy2(NAM / name, app / name)
    def ignore_python(directory: str, names: list) -> set:
        # Only the top level of the Python folder loses docs/launchers/headers. (A name-pattern filter would
        # also drop Lib\venv\scripts: fnmatch ignores case on Windows, and venv needs those launchers.)
        skip = {n for n in names if n == "__pycache__" or n.endswith(".pyc")}
        if Path(directory).resolve() == PYTHON_BASE.resolve():
            skip |= {n for n in names if n in ("Doc", "Scripts", "include", "NEWS.txt")}
        return skip

    shutil.copytree(PYTHON_BASE, STAGE / "python", ignore=ignore_python)
    setup = STAGE / "setup"
    (setup / "wheelhouse").mkdir(parents=True)
    for w in sorted(WHEELHOUSE.glob("*.whl")):
        link_or_copy(w, setup / "wheelhouse" / w.name)
    shutil.copy2(LOCK, setup / "requirements.lock")
    for v in VARIANTS:
        (setup / f"wheelhouse-{v}").mkdir()
        for w in sorted(variant_wheelhouse(v).glob("*.whl")):
            link_or_copy(w, setup / f"wheelhouse-{v}" / w.name)
        shutil.copy2(variant_lock(v), setup / f"requirements-{v}.lock")
    shutil.copy2(REDIST, setup / "vc_redist.x64.exe")
    for guide in ("LEGGIMI.txt", "README-EN.txt"):
        shutil.copy2(HERE / "src" / guide, setup / guide)
    # Windows PowerShell 5.1 reads a script as UTF-8 only if it has a BOM (the script has accents).
    (setup / "Installa.ps1").write_text((HERE / "src" / "Installa.ps1").read_text(encoding="utf-8"), encoding="utf-8-sig")
    for folder in ("NAM Generati", "File WAV caricati"):  # the installer also creates them; kept here for completeness
        pass
    leaks = scan_for_personal_paths(STAGE)
    say(f"stage pronto: {sum(1 for p in STAGE.rglob('*') if p.is_file())} file")
    if leaks:
        say("ATTENZIONE, riferimenti al PC di origine trovati (controlla):")
        for item in leaks[:20]:
            say("   " + item)
        raise SystemExit("il pacchetto contiene percorsi personali: correggi prima di procedere")


def scan_for_personal_paths(root: Path) -> list:
    """Text files (not wheels/binaries) must not mention this PC's user folder."""
    home = str(Path.home())
    needles = [home.encode(), home.replace("\\", "/").encode()]   # this PC's user folder must not end up in the package
    hits = []
    for path in root.rglob("*"):
        if not path.is_file() or path.suffix.lower() in (".whl", ".exe", ".dll", ".pyd", ".wav", ".png", ".ico", ".zip", ".gz", ".ttf"):
            continue
        try:
            data = path.read_bytes()
        except OSError:
            continue
        if b"\0" in data[:4096]:
            continue
        for needle in needles:
            if needle in data:
                hits.append(f"{path.relative_to(root)} -> {needle.decode()}")
                break
    return hits


def file_variant(rel: str) -> str:
    """Which hardware variant a staged file belongs to ("" = common); mirrors FileVariant() in Stub.cs."""
    for v in VARIANTS:
        if rel.startswith(f"setup/wheelhouse-{v}/") or rel == f"setup/requirements-{v}.lock":
            return v
    return ""


def need_by_variant() -> dict:
    """Free space each variant needs: the files that get extracted for it + the venv built from them."""
    sizes = {}
    for p in STAGE.rglob("*"):
        if p.is_file():
            sizes[file_variant(p.relative_to(STAGE).as_posix())] = sizes.get(file_variant(p.relative_to(STAGE).as_posix()), 0) + p.stat().st_size
    need = {}
    for v in VARIANTS:
        extracted = sizes.get("", 0) + sizes.get(v, 0) + (sizes.get("cpu", 0) if v == "amd" else 0)
        need[v] = extracted + VENV_ESTIMATE[v]
        say(f"variante {v}: estratti {extracted / 1e9:.2f} GB, spazio richiesto {need[v] / 1e9:.1f} GB")
    return need


def step_pack() -> None:
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    payload = sum(p.stat().st_size for p in STAGE.rglob("*") if p.is_file())
    say(f"impacchetto {payload / 1e9:.2f} GB...")
    needs = need_by_variant()
    started = time.time()
    info = pack.pack(None, STAGE, OUTPUT, need_bytes=max(needs.values()), need_by_variant=needs)
    shutil.copy2(STUB, OUTPUT_EXE)
    for guide in ("LEGGIMI.txt", "README-EN.txt"):   # the user guides travel next to the installer
        shutil.copy2(HERE / "src" / guide, OUTPUT.parent / guide)
    say(f"creati {OUTPUT_EXE.name} ({OUTPUT_EXE.stat().st_size / 1024:.0f} KB) e {OUTPUT.name}: {info['files']} file, {info['size'] / 1e9:.2f} GB in {time.time() - started:.0f}s")


def step_verify() -> None:
    say("verifico ogni SHA-256 del file creato...")
    if pack.verify(OUTPUT) != 0:
        raise SystemExit("verifica fallita")
    digest = hashlib.sha256()
    with open(OUTPUT, "rb") as f:
        while chunk := f.read(16 * 1024 * 1024):
            digest.update(chunk)
    (OUTPUT.parent / "Installa NAM.sha256.txt").write_text(f"{digest.hexdigest()}  {OUTPUT.name}\n", encoding="utf-8")
    say(f"SHA-256 del file intero: {digest.hexdigest()}")


PART_SIZE = 1_900_000_000   # GitHub caps a release file at 2 GiB (2_147_483_648 bytes)
RELEASE = HERE / "output" / "release"


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with open(path, "rb") as f:
        while chunk := f.read(16 * 1024 * 1024):
            digest.update(chunk)
    return digest.hexdigest()


def step_release() -> None:
    """Everything a GitHub release needs, in output\release: the exe, the data split in numbered parts (the exe reads
    "Installa NAM.dat.001", ".002", ... straight from the same folder), the two guides and SHA256SUMS.txt."""
    if not OUTPUT.exists() or not OUTPUT_EXE.exists():
        raise SystemExit("prima serve il pacchetto: python build.py pack")
    if RELEASE.exists():
        shutil.rmtree(RELEASE)
    RELEASE.mkdir(parents=True)
    shutil.copy2(OUTPUT_EXE, RELEASE / OUTPUT_EXE.name)
    for guide in ("LEGGIMI.txt", "README-EN.txt"):
        shutil.copy2(HERE / "src" / guide, RELEASE / guide)
    size, index = OUTPUT.stat().st_size, 0
    with open(OUTPUT, "rb") as src:
        while src.tell() < size:
            index += 1
            part = RELEASE / f"{OUTPUT.name}.{index:03d}"
            left = PART_SIZE
            with open(part, "wb") as dst:
                while left:
                    chunk = src.read(min(16 * 1024 * 1024, left))
                    if not chunk:
                        break
                    dst.write(chunk)
                    left -= len(chunk)
    files = sorted(p for p in RELEASE.iterdir() if p.is_file())
    (RELEASE / "SHA256SUMS.txt").write_text("".join(f"{sha256_file(p)} *{p.name}\n" for p in files), encoding="utf-8")
    for p in sorted(RELEASE.iterdir()):
        say(f"{p.name}: {p.stat().st_size / 1e6:.1f} MB")
    if any(p.stat().st_size >= 2 * 1024**3 for p in RELEASE.iterdir()):
        raise SystemExit("un file supera i 2 GiB: GitHub lo rifiuterebbe")


STEPS = {"lock": step_lock, "wheels": step_wheels, "redist": step_redist, "stub": step_stub, "stage": step_stage,
         "pack": step_pack, "verify": step_verify, "release": step_release}

if __name__ == "__main__":
    wanted = sys.argv[1] if len(sys.argv) > 1 else "all"
    if wanted == "all":
        # The lock files are committed; they are only regenerated (from a development venv with the CUDA torch) on request.
        steps = ("wheels", "redist", "stub", "stage", "pack", "verify")
        for name in (steps if LOCK.exists() and all(variant_lock(v).exists() for v in VARIANTS) else ("lock",) + steps):
            STEPS[name]()
    elif wanted in STEPS:
        STEPS[wanted]()
    else:
        raise SystemExit(__doc__)
