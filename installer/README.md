# NAM installer — how it is built and released

*[Versione italiana: README.it.md](README.it.md)*

`Installa NAM.exe` + `Installa NAM.dat` (in a GitHub release: `Installa NAM.dat.001`, `.002`, `.003`) form an **offline
installer** that carries everything: Python, PyTorch in **three builds** (NVIDIA CUDA, AMD ROCm, CPU only), all packages, the two
trainers, the web program and the Visual C++ runtime. Copy the files into one folder on the target PC, double-click the exe:
it asks the language, **detects the hardware**, and installs the right version there, with **no internet**.

## The files

- `Installa NAM.exe` (~30 KB) — the program you run: language, hardware detection, extraction with SHA-256 checks, then
  `Installa.ps1`. Windows cannot run an `.exe` larger than 4 GiB, so the data is a separate file next to it.
- `Installa NAM.dat` (~5.3 GB) — the package; or, from GitHub, its numbered parts (each under 2 GiB, the limit of a release
  file). The exe reads the parts straight from the same folder as if they were one file: **download them all, no joining needed**.
- `SHA256SUMS.txt`, `LEGGIMI.txt` (Italian) / `README-EN.txt` (English) — checksums and the end-user guide.

## What it does on the target PC

0. **Asks the language** (1 = English, 2 = Italiano; the Windows language is the suggestion). Installer, `Installa.ps1`,
   `Avvia/Ferma NAM.bat` and the page's first-visit language follow it (`nam-web\hardware.json`, `"language"`).
   `/LANG=en|it` (or `NAM_INSTALL_LANG`) skips the question; with `/S` the Windows language is used.
1. **Detects the hardware** (graphics cards via WMI, by vendor id: NVIDIA `10DE`, AMD `1002`) and picks the version:
   - **NVIDIA** card → PyTorch 2.11 CUDA 12.8 (the well-tested one);
   - else a suitable **AMD Radeon** (RX / PRO / AI PRO series, Ryzen AI 8050S/8060S/890M/880M; Windows 11 only) →
     PyTorch 2.9.1 **ROCm 7.2.1**, AMD's official preview for Windows (repo.radeon.com);
   - else → PyTorch 2.11 **CPU**.

   NVIDIA wins over AMD (laptops with an AMD iGPU plus an NVIDIA GPU). It shows what it found and lets you choose another
   version (1 = NVIDIA, 2 = AMD, 3 = CPU). Options: `/rileva` (only show it), `/VARIANT=nvidia|amd|cpu` (skip the detection).
2. Shows the folder, the free space needed **for that version**, and asks to confirm. If the folder is not empty or already has
   an installation it says so **before copying anything**.
3. Copies **only** the files this PC needs into `_setup\stage`, checking every SHA-256 (AMD also copies the CPU build: it is the fallback).
4. Runs `Installa.ps1`: checks Windows/hardware/driver, installs the Visual C++ runtime only if missing (UAC prompt once), builds the
   venv with `pip --no-index` (PyTorch of the variant first, then the common packages), checks the imports **and tries the GPU** (a
   small matrix multiplication), writes `nam-web\hardware.json`, moves the program in place and deletes `_setup`. **With AMD, if
   the GPU is not usable, it uninstalls ROCm and installs the CPU build by itself.**
5. Run again in an installed folder it updates the program and the Python environment (detection redone) and **leaves untouched**
   `NAM Generati`, `File WAV caricati`, `Backup`, the trainers (even if updated from the page) and `nam-web\settings.json` (LAN switch).

Nothing outside the folder is changed (no registry, no PATH) except the optional Microsoft Visual C++ runtime.
Uninstall = delete the folder. `"Installa NAM.exe" /verifica` only checks the files' integrity.

## Requirements of the target PC

- Windows 10/11 64-bit; for AMD acceleration **Windows 11** and Adrenalin driver **26.2.2+**.
- NVIDIA: RTX 20 series or newer (incl. 50 series), driver 570+.
- AMD (preview from AMD, official list): RX 9060 XT / 9070 / 9070 XT, RX 7700 / 7800 / 7900, Radeon PRO W7700 / W7800 / W7900,
  Radeon AI PRO R9700, Ryzen AI (gfx1150/1151). **Install path at most 76 characters** (ROCm files have very long names and Windows
  limits paths to 260): the installer refuses with a clear message otherwise.
- Free space while installing: NVIDIA ~10.6 GB, AMD ~12.3 GB, CPU ~3.6 GB; afterwards ~6 / ~8 / ~2 GB (plus the installer files).
- The exe is unsigned: SmartScreen may say "Windows protected your PC" → *More info → Run anyway*.

## Building it

Python 3.12 (the same one is copied into the installer) and ~16 GB of free disk for the cache. From the repository root:

```
python installer\build.py all        # first run downloads ~5.5 GB of wheels into installer\cache
python installer\build.py release    # installer\output\release\ : the files for a GitHub release
```

Steps (each can be run alone: `lock`, `wheels`, `redist`, `stub`, `stage`, `pack`, `verify`, `release`):

- `lock` — regenerates the lock files from the development venv (`pip freeze` of a venv with the CUDA torch): `requirements.lock`
  (common packages, no torch), `requirements-nvidia.lock`, `requirements-cpu.lock`, `requirements-amd.lock`. They are committed;
  `all` only runs `lock` when they are missing.
- `wheels` — downloads into `cache\wheelhouse` (common, PyPI), `wheelhouse-nvidia` (PyTorch cu128 index), `wheelhouse-cpu`
  (PyTorch cpu index), `wheelhouse-amd` (repo.radeon.com + the `rocm` meta package built from its sdist).
- `redist` — downloads `vc_redist.x64.exe` from Microsoft and checks its digital signature.
- `stub` — compiles `src\Stub.cs` with the C# compiler that ships with Windows.
- `stage` — assembles `cache\stage`: `nam-web`, `trainers`, the `.bat` files and READMEs, the base Python, the wheelhouses,
  `src\Installa.ps1`. Stops if it finds this PC's user folder in a text file.
- `pack` / `verify` — `pack.py` writes `Installa NAM.dat` (files, index with SHA-256 and space needed per variant, trailer) and copies
  the exe next to it; then everything is re-read and verified.
- `release` — splits the data in parts under 2 GiB and writes `SHA256SUMS.txt` into `output\release`.

After changing the program (`nam-web`, `trainers`, `.bat`) only `stage`, `pack`, `verify` (and `release`) are needed.
`$NAM_BASE_PYTHON` points to another Python 3.12 folder if the default one is not found.

## Publishing a release (GitHub CLI)

```
gh auth login                                                # once
python installer\build.py all
python installer\build.py release
gh release create v1.0.0 installer\output\release\* --title "NAM Trainer GUI 1.0" --notes-file installer\RELEASE_NOTES.md
```

Every release file must stay under 2 GiB (`release` checks it); uploading ~5.4 GB takes a while on a slow connection.

## Known limits

- Tested on an NVIDIA PC (real installs, upgrade over an existing install, corrupted/truncated file) and on the CPU build (with a
  real training). **AMD acceleration has not been tried on a real AMD card**: verified are the package, the offline install, the
  imports, both trainers with the PyTorch 2.9.1/ROCm stack on CPU, and the automatic fallback to CPU.
- The branch that installs the Visual C++ runtime (needs UAC) was not exercised: it was already present on the test PC.
- The graphics driver is not included: the installer checks it and warns.
- Installer and page are Italian and English; the server log and pip's technical output are not translated.
