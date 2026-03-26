@echo off
setlocal EnableDelayedExpansion
title MSFS Mod Manager v2.0

:: Set console properties
mode con: cols=100 lines=40

:: Variables
set "VERSION=2.0"
set "CONFIG_FILE=%APPDATA%\MSFS_ModInstaller\config.ini"
set "CONFLICTS_DB=%APPDATA%\MSFS_ModInstaller\conflicts.db"
set "PROFILES_DIR=%APPDATA%\MSFS_ModInstaller\profiles"
set "STAGING_DIR=%APPDATA%\MSFS_ModInstaller\MSFS_Disabled_Mods"
set "COMMUNITY="
set "CUSTOM_INSTALL_DIR="
set "USE_CUSTOM_DIR=no"
set "DEFAULT_BACKUP_DIR=%USERPROFILE%\Documents\MSFS_Mod_Backups"
set "CURRENT_THEME=1"

:: Initialize theme system
call :INIT_THEME_SYSTEM

:: Create necessary directories
if not exist "%APPDATA%\MSFS_ModInstaller" mkdir "%APPDATA%\MSFS_ModInstaller"
if not exist "%PROFILES_DIR%" mkdir "%PROFILES_DIR%"
if not exist "%STAGING_DIR%" mkdir "%STAGING_DIR%"
if not exist "%DEFAULT_BACKUP_DIR%" mkdir "%DEFAULT_BACKUP_DIR%"

:: Initialize conflicts database if not exists
if not exist "%CONFLICTS_DB%" (
    echo # MSFS Mod Conflicts Database > "%CONFLICTS_DB%"
    echo # Format: mod1,mod2,conflict_type,description >> "%CONFLICTS_DB%"
)

:: Check if files were dropped on the script
if not "%~1"=="" (
    set "DROPPED_FILES=yes"
    goto INSTALL_DROPPED
)

:: Load configuration and apply theme
call :LOAD_CONFIG
call :APPLY_THEME

:: Main Menu
:MAIN_MENU
cls
echo.
echo  ============================================================================================
echo                                    MSFS Mod Manager v%VERSION%
echo                                         by @eedev
echo                              Compatible with MSFS 2020 and 2024
echo  ============================================================================================
echo.
echo   BASIC OPERATIONS                          ADVANCED FEATURES
echo   [1] Install Mod(s)                        [7] Mod Conflict Scanner
echo   [2] Uninstall Mod(s)                      [8] Profile Management
echo   [3] Enable/Disable Mods                   [9] Batch Operations
echo   [4] Export Mod List                       [10] Find Duplicate Files
echo   [5] Backup Manager                        [11] Mod Health Check Plus
echo   [6] Settings                              
echo.
echo   [0] Exit
echo.
echo  ============================================================================================
echo.
set /p "choice=Select option (0-11): "

if "%choice%"=="0" goto EXIT_PROGRAM
if "%choice%"=="1" goto INSTALL
if "%choice%"=="2" goto UNINSTALL
if "%choice%"=="3" goto TOGGLE_MODS
if "%choice%"=="4" goto EXPORT_LIST
if "%choice%"=="5" goto BACKUP_MANAGER
if "%choice%"=="6" goto SETTINGS_MENU
if "%choice%"=="7" goto CONFLICT_SCANNER
if "%choice%"=="8" goto PROFILE_MANAGEMENT
if "%choice%"=="9" goto BATCH_OPERATIONS
if "%choice%"=="10" goto FIND_DUPLICATES
if "%choice%"=="11" goto HEALTH_CHECK_PLUS
goto MAIN_MENU

:EXIT_PROGRAM
cls
echo.
echo  %BORDER_LINE%
echo.
echo   Thank you for using MSFS Mod Manager!
echo.
echo  %BORDER_LINE%
echo.
timeout /t 2 /nobreak >nul
exit /b 0

:: -----------------------------
:: MOD CATEGORIES DETECTION
:: -----------------------------
:DETECT_MOD_TYPE
set "mod_path=%~1"
set "mod_type=Other"

:: Aircraft detection
if exist "%mod_path%\SimObjects\Airplanes" set "mod_type=Aircraft"
if exist "%mod_path%\SimObjects\Rotorcraft" set "mod_type=Helicopter"

:: Scenery detection
if exist "%mod_path%\scenery" set "mod_type=Scenery"
if exist "%mod_path%\materialLib" set "mod_type=Scenery"

:: Avionics/Instruments
if exist "%mod_path%\html_ui\Pages\VCockpit\Instruments" set "mod_type=Avionics"

:: Liveries
for /d %%d in ("%mod_path%\SimObjects\Airplanes\*") do (
    if exist "%%d\TEXTURE.*" set "mod_type=Livery"
)

:: Effects
if exist "%mod_path%\Effects" set "mod_type=Effects"

:: Models
if exist "%mod_path%\ModelBehaviorDefs" set "mod_type=Models"

:: Missions
if exist "%mod_path%\Missions" set "mod_type=Mission"

goto :eof

:: -----------------------------
:: CONFLICT SCANNER (FIXED)
:: -----------------------------
:CONFLICT_SCANNER
cls
call :SHOW_HEADER "MOD CONFLICT SCANNER"

call :LOAD_CONFIG
call :SETUP_COMMUNITY
if not defined COMMUNITY (
    echo  [ERROR] No Community folder detected.
    pause
    goto MAIN_MENU
)

echo  Scanning for potential conflicts...
echo.

set "conflict_count=0"
set "conflict_report=%APPDATA%\MSFS_ModInstaller\conflict_report.txt"

:: Clear previous report
echo Mod Conflict Report > "%conflict_report%"
echo Generated: %DATE% %TIME% >> "%conflict_report%"
echo. >> "%conflict_report%"

:: Build mod list
echo  Phase 1: Building mod index...
set "mod_count=0"
for /d %%m in ("%COMMUNITY%\*") do (
    set /a mod_count+=1
    set "mod[!mod_count!]=%%~nxm"
    set "mod_path[!mod_count!]=%%m"
)

echo  Found %mod_count% mods to analyze
echo.

:: Compare each mod pair
echo  Phase 2: Checking for conflicts...
echo.

for /l %%i in (1,1,%mod_count%) do (
    set /a next=%%i+1
    for /l %%j in (!next!,1,%mod_count%) do (
        :: Create file lists for comparison
        set "files1=%TEMP%\mod_files_%%i.tmp"
        set "files2=%TEMP%\mod_files_%%j.tmp"
        
        :: List all files in first mod
        dir /s /b /a-d "!mod_path[%%i]!" 2>nul > "!files1!"
        
        :: List all files in second mod
        dir /s /b /a-d "!mod_path[%%j]!" 2>nul > "!files2!"
        
        :: Check for conflicts
        set "has_conflict=no"
        for /f "tokens=*" %%f in (!files1!) do (
            set "file1=%%f"
            set "file1=!file1:%mod_path[%%i]%=!"
            
            for /f "tokens=*" %%g in (!files2!) do (
                set "file2=%%g"
                set "file2=!file2:%mod_path[%%j]%=!"
                
                if "!file1!"=="!file2!" (
                    set "has_conflict=yes"
                    goto :conflict_found
                )
            )
        )
        
        :conflict_found
        if "!has_conflict!"=="yes" (
            set /a conflict_count+=1
            echo   [CONFLICT] !mod[%%i]! vs !mod[%%j]!
            echo CONFLICT: !mod[%%i]! vs !mod[%%j]! >> "%conflict_report%"
            echo !mod[%%i]!,!mod[%%j]!,file_conflict,Overlapping files detected >> "%CONFLICTS_DB%"
        )
        
        :: Cleanup temp files
        if exist "!files1!" del "!files1!" 2>nul
        if exist "!files2!" del "!files2!" 2>nul
    )
)

echo.
echo  ============================================================================================
echo   SCAN COMPLETE
echo  ============================================================================================
echo.
echo   Conflicts found: %conflict_count%
echo.

if %conflict_count% gtr 0 (
    echo   Detailed report saved to:
    echo   %conflict_report%
)

pause
goto MAIN_MENU

:: -----------------------------
:: TOGGLE MODS (FIXED)
:: -----------------------------
:TOGGLE_MODS
cls
call :SHOW_HEADER "ENABLE/DISABLE MODS"

call :LOAD_CONFIG
call :SETUP_COMMUNITY
if not defined COMMUNITY (
    echo  [ERROR] No Community folder detected.
    pause
    goto MAIN_MENU
)

:: Ensure staging directory exists with proper permissions
if not exist "%STAGING_DIR%" (
    mkdir "%STAGING_DIR%" 2>nul
    if !errorlevel! neq 0 (
        echo  [ERROR] Cannot create staging directory: %STAGING_DIR%
        echo  Please check permissions or run as administrator.
        pause
        goto MAIN_MENU
    )
)

:TOGGLE_REFRESH
cls
call :SHOW_HEADER "ENABLE/DISABLE MODS"

:: First, clean up any duplicates
call :CLEANUP_DUPLICATES

echo  %BORDER_LINE%
echo   ENABLED MODS (in Community folder)
echo  %BORDER_LINE%
echo.

set "active_count=0"
for /d %%m in ("%COMMUNITY%\*") do (
    set /a active_count+=1
    set "active!active_count!=%%~nxm"
    set "padded=   !active_count!"
    set "padded=!padded:~-3!"
    echo   [!padded!] %%~nxm [ENABLED]
)

if %active_count% equ 0 echo   No active mods

echo.
echo  %BORDER_LINE%
echo   DISABLED MODS (in MSFS staging folder)
echo  %BORDER_LINE%
echo.

set "disabled_count=0"
:: Only show actual MSFS mods in staging
if exist "%STAGING_DIR%" (
    for /d %%m in ("%STAGING_DIR%\*") do (
        :: Check if it's actually a mod
        set "is_mod=no"
        if exist "%%m\manifest.json" set "is_mod=yes"
        if exist "%%m\layout.json" set "is_mod=yes"
        if exist "%%m\SimObjects" set "is_mod=yes"
        if exist "%%m\html_ui" set "is_mod=yes"
        if exist "%%m\Effects" set "is_mod=yes"
        if exist "%%m\ModelBehaviorDefs" set "is_mod=yes"
        if exist "%%m\scenery" set "is_mod=yes"
        
        if "!is_mod!"=="yes" (
            :: Check if this mod is NOT already in Community (avoid duplicates in list)
            if not exist "%COMMUNITY%\%%~nxm" (
                set /a disabled_count+=1
                set /a "num=%active_count%+!disabled_count!"
                set "disabled!disabled_count!=%%~nxm"
                set "padded=   !num!"
                set "padded=!padded:~-3!"
                echo   [!padded!] %%~nxm [DISABLED]
            )
        )
    )
)

if %disabled_count% equ 0 echo   No disabled mods

set /a "total_mods=%active_count%+%disabled_count%"
echo.
echo  %BORDER_LINE%
echo.
echo   [0] Back to menu
echo.
set /p "selection=%BULLET% Select mod to toggle (0-%total_mods%): "

if "%selection%"=="0" goto MAIN_MENU
if "%selection%"=="" goto MAIN_MENU

:: Validate input
set /a "test_num=%selection%" 2>nul
if %test_num% lss 1 goto TOGGLE_REFRESH
if %test_num% gtr %total_mods% goto TOGGLE_REFRESH

