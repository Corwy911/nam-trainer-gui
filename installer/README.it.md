# Installatore — costruzione e pubblicazione

*[English version: README.md](README.md)*

`Installa NAM.exe` + `Installa NAM.dat` (in una release GitHub: `Installa NAM.dat.001`, `.002`, `.003`) sono l'installatore:
**si porta dietro tutto** (Python, PyTorch in **tre versioni** — NVIDIA CUDA, AMD ROCm, solo CPU —, tutti i pacchetti, i due
trainer, il programma web e il runtime Visual C++). Si copiano nella stessa cartella del PC di destinazione e si lancia l'exe con
un doppio clic: **rileva l'hardware**, installa la versione giusta lì, **senza internet**.

## I file

`output\` contiene `Installa NAM.exe` (30 KB, il programma che si lancia) e `Installa NAM.dat` (5,3 GB, il pacchetto): **vanno
copiati insieme** nella stessa cartella (Windows non avvia un `.exe` da più di 4 GiB, quindi i dati non possono stare
dentro l'exe). Con `build.py release` nasce `output\release\`: exe, dati **divisi in parti sotto i 2 GiB** (`.dat.001`, `.002`, `.003`:
GitHub non accetta allegati più grandi), `SHA256SUMS.txt` e le guide `LEGGIMI.txt` / `README-EN.txt`. L'exe legge le parti
direttamente dalla stessa cartella come se fossero un file solo: chi scarica le mette **tutte** insieme, senza unirle.

## Cosa fa quando lo lanci su un altro PC

0. **Chiede la lingua** (1 = English, 2 = Italiano; il suggerimento è la lingua di Windows). Tutto il resto (installatore,
   `Installa.ps1`, `Avvia/Ferma NAM`) esce in quella lingua, che diventa anche la lingua con cui la pagina web si apre la prima
   volta (`nam-web\hardware.json`, `"language"`). `/LANG=en|it` (o `NAM_INSTALL_LANG`) salta la domanda; con `/S` vale la lingua di Windows.
1. **Rileva l'hardware** (schede video via WMI, dal vendor id: NVIDIA `10DE`, AMD `1002`) e sceglie la versione:
   - scheda **NVIDIA** → PyTorch 2.11 CUDA 12.8 (la versione collaudata);
   - altrimenti scheda **AMD Radeon** adatta (serie RX / PRO / AI PRO, Ryzen AI 8050S/8060S/890M/880M, solo Windows 11)
     → PyTorch 2.9.1 **ROCm 7.2.1**, l'anteprima ufficiale AMD per Windows (repo.radeon.com);
   - altrimenti → PyTorch 2.11 **CPU**.
   NVIDIA ha la precedenza (portatili con iGPU AMD + GPU NVIDIA). Mostra cosa ha trovato e permette di cambiare scelta
   (1 = NVIDIA, 2 = AMD, 3 = CPU). Opzioni: `/rileva` (mostra e basta), `/VARIANTE=nvidia|amd|cpu` (salta il rilevamento).
2. Mostra cartella di destinazione, spazio necessario **per quella versione** e chiede conferma. Se la cartella non è vuota
   o c'è già un'installazione, lo dice **prima** di copiare qualsiasi cosa.
3. Copia in `_setup\stage` **solo** i file utili a quel PC (le altre versioni di PyTorch restano nell'exe), verificando
   lo SHA-256 di ognuno. Con AMD copia anche la versione CPU, che serve da ripiego.
4. Lancia `Installa.ps1`: controlla Windows/hardware/driver, installa il runtime Visual C++ solo se manca, crea la venv
   con `pip --no-index` (prima PyTorch della variante, poi i pacchetti comuni), verifica gli import **e prova la GPU**
   (una piccola moltiplicazione di matrici), scrive `nam-web\hardware.json`, sposta il programma al suo posto e cancella
   `_setup`. **Con AMD, se la GPU non risulta utilizzabile, disinstalla ROCm e installa la versione CPU da sola.**
5. Se rilanciato in una cartella già installata, aggiorna programma e ambiente Python (rifacendo il rilevamento) e
   **lascia intatti** `NAM Generati`, `File WAV caricati`, `Backup`, i trainer (anche aggiornati dalla pagina web) e
   `nam-web\settings.json` (interruttore LAN).

Non si fa nulla fuori dalla cartella (né registro né PATH), tranne l'eventuale runtime Visual C++ di Microsoft.
Disinstallare = eliminare la cartella. `"Installa NAM.exe" /verifica` controlla solo l'integrità del file.

## Requisiti del PC di destinazione

- Windows 10/11 a 64 bit; per l'accelerazione AMD **Windows 11** e driver Adrenalin **26.2.2** o successivo.
- NVIDIA: RTX serie 20 o successive (anche serie 50), driver 570 o superiore.
- AMD (anteprima di AMD, elenco ufficiale): RX 9060 XT / 9070 / 9070 XT, RX 7700 / 7800 / 7900, Radeon PRO W7700 / W7800 /
  W7900, Radeon AI PRO R9700, Ryzen AI (gfx1150/1151). **Percorso di installazione al massimo 76 caratteri** (i file di
  ROCm hanno nomi lunghissimi e Windows limita i percorsi a 260): l'installatore si rifiuta con un messaggio chiaro.
- Spazio libero durante l'installazione (dipende dalla versione, lo mostra il programma): NVIDIA ~10,6 GB, AMD ~12,3 GB,
  CPU ~3,6 GB; a fine lavoro restano occupati ~6 / ~8 / ~2 GB (più l'exe, ~5,3 GB).
- L'exe non è firmato: SmartScreen può mostrare "Windows ha protetto il PC" → *Ulteriori informazioni → Esegui comunque*.

## Ricostruirlo

Serve Python 3.12 (lo stesso viene copiato nell'installatore) e circa 16 GB liberi per la cache. Dalla radice del repository:

```
python installer\build.py all        # la prima volta scarica ~5,5 GB di wheel in installer\cache
python installer\build.py release    # installer\output\release\ : i file per una release GitHub
```

Passi (si possono lanciare da soli: `lock`, `wheels`, `redist`, `stub`, `stage`, `pack`, `verify`):

- `lock`: `requirements.lock` = `pip freeze` della venv di sviluppo **senza torch/torchaudio** (pacchetti comuni); `requirements-nvidia.lock`
  (le versioni CUDA della venv), `requirements-cpu.lock` (stesse versioni `+cpu`), `requirements-amd.lock` (ROCm 7.2.1 fisso).
- `wheels`: scarica in `cache\wheelhouse` (comuni, PyPI), `cache\wheelhouse-nvidia` (indice PyTorch cu128),
  `cache\wheelhouse-cpu` (indice PyTorch cpu), `cache\wheelhouse-amd` (repo.radeon.com + il pacchetto `rocm` costruito dallo
  sdist). Le volte successive riusa la cache.
- `redist`: scarica `vc_redist.x64.exe` da Microsoft e ne verifica la firma digitale.
- `stub`: compila `src\Stub.cs` (estrazione + rilevamento hardware) con il compilatore C# incluso in Windows.
- `stage`: prepara `cache\stage` con `nam-web`, `trainers`, i `.bat` e il README dalla radice, il Python di base
  (`%LOCALAPPDATA%\Programs\Python\Python312`), le wheelhouse e `src\Installa.ps1`. Si ferma se trova percorsi del PC di origine.
- `pack` / `verify`: `pack.py` appende i file, l'indice (SHA-256 e spazio necessario per variante) e il trailer all'exe;
  poi rilegge e verifica tutto.

Dopo aver cambiato il programma in `NAM\` (nam-web, trainers, .bat) bastano `stage`, `pack`, `verify`.
`cache\` (~5,5 GB) si può eliminare quando vuoi: `build.py all` la ricrea scaricando di nuovo. `build.py release` spezza il `.dat` in parti
e scrive `SHA256SUMS.txt`. Le lock sono nel repository: `all` le rigenera solo se mancano.

## Pubblicare una release (GitHub CLI)

```
gh auth login                                                # una volta
python installer\build.py all
python installer\build.py release
gh release create v1.0.0 installer\output\release\* --title "NAM Trainer GUI 1.0" --notes-file installer\RELEASE_NOTES.md
```

Ogni file della release deve restare sotto i 2 GiB (`release` lo controlla); il caricamento di ~5,4 GB richiede tempo.

## Cartelle

- `src\Stub.cs`, `src\app.manifest` — programma di estrazione (C# 5 / .NET Framework 4, presente su ogni Windows 10/11).
- `src\Installa.ps1`, `src\LEGGIMI.txt` — script di installazione e guida per l'utente finale (finiscono nel pacchetto).
- `pack.py` — formato del contenitore: `[stub][dati][indice][trailer 32 byte]` (vedi la docstring).
- `build.py`, `requirements*.lock` — costruzione e versioni dei pacchetti.
- `cache\` — download riutilizzabili (wheelhouse, runtime VC++, stub compilato). `output\` — il risultato.

## Limiti noti

- Collaudato su questo PC (NVIDIA): installazione in cartelle nuove, con spazi/accenti, aggiornamento sopra un'installazione
  esistente, file danneggiato/troncato; il ramo **CPU** anche con un training di prova reale. **L'accelerazione AMD non è
  stata provata su una vera scheda AMD** (qui non ce n'è una): sono verificati il pacchetto, l'installazione offline, gli
  import e i due trainer con lo stack PyTorch 2.9.1/ROCm su CPU, e il ripiego automatico sulla CPU. La compatibilità con la
  GPU dipende da AMD (driver, scheda nell'elenco ufficiale).
- Non è stato possibile provarlo su un PC "vergine": in particolare il ramo che installa il runtime Visual C++ (serve UAC)
  non è stato eseguito perché qui è già presente.
- Il driver della scheda video non è incluso: l'installatore controlla e avvisa.
- Percorsi lunghi: Windows limita a 260 caratteri; l'installatore avvisa (oltre 90 caratteri) o rifiuta per AMD (oltre 76).
- Installatore, `Avvia NAM.bat`/`Ferma NAM.bat` e pagina web sono in italiano e inglese. Restano in italiano solo il log del
  server (`server.out.log`) e i messaggi tecnici di pip/Python; la documentazione lunga è in italiano (`README.md`) e la guida
  dell'installatore in due lingue (`LEGGIMI.txt`, `README-EN.txt`).
