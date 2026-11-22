@echo off
setlocal
cd /d "%~dp0"

where dotnet >nul 2>&1
if errorlevel 1 (
    echo [ERROR] .NET SDK 10 is required to build Lilly Integrated Mod Manager and the desktop app.
    set "BUILD_EXIT_CODE=1"
    goto :finish
)

echo [1/2] Building the RimWorld mod...
dotnet build "%~dp0Source\Lilly.IntegratedModManager.csproj" --configuration Release --nologo
if errorlevel 1 (
    set "BUILD_EXIT_CODE=%ERRORLEVEL%"
    goto :finish
)

echo.
echo [2/2] Publishing the standalone Windows desktop app...
set "PUBLISH_DIR=%TEMP%\LillyModManagerPublish"
if exist "%PUBLISH_DIR%" rmdir /s /q "%PUBLISH_DIR%"
dotnet publish "%~dp0Desktop\Lilly.ModManager.Desktop.csproj" --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true --output "%PUBLISH_DIR%" --nologo
if errorlevel 1 (
    set "BUILD_EXIT_CODE=%ERRORLEVEL%"
    goto :finish
)

copy /y "%PUBLISH_DIR%\Lilly.ModManager.exe" "%~dp0Lilly.ModManager.exe" >nul
if errorlevel 1 (
    set "BUILD_EXIT_CODE=1"
    goto :finish
)

echo.
echo [SUCCESS] RimWorld mod: "%~dp0Assemblies\Lilly.IntegratedModManager.dll"
echo [SUCCESS] Standalone app: "%~dp0Lilly.ModManager.exe"
set "BUILD_EXIT_CODE=0"

:finish
if not "%BUILD_EXIT_CODE%"=="0" echo [ERROR] Build failed with exit code %BUILD_EXIT_CODE%.
exit /b %BUILD_EXIT_CODE%