:: Process selection
if %test_num% leq %active_count% (
    :: Disable active mod
    set "mod_name=!active%selection%!"
    echo.
    echo  %BULLET% Disabling: !mod_name!
    
    :: Check if already exists in staging (cleanup duplicate)
    if exist "%STAGING_DIR%\!mod_name!" (
        echo  [INFO] Removing old duplicate from staging...
        rd /s /q "%STAGING_DIR%\!mod_name!" 2>nul
    )
    
    :: Use the safe move function
    call :SAFE_MOVE_MOD "%COMMUNITY%\!mod_name!" "%STAGING_DIR%\!mod_name!"
    
    if !errorlevel! equ 0 (
        echo  %CHECK% Mod disabled successfully
    ) else (
        echo  %FAIL% Failed to disable mod
    )
) else (
    :: Enable disabled mod
    set /a "disabled_index=%test_num%-%active_count%"
    set "mod_name=!disabled%disabled_index%!"
    echo.
    echo  %BULLET% Enabling: !mod_name!
    
    :: Use the safe move function
    call :SAFE_MOVE_MOD "%STAGING_DIR%\!mod_name!" "%COMMUNITY%\!mod_name!"
    
    if !errorlevel! equ 0 (
        echo  %CHECK% Mod enabled successfully
    ) else (
        echo  %FAIL% Failed to enable mod
    )
)

pause
goto TOGGLE_REFRESH

:: Cleanup function to remove duplicates
:CLEANUP_DUPLICATES
if not exist "%STAGING_DIR%" goto :eof

for /d %%m in ("%STAGING_DIR%\*") do (
    if exist "%COMMUNITY%\%%~nxm" (
        :: Found duplicate - mod exists in both locations
        echo  [CLEANUP] Found duplicate: %%~nxm
        echo  [CLEANUP] Removing from staging directory...
        rd /s /q "%%m" 2>nul
    )
)
goto :eof

:: -----------------------------
:: BATCH OPERATIONS (FIXED)
:: -----------------------------
:BATCH_OPERATIONS
cls
call :SHOW_HEADER "BATCH OPERATIONS"

echo  ============================================================================================
echo   BATCH OPERATIONS MENU
echo  ============================================================================================
echo.
echo   [1] Batch Disable Mods
echo   [2] Batch Enable Mods  
echo   [3] Batch Uninstall (with backup)
echo   [4] Batch Backup All Mods
echo   [5] Back to Main Menu
echo.
choice /C 12345 /N /M "Select operation (1-5): "

if %errorlevel%==5 goto MAIN_MENU
if %errorlevel%==4 goto BATCH_BACKUP_ALL
if %errorlevel%==3 goto BATCH_UNINSTALL_SAFE
if %errorlevel%==2 goto BATCH_ENABLE
if %errorlevel%==1 goto BATCH_DISABLE

:BATCH_DISABLE
cls
call :SHOW_HEADER "BATCH DISABLE MODS"

call :LOAD_CONFIG
call :SETUP_COMMUNITY
if not defined COMMUNITY (
    echo  [ERROR] No Community folder detected.
    pause
    goto BATCH_OPERATIONS
)

:: Ensure staging directory exists with proper permissions
if not exist "%STAGING_DIR%" (
    mkdir "%STAGING_DIR%" 2>nul
    if !errorlevel! neq 0 (
        echo  [ERROR] Cannot create staging directory: %STAGING_DIR%
        echo  Please check permissions or run as administrator.
        pause
        goto BATCH_OPERATIONS
    )
)

:: Check if any mods exist to disable
set "mod_exists=no"
for /d %%m in ("%COMMUNITY%\*") do set "mod_exists=yes"

if "%mod_exists%"=="no" (
    echo  No mods found in Community folder to disable.
    pause
    goto BATCH_OPERATIONS
)

echo  Select disable method:
echo.
echo   [1] Disable by type
echo   [2] Disable all mods
echo   [3] Select specific mods
echo   [4] Cancel
echo.
choice /C 1234 /N /M "Select method (1-4): "

if %errorlevel%==4 goto BATCH_OPERATIONS
if %errorlevel%==3 goto BATCH_DISABLE_SPECIFIC
if %errorlevel%==2 goto BATCH_DISABLE_ALL
if %errorlevel%==1 goto BATCH_DISABLE_TYPE

:BATCH_DISABLE_TYPE
echo.
echo  Select mod type to disable:
echo.
echo   [1] Aircraft
echo   [2] Scenery
echo   [3] Avionics
echo   [4] Livery
echo   [5] Cancel
echo.
choice /C 12345 /N /M "Select type (1-5): "

if %errorlevel%==5 goto BATCH_OPERATIONS
set "selected_type="
if %errorlevel%==1 set "selected_type=Aircraft"
if %errorlevel%==2 set "selected_type=Scenery"
if %errorlevel%==3 set "selected_type=Avionics"
if %errorlevel%==4 set "selected_type=Livery"

echo.
echo  Scanning for %selected_type% mods in: %COMMUNITY%
echo.

set "disabled_count=0"
set "failed_count=0"
for /d %%m in ("%COMMUNITY%\*") do (
    call :DETECT_MOD_TYPE "%%m"
    if "!mod_type!"=="%selected_type%" (
        echo  Disabling: %%~nxm
        call :SAFE_MOVE_MOD "%%m" "%STAGING_DIR%\%%~nxm"
        if !errorlevel! equ 0 (
            set /a disabled_count+=1
        ) else (
            set /a failed_count+=1
        )
    )
)

echo.
echo  [OK] Disabled %disabled_count% %selected_type% mod(s)
if %failed_count% gtr 0 echo  [WARNING] Failed to disable %failed_count% mod(s)
pause
goto BATCH_OPERATIONS

:BATCH_DISABLE_ALL
echo.
echo  This will disable ALL mods in your Community folder.
echo  Community folder: %COMMUNITY%
echo.
choice /C YN /N /M "Are you sure? (Y/N): "
if %errorlevel%==2 goto BATCH_OPERATIONS

echo.
set "disabled_count=0"
set "failed_count=0"
for /d %%m in ("%COMMUNITY%\*") do (
    echo  Disabling: %%~nxm
    call :SAFE_MOVE_MOD "%%m" "%STAGING_DIR%\%%~nxm"
    if !errorlevel! equ 0 (
        set /a disabled_count+=1
    ) else (
        set /a failed_count+=1
    )
)

echo.
echo  [OK] Disabled %disabled_count% mod(s)
if %failed_count% gtr 0 echo  [WARNING] Failed to disable %failed_count% mod(s)
pause
goto BATCH_OPERATIONS

:BATCH_DISABLE_SPECIFIC
cls
call :SHOW_HEADER "SELECT MODS TO DISABLE"

echo  Community folder: %COMMUNITY%
echo.
echo  Select mods to disable (space-separated numbers):
echo.
echo   [0] Cancel
echo.

:: List only directories in the Community folder
set "count=0"
pushd "%COMMUNITY%"
for /d %%m in (*) do (
    set /a count+=1
    set "mod!count!=%%m"
    set "padded=   !count!"
    set "padded=!padded:~-3!"
    echo   [!padded!] %%m
)
popd

if %count% equ 0 (
    echo  No mods found in Community folder.
    pause
    goto BATCH_OPERATIONS
)

echo.
set /p "selections=Enter selections (e.g., 1 3 5): "

if "%selections%"=="0" goto BATCH_OPERATIONS
if "%selections%"=="" goto BATCH_OPERATIONS

echo.
set "disabled_count=0"
set "failed_count=0"
for %%s in (%selections%) do (
    if defined mod%%s (
        echo  Disabling: !mod%%s!
        call :SAFE_MOVE_MOD "%COMMUNITY%\!mod%%s!" "%STAGING_DIR%\!mod%%s!"
        if !errorlevel! equ 0 (
            set /a disabled_count+=1
        ) else (
            set /a failed_count+=1
        )
    )
)

echo.
echo  [OK] Disabled %disabled_count% mod(s)
if %failed_count% gtr 0 echo  [WARNING] Failed to disable %failed_count% mod(s)
pause
goto BATCH_OPERATIONS

:: Safe move function with multiple fallback methods
:SAFE_MOVE_MOD
set "source=%~1"
set "dest=%~2"

:: Check if destination already exists
if exist "%dest%" (
    echo    [WARNING] Destination already exists. Checking for duplicates...
    
    :: Compare if they're the same mod
    set "is_duplicate=yes"
    
    :: If source still exists but destination also exists, we have a duplicate
    if exist "%source%" (
        echo    [INFO] Found duplicate mod. Removing from staging...
        rd /s /q "%source%" 2>nul
        if not exist "%source%" (
            echo    [OK] Duplicate removed from staging
            exit /b 0
        ) else (
            echo    [ERROR] Cannot remove duplicate. Please manually delete from: %source%
            exit /b 1
        )
    )
    exit /b 0
)

:: Method 1: Try robocopy (most reliable for large folders)
robocopy "%source%" "%dest%" /E /MOVE /R:2 /W:1 /NP /NJS /NJH /NDL /NC /NS >nul 2>&1

:: Check if source still exists
if not exist "%source%" (
    :: Success
    exit /b 0
)

:: Method 2: Try xcopy then delete
if not exist "%dest%" mkdir "%dest%"
xcopy "%source%\*" "%dest%\" /E /I /Y /Q >nul 2>&1

if !errorlevel! equ 0 (
    :: Try to remove source
    rd /s /q "%source%" 2>nul
    if not exist "%source%" (
        :: Success
        exit /b 0
    )
    
    :: If can't delete source, remove destination to avoid duplicates
    echo    [WARNING] Cannot remove source. Cleaning up to avoid duplicates...
    rd /s /q "%dest%" 2>nul
)

:: Method 3: Try PowerShell move
powershell -Command "Try { Move-Item -Path '%source%' -Destination '%dest%' -Force -ErrorAction Stop; exit 0 } Catch { exit 1 }" >nul 2>&1
if !errorlevel! equ 0 (
    exit /b 0
)

:: All methods failed
echo    [ERROR] Cannot move. Mod may be in use or protected.
exit /b 1

:BATCH_ENABLE
cls
call :SHOW_HEADER "BATCH ENABLE MODS"

call :LOAD_CONFIG
call :SETUP_COMMUNITY
if not defined COMMUNITY (
    echo  [ERROR] No Community folder detected.
    pause
    goto BATCH_OPERATIONS
)

:: Ensure staging directory exists
if not exist "%STAGING_DIR%" mkdir "%STAGING_DIR%"

echo  Staging directory: %STAGING_DIR%
echo  Community folder: %COMMUNITY%
echo.

:: Check if any MSFS mods exist in staging
set "mod_exists=no"
set "mod_count=0"
pushd "%STAGING_DIR%" 2>nul
for /d %%m in (*) do (
    :: Check if it's actually a mod
    set "is_mod=no"
    if exist "%%m\manifest.json" set "is_mod=yes"
    if exist "%%m\layout.json" set "is_mod=yes"
    if exist "%%m\SimObjects" set "is_mod=yes"
    if exist "%%m\html_ui" set "is_mod=yes"
    if exist "%%m\Effects" set "is_mod=yes"
    if exist "%%m\ModelBehaviorDefs" set "is_mod=yes"
    if exist "%%m\scenery" set "is_mod=yes"
    
    if "!is_mod!"=="yes" (
        set "mod_exists=yes"
        set /a mod_count+=1
    )
)
popd

if "%mod_exists%"=="no" (
    echo  No disabled MSFS mods found in staging directory.
    echo.
    echo  To disable mods first, use the "Batch Disable" option.
    pause
    goto BATCH_OPERATIONS
)

echo  Found %mod_count% disabled MSFS mod(s)
echo.
echo  Select enable method:
echo.
echo   [1] Enable all disabled mods
echo   [2] Enable by type
echo   [3] Select specific mods
echo   [4] Cancel
echo.
choice /C 1234 /N /M "Select method (1-4): "

if %errorlevel%==4 goto BATCH_OPERATIONS
if %errorlevel%==3 goto BATCH_ENABLE_SPECIFIC
if %errorlevel%==2 goto BATCH_ENABLE_TYPE
if %errorlevel%==1 goto BATCH_ENABLE_ALL

