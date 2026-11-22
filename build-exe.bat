@echo off
setlocal
cd /d "%~dp0"

where dotnet >nul 2>&1
if errorlevel 1 goto :install_sdk_prompt

dotnet --list-sdks 2>nul | findstr /r /c:"^10\." >nul
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

set "PUBLISH_DIR=%~dp0Desktop\publish\win-x64"

echo Publishing the standalone Windows desktop app...
if exist "%PUBLISH_DIR%" rmdir /s /q "%PUBLISH_DIR%"

dotnet publish "%~dp0Desktop\Lilly.ModManager.Desktop.csproj" --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true --output "%PUBLISH_DIR%" --nologo
if errorlevel 1 exit /b %ERRORLEVEL%

echo.
echo [SUCCESS] Standalone app: "%PUBLISH_DIR%\Lilly.ModManager.exe"
exit /b 0
