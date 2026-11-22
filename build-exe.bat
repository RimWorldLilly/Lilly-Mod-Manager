@echo off
setlocal
cd /d "%~dp0"

where dotnet >nul 2>&1
if errorlevel 1 (
    echo [ERROR] .NET SDK 10 is required to publish the standalone desktop app.
    exit /b 1
)

set "PUBLISH_DIR=%~dp0Desktop\publish\win-x64"

echo Publishing the standalone Windows desktop app...
if exist "%PUBLISH_DIR%" rmdir /s /q "%PUBLISH_DIR%"

dotnet publish "%~dp0Desktop\Lilly.ModManager.Desktop.csproj" --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true --output "%PUBLISH_DIR%" --nologo
if errorlevel 1 exit /b %ERRORLEVEL%

echo.
echo [SUCCESS] Standalone app: "%PUBLISH_DIR%\Lilly.ModManager.exe"
exit /b 0
