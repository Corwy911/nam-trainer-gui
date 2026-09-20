"""Which accelerator this installation was set up for (nam-web/hardware.json, written by the installer).

  variant "nvidia"  CUDA build of PyTorch, NVIDIA GPU
  variant "amd"     ROCm build of PyTorch (AMD's Windows preview), Radeon GPU
  variant "cpu"     CPU build of PyTorch: works everywhere, but training is much slower

No file (older installs, development copy) = "nvidia" if nvidia-smi exists, otherwise "cpu".
Only the standard library is used here: worker.py imports this module too.
"""

import json
import platform
import shutil
import subprocess
import sys
import time
from pathlib import Path
from typing import Optional

HARDWARE_FILE = Path(__file__).resolve().parent / "hardware.json"
VARIANTS = ("nvidia", "amd", "cpu")
_NO_WINDOW = subprocess.CREATE_NO_WINDOW if sys.platform == "win32" else 0


def _nvidia_smi() -> Optional[str]:
    found = shutil.which("nvidia-smi")
    if found:
        return found
    candidate = Path(r"C:\Program Files\NVIDIA Corporation\NVSMI\nvidia-smi.exe")
    return str(candidate) if candidate.exists() else None


def load() -> dict:
    try:
        data = json.loads(HARDWARE_FILE.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        data = {}
    if data.get("variant") not in VARIANTS:
        data = {**data, "variant": "nvidia" if _nvidia_smi() else "cpu", "assumed": True}
    return data


def variant() -> str:
    return load()["variant"]


def language() -> str:
    """The language chosen when installing ("it" | "en"); "" for a development copy. The page uses it as its default."""
    value = load().get("language")
    return value if value in ("it", "en") else ""


def cpu_name() -> str:
    return load().get("cpu_name") or platform.processor() or "CPU"


_gpu_cache = {"t": 0.0, "value": None}


def gpu_info() -> Optional[dict]:
    """What the header chip shows: live NVIDIA figures, or just the device name for AMD / CPU."""
    hw = load()
    kind = hw["variant"]
    if kind == "amd":
        return {"kind": "amd", "name": hw.get("gpu_name") or "AMD Radeon"}
    if kind == "cpu":
        return {"kind": "cpu", "name": cpu_name()}
    if time.time() - _gpu_cache["t"] < 2:
        return _gpu_cache["value"]
    value = None
    smi = _nvidia_smi()
    if smi:
        try:
            out = subprocess.run(
                [smi, "--query-gpu=name,utilization.gpu,memory.used,memory.total,temperature.gpu", "--format=csv,noheader,nounits"],
                capture_output=True, text=True, timeout=5, check=True, creationflags=_NO_WINDOW,
            ).stdout.strip().splitlines()[0]
            name, util, used, total, temp = [x.strip() for x in out.split(",")]
            value = {"kind": "nvidia", "name": name, "util": int(util), "mem_used": int(used), "mem_total": int(total), "temp": int(temp)}
        except (OSError, subprocess.SubprocessError, ValueError, IndexError):
            pass
    _gpu_cache.update(t=time.time(), value=value)
    return value
