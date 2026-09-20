# Installa NAM Trainer nella cartella di destinazione. Lanciato da "Installa NAM.exe" (non a mano).
#
# Il pacchetto e gia stato estratto e verificato in <Target>\_setup\stage con questa struttura:
#   app\      programma (nam-web, trainers, Avvia NAM.bat, Ferma NAM.bat, README.md)
#   python\   Python 3.12 di base (copia portatile, nessuna registrazione nel sistema)
#   setup\    questo script, requirements.lock (pacchetti comuni), requirements-<variante>.lock (PyTorch),
#             wheelhouse\ (pacchetti comuni, offline), wheelhouse-<variante>\ (solo quelle che servono),
#             vc_redist.x64.exe (runtime Visual C++ richiesto da PyTorch)
#
# La variante (nvidia | amd | cpu) l'ha scelta il programma di installazione dopo aver rilevato l'hardware:
#   nvidia  PyTorch CUDA           amd  PyTorch ROCm (anteprima ufficiale AMD per Windows)   cpu  PyTorch solo CPU
# Se con "amd" la GPU non risulta utilizzabile, si ripiega automaticamente sulla CPU.
#
# Cosa fa: controlli (Windows, cartella, hardware), runtime C++ se manca, crea la venv dalla
# wheelhouse SENZA rete, verifica gli import e la GPU, sposta i file al loro posto. Non tocca mai
# "NAM Generati", "File WAV caricati", "Backup" ne i trainer gia presenti (aggiornati dalla pagina).

param(
    [Parameter(Mandatory = $true)][string]$Target,
    [ValidateSet('nvidia', 'amd', 'cpu')][string]$Variant = 'nvidia',
    [string]$GpuName = '',
    [ValidateSet('it', 'en')][string]$Lang = 'en',   # language of every message (chosen in the installer program)
    [switch]$Silent,
    [switch]$Confirmed   # the installer program already asked "folder not empty / update existing?" before copying
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
try { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false) } catch {}

$SetupDir = Join-Path $Target '_setup'
$Stage = Join-Path $SetupDir 'stage'
$Payload = Join-Path $Stage 'setup'
$Log = Join-Path $SetupDir 'install.log'
$MinDriver = 570   # CUDA 12.8 (PyTorch incluso); le schede RTX serie 50 vogliono un driver recente

function T([string]$it, [string]$en) { if ($Lang -eq 'it') { return $it } else { return $en } }   # Italian / English text
function Say($text, $color = $null) { if ($color) { Write-Host $text -ForegroundColor $color } else { Write-Host $text } }
function Step($text) { Say ''; Say "== $text" 'Cyan' }
function Warn($text) { Say "   $(T 'ATTENZIONE' 'WARNING'): $text" 'Yellow' }
function Fail($text) { throw $text }
function Ask($question, [bool]$default = $true) {
    if ($Silent) { return $default }
    # Without a keyboard (input redirected / closed) nobody can answer: say no instead of failing inside Read-Host.
    try { if ([Console]::IsInputRedirected) { return $false } } catch { return $false }
    $hint = if ($Lang -eq 'it') { if ($default) { '[S/n]' } else { '[s/N]' } } else { if ($default) { '[Y/n]' } else { '[y/N]' } }
    try { $answer = Read-Host "$question $hint" } catch { return $false }
    if ($null -eq $answer) { return $false }
    $answer = $answer.Trim().ToLower()
    if ($answer -eq '') { return $default }
    return $answer -in @('s', 'si', 'sì', 'y', 'yes')
}
function Invoke-Native([string]$exe, [string[]]$arguments) {
    # Runs a program, shows its output on the console (Out-Host: it must not end up in the return value),
    # and returns only the exit code. stderr is not turned into PowerShell errors.
    $previous = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    try {
        # 2>&1 + "$_": stderr lines become plain text (no red NativeCommandError noise) and reach the log too.
        & $exe @arguments 2>&1 | ForEach-Object { "$_" } | Out-Host
        return $LASTEXITCODE
    } finally { $ErrorActionPreference = $previous }
}

