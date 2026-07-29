@echo off
setlocal
chcp 65001 >nul 2>&1
cd /d "%~dp0"
set "DOTNET=E:\02_asset\.tether\store\dotnet-sdk\dotnet.exe"

echo ============================================================
echo   BitChat - Windows
echo ============================================================
echo.

echo   [1] Unified Client + Bot  (single window, in-process)
echo   [2] Standalone GUI client (public relays)
echo   [3] Local test: relay + 2 clients (no internet)
echo   [4] Build all (Release)
echo   [5] Build all (Debug)
echo   [6] Clean + rebuild
echo   [0] Exit
echo.
choice /c 1234560 /n /m "Select: "

if errorlevel 7 exit /b 0
if errorlevel 6 goto :clean
if errorlevel 5 goto :debug
if errorlevel 4 goto :release
if errorlevel 3 goto :local
if errorlevel 2 goto :standalone
if errorlevel 1 goto :unified

:unified
if not exist "BitChat.Bot\bin\Release\net8.0-windows10.0.19041.0\BitChat.Bot.exe" call :build_rel
echo [*] Starting Unified Client + Bot...
start "" "BitChat.Bot\bin\Release\net8.0-windows10.0.19041.0\BitChat.Bot.exe"
goto :end

:standalone
if not exist "BitChat.App\bin\Release\net8.0-windows10.0.19041.0\BitChat.App.exe" call :build_rel
echo [*] Starting standalone App...
start "" "BitChat.App\bin\Release\net8.0-windows10.0.19041.0\BitChat.App.exe"
goto :end

:local
if not exist "BitChat.App\bin\Release\net8.0-windows10.0.19041.0\BitChat.App.exe" call :build_rel
echo [*] Starting local relay + 2 clients...
echo.
echo   First window: hosts relay on port 4869, auto-connects
echo   Second window: connects to the hosted relay
echo.
start "" "BitChat.App\bin\Release\net8.0-windows10.0.19041.0\BitChat.App.exe" --local
timeout /t 2 /nobreak >nul
start "" "BitChat.App\bin\Release\net8.0-windows10.0.19041.0\BitChat.App.exe" --local
goto :end

:release
:build_rel
echo [*] Building Release...
"%DOTNET%" build BitChat.sln -c Release --nologo -v q
goto :end

:debug
echo [*] Building Debug...
"%DOTNET%" build BitChat.sln -c Debug --nologo -v q
goto :end

:clean
echo [*] Cleaning...
"%DOTNET%" clean BitChat.sln -c Release -v q
"%DOTNET%" clean BitChat.sln -c Debug -v q
echo [*] Rebuilding Release...
"%DOTNET%" build BitChat.sln -c Release --nologo -v q
goto :end

:end
echo.
echo Done.
pause
exit /b 0
