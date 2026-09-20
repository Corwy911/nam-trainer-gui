# NAM Trainer GUI

A web interface to train [Neural Amp Modeler](https://github.com/sdatkinson/neural-amp-modeler) (NAM) models on a
Windows PC — NVIDIA GPU, AMD GPU or just the CPU — and use it from that PC or from any device on your network
(for instance upload the recordings from a Mac and let the Windows machine with the GPU do the work).

*[Versione italiana: README.it.md](README.it.md)*

## Features

- **Two trainers in one page**: the official `neural-amp-modeler` and
  [NAM Trainer Reloaded](https://github.com/SlamminMofo/NAM-Trainer-Reloaded) (A2 slimmable, standard/light/extended WaveNet,
  LSTM, two-stage training, learning-rate scheduling). Pick the model from a dropdown.
- **One folder holds everything**: models in `NAM Generati\`, uploaded WAV files in `File WAV caricati\` (subfolders included);
  the page mirrors both, so you can retrain any model with another trainer in two clicks.
- **Queue, live progress, ESR, plots, logs**, stop-and-export, download of the `.nam`.
- **Trainer updates from the page**: "Check neural-amp-modeler" / "Check NAM-Trainer-Reloaded" look for a new release, show what
  changed (and anything suspicious), test the new version with a mini-training, keep a backup and can roll back.
- **English / Italiano** in the page (dropdown) and in the installer.
- **LAN switch**: use it only from the PC where it is installed (like an app) or from other PCs on the network.
- **Offline installer** that detects the hardware and installs the right PyTorch: **NVIDIA** (CUDA 12.8, RTX 20–50 series),
  **AMD** (AMD's official ROCm 7.2.1 preview for Windows, Radeon RX 7000/9000 and a few others) or **CPU only**.

## Install (just to use it)

1. From the **[Releases](../../releases)** page download **all** the `Installa.NAM` files: `Installa.NAM.exe` and the parts
   `Installa.NAM.dat.001`, `.002`, `.003` (GitHub does not accept files over 2 GiB, so the package is split).
2. Put them **together in the same folder** — the one where you want NAM installed (a short, empty one, e.g. `C:\NAM`) — and keep
   the names as they are (the exe finds its data by its own name).
3. Double-click `Installa.NAM.exe`. It asks the language, detects the graphics card and installs the matching version,
   **without internet**. Windows may warn about an unknown publisher (the exe is not signed): *More info → Run anyway*.
4. Double-click `Avvia NAM.bat` and open the page shown (`http://localhost:8765`).

Check the downloads with `SHA256SUMS.txt` (release page); `Installa.NAM.exe /verifica` checks every file's integrity.
Details, options and requirements: [installer/README.md](installer/README.md).

**Requirements:** Windows 10/11 64-bit; NVIDIA RTX 20+ with driver 570+, *or* a supported Radeon on Windows 11 with the
Adrenalin 26.2.2+ driver, *or* any PC (CPU only, much slower). 10–13 GB free while installing, 3–8 GB afterwards.

> **AMD acceleration is experimental.** It uses AMD's ROCm-on-Windows PyTorch preview. The installer checks that the GPU really
> works and falls back to the CPU version by itself if it does not, but it has not been tried on a real Radeon by the author.
> Reports are welcome.

## Using it

1. `Avvia NAM.bat` starts the server in the background and opens the page; `Ferma NAM.bat` stops it completely.
2. Choose the **trainer**, the **input** (the DI / test signal) and one or more **outputs** (the reamped recordings) —
   new WAV files or ones already uploaded — and press *Start training*. The `.nam` appears in the card and in `NAM Generati\<name>\`.
3. Top right: language, the **LAN** switch (with LAN off the server answers only requests from the PC itself; it does not change
   Windows Firewall rules) and the hardware in use. With no usable GPU a notice appears and the default epochs are reduced.

More (Italian): [README.it.md](README.it.md).

## Repository layout

| Path | What |
|---|---|
| `nam-web/` | FastAPI server (`app.py`, `worker.py`, `updater.py`, …) and the page (`static/index.html`, texts in `static/i18n.json`) |
| `trainers/` | The two trainers, unmodified (`official/`, `reloaded/`) with their licenses; replaced by the updater |
| `installer/` | Builds the offline installer: `build.py`, `pack.py`, `src/Stub.cs` (extractor + hardware detection), `src/Installa.ps1` |
| `Avvia NAM.bat`, `Ferma NAM.bat` | Start / stop |
| `THIRD_PARTY.md` | Credits and licenses of what the installer redistributes |

### Run from source (development)

Needs Python 3.12 and, for GPU training, a PyTorch build that matches your card.

```
python -m venv venv
venv\Scripts\python -m pip install -r installer\requirements-nvidia.lock --extra-index-url https://download.pytorch.org/whl/cu128
venv\Scripts\python -m pip install -r installer\requirements.lock
venv\Scripts\python nam-web\app.py          # http://localhost:8765
```

(`requirements-cpu.lock` / `requirements-amd.lock` for the other variants; without `nam-web\hardware.json` the app assumes NVIDIA
if `nvidia-smi` exists, CPU otherwise.)

### Build the installer

```
python installer\build.py all        # downloads ~5.5 GB of wheels on the first run, then packs installer\output\Installa NAM.{exe,dat}
python installer\build.py release    # installer\output\release\: exe + data in parts < 2 GiB + SHA256SUMS.txt
```

See [installer/README.md](installer/README.md) for how a release is published.

## Credits and license

The code of this project is released under the **MIT License** ([LICENSE](LICENSE)). It bundles and installs third-party
software under their own licenses — see [THIRD_PARTY.md](THIRD_PARTY.md). This project is independent: it is not affiliated
with, or endorsed by, the authors of Neural Amp Modeler, NAM Trainer Reloaded, NVIDIA, AMD or Microsoft.
