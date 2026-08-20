#!/usr/bin/env python3
"""Vérification complète de l'état Git"""
import subprocess
import os

os.chdir(r"C:\Users\asus\Desktop\app pfe\SEBN-DOTNET")

print("=" * 80)
print("VÉRIFICATION DE L'ÉTAT GIT")
print("=" * 80)

# 1. Git status
print("\n1. GIT STATUS")
print("-" * 80)
result = subprocess.run(["git", "status", "--short"], capture_output=True, text=True)
print(result.stdout if result.stdout.strip() else "Clean (aucun changement non committée)")

# 2. Git log
print("\n2. DERNIERS 5 COMMITS")
print("-" * 80)
result = subprocess.run(["git", "log", "-5", "--oneline"], capture_output=True, text=True)
print(result.stdout)

# 3. Détails du dernier commit
print("\n3. CONTENU DU DERNIER COMMIT")
print("-" * 80)
result = subprocess.run(["git", "log", "-1", "--name-status"], capture_output=True, text=True)
print(result.stdout)

# 4. Vérifier si SeedGenerator.cs a changé
print("\n4. VÉRIFICATION DES FICHIERS CLÉS")
print("-" * 80)

files_to_check = [
    "SebnWeb/Data/SeedGenerator.cs",
    "SebnWeb/Data/AppDataStore.cs",
    "SebnWeb/Program.cs"
]

for file in files_to_check:
    result = subprocess.run(["git", "log", "-1", "--oneline", file], capture_output=True, text=True)
    commit_hash = result.stdout.split()[0] if result.stdout else "NOT IN GIT"
    print(f"  {file}: {commit_hash}")

# 5. Vérifier si le commit correct est sur main
print("\n5. DERNIERS COMMITS SUR MAIN")
print("-" * 80)
result = subprocess.run(["git", "log", "main", "-3", "--oneline"], capture_output=True, text=True)
print(result.stdout)

# 6. Vérifier origin/main (remote)
print("\n6. ÉTAT DU REMOTE (origin/main)")
print("-" * 80)
result = subprocess.run(["git", "remote", "-v"], capture_output=True, text=True)
print(result.stdout)

result = subprocess.run(["git", "log", "origin/main", "-3", "--oneline", "-q"], capture_output=True, text=True)
if result.stdout.strip():
    print("Derniers commits sur origin/main:")
    print(result.stdout)
else:
    print("origin/main non accessible (offline?)")

# 7. Vérifier si les corrections sont dans le HEAD
print("\n7. VÉRIFICATION DE LA PRÉSENCE DES CORRECTIONS DANS HEAD")
print("-" * 80)

checks = [
    ("SeedGenerator.Initialiser", "grep SeedGenerator.Initialiser SebnWeb/Data/SeedGenerator.cs | head -1"),
    ("GetRequiredService", "grep GetRequiredService SebnWeb/Program.cs | head -1"),
    ("_contentRootPath", "grep _contentRootPath SebnWeb/Data/AppDataStore.cs | head -1"),
]

for check_name, cmd in checks:
    result = subprocess.run(cmd, shell=True, capture_output=True, text=True)
    if result.stdout.strip():
        print(f"✓ {check_name}: TROUVÉ")
    else:
        print(f"✗ {check_name}: NON TROUVÉ")

print("\n" + "=" * 80)
