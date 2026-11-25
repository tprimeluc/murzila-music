@echo off
setlocal
call "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat" >nul
cl /nologo /EHsc /O2 main.cpp /Fe:MurzilaBrowserNative.exe /link user32.lib gdi32.lib
if errorlevel 1 exit /b 1
echo Built: %~dp0MurzilaBrowserNative.exe
