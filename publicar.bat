@echo off
setlocal
cd /d "%~dp0"
echo === 1/2: modo portatil  (dist\portable\LanCast.exe)
dotnet publish src\Host -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o dist\portable
if errorlevel 1 goto :erro
del dist\portable\*.pdb dist\portable\web.config 2>nul

echo.
echo === 2/2: instalador  (dist\installer\LanCast-Setup-x.y.z.exe)
set ISCC=tools\InnoSetup\ISCC.exe
if not exist "%ISCC%" set ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe
if not exist "%ISCC%" set ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe
for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "([xml](Get-Content src\Host\LanCastHost.csproj)).Project.PropertyGroup.Version | Where-Object { $_ }"`) do set APPVER=%%v
"%ISCC%" /DAppVersion=%APPVER% installer\LanCast.iss
if errorlevel 1 goto :erro

echo.
echo Pronto:
echo   Portatil:   dist\portable\LanCast.exe
echo   Instalador: dist\installer\
goto :fim
:erro
echo.
echo FALHOU. Veja as mensagens acima.
:fim
pause
