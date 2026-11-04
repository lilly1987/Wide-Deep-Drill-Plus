@echo off
setlocal
dotnet build "%~dp0Source\WideDeepDrillPlus.csproj" -c Release
if errorlevel 1 exit /b 1
