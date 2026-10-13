@echo off
rem Double-click to build and run the game. Passes arguments through, e.g. "run -Editor".
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run.ps1" %*
