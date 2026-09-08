@echo off
setlocal EnableExtensions

cd /d "%~dp0"

if /I "%~1"=="--core" goto run_core
if /I "%~1"=="--gateway" goto run_gateway
if /I "%~1"=="--view" goto run_view
if /I "%~1"=="--admin" goto run_admin

set "ROOT=%CD%"
set "ALPHA_ROOT=%ROOT%\.machiverse-alpha"
set "SKIP_BUILD=0"
if /I "%~1"=="--no-build" set "SKIP_BUILD=1"

echo.
echo ============================================================
echo  MachiVerse Alpha 1.0 - Local Windows Launcher
echo ============================================================
echo  Repository : %ROOT%
echo  Data root  : %ALPHA_ROOT%
echo.

where dotnet >nul 2>&1
if errorlevel 1 (
    echo [ERROR] dotnet was not found in PATH.
    echo Install the .NET SDK required by global.json and try again.
    goto failed
)

for /f "delims=" %%V in ('dotnet --version 2^>nul') do set "DOTNET_VERSION=%%V"
echo [OK] .NET SDK %DOTNET_VERSION%

if not exist "%ALPHA_ROOT%\world" mkdir "%ALPHA_ROOT%\world" >nul 2>&1
if not exist "%ALPHA_ROOT%\gateway" mkdir "%ALPHA_ROOT%\gateway" >nul 2>&1

if "%SKIP_BUILD%"=="1" (
    echo [SKIP] Release build ^(--no-build^)
) else (
    call :build_project "src\MachiVerse.Simulation.Core\MachiVerse.Simulation.Core.csproj" "Simulation Core"
    if errorlevel 1 goto build_failed
    call :build_project "src\MachiVerse.Gateway\MachiVerse.Gateway.csproj" "Gateway"
    if errorlevel 1 goto build_failed
    call :build_project "src\MachiVerse.View\MachiVerse.View.csproj" "General View"
    if errorlevel 1 goto build_failed
    call :build_project "src\MachiVerse.Administration.View\MachiVerse.Administration.View.csproj" "Administration View"
    if errorlevel 1 goto build_failed
)

echo.
echo [START] Simulation Core
start "MachiVerse Simulation Core" "%ComSpec%" /k call "%~f0" --core "%ALPHA_ROOT%"
call :wait_http "http://127.0.0.1:5710/healthz" "Simulation Core" 60
if errorlevel 1 goto startup_failed

echo [START] Gateway
start "MachiVerse Gateway" "%ComSpec%" /k call "%~f0" --gateway "%ALPHA_ROOT%"
call :wait_gateway 60
if errorlevel 1 goto startup_failed

echo [START] General View
start "MachiVerse General View" "%ComSpec%" /k call "%~f0" --view "%ALPHA_ROOT%"
call :wait_http "http://127.0.0.1:5750/" "General View" 60
if errorlevel 1 goto startup_failed

echo [START] Administration View
start "MachiVerse Administration View" "%ComSpec%" /k call "%~f0" --admin "%ALPHA_ROOT%"
call :wait_http "http://127.0.0.1:5660/" "Administration View" 60
if errorlevel 1 goto startup_failed

echo.
echo ============================================================
echo  MachiVerse Alpha 1.0 is running.
echo ============================================================
echo  General View       : http://127.0.0.1:5750/
echo  Administration View: http://127.0.0.1:5660/
echo  Gateway health     : http://127.0.0.1:5520/healthz
echo  Core health        : http://127.0.0.1:5710/healthz
echo.
echo Close the four component command windows to stop the local Alpha stack.
echo Data is kept under .machiverse-alpha so restart/recovery can be tested.
echo.

start "" "http://127.0.0.1:5750/"
start "" "http://127.0.0.1:5660/"

pause
exit /b 0

:build_project
echo.
echo [BUILD] %~2
dotnet build "%~1" --configuration Release
exit /b %errorlevel%

