# ============================================================
#  Démarrage complet de l'application SEBN
#  (microservice IA Python + backend .NET) chacun dans sa
#  propre fenêtre, pour que les deux restent actifs en même
#  temps sans se couper l'un l'autre.
#
#  UTILISATION : clic droit sur ce fichier > "Exécuter avec
#  PowerShell", ou depuis un terminal :
#      .\demarrer-application.ps1
# ============================================================

$racine = "C:\Users\asus\Desktop\app pfe\SEBN-DOTNET"

Write-Host "Démarrage du microservice IA (Python)..." -ForegroundColor Cyan
Start-Process powershell -ArgumentList @(
    "-NoExit",
    "-Command",
    "cd '$racine\ia-service'; .\venv\Scripts\Activate.ps1; uvicorn main:app --port 8000"
)

Write-Host "Attente de 8 secondes avant de lancer le backend .NET..." -ForegroundColor Cyan
Start-Sleep -Seconds 8

Write-Host "Démarrage du backend .NET..." -ForegroundColor Cyan
Start-Process powershell -ArgumentList @(
    "-NoExit",
    "-Command",
    "cd '$racine\SebnWeb'; dotnet run --urls http://localhost:5050"
)

Write-Host ""
Write-Host "Deux fenêtres PowerShell viennent de s'ouvrir : NE LES FERMEZ PAS." -ForegroundColor Yellow
Write-Host "Attendez environ 20 secondes, puis ouvrez : http://localhost:5050" -ForegroundColor Green
