@echo off
rem Slide scene classifier. Runs tools\deck-classify.py
rem Usage: deck-classify.cmd <input folder> [output folder]
rem   deck-classify.cmd src\4-wgo
rem   deck-classify.cmd src            (every project under src)
setlocal
cd /d "%~dp0"
if "%~1"=="" (
  echo Usage: deck-classify.cmd ^<input folder^> [output folder]
  echo   deck-classify.cmd src\4-wgo
  echo   deck-classify.cmd src
  exit /b 1
)
set PYTHONUTF8=1
where python >nul 2>nul
if %errorlevel%==0 (
  python "%~dp0tools\deck-classify.py" %*
) else (
  py "%~dp0tools\deck-classify.py" %*
)
endlocal
