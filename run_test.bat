@echo off
REM Script pour lancer l'app SEBN et tester le login

echo ================================================================================
echo LANCEMENT DE L'APPLICATION SEBN + TEST DE LOGIN
echo ================================================================================

REM Arrêter les anciens processus
echo.
echo Arrêt des processus dotnet en cours...
taskkill /F /IM dotnet.exe 2>nul
timeout /t 2

REM Lancer l'application en arrière-plan dans une nouvelle fenêtre
echo.
echo Lancement de l'application SebnWeb...
start "SebnWeb" cmd /k "cd /d C:\Users\asus\Desktop\app pfe\SEBN-DOTNET\SebnWeb && dotnet run"

REM Attendre que l'app démarre (écoute sur localhost:5000)
echo Attente du démarrage (10 secondes)...
timeout /t 10 /nobreak

REM Lancer le test Python
echo.
echo Lancement du test de login...
cd /d C:\Users\asus\Desktop\app pfe\SEBN-DOTNET
python test_login.py

pause
