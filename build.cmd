@echo off
rem Builds AlwaysOnKeys.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
cd /d "%~dp0"
if not exist dist mkdir dist
"%CSC%" /nologo /target:winexe /platform:x64 /optimize+ /unsafe /out:dist\AlwaysOnKeys.exe ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll src\*.cs
