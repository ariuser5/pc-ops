@echo off
setlocal enabledelayedexpansion
echo Hibernation scheduling. Press C to cancel...
for /l %%i in (10,-1,1) do (
	echo %%i seconds remaining...
	choice /c CH /t 1 /d H /n >nul
	if !errorlevel!==1 (
		echo Hibernation cancelled.
		exit /b
	)
)
echo Hibernating now...
shutdown /h