:BATCH_ENABLE_ALL
echo.
echo  Enabling all disabled MSFS mods...
echo.

set "enabled_count=0"
set "failed_count=0"
pushd "%STAGING_DIR%"
for /d %%m in (*) do (
    :: Check if it's actually a mod before enabling
    set "is_mod=no"
    if exist "%%m\manifest.json" set "is_mod=yes"
    if exist "%%m\layout.json" set "is_mod=yes"
    if exist "%%m\SimObjects" set "is_mod=yes"
    if exist "%%m\html_ui" set "is_mod=yes"
    if exist "%%m\Effects" set "is_mod=yes"
    if exist "%%m\ModelBehaviorDefs" set "is_mod=yes"
    if exist "%%m\scenery" set "is_mod=yes"
    
    if "!is_mod!"=="yes" (
        echo  Enabling: %%m
        call :SAFE_MOVE_MOD "%STAGING_DIR%\%%m" "%COMMUNITY%\%%m"
        if !errorlevel! equ 0 (
            set /a enabled_count+=1
        ) else (
            set /a failed_count+=1
        )
    )
)
popd

echo.
echo  [OK] Enabled %enabled_count% mod(s)
if %failed_count% gtr 0 echo  [WARNING] Failed to enable %failed_count% mod(s)
pause
goto BATCH_OPERATIONS

:BATCH_ENABLE_TYPE
echo.
echo  Select mod type to enable:
echo.
echo   [1] Aircraft
echo   [2] Scenery
echo   [3] Avionics
echo   [4] Livery
echo   [5] Cancel
echo.
choice /C 12345 /N /M "Select type (1-5): "

if %errorlevel%==5 goto BATCH_OPERATIONS
set "selected_type="
if %errorlevel%==1 set "selected_type=Aircraft"
if %errorlevel%==2 set "selected_type=Scenery"
if %errorlevel%==3 set "selected_type=Avionics"
if %errorlevel%==4 set "selected_type=Livery"

echo.
echo  Scanning for disabled %selected_type% mods...
echo.

set "enabled_count=0"
set "failed_count=0"
pushd "%STAGING_DIR%"
for /d %%m in (*) do (
    :: Check if it's actually a mod
    set "is_mod=no"
    if exist "%%m\manifest.json" set "is_mod=yes"
    if exist "%%m\layout.json" set "is_mod=yes"
    if exist "%%m\SimObjects" set "is_mod=yes"
    if exist "%%m\html_ui" set "is_mod=yes"
    if exist "%%m\Effects" set "is_mod=yes"
    if exist "%%m\ModelBehaviorDefs" set "is_mod=yes"
    if exist "%%m\scenery" set "is_mod=yes"
    
    if "!is_mod!"=="yes" (
        call :DETECT_MOD_TYPE "%STAGING_DIR%\%%m"
        if "!mod_type!"=="%selected_type%" (
            echo  Enabling: %%m
            call :SAFE_MOVE_MOD "%STAGING_DIR%\%%m" "%COMMUNITY%\%%m"
            if !errorlevel! equ 0 (
                set /a enabled_count+=1
            ) else (
                set /a failed_count+=1
            )
        )
    )
)
popd

echo.
echo  [OK] Enabled %enabled_count% %selected_type% mod(s)
if %failed_count% gtr 0 echo  [WARNING] Failed to enable %failed_count% mod(s)
pause
goto BATCH_OPERATIONS

:BATCH_ENABLE_SPECIFIC
cls
call :SHOW_HEADER "SELECT MODS TO ENABLE"

echo  Disabled MSFS mods in staging directory:
echo  %STAGING_DIR%
echo.
echo  Select mods to enable (space-separated numbers):
echo.
echo   [0] Cancel
echo.

:: List only actual MSFS mods
set "count=0"
pushd "%STAGING_DIR%"
for /d %%m in (*) do (
    :: Check if it's actually a mod
    set "is_mod=no"
    if exist "%%m\manifest.json" set "is_mod=yes"
    if exist "%%m\layout.json" set "is_mod=yes"
    if exist "%%m\SimObjects" set "is_mod=yes"
    if exist "%%m\html_ui" set "is_mod=yes"
    if exist "%%m\Effects" set "is_mod=yes"
    if exist "%%m\ModelBehaviorDefs" set "is_mod=yes"
    if exist "%%m\scenery" set "is_mod=yes"
    
    if "!is_mod!"=="yes" (
        set /a count+=1
        set "disabled_mod!count!=%%m"
        set "padded=   !count!"
        set "padded=!padded:~-3!"
        
        :: Get mod type
        call :DETECT_MOD_TYPE "%STAGING_DIR%\%%m"
        
        :: Get size
        set "folder_path=%STAGING_DIR%\%%m"
        call :GET_FOLDER_SIZE
        set /a "size_mb=!folder_size!/1048576"
        
        echo   [!padded!] %%m [!mod_type!] (~!size_mb! MB^)
    )
)
popd

if %count% equ 0 (
    echo  No disabled MSFS mods found.
    echo.
    echo  To disable mods first, use the "Batch Disable" option.
    pause
    goto BATCH_OPERATIONS
)

echo.
echo  Total disabled MSFS mods: %count%
echo.
set /p "selections=Enter selections (e.g., 1 3 5): "

if "%selections%"=="0" goto BATCH_OPERATIONS
if "%selections%"=="" goto BATCH_OPERATIONS

:: Confirm selection
echo.
echo  Selected mods to enable:
for %%s in (%selections%) do (
    if defined disabled_mod%%s echo   - !disabled_mod%%s!
)

echo.
choice /C YN /N /M "Enable selected mods? (Y/N): "
if %errorlevel%==2 goto BATCH_OPERATIONS

echo.
set "enabled_count=0"
set "failed_count=0"
for %%s in (%selections%) do (
    if defined disabled_mod%%s (
        echo  Enabling: !disabled_mod%%s!
        call :SAFE_MOVE_MOD "%STAGING_DIR%\!disabled_mod%%s!" "%COMMUNITY%\!disabled_mod%%s!"
        if !errorlevel! equ 0 (
            set /a enabled_count+=1
            echo  [OK] Enabled
        ) else (
            set /a failed_count+=1
            echo  [ERROR] Failed
        )
    )
)

echo.
echo  ============================================================================================
echo.
echo  Successfully enabled %enabled_count% mod(s)
if %failed_count% gtr 0 echo  Failed to enable %failed_count% mod(s)
echo.
pause
goto BATCH_OPERATIONS

:BATCH_UNINSTALL_SAFE
cls
call :SHOW_HEADER "BATCH UNINSTALL"

call :LOAD_CONFIG
call :SETUP_COMMUNITY
if not defined COMMUNITY (
    echo  [ERROR] No Community folder detected.
    pause
    goto BATCH_OPERATIONS
)

echo  Community folder: %COMMUNITY%
echo.

:: Check if any mods exist
set "mod_exists=no"
for /d %%m in ("%COMMUNITY%\*") do set "mod_exists=yes"

if "%mod_exists%"=="no" (
    echo  No mods found in Community folder.
    pause
    goto BATCH_OPERATIONS
)

echo  Select mods to uninstall (space-separated numbers):
echo.
echo   [0] Cancel
echo.

:: List only directories in the Community folder
set "count=0"
pushd "%COMMUNITY%"
for /d %%m in (*) do (
    set /a count+=1
    set "mod!count!=%%m"
    set "mod_path!count!=%COMMUNITY%\%%m"
    set "padded=   !count!"
    set "padded=!padded:~-3!"
    
    :: Get size
    set "folder_path=%COMMUNITY%\%%m"
    call :GET_FOLDER_SIZE
    set /a "size_mb=!folder_size!/1048576"
    
    echo   [!padded!] %%m (~!size_mb! MB^)
)
popd

echo.
set /p "selections=Enter selections (e.g., 1 3 5): "

if "%selections%"=="0" goto BATCH_OPERATIONS
if "%selections%"=="" goto BATCH_OPERATIONS

:: Confirm selection
echo.
echo  Selected mods for uninstall:
for %%s in (%selections%) do (
    if defined mod%%s echo   - !mod%%s!
)

echo.
choice /C YN /N /M "Uninstall selected mods? (Y/N): "
if %errorlevel%==2 goto BATCH_OPERATIONS

:: Create batch backup
call :GET_TIMESTAMP
set "batch_backup_dir=%DEFAULT_BACKUP_DIR%\BatchUninstall_%TIMESTAMP%"
if not exist "%DEFAULT_BACKUP_DIR%" mkdir "%DEFAULT_BACKUP_DIR%"
mkdir "%batch_backup_dir%" 2>nul

echo.
echo  Creating backup before uninstall...
echo.

set "uninstalled_count=0"
for %%s in (%selections%) do (
    if defined mod%%s (
        echo  Processing: !mod%%s!
        
        :: Backup
        mkdir "%batch_backup_dir%\!mod%%s!" 2>nul
        xcopy "!mod_path%%s!" "%batch_backup_dir%\!mod%%s!\" /E /I /Y /Q >nul
        
        :: Uninstall
        rd /s /q "!mod_path%%s!" 2>nul
        if !errorlevel! equ 0 (
            echo  [OK] Uninstalled
            set /a uninstalled_count+=1
        ) else (
            echo  [ERROR] Failed
        )
    )
)

echo.
echo  ============================================================================================
echo.
echo  Uninstalled: %uninstalled_count% mod(s)
echo  Backup location: %batch_backup_dir%
echo.
pause
goto BATCH_OPERATIONS

:BATCH_BACKUP_ALL
cls
call :SHOW_HEADER "BACKUP ALL MODS"

call :LOAD_CONFIG
call :SETUP_COMMUNITY
if not defined COMMUNITY (
    echo  [ERROR] No Community folder detected.
    pause
    goto BATCH_OPERATIONS
)

echo  Community folder: %COMMUNITY%
echo.

:: Check if any mods exist
set "mod_count=0"
for /d %%m in ("%COMMUNITY%\*") do set /a mod_count+=1

if %mod_count% equ 0 (
    echo  No mods found in Community folder.
    pause
    goto BATCH_OPERATIONS
)

:: Calculate size
echo  Calculating total size of %mod_count% mods...
set "folder_path=%COMMUNITY%"
call :GET_FOLDER_SIZE
set /a "size_mb=%folder_size%/1048576"

echo.
echo  Total size: ~%size_mb% MB
echo.
choice /C YN /N /M "Create full backup? (Y/N): "
if %errorlevel%==2 goto BATCH_OPERATIONS

:: Create backup
call :GET_TIMESTAMP
set "full_backup=%DEFAULT_BACKUP_DIR%\FullBackup_%TIMESTAMP%"
if not exist "%DEFAULT_BACKUP_DIR%" mkdir "%DEFAULT_BACKUP_DIR%"
mkdir "%full_backup%" 2>nul

echo.
echo  Backing up to: %full_backup%
echo.

set "backed_up=0"
pushd "%COMMUNITY%"
for /d %%m in (*) do (
    echo  Backing up: %%m
    xcopy "%%m" "%full_backup%\%%m\" /E /I /Y /Q >nul
    if !errorlevel! equ 0 set /a backed_up+=1
)
popd

:: Create info file
(
    echo MSFS Full Backup
    echo ================
    echo Date: %DATE% %TIME%
    echo Mods: %backed_up%
    echo Size: ~%size_mb% MB
) > "%full_backup%\backup_info.txt"

echo.
echo  [OK] Backed up %backed_up% mod(s)
pause
goto BATCH_OPERATIONS

:: -----------------------------
:: BACKUP MANAGER (IMPROVED)
:: -----------------------------
:BACKUP_MANAGER
cls
call :SHOW_HEADER "BACKUP MANAGER"

