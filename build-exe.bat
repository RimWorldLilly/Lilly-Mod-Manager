@echo off
setlocal
cd /d "%~dp0"

where dotnet >nul 2>&1
if errorlevel 1 goto :install_sdk_prompt

dotnet --list-sdks 2>nul | findstr /r /c:"^10\\." >nul
if errorlevel 1 goto :install_sdk_prompt
goto :sdk_ready

:install_sdk_prompt
echo.
echo [NOTICE] .NET SDK 10 was not found.
echo This build requires the .NET 10 SDK. A runtime-only installation is not enough.
echo.
echo Official download:
echo https://dotnet.microsoft.com/download/dotnet/10.0
echo.
choice /c YN /n /m "Open the official .NET 10 SDK download page now? [Y/N] "
if errorlevel 2 (
    echo.
    echo Install the .NET 10 SDK, then run this BAT file again.
    pause
    exit /b 1
)
start "" "https://dotnet.microsoft.com/download/dotnet/10.0"
echo.
echo Install the .NET 10 SDK, then run this BAT file again.
pause
exit /b 1

:sdk_ready
set "PUBLISH_DIR=%~dp0Desktop\\publish\\win-x64"

echo Publishing the standalone Windows desktop app...
if exist "%PUBLISH_DIR%" rmdir /s /q "%PUBLISH_DIR%"

dotnet publish "%~dp0Desktop\\Lilly.ModManager.Desktop.csproj" --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true --output "%PUBLISH_DIR%" --nologo
set "BUILD_EXIT=%ERRORLEVEL%"

call :cleanup
if not "%BUILD_EXIT%"=="0" (
    echo.
    echo [ERROR] Desktop app publish failed. Temporary build folders were cleaned.
    exit /b %BUILD_EXIT%
)

set "LMM_EXE_PATH=%PUBLISH_DIR%\\Lilly.ModManager.exe"
echo Creating a desktop shortcut...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; $exe=$env:LMM_EXE_PATH; if (-not (Test-Path -LiteralPath $exe)) { throw ('Published EXE not found: ' + $exe) }; $desktop=[Environment]::GetFolderPath('Desktop'); $shortcutPath=Join-Path $desktop 'Lilly Mod Manager.lnk'; $shell=New-Object -ComObject WScript.Shell; $shortcut=$shell.CreateShortcut($shortcutPath); $shortcut.TargetPath=$exe; $shortcut.WorkingDirectory=Split-Path -Parent $exe; $shortcut.IconLocation=$exe; $shortcut.Save(); Write-Output ('Shortcut created: ' + $shortcutPath)"
if errorlevel 1 (
    echo.
    echo [WARNING] Build succeeded, but desktop shortcut creation failed.
    exit /b 1
)

echo.
echo [SUCCESS] Standalone app: "%PUBLISH_DIR%\\Lilly.ModManager.exe"
echo [SUCCESS] Desktop shortcut: "Lilly Mod Manager.lnk"
exit /b 0

:cleanup
echo.
echo Cleaning temporary build folders...
if exist "%~dp0Desktop\\bin" rmdir /s /q "%~dp0Desktop\\bin"
if exist "%~dp0Desktop\\obj" rmdir /s /q "%~dp0Desktop\\obj"
if exist "%~dp0Source\\obj" rmdir /s /q "%~dp0Source\\obj"
exit /b 0
