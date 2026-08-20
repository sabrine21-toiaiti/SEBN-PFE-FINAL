import sqlite3
import sys

db_path = r"C:\Users\asus\Desktop\app pfe\SEBN-DOTNET\SebnWeb\Data\sebn.db"

try:
    conn = sqlite3.connect(db_path)
    cursor = conn.cursor()
    
    # Vérifier les utilisateurs
    cursor.execute("SELECT COUNT(*) FROM Utilisateurs")
    count = cursor.fetchone()[0]
    print(f"Nombre d'utilisateurs : {count}")
    
    # Récupérer les détails
    cursor.execute("SELECT IdUtilisateur, Login, MotDePasseHash, Role, NomAffichage FROM Utilisateurs ORDER BY IdUtilisateur")
    rows = cursor.fetchall()
    
    print("\nUtilisateurs présents :")
    for row in rows:
        id, login, hash, role, nom = row
        print(f"  {id}. {login} -> {nom} ({role}) [hash: {hash[:20]}...]")
    
    # Vérifier les caméras, postes, opérateurs
    cursor.execute("SELECT COUNT(*) FROM Cameras")
    print(f"\nCaméras : {cursor.fetchone()[0]}")
    
    cursor.execute("SELECT COUNT(*) FROM Postes")
    print(f"Postes : {cursor.fetchone()[0]}")
    
    cursor.execute("SELECT COUNT(*) FROM Operateurs")
    print(f"Opérateurs : {cursor.fetchone()[0]}")
    
    cursor.execute("SELECT COUNT(*) FROM Anomalies")
    print(f"Anomalies : {cursor.fetchone()[0]}")
    
    cursor.execute("SELECT * FROM SeedState WHERE Key = 'DemoData'")
    result = cursor.fetchone()
    print(f"SeedState marker : {result[1] if result else 'NOT SET'}")
    
    conn.close()
    print("\n✓ Base de données validée")
    
except Exception as e:
    print(f"✗ Erreur : {e}", file=sys.stderr)
    sys.exit(1)
