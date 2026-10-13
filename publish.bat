@echo off
rem Gera o portatil e o instalador. A logica fica em dev.ps1 (comando "publish").
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0dev.ps1" publish
if errorlevel 1 (
  echo.
  echo FALHOU. Veja as mensagens acima.
)
pause