echo  ============================================================================================
echo   BACKUP OPTIONS
echo  ============================================================================================
echo.
echo   [1] View all backups
echo   [2] Create mod backup
echo   [3] Create full backup
echo   [4] Restore from backup
echo   [5] Delete old backups
echo   [6] Open backup folder
echo   [7] Back to main menu
echo.
choice /C 1234567 /N /M "Select option (1-7): "

if %errorlevel%==7 goto MAIN_MENU
if %errorlevel%==6 goto OPEN_BACKUP_FOLDER
if %errorlevel%==5 goto DELETE_OLD_BACKUPS
if %errorlevel%==4 goto RESTORE_BACKUP
if %errorlevel%==3 goto CREATE_FULL_BACKUP
if %errorlevel%==2 goto CREATE_MOD_BACKUP
if %errorlevel%==1 goto VIEW_ALL_BACKUPS

:VIEW_ALL_BACKUPS
cls
call :SHOW_HEADER "ALL BACKUPS"

echo  Backup Directory: %DEFAULT_BACKUP_DIR%
echo.

if not exist "%DEFAULT_BACKUP_DIR%\*" (
    echo  No backups found.
    pause
    goto BACKUP_MANAGER
)

echo  ============================================================================================
echo   BACKUP LIST
echo  ============================================================================================
echo.

set "backup_count=0"
for /d %%b in ("%DEFAULT_BACKUP_DIR%\*") do (
    set /a backup_count+=1
    
    :: Get backup info
    set "backup_type=Mod Backup"
    if exist "%%b\backup_info.txt" set "backup_type=Full Backup"
    
    :: Get folder size
    set "folder_path=%%b"
    call :GET_FOLDER_SIZE
    set /a "size_mb=!folder_size!/1048576"
    
    echo   [!backup_count!] %%~nxb
    echo       Type: !backup_type!
    echo       Size: ~!size_mb! MB
    echo       Date: %%~tb
    echo.
)

echo  Total backups: %backup_count%
echo.
pause
goto BACKUP_MANAGER

:CREATE_MOD_BACKUP
cls
call :SHOW_HEADER "CREATE MOD BACKUP"

call :LOAD_CONFIG
call :SETUP_COMMUNITY
if not defined COMMUNITY (
    echo  [ERROR] No Community folder detected.
    pause
    goto BACKUP_MANAGER
)

echo  Select mod to backup:
echo.
echo   [0] Cancel
echo.

set "count=0"
for /d %%m in ("%COMMUNITY%\*") do (
    set /a count+=1
    set "mod!count!=%%~nxm"
    set "mod_path!count!=%%m"
    set "padded=   !count!"
    set "padded=!padded:~-3!"
    echo   [!padded!] %%~nxm
)

echo.
set /p "selection=Select mod (0-%count%): "

if "%selection%"=="0" goto BACKUP_MANAGER
if "%selection%"=="" goto BACKUP_MANAGER

:: Validate selection
set /a "test_num=%selection%" 2>nul
if %test_num% lss 1 goto BACKUP_MANAGER
if %test_num% gtr %count% goto BACKUP_MANAGER

set "selected_mod=!mod%selection%!"
set "selected_path=!mod_path%selection%!"

:: Create backup
call :GET_TIMESTAMP
set "backup_dir=%DEFAULT_BACKUP_DIR%\!selected_mod!_backup_%TIMESTAMP%"

echo.
echo  Creating backup of: !selected_mod!
mkdir "%backup_dir%" 2>nul

xcopy "!selected_path!" "%backup_dir%\" /E /I /Y /Q >nul

if !errorlevel! equ 0 (
    echo  [OK] Backup created successfully
    echo  Location: %backup_dir%
) else (
    echo  [ERROR] Backup failed
)

pause
goto BACKUP_MANAGER

:CREATE_FULL_BACKUP
cls
call :SHOW_HEADER "CREATE FULL BACKUP"

call :LOAD_CONFIG
call :SETUP_COMMUNITY
if not defined COMMUNITY (
    echo  [ERROR] No Community folder detected.
    pause
    goto BACKUP_MANAGER
)

:: Count mods
set "mod_count=0"
for /d %%m in ("%COMMUNITY%\*") do set /a mod_count+=1

echo  This will backup all %mod_count% mods in your Community folder.
echo.

:: Calculate size
echo  Calculating total size...
set "folder_path=%COMMUNITY%"
call :GET_FOLDER_SIZE
set /a "size_mb=%folder_size%/1048576"

echo  Total size: ~%size_mb% MB
echo.
choice /C YN /N /M "Continue? (Y/N): "
if %errorlevel%==2 goto BACKUP_MANAGER

:: Create backup
call :GET_TIMESTAMP
set "full_backup=%DEFAULT_BACKUP_DIR%\FullBackup_%TIMESTAMP%"
mkdir "%full_backup%" 2>nul

echo.
echo  Creating backup...
echo  This may take several minutes...
echo.

:: Progress counter
set "backed_up=0"
for /d %%m in ("%COMMUNITY%\*") do (
    set /a backed_up+=1
    set /a "percent=!backed_up!*100/%mod_count%"
    echo  [!percent!%%] Backing up: %%~nxm
    xcopy "%%m" "%full_backup%\%%~nxm\" /E /I /Y /Q >nul
)

:: Create backup info
(
    echo MSFS Full Community Backup
    echo =========================
    echo Created: %DATE% %TIME%
    echo Source: %COMMUNITY%
    echo Total Mods: %mod_count%
    echo Total Size: ~%size_mb% MB
) > "%full_backup%\backup_info.txt"

echo.
echo  ============================================================================================
echo.
echo  [OK] Full backup completed!
echo  Location: %full_backup%
echo  Mods backed up: %backed_up%
echo.
pause
goto BACKUP_MANAGER

:RESTORE_BACKUP
cls
call :SHOW_HEADER "RESTORE FROM BACKUP"

if not exist "%DEFAULT_BACKUP_DIR%\*" (
    echo  No backups found.
    pause
    goto BACKUP_MANAGER
)

echo  Select backup to restore:
echo.
echo   [0] Cancel
echo.

set "backup_count=0"
for /d %%b in ("%DEFAULT_BACKUP_DIR%\*") do (
    set /a backup_count+=1
    set "backup!backup_count!=%%b"
    set "backup_name!backup_count!=%%~nxb"
    
    set "backup_type=Mod"
    if exist "%%b\backup_info.txt" set "backup_type=Full"
    
    set "padded=   !backup_count!"
    set "padded=!padded:~-3!"
    echo   [!padded!] %%~nxb [!backup_type!]
)

echo.
set /p "selection=Select backup (0-%backup_count%): "

if "%selection%"=="0" goto BACKUP_MANAGER
if "%selection%"=="" goto BACKUP_MANAGER

:: Validate selection
set /a "test_num=%selection%" 2>nul
if %test_num% lss 1 goto BACKUP_MANAGER
if %test_num% gtr %backup_count% goto BACKUP_MANAGER

set "selected_backup=!backup%selection%!"
set "selected_name=!backup_name%selection%!"

echo.
echo  Selected: !selected_name!
echo.
echo  [WARNING] This will restore the backup to your Community folder.
echo  Existing mods may be overwritten!
echo.
choice /C YN /N /M "Continue? (Y/N): "
if %errorlevel%==2 goto BACKUP_MANAGER

echo.
echo  Restoring...

:: Check if full backup
if exist "!selected_backup!\backup_info.txt" (
    :: Full backup - restore all
    for /d %%m in ("!selected_backup!\*") do (
        if not "%%~nxm"=="backup_info.txt" (
            echo  Restoring: %%~nxm
            xcopy "%%m" "%COMMUNITY%\%%~nxm\" /E /I /Y /Q >nul
        )
    )
) else (
    :: Single mod backup
    :: Extract mod name from backup folder name
    set "mod_name=!selected_name!"
    set "mod_name=!mod_name:_backup_=|!"
    for /f "tokens=1 delims=|" %%n in ("!mod_name!") do set "mod_name=%%n"
    
    echo  Restoring mod: !mod_name!
    xcopy "!selected_backup!" "%COMMUNITY%\!mod_name!\" /E /I /Y /Q >nul
)

echo.
echo  [OK] Restore completed!
pause
goto BACKUP_MANAGER

:DELETE_OLD_BACKUPS
cls
call :SHOW_HEADER "DELETE OLD BACKUPS"

echo  Select deletion criteria:
echo.
echo   [1] Delete backups older than 7 days
echo   [2] Delete backups older than 30 days
echo   [3] Delete specific backups
echo   [4] Cancel
echo.
choice /C 1234 /N /M "Select option (1-4): "

if %errorlevel%==4 goto BACKUP_MANAGER
if %errorlevel%==3 goto DELETE_SPECIFIC_BACKUPS
if %errorlevel%==2 set "days=30"
if %errorlevel%==1 set "days=7"

echo.
echo  This will delete backups older than %days% days.
choice /C YN /N /M "Continue? (Y/N): "
if %errorlevel%==2 goto BACKUP_MANAGER

echo.
echo  Scanning for old backups...

:: Use forfiles to find and delete old backups
set "deleted=0"
forfiles /P "%DEFAULT_BACKUP_DIR%" /D -%days% /C "cmd /c if @isdir==TRUE echo Deleting: @file && rd /s /q @path && set /a deleted+=1" 2>nul

echo.
echo  [OK] Cleanup complete
pause
goto BACKUP_MANAGER

:DELETE_SPECIFIC_BACKUPS
cls
call :SHOW_HEADER "DELETE SPECIFIC BACKUPS"

echo  Select backups to delete (space-separated numbers):
echo.
echo   [0] Cancel
echo.

set "backup_count=0"
for /d %%b in ("%DEFAULT_BACKUP_DIR%\*") do (
    set /a backup_count+=1
    set "del_backup!backup_count!=%%b"
    set "padded=   !backup_count!"
    set "padded=!padded:~-3!"
    echo   [!padded!] %%~nxb
)

echo.
set /p "selections=Enter selections: "

if "%selections%"=="0" goto BACKUP_MANAGER
if "%selections%"=="" goto BACKUP_MANAGER

echo.
echo  Selected for deletion:
for %%s in (%selections%) do (
    if defined del_backup%%s (
        for %%b in ("!del_backup%%s!") do echo   - %%~nxb
    )
)

echo.
choice /C YN /N /M "Delete selected backups? (Y/N): "
if %errorlevel%==2 goto BACKUP_MANAGER

echo.
for %%s in (%selections%) do (
    if defined del_backup%%s (
        for %%b in ("!del_backup%%s!") do echo  Deleting: %%~nxb
        rd /s /q "!del_backup%%s!" 2>nul
    )
)

echo.
echo  [OK] Deletion complete
pause
goto BACKUP_MANAGER

:OPEN_BACKUP_FOLDER
if exist "%DEFAULT_BACKUP_DIR%" (
    start "" "%DEFAULT_BACKUP_DIR%"
) else (
    mkdir "%DEFAULT_BACKUP_DIR%"
    start "" "%DEFAULT_BACKUP_DIR%"
)
goto BACKUP_MANAGER

:: -----------------------------
:: HEALTH CHECK PLUS (IMPROVED)
:: -----------------------------
:HEALTH_CHECK_PLUS
cls
call :SHOW_HEADER "MOD HEALTH CHECK PLUS"

call :LOAD_CONFIG
call :SETUP_COMMUNITY
if not defined COMMUNITY (
    echo  [ERROR] No Community folder detected.
    pause
    goto MAIN_MENU
)

echo  Performing comprehensive health check...
echo.

set "issues=0"
set "warnings=0"
set "healthy=0"
set "report=%APPDATA%\MSFS_ModInstaller\health_report.txt"

echo Health Check Report > "%report%"
echo Generated: %DATE% %TIME% >> "%report%"
echo. >> "%report%"

echo  ============================================================================================
echo   SCAN RESULTS
echo  ============================================================================================
echo.

