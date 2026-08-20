@echo off
echo ============================================
echo   SEBN - Demarrage de l'application complete
echo ============================================
echo.

echo [1/2] Demarrage du microservice IA (Python)...
start "SEBN - IA Service" cmd /k "cd ia-service && venv\Scripts\activate && uvicorn main:app --port 8000"

timeout /t 4 /nobreak > nul

echo [2/2] Demarrage du backend/frontend (.NET)...
start "SEBN - Web App" cmd /k "cd SebnWeb && dotnet run"

echo.
echo Les deux services demarrent dans des fenetres separees.
echo Ouvrez votre navigateur sur : http://localhost:5000
echo.
pause
