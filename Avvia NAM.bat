@echo off
rem Avvia NAM Trainer in background e apre la GUI nel browser.
rem Con l'argomento "nobrowser" non apre il browser.
set "EXTRA="
if /i "%~1"=="nobrowser" set "EXTRA=-NoBrowser"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0nam-web\start.ps1" %EXTRA%
timeout /t 5 >nul
