@echo off
rem Double-click to start the BLADE demo: checks Ollama, builds, starts the server, warms the models, opens the app.
rem Closing the BLADE app stops the server. Add -NoBuild to skip the build step.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\start-demo.ps1" %*
