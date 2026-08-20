# Load the SQLite assembly
Add-Type -Path "C:\Users\asus\Desktop\app pfe\SEBN-DOTNET\SebnWeb\bin\Debug\net8.0\Microsoft.Data.Sqlite.dll"
Add-Type -AssemblyName System.Security

$dbPath = "C:\Users\asus\Desktop\app pfe\SEBN-DOTNET\SebnWeb\Data\sebn.db"
$connectionString = "Data Source=$dbPath;Mode=ReadWriteCreate"

Write-Host "═══════════════════════════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "INSPECTION COMPLÈTE DE LA BASE SQLITE" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════════════════════════════════════" -ForegroundColor Cyan

# 1. Vérifier l'existence du fichier
Write-Host "`n1. FICHIER DE BASE DE DONNÉES" -ForegroundColor Yellow
Write-Host "───────────────────────────────────────────────────────────────────────────────────" -ForegroundColor DarkGray
if (Test-Path $dbPath) {
    $size = (Get-Item $dbPath).Length
    Write-Host "✓ Fichier existe : $dbPath" -ForegroundColor Green
    Write-Host "  Taille : $size bytes" -ForegroundColor Green
    if ($size -eq 0) {
        Write-Host "  ⚠ ATTENTION : Fichier VIDE (0 bytes) - pas de données !" -ForegroundColor Red
    }
} else {
    Write-Host "✗ Fichier N'EXISTE PAS : $dbPath" -ForegroundColor Red
    exit
}

# 2. Connexion à la base
Write-Host "`n2. CONNEXION À LA BASE" -ForegroundColor Yellow
Write-Host "───────────────────────────────────────────────────────────────────────────────────" -ForegroundColor DarkGray
try {
    $connection = New-Object Microsoft.Data.Sqlite.SqliteConnection($connectionString)
    $connection.Open()
    Write-Host "✓ Connexion réussie" -ForegroundColor Green
} catch {
    Write-Host "✗ Erreur de connexion : $_" -ForegroundColor Red
    exit
}

# 3. Vérifier les tables
Write-Host "`n3. TABLES PRÉSENTES" -ForegroundColor Yellow
Write-Host "───────────────────────────────────────────────────────────────────────────────────" -ForegroundColor DarkGray
$cmd = $connection.CreateCommand()
$cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name"
$reader = $cmd.ExecuteReader()

while ($reader.Read()) {
    $tableName = $reader[0]
    $countCmd = $connection.CreateCommand()
    $countCmd.CommandText = "SELECT COUNT(*) FROM [$tableName]"
    $count = $countCmd.ExecuteScalar()
    Write-Host "  ✓ $tableName : $count lignes" -ForegroundColor Green
}
$reader.Close()

# 4. Vérifier les utilisateurs
Write-Host "`n4. UTILISATEURS DANS LA BASE" -ForegroundColor Yellow
Write-Host "───────────────────────────────────────────────────────────────────────────────────" -ForegroundColor DarkGray
$userCmd = $connection.CreateCommand()
$userCmd.CommandText = "SELECT COUNT(*) FROM Utilisateurs"
$userCount = $userCmd.ExecuteScalar()
Write-Host "Total : $userCount utilisateurs"

if ($userCount -gt 0) {
    $selectCmd = $connection.CreateCommand()
    $selectCmd.CommandText = "SELECT IdUtilisateur, Login, MotDePasseHash, Role, NomAffichage FROM Utilisateurs ORDER BY IdUtilisateur"
    $userReader = $selectCmd.ExecuteReader()
    
    while ($userReader.Read()) {
        $id = $userReader[0]
        $login = $userReader[1]
        $hash = $userReader[2]
        $role = $userReader[3]
        $nom = $userReader[4]
        
        Write-Host "`n  Utilisateur #$id" -ForegroundColor Cyan
        Write-Host "    Login      : $login"
        Write-Host "    Rôle       : $role"
        Write-Host "    Nom        : $nom"
        Write-Host "    Hash       : $($hash.Substring(0, 40))..."
    }
    $userReader.Close()
} else {
    Write-Host "✗ AUCUN UTILISATEUR TROUVÉ !" -ForegroundColor Red
}

