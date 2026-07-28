@echo off
chcp 65001 >nul 2>&1
setlocal EnableDelayedExpansion
cd /d "%~dp0.."

echo ============================================================
echo   SimpleBLE - Build for Windows (x64 Release)
echo ============================================================
echo.

set "SIMPLEBLE_DIR=%cd%\third_party\SimpleBLE"
set "SIMPLEBLE_SRC=%SIMPLEBLE_DIR%\src"
set "SIMPLEBLE_BUILD=%SIMPLEBLE_DIR%\build"
set "OUTPUT_DLL=%SIMPLEBLE_BUILD%\simplecble\Release\simpleble_c.dll"
set "TARGET_DLL=%cd%\third_party\SimpleBLE\simpleble_c.dll"

:: Check for CMake
where cmake >nul 2>&1
if errorlevel 1 (
    echo [ERROR] CMake not found. Install from https://cmake.org/download/
    exit /b 1
)

:: Check for Visual Studio
if not defined VisualStudioVersion (
    echo [WARN]  Not running from VS Developer Command Prompt.
    echo        Looking for Visual Studio installation...
    for /f "usebackq tokens=*" %%i in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -property installationPath 2^>nul`) do set "VS_PATH=%%i"
    if not defined VS_PATH (
        echo [ERROR] Visual Studio 2022 not found.
        exit /b 1
    )
    echo        Found: !VS_PATH!
)

:: Clone SimpleBLE if not present
if not exist "%SIMPLEBLE_SRC%" (
    echo [1/4] Cloning SimpleBLE...
    git clone --depth 1 https://github.com/OpenBluetoothToolbox/SimpleBLE "%SIMPLEBLE_SRC%"
    if errorlevel 1 (
        echo [ERROR] Failed to clone SimpleBLE.
        exit /b 1
    )
) else (
    echo [1/4] SimpleBLE source already present.
)

:: CMake configure
echo [2/4] Configuring CMake...
if exist "%SIMPLEBLE_BUILD%" rmdir /s /q "%SIMPLEBLE_BUILD%"
cmake -S "%SIMPLEBLE_SRC%" -B "%SIMPLEBLE_BUILD%" ^
    -G "Visual Studio 17 2022" ^
    -A x64 ^
    -DBUILD_SHARED_LIBS=ON ^
    -DSIMPLEBLE_BUILD_EXAMPLES=OFF ^
    -DSIMPLEBLE_BUILD_TESTS=OFF ^
    -DSIMPLEBLE_SIMPLEAIBLE=OFF ^
    -DSIMPLEBLE_SIMPLEPYBLE=OFF ^
    -DSIMPLEBLE_SIMPLEJAVABLE=OFF ^
    -DSIMPLEBLE_SIMPLERSBLE=OFF ^
    -DSIMPLEBLE_SIMPLEDROIDBLE=OFF
if errorlevel 1 (
    echo [ERROR] CMake configure failed.
    exit /b 1
)

:: Build
echo [3/4] Building Release (x64)...
cmake --build "%SIMPLEBLE_BUILD%" --config Release
if errorlevel 1 (
    echo [ERROR] Build failed.
    exit /b 1
)

:: Copy DLL
echo [4/4] Copying simpleble_c.dll...
if exist "%OUTPUT_DLL%" (
    copy /y "%OUTPUT_DLL%" "%TARGET_DLL%" >nul
    echo        simpleble_c.dll -> third_party\SimpleBLE\
) else (
    echo [ERROR] Build output not found: %OUTPUT_DLL%
    echo        Check build logs for errors.
    exit /b 1
)

echo.
echo ============================================================
echo   Build complete!
echo   DLL: third_party\SimpleBLE\simpleble_c.dll
echo ============================================================
pause
exit /b 0