for /d %%m in ("%COMMUNITY%\*") do (
    call :CHECK_MOD_HEALTH_DETAILED "%%m" "%%~nxm"
)

echo  ============================================================================================
echo   SUMMARY
echo  ============================================================================================
echo.
echo   [OK] Healthy mods: %healthy%
echo   [WARNING] Warnings: %warnings%
echo   [ERROR] Critical issues: %issues%
echo.
echo   Detailed report saved to:
echo   %report%
echo.

if %issues% gtr 0 (
    echo   RECOMMENDATION: Some mods have critical issues and should be reinstalled.
    echo.
)

pause
goto MAIN_MENU

:CHECK_MOD_HEALTH_DETAILED
set "mod_path=%~1"
set "mod_name=%~2"
set "health_status=HEALTHY"
set "issues_found="

:: Check 1: Valid mod structure
set "has_valid_structure=no"
if exist "%mod_path%\manifest.json" set "has_valid_structure=yes"
if exist "%mod_path%\layout.json" set "has_valid_structure=yes"
if exist "%mod_path%\SimObjects" set "has_valid_structure=yes"
if exist "%mod_path%\html_ui" set "has_valid_structure=yes"
if exist "%mod_path%\scenery" set "has_valid_structure=yes"
if exist "%mod_path%\Effects" set "has_valid_structure=yes"
if exist "%mod_path%\ModelBehaviorDefs" set "has_valid_structure=yes"

if "!has_valid_structure!"=="no" (
    set "health_status=ERROR"
    set "issues_found=!issues_found! - No valid mod structure detected"
    set /a issues+=1
)

:: Check 2: Empty folder
dir /b "%mod_path%" 2>nul | findstr "^" >nul
if %errorlevel% neq 0 (
    set "health_status=WARNING"
    set "issues_found=!issues_found! - Empty folder"
    set /a warnings+=1
)

:: Check 3: Aircraft specific checks
if exist "%mod_path%\SimObjects\Airplanes" (
    if not exist "%mod_path%\manifest.json" (
        set "health_status=WARNING"
        set "issues_found=!issues_found! - Aircraft missing manifest.json"
        set /a warnings+=1
    )
    
    :: Check for aircraft.cfg
    set "has_aircraft_cfg=no"
    for /d %%a in ("%mod_path%\SimObjects\Airplanes\*") do (
        if exist "%%a\aircraft.cfg" set "has_aircraft_cfg=yes"
    )
    if "!has_aircraft_cfg!"=="no" (
        set "health_status=ERROR"
        set "issues_found=!issues_found! - No aircraft.cfg found"
        set /a issues+=1
    )
)

:: Check 4: Scenery specific checks
if exist "%mod_path%\scenery" (
    if not exist "%mod_path%\scenery\*.bgl" (
        set "health_status=WARNING"
        set "issues_found=!issues_found! - Scenery folder missing BGL files"
        set /a warnings+=1
    )
)

:: Check 5: File permissions (try to create temp file)
echo test > "%mod_path%\~health_check.tmp" 2>nul
if exist "%mod_path%\~health_check.tmp" (
    del "%mod_path%\~health_check.tmp" 2>nul
) else (
    set "health_status=WARNING"
    set "issues_found=!issues_found! - Possible permission issues"
    set /a warnings+=1
)

:: Display and log results
if "!health_status!"=="HEALTHY" (
    set /a healthy+=1
    echo   [OK] !mod_name!
    echo [OK] !mod_name! >> "%report%"
) else (
    echo   [!health_status!] !mod_name!
    echo [!health_status!] !mod_name! >> "%report%"
    if defined issues_found (
        echo       Issues:!issues_found!
        echo     Issues:!issues_found! >> "%report%"
    )
)

echo. >> "%report%"
goto :eof

:: -----------------------------
:: FIND DUPLICATES (FIXED)
:: -----------------------------
:FIND_DUPLICATES
cls
call :SHOW_HEADER "FIND DUPLICATE FILES"

call :LOAD_CONFIG
call :SETUP_COMMUNITY
if not defined COMMUNITY (
    echo  [ERROR] No Community folder detected.
    pause
    goto MAIN_MENU
)

echo  This will scan for duplicate files across all mods.
echo  Note: This may take several minutes for large collections.
echo.
choice /C YN /N /M "Continue? (Y/N): "
if %errorlevel%==2 goto MAIN_MENU

echo.
echo  Scanning for duplicates...

set "dup_report=%APPDATA%\MSFS_ModInstaller\duplicates_report.txt"
set "temp_file=%TEMP%\msfs_dup_scan.tmp"

echo Duplicate Files Report > "%dup_report%"
echo Generated: %DATE% %TIME% >> "%dup_report%"
echo. >> "%dup_report%"

:: Create file list with sizes
echo  Phase 1: Building file index...
del "%temp_file%" 2>nul

for /d %%m in ("%COMMUNITY%\*") do (
    echo   Scanning: %%~nxm
    for /r "%%m" %%f in (*) do (
        if exist "%%f" (
            :: Get relative path
            set "full_path=%%f"
            set "rel_path=!full_path:%%m=!"
            
            :: Store: filename|size|mod|fullpath
            echo %%~nxf^|%%~zf^|%%~nxm^|%%f >> "%temp_file%"
        )
    )
)

echo.
echo  Phase 2: Analyzing for duplicates...

:: Sort by filename and size
sort "%temp_file%" > "%temp_file%.sorted"

:: Find duplicates
set "dup_count=0"
set "last_key="
set "last_mod="

for /f "tokens=1,2,3,4 delims=|" %%a in (%temp_file%.sorted) do (
    set "current_key=%%a|%%b"
    set "current_mod=%%c"
    set "current_path=%%d"
    
    if "!current_key!"=="!last_key!" if not "!current_mod!"=="!last_mod!" (
        if not defined dup_group_!dup_count! (
            set /a dup_count+=1
            echo. >> "%dup_report%"
            echo Duplicate #!dup_count!: %%a (Size: %%b bytes) >> "%dup_report%"
            echo   In mod: !last_mod! >> "%dup_report%"
            set "dup_group_!dup_count!=1"
        )
        echo   In mod: !current_mod! >> "%dup_report%"
    )
    
    set "last_key=!current_key!"
    set "last_mod=!current_mod!"
)

:: Cleanup
del "%temp_file%" 2>nul
del "%temp_file%.sorted" 2>nul

echo.
echo  ============================================================================================
echo   SCAN COMPLETE
echo  ============================================================================================
echo.
echo   Duplicate groups found: %dup_count%
echo.

if %dup_count% gtr 0 (
    echo   Report saved to:
    echo   %dup_report%
    echo.
    choice /C YN /N /M "Open report? (Y/N): "
    if !errorlevel!==1 start "" "%dup_report%"
) else (
    echo   No duplicate files found across mods.
)

echo.
pause
goto MAIN_MENU

:: -----------------------------
:: PROFILE MANAGEMENT (FIXED)
:: -----------------------------
:PROFILE_MANAGEMENT
cls
call :SHOW_HEADER "PROFILE MANAGEMENT"

echo  ============================================================================================
echo   PROFILE OPTIONS
echo  ============================================================================================
echo.
echo   [1] Create New Profile
echo   [2] Load Profile
echo   [3] Delete Profile
echo   [4] Export Profile
echo   [5] Import Profile
echo   [6] Back to Main Menu
echo.
choice /C 123456 /N /M "Select option (1-6): "

if %errorlevel%==6 goto MAIN_MENU
if %errorlevel%==5 goto IMPORT_PROFILE
if %errorlevel%==4 goto EXPORT_PROFILE
if %errorlevel%==3 goto DELETE_PROFILE
if %errorlevel%==2 goto LOAD_PROFILE
if %errorlevel%==1 goto CREATE_PROFILE

:CREATE_PROFILE
echo.
set /p "profile_name=Enter profile name: "

if "%profile_name%"=="" (
    echo [ERROR] Profile name cannot be empty
    pause
    goto PROFILE_MANAGEMENT
)

:: Remove invalid characters
set "profile_name=%profile_name:\=%"
set "profile_name=%profile_name:/=%"
set "profile_name=%profile_name::=%"
set "profile_name=%profile_name:*=%"
set "profile_name=%profile_name:?=%"
set "profile_name=%profile_name:<=%"
set "profile_name=%profile_name:>=%"
set "profile_name=%profile_name:|=%"

set "profile_file=%PROFILES_DIR%\%profile_name%.profile"

echo.
echo  Creating profile: %profile_name%

:: Save current state
(
    echo # MSFS Mod Profile
    echo # Name: %profile_name%
    echo # Created: %DATE% %TIME%
    echo.
    echo [ENABLED]
    for /d %%m in ("%COMMUNITY%\*") do echo %%~nxm
    echo.
    echo [DISABLED]
    for /d %%m in ("%STAGING_DIR%\*") do echo %%~nxm
) > "%profile_file%"

echo  [OK] Profile saved
pause
goto PROFILE_MANAGEMENT

:LOAD_PROFILE
cls
call :SHOW_HEADER "LOAD PROFILE"

if not exist "%PROFILES_DIR%\*.profile" (
    echo  No profiles found.
    pause
    goto PROFILE_MANAGEMENT
)

echo  Available profiles:
echo.

set "count=0"
for %%f in ("%PROFILES_DIR%\*.profile") do (
    set /a count+=1
    set "profile!count!=%%~nf"
    set "profile_file!count!=%%f"
    echo   [!count!] %%~nf
)

echo.
echo   [0] Cancel
echo.
set /p "selection=Select profile (0-%count%): "

if "%selection%"=="0" goto PROFILE_MANAGEMENT
if "%selection%"=="" goto PROFILE_MANAGEMENT

:: Validate selection
set /a "test_num=%selection%" 2>nul
if %test_num% lss 1 goto PROFILE_MANAGEMENT
if %test_num% gtr %count% goto PROFILE_MANAGEMENT

set "selected_profile=!profile%selection%!"
set "selected_file=!profile_file%selection%!"

echo.
echo  Loading profile: !selected_profile!
echo  This will rearrange your mods to match the profile.
choice /C YN /N /M "Continue? (Y/N): "
if %errorlevel%==2 goto PROFILE_MANAGEMENT

echo.

:: First, move all to staging
echo  Preparing mods...
for /d %%m in ("%COMMUNITY%\*") do (
    move "%%m" "%STAGING_DIR%\" >nul 2>&1
)

:: Load profile
set "section="
for /f "tokens=*" %%l in (%selected_file%) do (
    set "line=%%l"
    
    if "!line!"=="[ENABLED]" set "section=enabled"
    if "!line!"=="[DISABLED]" set "section=disabled"
    
    if not "!line:~0,1!"=="#" if not "!line:~0,1!"=="[" if not "!line!"=="" (
        if "!section!"=="enabled" (
            if exist "%STAGING_DIR%\!line!" (
                echo  Enabling: !line!
                move "%STAGING_DIR%\!line!" "%COMMUNITY%\" >nul 2>&1
            )
        )
    )
)

echo.
echo  [OK] Profile loaded
pause
goto PROFILE_MANAGEMENT

:DELETE_PROFILE
cls
call :SHOW_HEADER "DELETE PROFILE"

if not exist "%PROFILES_DIR%\*.profile" (
    echo  No profiles found.
    pause
    goto PROFILE_MANAGEMENT
)

echo  Select profile to delete:
echo.

set "count=0"
for %%f in ("%PROFILES_DIR%\*.profile") do (
    set /a count+=1
    set "del_profile!count!=%%f"
    echo   [!count!] %%~nf
)

echo.
echo   [0] Cancel
echo.
set /p "selection=Select profile (0-%count%): "

if "%selection%"=="0" goto PROFILE_MANAGEMENT
if "%selection%"=="" goto PROFILE_MANAGEMENT

