@echo off
setlocal
rem Build script: sources live next to this script; the exe is written to "..\JCD Creator".
set "HERE=%~dp0"
set "SRC=%HERE%"
set "OUTDIR=%HERE%..\JCD Creator"
set "OUT=%OUTDIR%\JcdCreator.exe"
if not exist "%OUTDIR%" mkdir "%OUTDIR%"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" goto nocsc

if not exist "%SRC%*.cs" goto nosrc

"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 /out:"%OUT%" ^
 /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
 "%SRC%*.cs"
if errorlevel 1 goto failed

echo.
echo Build OK: "%OUT%"
exit /b 0

:nocsc
echo csc.exe (Microsoft .NET Framework 4.x) not found.
exit /b 1

:nosrc
echo Source files not found in "%SRC%".
exit /b 1

:failed
echo.
echo BUILD FAILED.
exit /b 1
