@echo off
setlocal
title GP Score Log - dev Worker

for %%I in ("%~dp0..") do set "DEV_WORKER_ROOT=%%~fI"
tasklist /FI "IMAGENAME eq DDRGpScoreViewer.exe" /NH 2>nul | find /I "DDRGpScoreViewer.exe" >nul
if not errorlevel 1 (
    echo GP Score Log is already running. Exit it from the notification-area menu first.
    goto :failed
)
where dotnet >nul 2>nul
if errorlevel 1 (
    echo The .NET SDK is required to start the development app.
    goto :failed
)
if not exist "%DEV_WORKER_ROOT%\databases\ddrgp-master.sqlite" (
    echo Missing development database: databases\ddrgp-master.sqlite
    goto :failed
)
if not exist "%DEV_WORKER_ROOT%\databases\jacket-catalog-release.sqlite" (
    echo Missing development database: databases\jacket-catalog-release.sqlite
    goto :failed
)

set "DDRGP_WEB_API_ORIGIN=https://ddrgp-scorelog-dev.tts1374.workers.dev"
set "DDRGP_SCORE_VIEWER_DEVELOPMENT_ROOT=%DEV_WORKER_ROOT%"
echo Starting GP Score Log with the dev Worker: %DDRGP_WEB_API_ORIGIN%
pushd "%DEV_WORKER_ROOT%"
if errorlevel 1 goto :failed
dotnet run --project "%DEV_WORKER_ROOT%\app\src\DDRGpScoreViewer\DDRGpScoreViewer.csproj" --configuration Debug --no-restore
set "DEV_WORKER_EXIT_CODE=%ERRORLEVEL%"
popd
if not "%DEV_WORKER_EXIT_CODE%"=="0" (
    echo The development app could not start. Check the message above.
    pause
)
exit /b %DEV_WORKER_EXIT_CODE%

:failed
pause
exit /b 1