:: Validate selection
set /a "test_num=%selection%" 2>nul
if %test_num% lss 1 goto PROFILE_MANAGEMENT
if %test_num% gtr %count% goto PROFILE_MANAGEMENT

set "selected_file=!del_profile%selection%!"

echo.
choice /C YN /N /M "Delete this profile? (Y/N): "
if %errorlevel%==2 goto PROFILE_MANAGEMENT

del "!selected_file!" 2>nul
echo  [OK] Profile deleted
pause
goto PROFILE_MANAGEMENT

:EXPORT_PROFILE
cls
call :SHOW_HEADER "EXPORT PROFILE"

if not exist "%PROFILES_DIR%\*.profile" (
    echo  No profiles found.
    pause
    goto PROFILE_MANAGEMENT
)

echo  Select profile to export:
echo.

set "count=0"
for %%f in ("%PROFILES_DIR%\*.profile") do (
    set /a count+=1
    set "exp_profile!count!=%%f"
    echo   [!count!] %%~nf
)

echo.
echo   [0] Cancel
echo.
set /p "selection=Select profile (0-%count%): "

if "%selection%"=="0" goto PROFILE_MANAGEMENT
if "%selection%"=="" goto PROFILE_MANAGEMENT

:: Validate selection
set /a "test_num=%selection%" 2>nul
if %test_num% lss 1 goto PROFILE_MANAGEMENT
if %test_num% gtr %count% goto PROFILE_MANAGEMENT

set "source_file=!exp_profile%selection%!"
set "export_file=%USERPROFILE%\Desktop\MSFS_Profile_Export.profile"

copy "!source_file!" "!export_file!" >nul
echo.
echo  [OK] Profile exported to Desktop
pause
goto PROFILE_MANAGEMENT

:IMPORT_PROFILE
echo.
set /p "import_file=Enter path to profile file: "
set "import_file=!import_file:"=!"

if not exist "!import_file!" (
    echo  [ERROR] File not found
    pause
    goto PROFILE_MANAGEMENT
)

:: Extract filename
for %%f in ("!import_file!") do set "profile_name=%%~nf"

:: Copy to profiles directory
copy "!import_file!" "%PROFILES_DIR%\!profile_name!.profile" >nul

echo.
echo  [OK] Profile imported: !profile_name!
pause
goto PROFILE_MANAGEMENT

:: -----------------------------
:: UNINSTALL (FIXED)
:: -----------------------------
:UNINSTALL
cls
call :SHOW_HEADER "MOD UNINSTALLATION"

call :LOAD_CONFIG
call :SETUP_COMMUNITY
if not defined COMMUNITY (
    echo  [ERROR] No Community folder detected.
    pause
    goto MAIN_MENU
)

echo  [OK] Community Folder: %COMMUNITY%
echo.

:: List all mods with numbers
set "count=0"
echo  ============================================================================================
echo   INSTALLED MODS
echo  ============================================================================================
echo.

for /d %%f in ("%COMMUNITY%\*") do (
    set /a count+=1
    set "mod!count!=%%~nxf"
    set "mod_path!count!=%%f"
    set "padded=   !count!"
    set "padded=!padded:~-3!"
    
    :: Get folder size
    set "folder_path=%%f"
    call :GET_FOLDER_SIZE
    set /a "size_mb=!folder_size!/1048576"
    
    echo   [!padded!] %%~nxf (~!size_mb! MB)
)

echo.
echo  ============================================================================================

if %count% equ 0 (
    echo.
    echo  No mods found.
    pause
    goto MAIN_MENU
)

echo.
echo   [0] Cancel
echo.
set /p "selection=Select mod to uninstall (0-%count%): "

:: Check for cancel
if "%selection%"=="0" goto MAIN_MENU
if "%selection%"=="" goto MAIN_MENU

:: Validate number is in range
set /a "test_num=%selection%" 2>nul
if %test_num% lss 1 goto MAIN_MENU
if %test_num% gtr %count% goto MAIN_MENU

:: Get selected mod
set "mod_to_remove=!mod%selection%!"
set "mod_to_remove_path=!mod_path%selection%!"

:: Confirm uninstall
echo.
echo  Selected: %mod_to_remove%
echo.
choice /C YN /N /M "Are you sure you want to uninstall? (Y/N): "
if %errorlevel%==2 goto MAIN_MENU

:: Ask about backup
echo.
choice /C YN /N /M "Create backup before uninstalling? (Y/N): "

if %errorlevel%==1 (
    :: Create backup
    call :GET_TIMESTAMP
    set "BACKUP_DIR=%DEFAULT_BACKUP_DIR%\!mod_to_remove!_backup_!TIMESTAMP!"
    
    echo.
    echo  Creating backup...
    
    if not exist "%DEFAULT_BACKUP_DIR%" mkdir "%DEFAULT_BACKUP_DIR%"
    mkdir "!BACKUP_DIR!" 2>nul
    
    :: Copy mod to backup
    xcopy "!mod_to_remove_path!" "!BACKUP_DIR!\" /E /I /Y /Q >nul
    
    if !errorlevel! equ 0 (
        echo  [OK] Backup created
    ) else (
        echo  [ERROR] Backup failed
        pause
        goto MAIN_MENU
    )
)

:: Remove mod
echo.
echo  Uninstalling %mod_to_remove%...
rd /s /q "!mod_to_remove_path!" 2>nul

if !errorlevel! equ 0 (
    echo  [OK] Mod uninstalled successfully
    
    :: Log uninstall
    set "LOGFILE=%APPDATA%\MSFS_ModInstaller\uninstall_log.txt"
    (echo [%DATE% %TIME%] UNINSTALLED: %mod_to_remove%) >> "!LOGFILE!"
) else (
    echo  [ERROR] Failed to uninstall mod
)

echo.
pause
goto MAIN_MENU

:: -----------------------------
:: EXPORT LIST (SIMPLIFIED)
:: -----------------------------
:EXPORT_LIST
cls
call :SHOW_HEADER "EXPORT MOD LIST"

call :LOAD_CONFIG
call :SETUP_COMMUNITY
if not defined COMMUNITY (
    echo  [ERROR] No Community folder detected.
    pause
    goto MAIN_MENU
)

:: Generate filename with timestamp
call :GET_TIMESTAMP
set "EXPORT_FILE=%USERPROFILE%\Desktop\MSFS_Mods_List_%TIMESTAMP%.txt"

echo  Generating mod list...
echo.

:: Create file with header
(
echo ================================================================================
echo                              MSFS MOD LIST REPORT
echo ================================================================================
echo.
echo Generated: %DATE% %TIME%
echo Community Folder: %COMMUNITY%
echo.
echo ================================================================================
echo.
echo ENABLED MODS:
echo.
) > "%EXPORT_FILE%"

:: Add enabled mods
set "count=0"
set "total_size=0"

for /d %%f in ("%COMMUNITY%\*") do (
    set /a count+=1
    echo [!count!] %%~nxf >> "%EXPORT_FILE%"
    
    :: Get folder size
    set "folder_path=%%f"
    call :GET_FOLDER_SIZE
    set /a "size_mb=!folder_size!/1048576"
    set /a "total_size+=!size_mb!"
    echo      Size: ~!size_mb! MB >> "%EXPORT_FILE%"
    
    :: Check mod type
    call :DETECT_MOD_TYPE "%%f"
    echo      Type: !mod_type! >> "%EXPORT_FILE%"
    echo. >> "%EXPORT_FILE%"
)

:: Add disabled mods
if exist "%STAGING_DIR%\*" (
    echo. >> "%EXPORT_FILE%"
    echo DISABLED MODS: >> "%EXPORT_FILE%"
    echo. >> "%EXPORT_FILE%"
    
    for /d %%f in ("%STAGING_DIR%\*") do (
        echo - %%~nxf >> "%EXPORT_FILE%"
    )
)

:: Add summary
(
echo.
echo ================================================================================
echo SUMMARY
echo ================================================================================
echo Total enabled mods: %count%
echo Total size: ~%total_size% MB
echo ================================================================================
) >> "%EXPORT_FILE%"

echo  [OK] Export complete!
echo.
echo  File saved to: %EXPORT_FILE%
echo  Found %count% enabled mods (~%total_size% MB total)
echo.
pause
goto MAIN_MENU

:: -----------------------------
:: HELPER FUNCTIONS
:: -----------------------------
:SHOW_HEADER
echo.
echo  %BORDER_LINE%
echo   %~1
echo  %BORDER_LINE%
echo.
goto :eof

:GET_TIMESTAMP
set "d=%DATE:~-4%%DATE:~4,2%%DATE:~7,2%"
set "t=%TIME: =0%"
set "t=%t:~0,2%%t:~3,2%%t:~6,2%"
set "TIMESTAMP=%d%_%t%"
goto :eof

:GET_FOLDER_SIZE
set "folder_size=0"
for /f "tokens=3" %%a in ('dir "%folder_path%" /s /-c 2^>nul ^| findstr /c:"File(s)"') do (
    set "folder_size=%%a"
)
goto :eof

:SETUP_COMMUNITY
if "%USE_CUSTOM_DIR%"=="yes" if defined CUSTOM_INSTALL_DIR (
    set "COMMUNITY=%CUSTOM_INSTALL_DIR%"
    goto :eof
)
call :DETECT_COMMUNITY
goto :eof

:DETECT_COMMUNITY
set "COMMUNITY="

:: List of paths to check
set "paths[0]=%LOCALAPPDATA%\Packages\Microsoft.FlightSimulator_8wekyb3d8bbwe\LocalCache\Packages\Community"
set "paths[1]=%LOCALAPPDATA%\Packages\Microsoft.Limitless_8wekyb3d8bbwe\LocalCache\Packages\Community"
set "paths[2]=C:\Program Files (x86)\Steam\steamapps\common\MicrosoftFlightSimulator\Community"
set "paths[3]=C:\Program Files\Steam\steamapps\common\MicrosoftFlightSimulator\Community"
set "paths[4]=D:\Steam\steamapps\common\MicrosoftFlightSimulator\Community"
set "paths[5]=E:\Steam\steamapps\common\MicrosoftFlightSimulator\Community"

:: Check each path
for /l %%i in (0,1,5) do (
    if exist "!paths[%%i]!" (
        set "COMMUNITY=!paths[%%i]!"
        goto :eof
    )
)

:: Not found - ask user
echo  [WARNING] Could not auto-detect Community folder.
echo.
echo  Common locations:
echo  - MS Store: %LOCALAPPDATA%\Packages\...\Community
echo  - Steam: C:\Program Files (x86)\Steam\...\Community
echo.
set /p "COMMUNITY=Enter your Community folder path: "
set "COMMUNITY=!COMMUNITY:"=!"

if exist "!COMMUNITY!" (
    echo  [OK] Community folder set
) else (
    set "COMMUNITY="
)
goto :eof

:SAVE_CONFIG
if not exist "%APPDATA%\MSFS_ModInstaller" mkdir "%APPDATA%\MSFS_ModInstaller"
(
echo USE_CUSTOM_DIR=%USE_CUSTOM_DIR%
echo CUSTOM_INSTALL_DIR=%CUSTOM_INSTALL_DIR%
echo CURRENT_THEME=%CURRENT_THEME%
) > "%CONFIG_FILE%"
goto :eof

:LOAD_CONFIG
if exist "%CONFIG_FILE%" (
    for /f "usebackq tokens=1,2 delims==" %%a in ("%CONFIG_FILE%") do (
        if "%%a"=="USE_CUSTOM_DIR" set "USE_CUSTOM_DIR=%%b"
        if "%%a"=="CUSTOM_INSTALL_DIR" set "CUSTOM_INSTALL_DIR=%%b"
        if "%%a"=="CURRENT_THEME" set "CURRENT_THEME=%%b"
    )
)
:: Validate theme number
if not defined CURRENT_THEME set "CURRENT_THEME=1"
set /a "theme_test=%CURRENT_THEME%" 2>nul
if %theme_test% lss 1 set "CURRENT_THEME=1"
if %theme_test% gtr 4 set "CURRENT_THEME=1"
goto :eof