:wait_http
set "WAIT_URL=%~1"
set "WAIT_NAME=%~2"
set "WAIT_SECONDS=%~3"
echo [WAIT] %WAIT_NAME% ...
for /l %%I in (1,1,%WAIT_SECONDS%) do (
    powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; $r=Invoke-WebRequest -UseBasicParsing -Uri '%WAIT_URL%' -TimeoutSec 2; if($r.StatusCode -ge 200 -and $r.StatusCode -lt 400){exit 0}else{exit 1}" >nul 2>&1
    if not errorlevel 1 (
        echo [OK] %WAIT_NAME%
        exit /b 0
    )
    timeout /t 1 /nobreak >nul
)
echo [ERROR] %WAIT_NAME% did not become ready: %WAIT_URL%
exit /b 1

:wait_gateway
set "WAIT_SECONDS=%~1"
echo [WAIT] Gateway Core link ...
for /l %%I in (1,1,%WAIT_SECONDS%) do (
    powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; $d=((Invoke-WebRequest -UseBasicParsing -Uri 'http://127.0.0.1:5520/healthz' -TimeoutSec 2).Content | ConvertFrom-Json); if($d.status -eq 'ready' -and $d.core.negotiated -eq $true -and $d.core.syncState -eq 'Synced'){exit 0}else{exit 1}" >nul 2>&1
    if not errorlevel 1 (
        echo [OK] Gateway is ready and Core is Synced
        exit /b 0
    )
    timeout /t 1 /nobreak >nul
)
echo [ERROR] Gateway did not reach ready/Synced state.
exit /b 1

:run_core
set "ALPHA_ROOT=%~2"
set "MACHIVERSE_ALPHA_LOCAL=1"
set "MACHIVERSE_WORLD_ROOT=%ALPHA_ROOT%\world"
set "Kestrel__Endpoints__Grpc__Url=http://127.0.0.1:5711"
set "Kestrel__Endpoints__Grpc__Protocols=Http2"
set "Kestrel__Endpoints__Health__Url=http://127.0.0.1:5710"
set "Kestrel__Endpoints__Health__Protocols=Http1"
title MachiVerse Simulation Core
echo Starting Simulation Core...
dotnet run --project "src\MachiVerse.Simulation.Core\MachiVerse.Simulation.Core.csproj" --configuration Release --no-build
exit /b %errorlevel%

:run_gateway
set "ALPHA_ROOT=%~2"
set "MACHIVERSE_ALPHA_LOCAL=1"
set "MACHIVERSE_CORE_ENDPOINT=http://127.0.0.1:5711"
set "MACHIVERSE_GATEWAY_DATA_ROOT=%ALPHA_ROOT%\gateway"
set "ASPNETCORE_URLS=http://127.0.0.1:5520"
title MachiVerse Gateway
echo Starting Gateway...
dotnet run --project "src\MachiVerse.Gateway\MachiVerse.Gateway.csproj" --configuration Release --no-build
exit /b %errorlevel%

:run_view
set "ASPNETCORE_URLS=http://127.0.0.1:5750"
title MachiVerse General View
echo Starting General View...
dotnet run --project "src\MachiVerse.View\MachiVerse.View.csproj" --configuration Release --no-build --no-launch-profile
exit /b %errorlevel%

:run_admin
set "ASPNETCORE_URLS=http://127.0.0.1:5660"
title MachiVerse Administration View
echo Starting Administration View...
dotnet run --project "src\MachiVerse.Administration.View\MachiVerse.Administration.View.csproj" --configuration Release --no-build --no-launch-profile
exit /b %errorlevel%

:build_failed
echo.
echo [ERROR] Release build failed. No component was started.
goto failed

:startup_failed
echo.
echo [ERROR] Alpha startup failed.
echo Check the component command windows for details.
goto failed

:failed
echo.
pause
exit /b 1
