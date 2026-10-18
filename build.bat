@echo off
rem Builds bin\BetterGlorious.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
rem No SDK or Visual Studio needed. The WebView2 SDK is fetched from nuget.org on first build.
setlocal
set ROOT=%~dp0
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
set WV2VER=1.0.4258.31
set LIB=%ROOT%lib\webview2

if not exist "%LIB%\Microsoft.Web.WebView2.Core.dll" (
  echo Fetching WebView2 SDK %WV2VER%...
  if not exist "%LIB%" mkdir "%LIB%"
  curl -sSL -o "%LIB%\wv2.zip" "https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/%WV2VER%/microsoft.web.webview2.%WV2VER%.nupkg" || exit /b 1
  mkdir "%LIB%\pkg" 2>nul
  tar -xf "%LIB%\wv2.zip" -C "%LIB%\pkg" || exit /b 1
  copy /y "%LIB%\pkg\lib\net462\Microsoft.Web.WebView2.Core.dll" "%LIB%\" >nul
  copy /y "%LIB%\pkg\lib\net462\Microsoft.Web.WebView2.WinForms.dll" "%LIB%\" >nul
  copy /y "%LIB%\pkg\runtimes\win-x64\native\WebView2Loader.dll" "%LIB%\" >nul
  rmdir /s /q "%LIB%\pkg"
  del "%LIB%\wv2.zip"
)

if not exist "%ROOT%bin" mkdir "%ROOT%bin"
rem Tray text, one JSON per language, embedded as BetterGlorious.Locales.<code>.json
setlocal enabledelayedexpansion
set RES=
for %%f in ("%ROOT%src\Locales\*.json") do set RES=!RES! /resource:"%%f",BetterGlorious.Locales.%%~nxf
"%CSC%" /nologo /target:winexe /optimize+ /platform:x64 ^
  /out:"%ROOT%bin\BetterGlorious.exe" ^
  /win32manifest:"%ROOT%src\app.manifest" ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  /r:"%LIB%\Microsoft.Web.WebView2.Core.dll" /r:"%LIB%\Microsoft.Web.WebView2.WinForms.dll" ^
  /recurse:"%ROOT%src\*.cs" !RES!
if errorlevel 1 exit /b 1

copy /y "%LIB%\*.dll" "%ROOT%bin\" >nul
robocopy "%ROOT%ui" "%ROOT%bin\ui" /mir /njh /njs /nfl /ndl >nul
echo Built bin\BetterGlorious.exe
exit /b 0