:: -----------------------------
:: SETTINGS MENU
:: -----------------------------
:SETTINGS_MENU
cls
call :SHOW_HEADER "SETTINGS"

call :DETECT_COMMUNITY

:SETTINGS_MENU
cls
call :SHOW_HEADER "SETTINGS"

call :DETECT_COMMUNITY

echo  %BORDER_LINE%
echo   CURRENT CONFIGURATION
echo  %BORDER_LINE%
echo.

if "%USE_CUSTOM_DIR%"=="yes" (
    echo   %BULLET% Install Mode: Custom Directory
    echo   %BULLET% Custom Path: %CUSTOM_INSTALL_DIR%
) else (
    echo   %BULLET% Install Mode: Auto-detected
)

if defined COMMUNITY (
    echo   %BULLET% Active Path: %COMMUNITY%
) else (
    echo   %BULLET% Active Path: Not found
)

echo   %BULLET% Backup Directory: %DEFAULT_BACKUP_DIR%
echo   %BULLET% Staging Directory: %STAGING_DIR%
echo   %BULLET% Current Theme: !THEME_%CURRENT_THEME%_NAME!
echo.
echo  %BORDER_LINE%
echo   OPTIONS
echo  %BORDER_LINE%
echo.
echo   [1] Use auto-detected Community folder
echo   [2] Set custom directory (for Addon Linker)
echo   [3] Change backup directory
echo   [4] Change theme
echo   [5] Reset all settings
echo   [6] Back to main menu
echo.
choice /C 123456 /N /M "Select option (1-6): "

set "choice_level=%errorlevel%"
if "%choice_level%"=="6" goto MAIN_MENU
if "%choice_level%"=="5" goto RESET_SETTINGS
if "%choice_level%"=="4" goto THEME_MENU
if "%choice_level%"=="3" goto CHANGE_BACKUP_DIR
if "%choice_level%"=="2" goto SET_CUSTOM
if "%choice_level%"=="1" (
    set "USE_CUSTOM_DIR=no"
    call :SAVE_CONFIG
    echo.
    echo  %CHECK% Will use auto-detected Community folder.
    pause
    goto MAIN_MENU
)

:THEME_MENU
cls
call :SHOW_HEADER "THEME SELECTION"

echo  Select a visual theme for the interface:
echo.
echo  %BORDER_LINE%
echo.
echo   [1] %THEME_1_NAME%
echo       Classic terminal green with simple borders
echo.
echo   [2] %THEME_2_NAME%
echo       Cool blue theme with minimalist style
echo.
echo   [3] %THEME_3_NAME%
echo       Modern purple theme with tech aesthetics
echo.
echo   [4] %THEME_4_NAME%
echo       Warm orange theme with bold appearance
echo.
echo  %BORDER_LINE%
echo.
echo   Current theme: !THEME_%CURRENT_THEME%_NAME!
echo.
echo   [0] Back to settings
echo.

:: Use set /p instead of choice to avoid issues
set /p "theme_choice=Select theme (0-4): "

:: Validate input
if "%theme_choice%"=="0" goto SETTINGS_MENU
if "%theme_choice%"=="1" goto SET_THEME_1
if "%theme_choice%"=="2" goto SET_THEME_2
if "%theme_choice%"=="3" goto SET_THEME_3
if "%theme_choice%"=="4" goto SET_THEME_4
goto THEME_MENU

:SET_THEME_1
set "CURRENT_THEME=1"
call :SAVE_CONFIG
call :APPLY_THEME
echo.
echo  %CHECK% Theme changed to %THEME_1_NAME%
pause
goto SETTINGS_MENU

:SET_THEME_2
set "CURRENT_THEME=2"
call :SAVE_CONFIG
call :APPLY_THEME
echo.
echo  %CHECK% Theme changed to %THEME_2_NAME%
pause
goto SETTINGS_MENU

:SET_THEME_3
set "CURRENT_THEME=3"
call :SAVE_CONFIG
call :APPLY_THEME
echo.
echo  %CHECK% Theme changed to %THEME_3_NAME%
pause
goto SETTINGS_MENU

:SET_THEME_4
set "CURRENT_THEME=4"
call :SAVE_CONFIG
call :APPLY_THEME
echo.
echo  %CHECK% Theme changed to %THEME_4_NAME%
pause
goto SETTINGS_MENU

set "choice_level=%errorlevel%"
if "%choice_level%"=="6" goto MAIN_MENU
if "%choice_level%"=="5" goto RESET_SETTINGS
if "%choice_level%"=="4" goto THEME_MENU
if "%choice_level%"=="3" goto CHANGE_BACKUP_DIR
if "%choice_level%"=="2" goto SET_CUSTOM
if "%choice_level%"=="1" (
    set "USE_CUSTOM_DIR=no"
    call :SAVE_CONFIG
    echo.
    echo  [OK] Will use auto-detected Community folder.
    pause
    goto MAIN_MENU
)

:THEME_MENU
cls
call :SHOW_HEADER "THEME SELECTION"

