@echo off
cd /d "%~dp0"
if not exist "node_modules\htmlparser2\package.json" (
  echo Run npm ci in this folder before starting.
  pause
  exit /b 1
)
node --env-file-if-exists=.env server.mjs
pause