function Get-NativeLines([string]$exe, [string[]]$arguments) {
    # Output (stdout + stderr) of a program as plain text lines. Libraries such as PyTorch print harmless warnings on
    # stderr; under $ErrorActionPreference='Stop' those would otherwise be raised as errors.
    $previous = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    try { return @(& $exe @arguments 2>&1 | ForEach-Object { "$_" }) } finally { $ErrorActionPreference = $previous }
}

try { Start-Transcript -Path $Log -Force | Out-Null } catch {}
$exitCode = 0
try {
    Say (T '=== Preparazione dell''ambiente ===' '=== Preparing the environment ===') 'Green'

    # ---------------------------------------------------------------- 1. sistema
    Step (T 'Controllo del sistema' 'Checking the system')
    if (-not [Environment]::Is64BitOperatingSystem) { Fail (T 'Serve Windows a 64 bit.' '64-bit Windows is required.') }
    if ([Environment]::OSVersion.Version.Major -lt 10) { Fail (T 'Serve Windows 10 o successivo.' 'Windows 10 or later is required.') }
    Say (T "   Windows $([Environment]::OSVersion.Version) a 64 bit: ok" "   Windows $([Environment]::OSVersion.Version) 64-bit: ok")
    if ($Target.Length -gt 90) {
        Warn (T "il percorso di installazione e lungo ($($Target.Length) caratteri). Windows limita i percorsi a 260 caratteri e alcuni pacchetti Python hanno file con nomi lunghi: e consigliata una cartella piu corta (es. C:\NAM)." "the installation path is long ($($Target.Length) characters). Windows limits paths to 260 characters and some Python packages have long file names: a shorter folder is recommended (e.g. C:\NAM).")
        if (-not (Ask (T 'Continuare comunque?' 'Continue anyway?') $false)) { Fail (T 'Installazione annullata.' 'Installation cancelled.') }
    }

    $existing = (Test-Path -LiteralPath (Join-Path $Target 'nam-web\app.py')) -or (Test-Path -LiteralPath (Join-Path $Target 'venv\Scripts\python.exe'))
    if ($existing) {
        Say (T '   Trovata un''installazione esistente in questa cartella.' '   An existing installation was found in this folder.') 'Yellow'
        Say (T '   Verranno aggiornati il programma e l''ambiente Python. Restano INTATTI: NAM Generati, File WAV caricati, Backup, i trainer (anche se aggiornati dalla pagina web) e le impostazioni della pagina.' '   The program and the Python environment will be updated. These stay UNTOUCHED: NAM Generati (models), File WAV caricati (WAV files), Backup, the trainers (even if updated from the web page) and the page settings.')
        if (-not $Confirmed -and -not (Ask (T 'Aggiornare l''installazione esistente?' 'Update the existing installation?') $true)) { Fail (T 'Installazione annullata.' 'Installation cancelled.') }
        $stop = Join-Path $Target 'nam-web\stop.ps1'
        if (Test-Path -LiteralPath $stop) {
            Step (T 'Fermo il server in esecuzione' 'Stopping the running server')
            & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $stop
        }
        $stillRunning = @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -and $_.CommandLine -like "*$Target*" -and $_.Name -like 'python*' })
        if ($stillRunning.Count -gt 0) { Fail (T 'Ci sono ancora processi Python di NAM in esecuzione: chiudili (Ferma NAM.bat) e rilancia.' 'NAM Python processes are still running: close them (Ferma NAM.bat) and run the installer again.') }
    } else {
        $others = @(Get-ChildItem -LiteralPath $Target -Force | Where-Object { $_.Name -ne '_setup' -and $_.Extension -ne '.exe' -and $_.Name -notlike 'Installa NAM*' -and $_.Name -notlike 'SHA256SUMS*' })
        if ($others.Count -gt 0 -and -not $Confirmed) {
            Warn (T 'la cartella non e vuota. Contiene:' 'the folder is not empty. It contains:')
            $others | Select-Object -First 8 | ForEach-Object { Say "      $($_.Name)" }
            if (-not (Ask (T 'Installare NAM qui comunque (i file esistenti non vengono toccati)?' 'Install NAM here anyway (existing files are not touched)?') $false)) { Fail (T 'Installazione annullata: scegli una cartella vuota.' 'Installation cancelled: choose an empty folder.') }
        }
    }

    # ---------------------------------------------------------------- 2. hardware
    $variantLabel = @{ nvidia = 'NVIDIA (CUDA)'; amd = 'AMD (ROCm)'; cpu = (T 'solo CPU' 'CPU only') }
    $cpuName = ''
    try { $cpuName = (Get-CimInstance Win32_Processor -ErrorAction Stop | Select-Object -First 1).Name.Trim() } catch { $cpuName = $env:PROCESSOR_IDENTIFIER }
    Step (T "Hardware: versione $($variantLabel[$Variant])" "Hardware: $($variantLabel[$Variant]) version")
    if ($cpuName) { Say (T "   Processore: $cpuName" "   Processor: $cpuName") }
    $hasGpu = $false
    if ($Variant -eq 'nvidia') {
    $smi = (Get-Command nvidia-smi -ErrorAction SilentlyContinue)
    $smiPath = if ($smi) { $smi.Source } elseif (Test-Path "$env:ProgramFiles\NVIDIA Corporation\NVSMI\nvidia-smi.exe") { "$env:ProgramFiles\NVIDIA Corporation\NVSMI\nvidia-smi.exe" } else { $null }
    if (-not $smiPath) {
        Warn (T 'nvidia-smi non trovato: driver NVIDIA non installato o GPU assente. Puoi installare comunque, ma il training richiede una GPU NVIDIA (RTX 20 o successive) con driver aggiornato.' 'nvidia-smi not found: NVIDIA driver not installed or no GPU. You can install anyway, but training needs an NVIDIA GPU (RTX 20 or newer) with an up-to-date driver.')
    } else {
        $info = & $smiPath --query-gpu=name,driver_version,compute_cap --format=csv,noheader 2>$null
        if (-not $info) { $info = & $smiPath --query-gpu=name,driver_version --format=csv,noheader 2>$null }
        if ($info) {
            $first = ($info | Select-Object -First 1).Split(',') | ForEach-Object { $_.Trim() }
            $hasGpu = $true
            Say "   $($first[0]) | driver $($first[1])$(if ($first.Count -gt 2) { " | $(T 'capacita di calcolo' 'compute capability') $($first[2])" })"
            $major = 0
            if ([int]::TryParse(($first[1] -split '\.')[0], [ref]$major) -and $major -lt $MinDriver) {
                Warn (T "driver NVIDIA $($first[1]): per CUDA 12.8 serve la versione $MinDriver o successiva. Aggiornalo da nvidia.com o GeForce Experience." "NVIDIA driver $($first[1]): CUDA 12.8 needs version $MinDriver or later. Update it from nvidia.com or GeForce Experience.")
            }
            if ($first.Count -gt 2) {
                $cc = 0.0
                if ([double]::TryParse($first[2], [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$cc) -and $cc -lt 7.5) {
                    Warn (T "questa GPU (capacita $cc) e troppo vecchia per il PyTorch incluso (serve RTX 20 / capacita 7.5 o superiore)." "this GPU (compute capability $cc) is too old for the bundled PyTorch (RTX 20 / capability 7.5 or newer is required).")
                }
            }
        }
    }
    } elseif ($Variant -eq 'amd') {
        $hasGpu = $true
        Say "   $(T 'Scheda video' 'Graphics card'): $(if ($GpuName) { $GpuName } else { 'AMD Radeon' })"
        Say (T '   Accelerazione AMD: PyTorch ROCm 7.2.1 (anteprima ufficiale AMD per Windows). Servono Windows 11 e il driver Adrenalin 26.2.2 o successivo.' '   AMD acceleration: PyTorch ROCm 7.2.1 (AMD''s official preview for Windows). It needs Windows 11 and the Adrenalin 26.2.2 driver or later.')
        Say (T '   Alla fine verifico che la GPU funzioni davvero: se non e utilizzabile, installo la versione CPU (che e inclusa nel pacchetto).' '   At the end I check that the GPU really works: if it is not usable, the CPU version (included in the package) is installed.')
        if ([Environment]::OSVersion.Version.Build -lt 22000) { Warn (T 'Windows 10: l''accelerazione AMD e ufficialmente prevista solo per Windows 11.' 'Windows 10: AMD acceleration is officially supported only on Windows 11.') }
    } else {
        Say (T '   Nessuna GPU NVIDIA o AMD utilizzabile rilevata: il training girera sul processore.' '   No usable NVIDIA or AMD GPU detected: training will run on the processor.')
        Say (T '   Funziona, ma e molto piu lento (i valori predefiniti nella pagina vengono ridotti). Un modello completo puo richiedere ore.' '   It works, but it is much slower (the defaults in the page are reduced). A full model can take hours.')
    }

    # ---------------------------------------------------------------- 3. runtime Visual C++
    Step (T 'Runtime Visual C++ (richiesto da PyTorch)' 'Visual C++ runtime (required by PyTorch)')
    $vc = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64' -ErrorAction SilentlyContinue
    $vcOk = $vc -and $vc.Installed -eq 1 -and ($vc.Major -gt 14 -or ($vc.Major -eq 14 -and $vc.Minor -ge 38))
    if ($vcOk) {
        Say (T "   gia installato (versione $($vc.Version)): ok" "   already installed (version $($vc.Version)): ok")
    } else {
        $redist = Join-Path $Payload 'vc_redist.x64.exe'
        Say (T '   Manca (o e troppo vecchio): installo quello incluso. Windows chiedera il permesso di amministratore.' '   Missing (or too old): installing the bundled one. Windows will ask for administrator permission.')
        try {
            $p = Start-Process -FilePath $redist -ArgumentList '/install', '/quiet', '/norestart' -Verb RunAs -Wait -PassThru
            if ($p.ExitCode -in 0, 1638) { Say (T '   installato: ok' '   installed: ok') }
            elseif ($p.ExitCode -eq 3010) { Warn (T 'installato, ma Windows chiede un riavvio del PC prima di usare la GPU.' 'installed, but Windows asks for a restart before the GPU can be used.') }
            else { Warn (T "l'installazione del runtime e terminata con codice $($p.ExitCode). Se PyTorch non parte, installa 'Microsoft Visual C++ Redistributable 2015-2022 (x64)'." "the runtime installation ended with code $($p.ExitCode). If PyTorch does not start, install 'Microsoft Visual C++ Redistributable 2015-2022 (x64)'.") }
        } catch {
            Warn (T "non sono riuscito ad avviare l'installazione del runtime ($($_.Exception.Message)). Se PyTorch non parte, installa 'Microsoft Visual C++ Redistributable 2015-2022 (x64)'." "I could not start the runtime installation ($($_.Exception.Message)). If PyTorch does not start, install 'Microsoft Visual C++ Redistributable 2015-2022 (x64)'.")
        }
    }

    # ---------------------------------------------------------------- 4. Python + ambiente
    Step (T 'Python e pacchetti (installazione offline)' 'Python and packages (offline installation)')
    $env:PYTHONHOME = $null; $env:PYTHONPATH = $null; $env:PYTHONUSERBASE = $null
    $pythonDir = Join-Path $Target 'python'
    $venvDir = Join-Path $Target 'venv'
    if (Test-Path -LiteralPath $venvDir) { Say (T '   Rimuovo la vecchia venv...' '   Removing the old venv...'); Remove-Item -LiteralPath $venvDir -Recurse -Force }
    if (Test-Path -LiteralPath $pythonDir) { Say (T '   Rimuovo il vecchio Python...' '   Removing the old Python...'); Remove-Item -LiteralPath $pythonDir -Recurse -Force }
    Move-Item -LiteralPath (Join-Path $Stage 'python') -Destination $pythonDir
    $basePython = Join-Path $pythonDir 'python.exe'
    Say "   $(T 'Python di base' 'Base Python'): $(& $basePython --version)"

    Say (T '   Creo l''ambiente virtuale...' '   Creating the virtual environment...')
    if ((Invoke-Native $basePython @('-m', 'venv', $venvDir)) -ne 0) { Fail (T 'Creazione della venv fallita.' 'Creating the venv failed.') }
    $venvPython = Join-Path $venvDir 'Scripts\python.exe'

    $wheelhouse = Join-Path $Payload 'wheelhouse'   # pacchetti comuni a tutte le varianti

    function Install-Torch([string]$name) {
        # PyTorch (e, per AMD, lo stack ROCm) della variante $name, prima di tutto il resto: i pacchetti
        # comuni dipendono da "torch" e trovano gia quello giusto.
        $dir = Join-Path $Payload "wheelhouse-$name"
        $n = @(Get-ChildItem -LiteralPath $dir -Filter *.whl).Count
        Say (T "   Installo PyTorch versione $($variantLabel[$name]) ($n pacchetti)..." "   Installing PyTorch, $($variantLabel[$name]) version ($n packages)...")
        $torchArgs = @('-m', 'pip', 'install', '--no-index', '--find-links', $dir, '--find-links', $wheelhouse, '--disable-pip-version-check',
                       '--no-warn-script-location', '--no-input', '-r', (Join-Path $Payload "requirements-$name.lock"))
        return (Invoke-Native $venvPython $torchArgs)
    }
    $requested = $Variant

    # Import di tutto il necessario + prova della GPU (una piccola moltiplicazione di matrici, per scovare driver
    # o librerie che si importano ma poi non funzionano). Stampa una sola riga: IMPORT_OK|versione|cuda/nocuda/cudaerr|nome
    $check = @'
import sys, torch, torchaudio, pytorch_lightning, fastapi, uvicorn, soundfile, scipy, matplotlib, librosa, multipart, packaging, nam
state, gpu = 'nocuda', '-'
if torch.cuda.is_available():
    try:
        gpu = torch.cuda.get_device_name(0)
        x = torch.randn(256, 256, device='cuda')
        float((x @ x).sum().item())
        torch.cuda.synchronize()
        state = 'cuda'
    except Exception as e:
        state, gpu = 'cudaerr', type(e).__name__ + ': ' + str(e)[:120].replace('|', '/').replace('\n', ' ')
print('IMPORT_OK', torch.__version__, state, gpu, sep='|')
'@
    function Test-Environment {
        $out = Get-NativeLines $venvPython @('-c', $check)
        $line = $out | Where-Object { $_ -like 'IMPORT_OK|*' } | Select-Object -First 1
        return @{ Line = $line; Output = $out }
    }

    if ((Install-Torch $Variant) -ne 0) {
        if ($Variant -ne 'amd') { Fail (T 'Installazione di PyTorch fallita (vedi sopra).' 'PyTorch installation failed (see above).') }
        Warn (T 'l''installazione dello stack AMD non e riuscita (vedi sopra): passo alla versione CPU.' 'the AMD stack installation failed (see above): switching to the CPU version.')
        $null = Invoke-Native $venvPython @('-m', 'pip', 'uninstall', '-y', 'torch', 'torchaudio', 'rocm', 'rocm-sdk-core', 'rocm-sdk-devel', 'rocm-sdk-libraries-custom')
        $Variant = 'cpu'; $requested = 'amd'
        if ((Install-Torch 'cpu') -ne 0) { Fail (T 'Installazione di PyTorch (CPU) fallita (vedi sopra).' 'PyTorch (CPU) installation failed (see above).') }
    }

    $count = @(Get-ChildItem -LiteralPath $wheelhouse -Filter *.whl).Count
    Say (T "   Installo gli altri $count pacchetti dalla wheelhouse locale (nessun download, ci vuole qualche minuto)..." "   Installing the other $count packages from the local wheelhouse (no download, this takes a few minutes)...")
    $pipArgs = @('-m', 'pip', 'install', '--no-index', '--find-links', $wheelhouse, '--disable-pip-version-check',
                 '--no-warn-script-location', '--no-input', '-r', (Join-Path $Payload 'requirements.lock'))
    if ((Invoke-Native $venvPython $pipArgs) -ne 0) { Fail (T 'Installazione dei pacchetti Python fallita (vedi sopra).' 'Installing the Python packages failed (see above).') }

    Say (T '   Verifico che tutto si importi e provo la GPU...' '   Checking that everything imports and trying the GPU...')
    $result = Test-Environment
    $parts = if ($result.Line) { $result.Line.Split('|') } else { $null }
    if ($Variant -eq 'amd' -and (-not $parts -or $parts[2] -ne 'cuda')) {
        # AMD non utilizzabile (scheda non supportata dal PyTorch ROCm, driver troppo vecchio, ...): CPU al suo posto.
        if ($parts) { Warn (T "PyTorch ROCm non puo usare la GPU AMD ($($parts[2]): $($parts[3]))." "PyTorch ROCm cannot use the AMD GPU ($($parts[2]): $($parts[3])).") }
        else { $result.Output | Select-Object -Last 6 | ForEach-Object { Say "      $_" }; Warn (T 'PyTorch ROCm non parte su questo PC.' 'PyTorch ROCm does not start on this PC.') }
        Warn (T 'Passo alla versione CPU: il training funzionera sul processore (piu lento). Con un driver AMD piu recente puoi rilanciare l''installazione.' 'Switching to the CPU version: training will run on the processor (slower). With a newer AMD driver you can run the installer again.')
        $null = Invoke-Native $venvPython @('-m', 'pip', 'uninstall', '-y', 'torch', 'torchaudio', 'rocm', 'rocm-sdk-core', 'rocm-sdk-devel', 'rocm-sdk-libraries-custom')
        $Variant = 'cpu'; $requested = 'amd'
        if ((Install-Torch 'cpu') -ne 0) { Fail (T 'Installazione di PyTorch (CPU) fallita (vedi sopra).' 'PyTorch (CPU) installation failed (see above).') }
        $result = Test-Environment
        $parts = if ($result.Line) { $result.Line.Split('|') } else { $null }
    }
    if (-not $parts) {
        $result.Output | Select-Object -Last 8 | ForEach-Object { Say "      $_" }
        Fail (T 'Uno o piu pacchetti non si importano. Se PyTorch segnala una DLL mancante, installa "Microsoft Visual C++ Redistributable 2015-2022 (x64)" e rilancia.' 'One or more packages cannot be imported. If PyTorch reports a missing DLL, install "Microsoft Visual C++ Redistributable 2015-2022 (x64)" and run the installer again.')
    }
    Say "   PyTorch $($parts[1]): ok"
    if ($parts[2] -eq 'cuda') { Say (T "   GPU utilizzabile da PyTorch: $($parts[3])" "   GPU usable by PyTorch: $($parts[3])") 'Green' }
    elseif ($Variant -eq 'cpu') { Say (T '   PyTorch usera il processore (nessuna GPU utilizzabile).' '   PyTorch will use the processor (no usable GPU).') }
    elseif ($hasGpu) { Warn (T 'PyTorch non vede la GPU (driver vecchio o riavvio necessario?). Il programma si avvia, ma il training non partira finche CUDA non e disponibile.' 'PyTorch does not see the GPU (old driver or restart needed?). The program starts, but training will not run until CUDA is available.') }
    $gpuFinal = if ($Variant -eq 'cpu') { '' } elseif ($parts[2] -eq 'cuda') { $parts[3] } else { $GpuName }

    # ---------------------------------------------------------------- 5. programma
    Step (T 'Copio il programma' 'Copying the program')
    $app = Join-Path $Stage 'app'
    $webDir = Join-Path $Target 'nam-web'
    # Page settings (LAN switch) survive an update.
    $savedSettings = $null
    $settingsFile = Join-Path $webDir 'settings.json'
    if (Test-Path -LiteralPath $settingsFile) { $savedSettings = [IO.File]::ReadAllBytes($settingsFile) }
    if (Test-Path -LiteralPath $webDir) { Remove-Item -LiteralPath $webDir -Recurse -Force }
    Move-Item -LiteralPath (Join-Path $app 'nam-web') -Destination $webDir
    if ($savedSettings) { [IO.File]::WriteAllBytes($settingsFile, $savedSettings) }
    # Quale acceleratore usa questa installazione (letto dal programma: variante, nome delle schede, ...).
    $hardware = [ordered]@{ variant = $Variant; gpu_name = $gpuFinal; cpu_name = $cpuName; requested = $requested; language = $Lang;
                            installed = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss') }
    [IO.File]::WriteAllText((Join-Path $webDir 'hardware.json'), ($hardware | ConvertTo-Json), (New-Object System.Text.UTF8Encoding($false)))
    $trainersDir = Join-Path $Target 'trainers'
    if (-not (Test-Path -LiteralPath $trainersDir)) {
        Move-Item -LiteralPath (Join-Path $app 'trainers') -Destination $trainersDir
    } else {
        foreach ($sub in Get-ChildItem -LiteralPath (Join-Path $app 'trainers') -Directory) {
            $dest = Join-Path $trainersDir $sub.Name
            if (-not (Test-Path -LiteralPath $dest)) { Move-Item -LiteralPath $sub.FullName -Destination $dest; Say (T "   aggiunto il trainer $($sub.Name)" "   added the trainer $($sub.Name)") }
            else { Say (T "   trainer $($sub.Name): mantengo quello installato" "   trainer $($sub.Name): keeping the installed one") }
        }
    }
    foreach ($file in 'Avvia NAM.bat', 'Ferma NAM.bat', 'README.md', 'README.it.md') {
        Copy-Item -LiteralPath (Join-Path $app $file) -Destination (Join-Path $Target $file) -Force
    }
    foreach ($folder in 'NAM Generati', 'File WAV caricati') {
        New-Item -ItemType Directory -Force -Path (Join-Path $Target $folder) | Out-Null
    }
    $versionCode = "import sys; sys.path.insert(0, sys.argv[1]); import trainers; print('VERSIONI|' + trainers.trainer_version('official') + '|' + trainers.trainer_version('reloaded'))"
    $versions = (Get-NativeLines $venvPython @('-c', $versionCode, $webDir)) | Where-Object { $_ -like 'VERSIONI|*' } | Select-Object -First 1
    if (-not $versions) { Fail (T 'I trainer non risultano installati correttamente (trainers.py non si importa).' 'The trainers do not seem to be installed correctly (trainers.py cannot be imported).') }
    Say (T "   Trainer: ufficiale $(($versions -split '\|')[1]), Reloaded $(($versions -split '\|')[2])" "   Trainers: official $(($versions -split '\|')[1]), Reloaded $(($versions -split '\|')[2])")

    Say ''
    Say '=====================================================' 'Green'
    Say (T '  Installazione completata.' '  Installation completed.') 'Green'
    Say '=====================================================' 'Green'
    Say ''
    Say (T "  Per avviare NAM:  doppio clic su  $Target\Avvia NAM.bat" "  To start NAM:  double-click  $Target\Avvia NAM.bat")
    Say (T "  Versione installata: $($variantLabel[$Variant])$(if ($requested -ne $Variant) { ' (la GPU AMD non era utilizzabile)' })" "  Version installed: $($variantLabel[$Variant])$(if ($requested -ne $Variant) { ' (the AMD GPU was not usable)' })")
    Say (T '  Poi apri la pagina indicata (http://localhost:8765 o, da un altro dispositivo, l''indirizzo IP del PC).' '  Then open the page shown (http://localhost:8765 or, from another device, the PC''s IP address).')
    Say (T '  Nella pagina l''interruttore LAN sceglie se usarla solo da questo PC o anche da altri PC della rete;' '  In the page, the LAN switch chooses whether it can be used only from this PC or also from other PCs on the network;')
    Say (T '  la prima volta Windows chiedera di consentire l''accesso di rete a Python: accetta per usarlo dalla LAN.' '  the first time, Windows will ask to allow Python on the network: accept it to use the page over the LAN.')
    Say (T '  Per fermarlo: Ferma NAM.bat.  Per disinstallare: elimina la cartella.' '  To stop it: Ferma NAM.bat.  To uninstall: delete the folder.')
} catch {
    $exitCode = 1
    Say ''
    Say "$(T 'ERRORE' 'ERROR'): $($_.Exception.Message)" 'Red'
} finally {
    try { Stop-Transcript | Out-Null } catch {}
}
if ($exitCode -eq 0) {
    try { Copy-Item -LiteralPath $Log -Destination (Join-Path $Target 'nam-web\installazione.log') -Force } catch {}
    if (-not $Silent -and (Ask (T 'Vuoi avviare NAM adesso?' 'Start NAM now?') $true)) {
        Start-Process -FilePath (Join-Path $Target 'Avvia NAM.bat') -WorkingDirectory $Target
    }
}
exit $exitCode