echo %SEPARATOR%
echo                           AVAILABLE THEMES
echo %SEPARATOR%
echo.
echo  %BULLET% [1] !THEME_1_NAME! - Classic Terminal Style
echo      Frame: Box design with + corners and = lines
echo      Bullets: Triple arrows (>>>) 
echo      Status: [+] Success, [X] Failed
echo.
echo  %BULLET% [2] !THEME_2_NAME! - Wave Ocean Design
echo      Frame: Flowing wave pattern (~)
echo      Bullets: Wave arrow (~>)
echo      Status: [*] Success, [-] Failed
echo.
echo  %BULLET% [3] !THEME_3_NAME! - Bold Block Style
echo      Frame: Heavy borders with ## design
echo      Bullets: Block brackets [#]
echo      Status: [#] Success, [!] Failed
echo.
echo  %BULLET% [4] !THEME_4_NAME! - Decorative Star Pattern
echo      Frame: Alternating star pattern (*+*)
echo      Bullets: Star arrow (*>)
echo      Status: ^<*^> Success, ^<!^> Failed
echo.
echo %SEPARATOR%
echo.
echo   Current theme: !THEME_%CURRENT_THEME%_NAME!
echo.
echo %SEPARATOR%
echo.
echo   [0] Back to settings
echo.
echo.
echo  %BULLET% Select theme (0-4): 
choice /C 01234 /N
set "theme_choice=%errorlevel%"
if "%theme_choice%"=="1" goto SETTINGS_MENU
set /a "theme_choice=%theme_choice%-1"

if %theme_choice% geq 1 if %theme_choice% leq 4 (
    set "CURRENT_THEME=%theme_choice%"
    call :SAVE_CONFIG
    call :APPLY_THEME
    echo.
    echo  %CHECK% Theme changed to !THEME_%CURRENT_THEME%_NAME!
    pause
)
goto SETTINGS_MENU

set "theme_choice=%errorlevel%"
if "%theme_choice%"=="1" goto SETTINGS_MENU
set /a "theme_choice=%theme_choice%-1"

if %theme_choice% geq 1 if %theme_choice% leq 4 (
    set "CURRENT_THEME=%theme_choice%"
    call :SAVE_CONFIG
    call :APPLY_THEME
    echo.
    echo  [OK] Theme changed to !THEME_%CURRENT_THEME%_NAME!
    pause
)
goto SETTINGS_MENU

:: -----------------------------
:: THEME SYSTEM
:: -----------------------------
:INIT_THEME_SYSTEM
:: Safe theme system initialization
:: Default to classic color if themes fail
set "USE_THEMES=yes"
set "ANSI_SUPPORTED=no"
set "ESC="

:: Test if we're on Windows 10/11
ver | findstr /i "10\." >nul && set "WIN10=yes" || set "WIN10=no"

:: Try to generate ESC character safely
for /f %%a in ('echo prompt $E ^| cmd') do set "ESC=%%a" 2>nul

:: Test ANSI support without crashing
if defined ESC (
    echo !ESC![31mTest!ESC![0m > "%TEMP%\ansi_test.tmp" 2>nul
    findstr "Test" "%TEMP%\ansi_test.tmp" >nul 2>&1
    if !errorlevel! equ 0 set "ANSI_SUPPORTED=yes"
    del "%TEMP%\ansi_test.tmp" 2>nul
)

:: Define safe themes using standard ASCII
:: Theme 1: Classic Green
set "THEME_1_NAME=Classic Green"
set "THEME_1_COLOR=0A"

:: Theme 2: Ocean Blue  
set "THEME_2_NAME=Ocean Blue"
set "THEME_2_COLOR=09"

:: Theme 3: Cyber Purple
set "THEME_3_NAME=Cyber Purple"
set "THEME_3_COLOR=0D"

:: Theme 4: Fire Orange
set "THEME_4_NAME=Fire Orange"
set "THEME_4_COLOR=0C"

goto :eof

:APPLY_THEME
:: Apply selected theme safely
if not defined CURRENT_THEME set "CURRENT_THEME=1"

:: Apply color
if "%CURRENT_THEME%"=="1" color %THEME_1_COLOR% 2>nul
if "%CURRENT_THEME%"=="2" color %THEME_2_COLOR% 2>nul
if "%CURRENT_THEME%"=="3" color %THEME_3_COLOR% 2>nul
if "%CURRENT_THEME%"=="4" color %THEME_4_COLOR% 2>nul

:: Set theme-specific styles (using safe ASCII)
if "%CURRENT_THEME%"=="1" (
    set "BORDER_LINE============================================================================================"
    set "BORDER_CHAR=|"
    set "BULLET=>"
    set "CHECK=[OK]"
    set "FAIL=[X]"
)
if "%CURRENT_THEME%"=="2" (
    set "BORDER_LINE=--------------------------------------------------------------------------------------------"
    set "BORDER_CHAR=-"
    set "BULLET=-"
    set "CHECK=[*]"
    set "FAIL=[-]"
)
if "%CURRENT_THEME%"=="3" (
    set "BORDER_LINE=********************************************************************************************"
    set "BORDER_CHAR=*"
    set "BULLET=*"
    set "CHECK=[+]"
    set "FAIL=[!]"
)
if "%CURRENT_THEME%"=="4" (
    set "BORDER_LINE=############################################################################################"
    set "BORDER_CHAR=#"
    set "BULLET=#"
    set "CHECK=[#]"
    set "FAIL=[X]"
)

:: If ANSI is supported and we're on Win10/11, enable enhanced colors
if "%ANSI_SUPPORTED%"=="yes" if "%WIN10%"=="yes" (
    :: Enable ANSI processing
    reg add HKCU\Console /v VirtualTerminalLevel /t REG_DWORD /d 1 /f >nul 2>&1
)

goto :eof

:SET_CUSTOM
echo.
echo  Enter custom install directory path:
echo  (This is useful for Addon Linker users)
echo.
set /p "CUSTOM_INSTALL_DIR=Path: "

:: Strip quotes and validate
set "CUSTOM_INSTALL_DIR=!CUSTOM_INSTALL_DIR:"=!"
if not exist "!CUSTOM_INSTALL_DIR!" (
    echo.
    echo  [ERROR] Directory does not exist.
    pause
    goto SETTINGS_MENU
)

set "USE_CUSTOM_DIR=yes"
call :SAVE_CONFIG
echo.
echo  [OK] Custom directory saved.
pause
goto MAIN_MENU

:CHANGE_BACKUP_DIR
echo.
echo  Current backup directory: %DEFAULT_BACKUP_DIR%
echo.
set /p "new_backup=Enter new backup directory path (or press Enter to keep current): "

if not "%new_backup%"=="" (
    set "new_backup=!new_backup:"=!"
    if not exist "!new_backup!" (
        echo.
        echo  Directory doesn't exist. Create it?
        choice /C YN /N /M "(Y/N): "
        if !errorlevel!==1 (
            mkdir "!new_backup!" 2>nul
            set "DEFAULT_BACKUP_DIR=!new_backup!"
            call :SAVE_CONFIG
            echo  [OK] Backup directory updated.
        )
    ) else (
        set "DEFAULT_BACKUP_DIR=!new_backup!"
        call :SAVE_CONFIG
        echo  [OK] Backup directory updated.
    )
)
pause
goto SETTINGS_MENU

:RESET_SETTINGS
echo.
echo  Are you sure you want to reset all settings?
choice /C YN /N /M "(Y/N): "
if %errorlevel%==2 goto SETTINGS_MENU

:: Delete config file
if exist "%CONFIG_FILE%" del "%CONFIG_FILE%"
set "USE_CUSTOM_DIR=no"
set "CUSTOM_INSTALL_DIR="
set "DEFAULT_BACKUP_DIR=%USERPROFILE%\Documents\MSFS_Mod_Backups"
set "CURRENT_THEME=1"
call :APPLY_THEME
echo.
echo  [OK] Settings reset to defaults.
pause
goto MAIN_MENU

:: -----------------------------
:: INSTALL DROPPED FILES
:: -----------------------------
:INSTALL_DROPPED
cls

:: Load config and apply theme first
call :LOAD_CONFIG
call :APPLY_THEME

call :SHOW_HEADER "QUICK INSTALL"
echo  Processing dropped files...
echo.

:: Load config and detect community folder
call :LOAD_CONFIG
call :SETUP_COMMUNITY
if not defined COMMUNITY (
    echo  [ERROR] No Community folder detected.
    echo.
    echo  Please run the installer normally and configure first.
    pause
    exit /b 1
)

:: Process all dropped files
set "TOTAL_INSTALLED=0"
set "TOTAL_SKIPPED=0"
set "TOTAL_FAILED=0"
call :GET_TIMESTAMP

:: Process each argument
set "argcount=0"
:PROCESS_ARGS
if "%~1"=="" goto ARGS_DONE
set /a argcount+=1
echo.
echo  [%argcount%] Processing: %~nx1
call :INSTALL_SINGLE "%~1"
shift
goto PROCESS_ARGS

:ARGS_DONE
call :SHOW_INSTALL_SUMMARY
pause
exit /b 0

:: -----------------------------
:: INSTALL SECTION
:: -----------------------------
:INSTALL
cls
call :SHOW_HEADER "MOD INSTALLATION"

:: Load saved config
call :LOAD_CONFIG
call :SETUP_COMMUNITY
if not defined COMMUNITY (
    echo  [ERROR] No Community folder detected or configured.
    pause
    goto MAIN_MENU
)

echo  [OK] Install Directory: %COMMUNITY%
echo.

:: Initialize counters
set "TOTAL_INSTALLED=0"
set "TOTAL_SKIPPED=0" 
set "TOTAL_FAILED=0"
set "SESSION_COUNT=0"

:INSTALL_LOOP
echo  ============================================================================================
echo.
echo   MOD DRAG AND DROP INSTALLATION
echo.
echo   1. Drag a mod file (ZIP or folder) into this window
echo   2. Press ENTER to install it
echo   3. Repeat for more mods
echo.
echo   Type 'done' or press ENTER without input to finish
echo.
if %SESSION_COUNT% gtr 0 (
    echo   Progress: %TOTAL_INSTALLED% installed, %TOTAL_SKIPPED% skipped, %TOTAL_FAILED% failed
    echo.
)
echo  ============================================================================================
echo.

:: Get input
set "MODPATH="
set /p "MODPATH=Drag mod here or type 'done': "

:: Clean up the input
set "MODPATH=!MODPATH:"=!"

:: Check for exit conditions
if /i "!MODPATH!"=="done" goto INSTALL_SUMMARY
if not defined MODPATH goto INSTALL_SUMMARY
if "!MODPATH!"=="" goto INSTALL_SUMMARY

:: Increment session counter
set /a SESSION_COUNT+=1

:: Process the single path
echo.
if exist "!MODPATH!" (
    echo  Processing: !MODPATH!
    call :INSTALL_SINGLE "!MODPATH!"
) else (
    echo  [ERROR] File not found: !MODPATH!
    set /a TOTAL_FAILED+=1
)

:: Continue loop
echo.
echo  Press any key to continue...
pause >nul
cls
call :SHOW_HEADER "MOD INSTALLATION"
echo  [OK] Install Directory: %COMMUNITY%
echo.
goto :INSTALL_LOOP

:INSTALL_SUMMARY
if %SESSION_COUNT% gtr 0 (
    call :SHOW_INSTALL_SUMMARY
)
pause
goto MAIN_MENU

:SHOW_INSTALL_SUMMARY
echo.
echo  ============================================================================================
echo   INSTALLATION COMPLETE
echo  ============================================================================================
echo.
if defined SESSION_COUNT (
    echo   Processed: %SESSION_COUNT% file(s)
)
if %TOTAL_INSTALLED% gtr 0 echo   [OK] Installed: %TOTAL_INSTALLED% mod(s)
if %TOTAL_SKIPPED% gtr 0 echo   [WARNING] Skipped: %TOTAL_SKIPPED% mod(s)
if %TOTAL_FAILED% gtr 0 echo   [ERROR] Failed: %TOTAL_FAILED% mod(s)
echo.
echo  ============================================================================================
echo.
goto :eof

:INSTALL_SINGLE
set "MODPATH=%~1"

:: Check for conflicts before installing
if exist "!MODPATH!" (
    for %%f in ("!MODPATH!") do set "new_mod_name=%%~nf"
    
    :: Simple conflict check
    for /f "tokens=1,2 delims=," %%a in (%CONFLICTS_DB%) do (
        if /i "%%a"=="!new_mod_name!" (
            echo  [WARNING] This mod may conflict with: %%b
            choice /C YN /N /M "Install anyway? (Y/N): "
            if !errorlevel!==2 (
                set /a TOTAL_SKIPPED+=1
                goto :eof
            )
        )
    )
)

:: Validate path exists
if not exist "!MODPATH!" (
    echo  [ERROR] Path does not exist: !MODPATH!
    set /a TOTAL_FAILED+=1
    goto :eof
)

:: Create timestamp if not exists
if not defined TIMESTAMP call :GET_TIMESTAMP

:: Create unique temp directory
set "TEMPDIR=%TEMP%\MSFS_MOD_%TIMESTAMP%_%RANDOM%"
set "INSTALLFOLDER="
set "CLEANUP_NEEDED=no"

:: Create temp directory
if exist "!TEMPDIR!" rd /s /q "!TEMPDIR!" 2>nul
mkdir "!TEMPDIR!" 2>nul

:: Check if ZIP
set "IS_ZIP=no"
for %%x in ("%MODPATH%") do (
    set "file_ext=%%~xx"
    if /i "!file_ext!"==".zip" set "IS_ZIP=yes"
)

if "!IS_ZIP!"=="yes" (
    echo  Extracting ZIP file...
    
    :: Use tar to extract
    pushd "!TEMPDIR!"
    tar -xf "!MODPATH!" 2>nul
    set "extract_error=!errorlevel!"
    popd
    
    if !extract_error! neq 0 (
        echo  [ERROR] Failed to extract ZIP file.
        set /a TOTAL_FAILED+=1
        rd /s /q "!TEMPDIR!" 2>nul
        goto :eof
    )
    
    set "INSTALLFOLDER=!TEMPDIR!"
    set "CLEANUP_NEEDED=yes"
) else (
    :: Check if it's a directory
    if exist "!MODPATH!\*" (
        set "INSTALLFOLDER=!MODPATH!"
    ) else (
        echo  [ERROR] Not a valid ZIP file or folder.
        set /a TOTAL_FAILED+=1
        rd /s /q "!TEMPDIR!" 2>nul
        goto :eof
    )
)

:: Install mods from folder
call :INSTALL_FROM_FOLDER "!INSTALLFOLDER!" "!MODPATH!"

:: Cleanup
if "!CLEANUP_NEEDED!"=="yes" (
    rd /s /q "!TEMPDIR!" 2>nul
)
goto :eof

:INSTALL_FROM_FOLDER
set "source_folder=%~1"
set "original_path=%~2"
set "found_mods=0"

:: Check if root folder is a mod
set "is_root_mod=no"
if exist "!source_folder!\manifest.json" set "is_root_mod=yes"
if exist "!source_folder!\layout.json" set "is_root_mod=yes"
if exist "!source_folder!\SimObjects" set "is_root_mod=yes"
if exist "!source_folder!\html_ui" set "is_root_mod=yes"
if exist "!source_folder!\Effects" set "is_root_mod=yes"
if exist "!source_folder!\ModelBehaviorDefs" set "is_root_mod=yes"
if exist "!source_folder!\scenery" set "is_root_mod=yes"

if "!is_root_mod!"=="yes" (
    :: Get mod name from original path
    for %%f in ("!original_path!") do (
        set "file_ext=%%~xf"
        if /i "!file_ext!"==".zip" (
            set "modname=%%~nf"
        ) else (
            set "modname=%%~nxf"
        )
    )
    
    call :INSTALL_MOD_DIRECT "!source_folder!" "!modname!"
    set /a found_mods+=1
) else (
    :: Check subfolders
    for /d %%f in ("!source_folder!\*") do (
        set "is_mod=no"
        if exist "%%f\manifest.json" set "is_mod=yes"
        if exist "%%f\layout.json" set "is_mod=yes"
        if exist "%%f\SimObjects" set "is_mod=yes"
        if exist "%%f\html_ui" set "is_mod=yes"
        if exist "%%f\Effects" set "is_mod=yes"
        if exist "%%f\ModelBehaviorDefs" set "is_mod=yes"
        if exist "%%f\scenery" set "is_mod=yes"
        
        if "!is_mod!"=="yes" (
            call :INSTALL_MOD_DIRECT "%%f" "%%~nxf"
            set /a found_mods+=1
        )
    )
)

:: No valid mods found
if !found_mods! equ 0 (
    echo  [WARNING] No valid mod structure detected.
    choice /C YN /N /M "Force install anyway? (Y/N): "
    if !errorlevel!==2 (
        set /a TOTAL_SKIPPED+=1
    ) else (
        for %%f in ("!original_path!") do set "forcename=%%~nf"
        call :INSTALL_MOD_DIRECT "!source_folder!" "!forcename!"
    )
)
goto :eof

:INSTALL_MOD_DIRECT
set "mod_source=%~1"
set "mod_name=%~2"
set "target_path=%COMMUNITY%\%mod_name%"

:: Check if exists
if exist "!target_path!" (
    echo  [WARNING] Mod already exists: !mod_name!
    choice /C YN /N /M "Overwrite? (Y/N): "
    if !errorlevel!==2 (
        echo  [SKIPPED] !mod_name!
        set /a TOTAL_SKIPPED+=1
        goto :eof
    )
    echo  Removing old version...
    rd /s /q "!target_path!" 2>nul
)

:: Copy mod
echo  Installing: !mod_name!
xcopy "!mod_source!\*" "!target_path!\" /E /I /Y /Q >nul 2>&1

if !errorlevel! equ 0 (
    echo  [INSTALLED] !mod_name!
    set /a TOTAL_INSTALLED+=1
    
    :: Log installation
    set "LOGFILE=%APPDATA%\MSFS_ModInstaller\install_log.txt"
    (echo [%DATE% %TIME%] INSTALLED: !mod_name!) >> "!LOGFILE!"
) else (
    echo  [ERROR] Failed to install !mod_name!
    set /a TOTAL_FAILED+=1
)
goto :eof