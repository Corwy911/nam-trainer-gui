# Third-party software / Software di terze parti

This project's own code is MIT-licensed (see `LICENSE`). It includes, downloads or redistributes the following software, each
under its own license. Nothing here is legal advice; check the licenses linked below before redistributing.

## In the repository (`trainers/`)

| Software | Author | License | Where |
|---|---|---|---|
| [neural-amp-modeler](https://github.com/sdatkinson/neural-amp-modeler) 0.13.0 | Steven Atkinson | MIT | `trainers/official/` (unmodified `nam` package from PyPI, with its `LICENSE`) |
| [NAM Trainer Reloaded](https://github.com/SlamminMofo/NAM-Trainer-Reloaded) 1.0.1 | SlamminMofo (fork of neural-amp-modeler) | MIT | `trainers/reloaded/` (unmodified, with its `LICENSE`) |
| auraloss (bundled inside both trainers) | Christian Steinmetz | Apache-2.0 | `trainers/*/nam/_dependencies/auraloss/LICENSE` |

The page can update these two folders from the upstream releases (the "Check …" buttons).

## In the installer release (`Installa NAM.exe` + `Installa NAM.dat.*`), not in the repository

The release files carry Python packages installed offline by the installer. Their licenses are in each package's
`*.dist-info` folder once installed. The main ones:

- **Python 3.12** — PSF License.
- **PyTorch / torchaudio** (BSD-3-Clause). The NVIDIA build (`+cu128`) contains NVIDIA CUDA runtime libraries, distributed under the
  [NVIDIA CUDA EULA](https://docs.nvidia.com/cuda/eula/). The CPU build comes from the same PyTorch project.
- **AMD ROCm SDK / PyTorch for Windows** (`rocm-sdk-*`, `torch +rocm7.2.1`) — AMD's public preview packages from `repo.radeon.com`,
  under AMD's and the components' own licenses.
- **NumPy, SciPy, scikit-learn, matplotlib, librosa, PyTorch Lightning, FastAPI, Starlette, uvicorn, pydantic, Hugging Face
  libraries, …** — BSD / MIT / Apache-2.0 style licenses (full list: `installer/requirements.lock`).
- **Microsoft Visual C++ Redistributable 2015-2022 (x64)** — Microsoft's redistribution terms
  (the installer runs it only if the PC does not have it).

This project is independent and is not affiliated with or endorsed by any of the above.

---

## Italiano

Il codice di questo progetto è sotto licenza MIT (`LICENSE`). Il repository contiene i due trainer non modificati
(`trainers/`, entrambi MIT, con i rispettivi `LICENSE`; dentro c'è anche auraloss, Apache-2.0). Il pacchetto dell'installatore
(nella release, non nel repository) ridistribuisce Python, PyTorch (con le librerie CUDA di NVIDIA nella versione NVIDIA),
lo stack ROCm di AMD (versione AMD), i pacchetti Python elencati in `installer/requirements.lock` e il runtime Visual C++ di
Microsoft: ognuno resta sotto la propria licenza. Non è consulenza legale: controlla le licenze prima di ridistribuire.
