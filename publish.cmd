@echo off
rem Builds a single self-contained FNFTracker.exe into .\publish
dotnet publish FnfTracker.App\FnfTracker.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
if %errorlevel% neq 0 (
  echo.
  echo Publish failed.
  pause
  exit /b %errorlevel%
)
echo.
echo Done: publish\FNFTracker.exe
pause
