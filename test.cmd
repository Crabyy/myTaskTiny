@echo off
cd /d "%~dp0"
start /wait "" myTinyTask.exe --self-test
set "testExit=%errorlevel%"
type test-results.txt
echo.
pause
exit /b %testExit%
