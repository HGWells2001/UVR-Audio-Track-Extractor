@echo off
setlocal
cd /d "%~dp0"
if not exist "dist\UVR_Audio_Track_Extractor.exe" (
    echo Il programma non e' ancora compilato.
    call "COMPILA_EXE.bat"
    if errorlevel 1 exit /b 1
)
start "" "%~dp0dist\UVR_Audio_Track_Extractor.exe"
