# NAM Trainer (NVIDIA / AMD / CPU) con GUI web

*[English version: README.md](README.md)*

Interfaccia web per addestrare modelli [Neural Amp Modeler](https://github.com/sdatkinson/neural-amp-modeler) su un PC
Windows (GPU NVIDIA o AMD, o solo CPU), usabile dal PC stesso o da altri dispositivi della rete (es. dal Mac).

## Installazione (per chi vuole solo usarlo)

1. Dalla pagina **Releases** del repository scarica **tutti** i file di `Installa NAM`: `Installa NAM.exe` e le parti
   `Installa NAM.dat.001`, `.002`, `.003` (GitHub non accetta file oltre 2 GiB, quindi il pacchetto è diviso).
2. Mettili **insieme, nella stessa cartella** (quella dove vuoi installare NAM, meglio corta e vuota, es. `C:\NAM`).
3. Doppio clic su `Installa NAM.exe`: sceglie la lingua, riconosce da solo la scheda video (NVIDIA / AMD / nessuna) e installa
   la versione giusta, **senza internet**. Windows può avvisare che l'editore è sconosciuto (l'exe non è firmato).
4. `Avvia NAM.bat` e apri la pagina indicata. Dettagli, requisiti e opzioni: [installer/README.it.md](installer/README.it.md).

Verifica dei download: `SHA256SUMS.txt` nella release; `"Installa NAM.exe" /verifica` controlla l'integrità di tutti i file.

## Struttura del repository (per chi vuole modificarlo)

- `nam-web\` — server FastAPI e pagina web · `trainers\` — i due trainer (copie non modificate, con le loro licenze)
- `installer\` — come si costruisce l'installatore (`python installer\build.py all`, poi `release`)
- `Avvia NAM.bat`, `Ferma NAM.bat` — avvio/arresto · `THIRD_PARTY.md` — licenze e crediti

Tutto è dentro questa cartella `NAM` (nell'uso quotidiano, dopo l'installazione):

- `NAM Generati\` — **i modelli**: una sottocartella per modello con `<nome>.nam`, `<nome>.png` (grafico), `<nome>.log` e `_lavoro\` (impostazioni del job)
- `File WAV caricati\` — **i WAV caricati** (input DI e output reamp), sottocartelle comprese
- `trainers\official\` — neural-amp-modeler (Steven Atkinson), copia non modificata del pacchetto `nam` + `source.json` (versione/origine)
- `trainers\reloaded\` — [NAM Trainer Reloaded](https://github.com/SlamminMofo/NAM-Trainer-Reloaded) (SlamminMofo, MIT), stessa struttura, più `catalog.json` (elenco architetture generato dal pacchetto)
- `Backup\` — copie delle versioni precedenti dei due trainer (ultime 5 per trainer), create dagli aggiornamenti
- `venv\` — Python 3.12 + PyTorch. Nella copia di sviluppo: 2.11 CUDA 12.8 (supporta RTX 50xx); con l'installatore dipende dall'hardware (vedi "Lingua, LAN, hardware"). Non viene mai modificata dagli aggiornamenti
- `nam-web\` — server FastAPI + pagina web (`app.py`, `worker.py`, `trainers.py`, `updater.py`, `selftest.py`, `static\index.html`; testi in `static\i18n.json`; `hardware.json` e `settings.json` scritti dall'installatore / dalla pagina)
- `Avvia NAM.bat` — avvia il server in background e apre la GUI nel browser
- `Ferma NAM.bat` — ferma completamente il server e qualsiasi training/aggiornamento in corso

## Uso

1. Doppio clic su `Avvia NAM.bat`.
2. Sul PC apri `http://localhost:8765`; da un altro dispositivo in LAN `http://<IP-del-PC>:8765` (l'IP viene stampato all'avvio se la LAN è attiva, ed è mostrato nella pagina).
3. Scegli il **trainer / modello** dalla tendina.
4. Scegli l'**input** dalla tendina (rispecchia `File WAV caricati`) o caricane uno nuovo; aggiungi gli **output**
   caricando WAV nuovi e/o scegliendoli tra quelli già caricati. Facoltativo: una sottocartella per i WAV nuovi.
5. Avvia. A fine training scarica il `.nam` dalla card, oppure ritrovalo in `NAM Generati\<nome>\`.

Opzioni del server: `--port 9000`.

## Lingua, LAN, hardware (barra in alto nella pagina)

- **Lingua**: tendina Italiano / English (ricordata dal browser; la prima volta usa la lingua scelta durante l'installazione,
  o quella del browser in una copia senza installatore). Tutti i testi
  stanno in `static\i18n.json` (server e pagina lo condividono): aggiungere una lingua = aggiungere una sezione lì.
  Anche i messaggi generati in background (log degli aggiornamenti, errori dei training) sono salvati senza lingua e
  tradotti quando li leggi, quindi cambiano insieme alla pagina.
- **LAN**: l'interruttore sceglie se la pagina è usabile solo dal PC dove NAM è installato (come un'app, con
  `http://localhost:8765`) oppure anche da altri PC della rete. Il server resta in ascolto su tutte le interfacce e
  **rifiuta (403) le richieste non locali** quando la LAN è disattivata: si cambia al volo, senza riavviare, e la scelta
  è in `nam-web\settings.json` (predefinito: attiva). Non tocca le regole del firewall di Windows. Se lo disattivi da un
  altro PC perdi l'accesso da lì (si riattiva solo dal PC di NAM o modificando `settings.json`).
