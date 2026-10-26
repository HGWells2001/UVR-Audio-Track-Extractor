# UVR Audio Track Extractor V5

Windows Forms application for offline extraction of audio tracks and reconstruction of a 5.1 surround soundtrack from UVR-processed WAV stems or chunk folders.

- Extract tracks from video, split channels, and optionally create chunks for UVR.
- Recompose **FL / FR / FC / LFE / SL / SR** into WAV, FLAC or AC3.
- Supports 5.1(side) and 5.1(back) layouts.
- Optionally mix **Italian dialogue into the center channel**, with a **-12 to +6 dB voice slider** (default **+6 dB**).
- No Internet connection required at runtime. FFmpeg and FFprobe must be on the PATH or in `dist/bin`.

## Build (Windows 10/11)

Run `COMPILA_EXE.bat`. It uses the installed .NET Framework 4.x C# compiler and creates `dist/UVR_Audio_Track_Extractor.exe`. Then run `AVVIA.bat`.

**The C# Windows Forms build has not been compiled or verified in this Linux environment.**

For full usage instructions (in Italian), read [LEGGIMI.md](LEGGIMI.md).

This repository contains the application **source code** and build scripts, not FFmpeg binaries or generated audio files.
