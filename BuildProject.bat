@echo off
setlocal
dotnet build "%~dp0VibeSkuaX.sln" -c Release --nologo
exit /b %ERRORLEVEL%