- **Hardware** (`nam-web\hardware.json`, scritto dall'installatore): `nvidia` (PyTorch CUDA), `amd` (PyTorch ROCm,
  anteprima ufficiale AMD per Windows 11, Radeon RX 7000/9000 e simili) o `cpu`. In alto a destra la pagina mostra la
  scheda in uso; con `cpu` compare un avviso e i valori predefiniti di epoche/batch sono ridotti (training molto più lento).
  Senza il file (copia di sviluppo) vale `nvidia` se c'è `nvidia-smi`, altrimenti `cpu`.

## Le due cartelle sono lo "specchio" della pagina

- **`NAM Generati`**: la sezione "Modelli generati" mostra ciò che c'è nella cartella, a ogni aggiornamento della
  pagina. Ogni `.nam` presente, anche messo a mano e in qualsiasi sottocartella, compare scaricabile. Se ne cancelli
  uno da Esplora risorse sparisce dalla pagina. Dal web si possono eliminare solo le cartelle create dall'app
  (pulsante "Elimina"); i `.nam` messi a mano si gestiscono da Esplora risorse.
- **`File WAV caricati`**: le tendine di input/output elencano i WAV della cartella per sottocartella. I WAV caricati
  dal web finiscono qui; se carichi lo stesso file due volte non viene duplicato, se ha lo stesso nome ma contenuto
  diverso diventa `nome (2).wav`.
- **Riallena con un altro modello**: sulla card di un modello precompila input, output e metadati; scegli un altro
  trainer e avvia. Nasce una nuova cartella in `NAM Generati` (es. `Amp (A2 Full+Lite)`).

## Aggiornamenti dei trainer (card "Aggiornamenti dei trainer")

Due pulsanti: **Check neural-amp-modeler** e **Check NAM-Trainer-Reloaded**. Si segue l'ultima *release* (PyPI/GitHub
per l'ufficiale, release GitHub per Reloaded), non `main`.

1. **Check**: interroga il repository. Se sei aggiornato lo dice; altrimenti mostra versione, note di rilascio, file
   modificati e le **novità da controllare** (nuove righe che usano rete, comandi di sistema, codice dinamico, file di
   tipo non consentito). Non modifica nulla.
2. **Installa X** (solo dopo la tua conferma): riscarica e verifica (sha256 su PyPI), poi **prova il nuovo trainer con
   un mini-training su GPU** usando il worker vero (1-3 minuti). Solo se riesce fa il **backup** della versione attuale in
   `Backup\` e scambia le cartelle. Se qualcosa fallisce, la versione in uso non viene toccata (o viene rimessa a posto).
3. **Ripristina**: dal menu dei backup rimette una versione precedente (quella attuale viene prima salvata, quindi è
   reversibile).

Note: la `venv` (PyTorch) non viene toccata. Un aggiornamento è rifiutato se ci sono training in corso o in coda, e
durante l'aggiornamento non partono nuovi training. Se il server viene fermato a metà, all'avvio successivo la cartella
del trainer viene ripulita/rimessa a posto da sola. Il collaudo usa audio sintetico: dice che il trainer funziona con
questa GUI, non che la qualità dei modelli sia uguale (se noti un peggioramento, "Ripristina"). Le architetture di
Reloaded nel menu seguono la versione installata (`trainers\reloaded\catalog.json`).

Limite noto: il controllo delle "novità sospette" si basa su pattern: è un aiuto, non una garanzia di sicurezza.

## PC pulito

Nulla viene scritto fuori da questa cartella: i file temporanei, gli upload in corso, la cache di matplotlib e i
checkpoint dei training vanno in `nam-web\tmp` e `nam-web\cache` (i workers ricevono `TEMP`/`TMP`/`MPLCONFIGDIR` puntati lì;
`tmp` viene svuotata a ogni avvio; i checkpoint stanno in un percorso corto `tmp\train-<id>` perché Windows limita i
percorsi a 260 caratteri e il Reloaded ne crea di molto profondi). Lo stato dei job sta in `NAM Generati\<nome>\_lavoro\`,
non c'è nessun database: le cartelle *sono* i dati.

## Trainer disponibili

- **NAM ufficiale**: neural-amp-modeler (versione mostrata nel menu), architettura standard.
- **SlamminMofo Reloaded**: A2 (`.nam` slimmable Full+Lite), WaveNet standard/leggeri, WaveNet estesi, LSTM. Pannello
  "Parametri Reloaded": learning rate, decay, scheduler, loss MRSTFT, training a due fasi.

Ogni job gira in un processo separato; il worker mette `trainers\<trainer>` davanti a `sys.path`, così il `nam` di quel
trainer prende il posto di qualunque altro solo per quel job.

## Note

- I file di input riconosciuti dipendono dal trainer: un segnale di test personalizzato viene rifiutato (il fork
  riconosce anche alcune varianti "TTS input" in più).
- I training girano uno alla volta (coda). "Ferma ed esporta" interrompe e salva il miglior checkpoint.
- Nessuna autenticazione: con la LAN attiva chiunque sulla rete può usare il server (anche gli aggiornamenti). Disattiva l'interruttore LAN se non serve.
- Se il Mac non raggiunge il PC: regola firewall in ingresso per la porta 8765 (rete Privata).
- La cartella `NAM` si può spostare, ma la `venv` contiene percorsi assoluti (launcher come `pip.exe`): dopo uno
  spostamento vanno aggiornati (`python -m pip` funziona comunque).
- Il pacchetto pip `neural-amp-modeler` nella `venv` (0.13.0) resta come ripiego per il trainer ufficiale; la versione in uso è
  quella di `trainers\official`, mostrata nella pagina.
