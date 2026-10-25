@echo off
rem DaikonLine build script (uses csc.exe bundled with Windows / .NET Framework 4.x)
rem Output: dist\DaikonLine.exe
rem Usage : build.bat [/nopause]
setlocal EnableDelayedExpansion
cd /d "%~dp0"

set "NOPAUSE="
for %%A in (%*) do (
  if /i "%%~A"=="/nopause" set "NOPAUSE=1"
)

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo csc.exe not found. .NET Framework 4.x is required.
  goto :fail
)

set "SRC=src"
set "ICON=%SRC%\Resources\app.ico"

if not exist "dist" mkdir "dist"

"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 ^
  /win32icon:%ICON% /resource:%ICON%,DaikonLine.app.ico ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll ^
  /out:dist\DaikonLine.exe /recurse:%SRC%\*.cs
if errorlevel 1 (
  echo.
  echo Build FAILED.
  goto :fail
)

rem Not user-edited: always update
rem (dist\data\settings.json is created by the app itself and is never touched here)
copy /y "README.md" "dist\" >nul
if exist "LICENSE.md" copy /y "LICENSE.md" "dist\" >nul

echo.
echo Build OK: dist\DaikonLine.exe
if not defined NOPAUSE pause
exit /b 0

:fail
if not defined NOPAUSE pause
exit /b 1
