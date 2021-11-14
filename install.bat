@echo off
set MOD_NAME=AtmosphericDragConfigurable
set SE_PATH=C:\Users\Jamac\AppData\Roaming\SpaceEngineers
set SE_INSTALL_PATH=D:\Steam\steamapps\common\SpaceEngineers

if "%MOD_NAME%" == "" (
    echo Please set MOD_NAME to the name of your mod
    exit /b 1
)

if "%SE_PATH%" == "C:\Users\USERNAME\AppData\Roaming\SpaceEngineers" (
    echo Please set SE_PATH to the path to your Space Engineers AppData directory
    exit /b 1
)

cd /D "%~dp0"
if exist .\Data\Scripts (
    set INSTALL_PATH="%SE_PATH%\Mods\%MOD_NAME%"
    echo Installing to %INSTALL_PATH%...
    copy %INSTALL_PATH%\modinfo.sbmi . 1>nul 2>nul
    rd /S /Q %INSTALL_PATH%
    md %INSTALL_PATH%
    copy .\metadata.mod %INSTALL_PATH% 1>nul 2>nul
    copy .\modinfo.sbmi %INSTALL_PATH% 1>nul 2>nul
    copy .\thumb.jpg %INSTALL_PATH% 1>nul 2>nul
    copy .\README* %INSTALL_PATH% 1>nul 2>nul
    robocopy /E /NS /NC /NFL /NDL /NP /NJH /NJS .\Data %INSTALL_PATH%\Data 1>nul
    del /Q %INSTALL_PATH%\Data\Scripts\*.sln 1>nul 2>nul
    del /Q %INSTALL_PATH%\Data\Scripts\%MOD_NAME%\*.csproj 1>nul 2>nul
    rd /S /Q %INSTALL_PATH%\Data\Scripts\%MOD_NAME%\bin 1>nul 2>nul
    rd /S /Q %INSTALL_PATH%\Data\Scripts\%MOD_NAME%\obj 1>nul 2>nul
    echo Installed mod at %INSTALL_PATH%
) else (
    echo Performing initial setup...
    rd /S /Q .git
    mkdir .\Data\Scripts\%MOD_NAME%
    copy .\setup_files\template.cs .\Data\Scripts\%MOD_NAME%\%MOD_NAME%.cs 1>nul 2>nul
    copy .\setup_files\SE_Mod.csproj .\Data\Scripts\%MOD_NAME% 1>nul 2>nul
    powershell -Command "(Get-Content .\Data\Scripts\%MOD_NAME%\SE_Mod.csproj) -replace 'SE_INSTALL_PATH', '%SE_INSTALL_PATH%' | Out-File -encoding ASCII .\Data\Scripts\%MOD_NAME%\SE_Mod.csproj"
    rd /S /Q setup_files
    cd .\Data\Scripts
    dotnet new sln -n SE_Mod
    dotnet sln add %MOD_NAME%\SE_Mod.csproj
    dotnet restore
    echo Setup complete! Restart VS Code if you already had the folder open in it.
)
