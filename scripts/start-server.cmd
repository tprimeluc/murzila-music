@echo off
chcp 65001 >nul
cd /d "%~dp0"
if "%MURZILA_SERVER_URL%"=="" set MURZILA_SERVER_URL=http://murzila-music.cc.cd
if "%MURZILA_BIND_URL%"=="" set MURZILA_BIND_URL=http://0.0.0.0:6001
if "%YOOKASSA_SHOP_ID%"=="" echo YooKassa keys not set - local test payment mode.
if "%YOOKASSA_SECRET_KEY%"=="" echo Real money is not charged in test mode.
echo Murzila-Music online service: %MURZILA_SERVER_URL%
"%~dp0Server\MurzilaMusic.Server.exe"
pause