# 5. Vérifier le hash
Write-Host "`n5. VÉRIFICATION DU HASH SHA256" -ForegroundColor Yellow
Write-Host "───────────────────────────────────────────────────────────────────────────────────" -ForegroundColor DarkGray
$testPassword = "sebn2026"
$encoding = [System.Text.Encoding]::UTF8
$sha256 = [System.Security.Cryptography.SHA256]::Create()
$bytes = $sha256.ComputeHash($encoding.GetBytes($testPassword))
$calculatedHash = ([BitConverter]::ToString($bytes) -replace '-').ToLower()

Write-Host "Mot de passe de test : '$testPassword'"
Write-Host "SHA256 calculé       : $calculatedHash" -ForegroundColor Cyan

$hashCmd = $connection.CreateCommand()
$hashCmd.CommandText = "SELECT MotDePasseHash FROM Utilisateurs WHERE Login = 'superviseur'"
$storedHashObj = $hashCmd.ExecuteScalar()

if ($storedHashObj) {
    $storedHash = $storedHashObj.ToString()
    Write-Host "Hash stocké          : $storedHash" -ForegroundColor Cyan
    
    if ($storedHash -eq $calculatedHash) {
        Write-Host "✓ LES HASHES CORRESPONDENT !" -ForegroundColor Green
    } else {
        Write-Host "✗ LES HASHES NE CORRESPONDENT PAS !" -ForegroundColor Red
    }
} else {
    Write-Host "✗ Utilisateur 'superviseur' non trouvé en base !" -ForegroundColor Red
}

# 6. Vérifier SeedState
Write-Host "`n6. STATUT DU SEED" -ForegroundColor Yellow
Write-Host "───────────────────────────────────────────────────────────────────────────────────" -ForegroundColor DarkGray
$seedCmd = $connection.CreateCommand()
$seedCmd.CommandText = "SELECT Value FROM SeedState WHERE Key = 'DemoData'"
$seedValue = $seedCmd.ExecuteScalar()

if ($seedValue) {
    Write-Host "✓ SeedState trouvé : DemoData = $seedValue" -ForegroundColor Green
} else {
    Write-Host "✗ SeedState 'DemoData' NOT FOUND" -ForegroundColor Red
}

# 7. Résumé
Write-Host "`n7. RÉSUMÉ FINAL" -ForegroundColor Yellow
Write-Host "═══════════════════════════════════════════════════════════════════════════════════" -ForegroundColor Cyan

$anomCmd = $connection.CreateCommand()
$anomCmd.CommandText = "SELECT COUNT(*) FROM Anomalies"
$anomCount = $anomCmd.ExecuteScalar()

if ($userCount -ge 4) {
    Write-Host "✓ Utilisateurs présents : $userCount" -ForegroundColor Green
} else {
    Write-Host "✗ Utilisateurs INSUFFISANT : $userCount (attendu: 4)" -ForegroundColor Red
}

if ($anomCount -ge 420) {
    Write-Host "✓ Anomalies présentes : $anomCount" -ForegroundColor Green
} else {
    Write-Host "✗ Anomalies INSUFFISANT : $anomCount (attendu: 420)" -ForegroundColor Red
}

if ($storedHashObj -and $storedHashObj.ToString() -eq $calculatedHash) {
    Write-Host "✓ Hash 'sebn2026' correct" -ForegroundColor Green
} else {
    Write-Host "✗ Hash 'sebn2026' INCORRECT" -ForegroundColor Red
}

Write-Host "`n═══════════════════════════════════════════════════════════════════════════════════" -ForegroundColor Cyan

$connection.Close()
