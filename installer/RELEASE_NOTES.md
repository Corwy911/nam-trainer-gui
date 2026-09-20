## NAM Trainer GUI 1.0

First public release: a web GUI to train Neural Amp Modeler models on Windows with an **offline installer that detects your hardware**.

### Download and install

Download **all** the `Installa.NAM` files of this release — `Installa.NAM.exe` and `Installa.NAM.dat.001`, `.002`, `.003` — and put them
**together in one empty, short folder** (e.g. `C:\NAM`; keep the names as they are: the exe finds its data by its own name).
Double-click `Installa.NAM.exe` (choose the language; Windows may warn that the publisher is unknown: *More info → Run anyway*), then
`Avvia NAM.bat`. No internet is needed. Check the downloads with `SHA256SUMS.txt` (`Get-FileHash <file>` in PowerShell);
`Installa.NAM.exe /verifica` checks every file. Guides: `README-EN.txt` (English), `LEGGIMI.txt` (Italiano).

### What's in it

- Two trainers in one page: official **neural-amp-modeler 0.13.0** and **NAM Trainer Reloaded 1.0.1** (A2, WaveNet variants, LSTM, two-stage training).
- Models and uploaded WAV files live in two mirrored folders; retrain any model with another trainer in two clicks.
- Queue, live progress, plots, logs; **trainer updates from the page** with test, backup and rollback.
- **English / Italiano**, and a **LAN switch** (use it only from the PC where it is installed, or from other PCs on the network).
- Installer for **NVIDIA** (CUDA 12.8, RTX 20–50), **AMD** (ROCm 7.2.1 preview, Windows 11) or **CPU only**, chosen automatically.

### Please note

- **AMD acceleration is experimental and has not been tried on a real Radeon by the author.** If the GPU is not usable the installer installs the CPU version by itself.
- With no usable GPU, training runs on the CPU and is much slower.
- The exe is not code-signed (SmartScreen warning). For AMD the install path must be at most 76 characters.
- Requirements: Windows 10/11 64-bit; NVIDIA driver 570+ (RTX 20+); AMD driver 26.2.2+ on Windows 11; 10–13 GB free while installing.

---

## NAM Trainer GUI 1.0 (Italiano)

Prima versione pubblica: interfaccia web per addestrare modelli Neural Amp Modeler su Windows, con **installatore offline che riconosce l'hardware**.

**Scarica TUTTI** i file `Installa.NAM` di questa release (`Installa.NAM.exe` e `Installa.NAM.dat.001`, `.002`, `.003`) e mettili **insieme in
una cartella vuota e corta** (es. `C:\NAM`, senza cambiare i nomi). Doppio clic su `Installa.NAM.exe` (scegli la lingua; se Windows avvisa che l'editore è
sconosciuto: *Ulteriori informazioni → Esegui comunque*), poi `Avvia NAM.bat`. Non serve internet. Controlla i download con
`SHA256SUMS.txt`.

L'accelerazione **AMD è sperimentale e l'autore non l'ha provata su una vera Radeon**: se la GPU non è utilizzabile l'installatore
installa da solo la versione CPU. Senza GPU utilizzabile il training gira sul processore ed è molto più lento.
