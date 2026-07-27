@echo off
setlocal
chcp 65001 >nul 2>&1
cd /d "%~dp0"
set "DOTNET=E:\02_asset\.tether\store\dotnet-sdk\dotnet.exe"

echo ============================================================
echo   BitChat - Windows Client
echo ============================================================
echo.

if not exist "%DOTNET%" (
    echo [FATAL] .NET SDK not found at %DOTNET%
    echo [*]     Run: E:\03_infra\tether\apps\dotnet-sdk\configure.bat
    pause
    exit /b 1
)

for /f "delims=" %%v in ('"%DOTNET%" --version 2^>nul') do set "VER=%%v"
if "%VER%"=="" (
    echo [FATAL] .NET SDK not functional
    pause
    exit /b 1
)
echo [OK] .NET SDK %VER%

:check_build
if exist "BitChat.App\bin\Release\net8.0\BitChat.App.exe" goto :launch

echo [*] Building Release (1st run only, ~3s)...
"%DOTNET%" build BitChat.sln -c Release --nologo -v q
if errorlevel 1 (
    echo [FATAL] Build failed
    pause
    exit /b 1
)
echo [OK] Build complete

:launch
echo [*] Starting BitChat...
start "" "BitChat.App\bin\Release\net8.0\BitChat.App.exe"
echo [OK] Client launched
echo.
echo   Your npub will appear in the app header.
echo   Connect with: Connect button
echo   Pull relay list: E:\04_archive\bitchat\relays\online_relays_gps.csv
echo ============================================================
exit /b 0
