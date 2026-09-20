NAM Trainer - installation
==========================

(The Italian version of this guide is in LEGGIMI.txt.)

Downloaded from GitHub the files are named with dots instead of spaces: Installa.NAM.exe and Installa.NAM.dat.001, .002, .003
(the data comes in parts there). Wherever this guide says "Installa NAM.exe" use that name; keep all the files together,
under the names they have: the exe finds its data by its own name.

The installer is TWO files that must stay together in the same folder:
   Installa NAM.exe   (the small program you run)
   Installa NAM.dat   (the package, about 5 GB: Python, PyTorch in three versions, all packages, the trainers)

1. Copy both files into the folder where you want to install the program
   (e.g. C:\NAM or D:\Tools\NAM). A short, empty folder is best.
2. Double-click "Installa NAM.exe".
   - The first question is the language of the installer: 1 = English, 2 = Italiano (Enter accepts the
     suggested one, taken from the Windows display language). The choice also sets the language the web page
     opens in the first time; it can be changed at any moment from the page.
   - Windows may warn that the publisher is unknown (the program is not signed):
     "More info" -> "Run anyway".
   - The program recognises the PC's hardware by itself and installs the right version:
       * NVIDIA card        -> PyTorch CUDA (the fastest and best tested version)
       * AMD Radeon card    -> PyTorch ROCm, AMD's official acceleration for Windows (see below)
       * neither            -> PyTorch CPU only (it works, but training is much slower)
     Before copying anything it shows what it found and what it will install; if that is not what you want you
     can pick the version by hand (1 = NVIDIA, 2 = AMD, 3 = CPU only).
   - About 10-15 GB of free space are needed during the installation (it depends on the version: the program
     tells you); when it is over, 3 to 8 GB stay in use (plus the installation files).
   - No internet needed: Python, PyTorch (all three versions) and the packages are in the .dat file.
   - If the Visual C++ runtime is missing (rare), Windows asks once for administrator permission.
3. When it is done: double-click "Avvia NAM.bat" and open the page shown.

PC requirements
   - Windows 10/11, 64-bit.
   - NVIDIA: RTX 20 series or newer (including 50 series) GPU with a recent NVIDIA driver (570 or later).
   - AMD: Windows 11, Adrenalin driver 26.2.2 or later, and a Radeon supported by AMD for PyTorch:
     RX 9000 (9060 XT, 9070, 9070 XT), RX 7700/7800/7900, Radeon PRO W7700/W7800/W7900, Radeon AI PRO R9700,
     Ryzen AI (Radeon 8050S/8060S/890M/880M). It is a preview from AMD: if the GPU is not usable the
     installation falls back to the CPU version by itself. For AMD the folder path must be short
     (76 characters at most, e.g. C:\NAM).
   - No usable GPU: the CPU version is installed. The page shows a notice and lowers the default values
     (fewer epochs): a full model can take hours.

In the web page
   - Top right: the language (Italiano / English) and the LAN switch:
       LAN on  = the page can also be used from other PCs on the network (addresses shown below it);
       LAN off = only from the PC where NAM is installed (http://localhost:8765), like an app.
     If you turn it off from another PC you lose access from there: it can only be turned back on from the
     PC where NAM is installed.

Check that the installation files are not damaged (after copying them):
   "Installa NAM.exe" /verifica
See what it recognises and would install, without installing anything:
   "Installa NAM.exe" /rileva
Choose the version by hand (nvidia, amd or cpu), skipping the detection:
   "Installa NAM.exe" /VARIANT=cpu
Choose the language without being asked (en or it), for example for an unattended install:
   "Installa NAM.exe" /S /LANG=en

Reinstalling / updating: run "Installa NAM.exe" again in the same folder (also to change graphics card: the
detection is redone). Models (NAM Generati), WAV files (File WAV caricati), Backup, updated trainers and the
page settings (LAN) stay untouched.

Uninstalling: close NAM (Ferma NAM.bat) and delete the folder. Nothing else on the PC is modified
(except, if it was missing, Microsoft's Visual C++ runtime).

Firewall: the first time the server starts, Windows asks to allow Python on the private network. Accept to use
the page from another device on the LAN (e.g. from the Mac). The LAN switch in the page does not change the
Windows firewall rules: it limits access inside NAM itself.
