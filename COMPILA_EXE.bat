@echo off
setlocal EnableExtensions
cd /d "%~dp0"
title UVR Audio Track Extractor - Compilazione OFFLINE
echo.
echo ======================================================
echo  UVR Audio Track Extractor - Build Windows offline
echo ======================================================
echo.
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo ERRORE: compilatore .NET Framework non trovato.
    echo Abilita .NET Framework 4.x in Windows.
    pause
    exit /b 1
)
if not exist "dist" mkdir "dist"
if not exist "dist\bin" mkdir "dist\bin"
echo Compilazione dell'applicazione .exe...
"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu /out:"dist\UVR_Audio_Track_Extractor.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll "UVR_Audio_Track_Extractor.cs" "RecomposeForm.cs"
if errorlevel 1 (
    echo.
    echo ERRORE: compilazione fallita. Copia l'errore e invialo.
    pause
    exit /b 1
)
echo.
echo Eseguibile creato: dist\UVR_Audio_Track_Extractor.exe
echo.
echo Ricerca di FFmpeg e FFprobe gia installati...
if not exist "dist\bin\ffmpeg.exe" (
    for /f "delims=" %%I in ('where ffmpeg.exe 2^>nul') do (
        if not exist "dist\bin\ffmpeg.exe" copy /y "%%I" "dist\bin\ffmpeg.exe" >nul
    )
)
if not exist "dist\bin\ffprobe.exe" (
    for /f "delims=" %%I in ('where ffprobe.exe 2^>nul') do (
        if not exist "dist\bin\ffprobe.exe" copy /y "%%I" "dist\bin\ffprobe.exe" >nul
    )
)
if exist "dist\bin\ffmpeg.exe" (
    echo OK: FFmpeg disponibile nella cartella portatile.
) else (
    echo ATTENZIONE: ffmpeg.exe non trovato. Copialo in dist\bin.
)
if exist "dist\bin\ffprobe.exe" (
    echo OK: FFprobe disponibile nella cartella portatile.
) else (
    echo ATTENZIONE: ffprobe.exe non trovato. Copialo in dist\bin.
)
echo.
echo Per avviare: doppio clic su dist\UVR_Audio_Track_Extractor.exe
echo Per spostarlo su un altro PC offline: copia TUTTA la cartella dist.
echo.
pause
