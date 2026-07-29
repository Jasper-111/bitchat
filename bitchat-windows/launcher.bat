@echo off
setlocal
chcp 65001 >nul 2>&1
cd /d "%~dp0"
set "DOTNET=E:\02_asset\.tether\store\dotnet-sdk\dotnet.exe"

echo ============================================================
echo   BitChat - Windows
echo ============================================================
echo.

echo   [1] Unified Client + Bot  (primary)
echo   [2] Standalone GUI client (external relays)
echo   [3] Build all (Release)
echo   [4] Build all (Debug)
echo   [5] Clean + rebuild
echo   [0] Exit
echo.
choice /c 123450 /n /m "Select: "

if errorlevel 6 exit /b 0
if errorlevel 5 goto :clean
if errorlevel 4 goto :debug
if errorlevel 3 goto :release
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
