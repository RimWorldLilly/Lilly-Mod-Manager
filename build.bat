@echo off
setlocal
cd /d "%~dp0"

where dotnet >nul 2>&1
if errorlevel 1 (
    echo [ERROR] .NET SDK 10 is required to build Lilly Integrated Mod Manager.
    exit /b 1
)

echo Building the RimWorld mod...
dotnet build "%~dp0Source\Lilly.IntegratedModManager.csproj" --configuration Release --nologo
if errorlevel 1 exit /b %ERRORLEVEL%

echo.
echo [SUCCESS] RimWorld mod: "%~dp0Assemblies\Lilly.IntegratedModManager.dll"
exit /b 0
