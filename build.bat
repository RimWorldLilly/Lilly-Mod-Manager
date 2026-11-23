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
echo Building the RimWorld mod...
dotnet build "%~dp0Source\Lilly.IntegratedModManager.csproj" --configuration Release --nologo
set "BUILD_EXIT=%ERRORLEVEL%"

call :cleanup
if not "%BUILD_EXIT%"=="0" (
    echo.
    echo [ERROR] RimWorld mod build failed. Temporary build folders were cleaned.
    exit /b %BUILD_EXIT%
)

echo.
echo [SUCCESS] RimWorld mod: "%~dp0Assemblies\Lilly.IntegratedModManager.dll"
exit /b 0

:cleanup
echo.
echo Cleaning temporary build folders...
if exist "%~dp0Desktop\bin" rmdir /s /q "%~dp0Desktop\bin"
if exist "%~dp0Desktop\obj" rmdir /s /q "%~dp0Desktop\obj"
if exist "%~dp0Source\obj" rmdir /s /q "%~dp0Source\obj"
exit /b 0
