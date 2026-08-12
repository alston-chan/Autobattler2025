@echo off
REM ============================================================
REM  Equipment Designer — one-click launcher
REM  Double-click this file. It will:
REM    1. rebuild the catalog from Items.csv
REM    2. start the local server (auto-save enabled)
REM    3. open http://localhost:8642 in your browser
REM  Close this window (or Ctrl+C) to stop the server.
REM ============================================================

cd /d "%~dp0.."

echo.
echo  [1/2] Building catalog from Assets\Data\Items.csv ...
echo.
node Tools\build-data.js
if errorlevel 1 (
  echo.
  echo  ERROR: build failed. Is Node.js installed?  https://nodejs.org
  echo.
  pause
  exit /b 1
)

echo.
echo  [2/2] Starting server ...

REM Open the browser a moment later, so the server is listening first.
start /b cmd /c "timeout /t 2 /nobreak >nul & start "" http://localhost:8642"

node Tools\serve.js

echo.
echo  Server stopped.
pause
