using System.Data;
using System.Linq;
using Microsoft.Data.Sqlite;
using SebnWeb.Models;

namespace SebnWeb.Data;

/// <summary>
/// Initialise les données minimales nécessaires au fonctionnement de l'application
/// avec SQLite, sans introduire de logique métier différente de l'ancienne version.
/// </summary>
public static class SeedGenerator
{
    public static void Initialiser(string connectionString)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS SeedState (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );
        ";
        command.ExecuteNonQuery();

        using var validationCmd = connection.CreateCommand();
        validationCmd.CommandText = @"
            SELECT
                (SELECT COUNT(*) FROM Utilisateurs),
                (SELECT COUNT(*) FROM Cameras),
                (SELECT COUNT(*) FROM Postes),
                (SELECT COUNT(*) FROM Operateurs),
                (SELECT COUNT(*) FROM Anomalies),
                (SELECT COUNT(*) FROM Anomalies WHERE ImagePreuve LIKE 'captures/anomalie_%'),
                (SELECT Value FROM SeedState WHERE Key = 'DemoData')
        ";
        using var reader = validationCmd.ExecuteReader();
        reader.Read();
        int utilisateursCount = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
        int camerasCount = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
        int postesCount = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
        int operateursCount = reader.IsDBNull(3) ? 0 : reader.GetInt32(3);
        int anomaliesCount = reader.IsDBNull(4) ? 0 : reader.GetInt32(4);
        int seedAnomaliesCount = reader.IsDBNull(5) ? 0 : reader.GetInt32(5);
        var markerValue = reader.IsDBNull(6) ? null : reader.GetString(6);

        bool donneesStatiqueOk = utilisateursCount >= 4 && camerasCount >= 4 && postesCount >= 4 && operateursCount >= 12;
        bool utilisateursCoherents = true;

        using (var verifyUsers = connection.CreateCommand())
        {
            verifyUsers.CommandText = @"
                SELECT Login, Role, MotDePasseHash FROM Utilisateurs;
            ";
            using var verifyReader = verifyUsers.ExecuteReader();
            var actualUsers = new Dictionary<string, (string Role, string Hash)>();
            while (verifyReader.Read())
            {
                var login = verifyReader.GetString(0);
                var role = verifyReader.GetString(1);
                var hash = verifyReader.GetString(2);
                actualUsers[login] = (role, hash);
            }

            var expectedUsers = new Dictionary<string, (string Role, string Hash)> {
                ["superviseur"] = ("SuperviseurProduction", Utilisateur.Hacher("sebn2026")),
                ["qualite"] = ("AuditeurQualite", Utilisateur.Hacher("sebn2026")),
                ["admin"] = ("AdminPit", Utilisateur.Hacher("sebn2026")),
                ["direction"] = ("Direction", Utilisateur.Hacher("sebn2026"))
            };

            foreach (var expected in expectedUsers)
            {
                if (!actualUsers.TryGetValue(expected.Key, out var current) ||
                    current.Role != expected.Value.Role ||
                    current.Hash != expected.Value.Hash)
                {
                    utilisateursCoherents = false;
                    break;
                }
            }

            if (actualUsers.Count != expectedUsers.Count)
            {
                utilisateursCoherents = false;
            }

            foreach (var actual in actualUsers)
            {
                if (!expectedUsers.ContainsKey(actual.Key))
                {
                    utilisateursCoherents = false;
                    break;
                }
            }
        }

        if (donneesStatiqueOk && anomaliesCount >= 420 && seedAnomaliesCount >= 420 && markerValue == "1" && utilisateursCoherents)
        {
            reader.Close();
            return;
        }

        if (!reader.IsClosed)
            reader.Close();

        using var tx = connection.BeginTransaction();

        if (seedAnomaliesCount > 0 && seedAnomaliesCount < 420)
        {
            using var deleteSeedCmd = connection.CreateCommand();
            deleteSeedCmd.Transaction = tx;
            deleteSeedCmd.CommandText = "DELETE FROM Anomalies WHERE ImagePreuve LIKE 'captures/anomalie_%';";
            deleteSeedCmd.ExecuteNonQuery();
        }

        using var deleteLegacyUsers = connection.CreateCommand();
        deleteLegacyUsers.Transaction = tx;
        deleteLegacyUsers.CommandText = @"
            DELETE FROM Utilisateurs
            WHERE Login NOT IN ('superviseur', 'qualite', 'admin', 'direction');
        ";
        deleteLegacyUsers.ExecuteNonQuery();

        using var insertCommand = connection.CreateCommand();
        insertCommand.Transaction = tx;
        insertCommand.CommandText = @"
            INSERT INTO Utilisateurs (IdUtilisateur, Login, MotDePasseHash, Role, NomAffichage)
            VALUES (1, 'superviseur', $hash1, 'SuperviseurProduction', 'Superviseur Production')
            ON CONFLICT(Login) DO UPDATE SET
                MotDePasseHash = excluded.MotDePasseHash,
                Role = excluded.Role,
                NomAffichage = excluded.NomAffichage;

            INSERT INTO Utilisateurs (IdUtilisateur, Login, MotDePasseHash, Role, NomAffichage)
            VALUES (2, 'qualite', $hash2, 'AuditeurQualite', 'Auditeur Qualité')
            ON CONFLICT(Login) DO UPDATE SET
                MotDePasseHash = excluded.MotDePasseHash,
                Role = excluded.Role,
                NomAffichage = excluded.NomAffichage;

            INSERT INTO Utilisateurs (IdUtilisateur, Login, MotDePasseHash, Role, NomAffichage)
            VALUES (3, 'admin', $hash3, 'AdminPit', 'Admin PIT')
            ON CONFLICT(Login) DO UPDATE SET
                MotDePasseHash = excluded.MotDePasseHash,
                Role = excluded.Role,
                NomAffichage = excluded.NomAffichage;

            INSERT INTO Utilisateurs (IdUtilisateur, Login, MotDePasseHash, Role, NomAffichage)
            VALUES (4, 'direction', $hash4, 'Direction', 'Direction')
            ON CONFLICT(Login) DO UPDATE SET
                MotDePasseHash = excluded.MotDePasseHash,
                Role = excluded.Role,
                NomAffichage = excluded.NomAffichage;

            INSERT OR IGNORE INTO Cameras (IdCamera, StatutConnexion)
            VALUES ('CAM-01', 'Active');

            INSERT OR IGNORE INTO Cameras (IdCamera, StatutConnexion)
            VALUES ('CAM-02', 'Active');

            INSERT OR IGNORE INTO Cameras (IdCamera, StatutConnexion)
            VALUES ('CAM-03', 'Active');

            INSERT OR IGNORE INTO Cameras (IdCamera, StatutConnexion)
            VALUES ('CAM-04', 'HorsLigne');

            INSERT OR IGNORE INTO Postes (IdPoste, LigneProduction, IdCamera)
            VALUES ('P01', 'Ligne El Fejja 01', 'CAM-01');

            INSERT OR IGNORE INTO Postes (IdPoste, LigneProduction, IdCamera)
            VALUES ('P02', 'Ligne El Fejja 02', 'CAM-02');

            INSERT OR IGNORE INTO Postes (IdPoste, LigneProduction, IdCamera)
            VALUES ('P03', 'Ligne El Fejja 03', 'CAM-03');

            INSERT OR IGNORE INTO Postes (IdPoste, LigneProduction, IdCamera)
            VALUES ('P04', 'Ligne El Fejja 04', 'CAM-04');

            INSERT OR IGNORE INTO Operateurs (MatriculeOp, NomOp, PrenomOp, Equipe)
            VALUES ('OP101', 'Ben Ali', 'Ahmed', 'Équipe A');

            INSERT OR IGNORE INTO Operateurs (MatriculeOp, NomOp, PrenomOp, Equipe)
            VALUES ('OP102', 'Trabelsi', 'Mohamed', 'Équipe B');

            INSERT OR IGNORE INTO Operateurs (MatriculeOp, NomOp, PrenomOp, Equipe)
            VALUES ('OP103', 'Jendoubi', 'Fatma', 'Équipe C');

            INSERT OR IGNORE INTO Operateurs (MatriculeOp, NomOp, PrenomOp, Equipe)
            VALUES ('OP104', 'Gharbi', 'Ines', 'Équipe A');

            INSERT OR IGNORE INTO Operateurs (MatriculeOp, NomOp, PrenomOp, Equipe)
            VALUES ('OP105', 'Chaabane', 'Youssef', 'Équipe B');

            INSERT OR IGNORE INTO Operateurs (MatriculeOp, NomOp, PrenomOp, Equipe)
            VALUES ('OP106', 'Mansouri', 'Amira', 'Équipe C');

            INSERT OR IGNORE INTO Operateurs (MatriculeOp, NomOp, PrenomOp, Equipe)
            VALUES ('OP107', 'Belhaj', 'Sami', 'Équipe A');

            INSERT OR IGNORE INTO Operateurs (MatriculeOp, NomOp, PrenomOp, Equipe)
            VALUES ('OP108', 'Kefi', 'Rania', 'Équipe B');

            INSERT OR IGNORE INTO Operateurs (MatriculeOp, NomOp, PrenomOp, Equipe)
            VALUES ('OP109', 'Bouzid', 'Karim', 'Équipe C');

            INSERT OR IGNORE INTO Operateurs (MatriculeOp, NomOp, PrenomOp, Equipe)
            VALUES ('OP110', 'Hamdi', 'Nour', 'Équipe A');

            INSERT OR IGNORE INTO Operateurs (MatriculeOp, NomOp, PrenomOp, Equipe)
            VALUES ('OP111', 'Sassi', 'Bilel', 'Équipe B');

            INSERT OR IGNORE INTO Operateurs (MatriculeOp, NomOp, PrenomOp, Equipe)
            VALUES ('OP112', 'Riahi', 'Salma', 'Équipe C');
        ";
        insertCommand.Parameters.AddWithValue("$hash1", Utilisateur.Hacher("sebn2026"));
        insertCommand.Parameters.AddWithValue("$hash2", Utilisateur.Hacher("sebn2026"));
        insertCommand.Parameters.AddWithValue("$hash3", Utilisateur.Hacher("sebn2026"));
        insertCommand.Parameters.AddWithValue("$hash4", Utilisateur.Hacher("sebn2026"));
        insertCommand.Parameters.AddWithValue("$hash5", Utilisateur.Hacher("sebn2026"));
        insertCommand.ExecuteNonQuery();

        var classesParType = new Dictionary<string, string[]>
        {
            ["Qualité"] = new[] { "connecteur_manquant", "fil_mal_positionne", "defaut_couleur", "sertissage_defectueux" },
            ["Production"] = new[] { "cable_mal_clipse", "sous_ensemble_incomplet" },
            ["5S"] = new[] { "outil_hors_zone", "poste_desordre" }
        };

        var rnd = new Random(42);
        var typesPonderes = new List<(string type, int poids)> { ("Qualité", 45), ("Production", 30), ("5S", 25) };
        var postes = new[] { "P01", "P02", "P03", "P04" };
        var operateurs = new[]
        {
            "OP101", "OP102", "OP103", "OP104", "OP105", "OP106",
            "OP107", "OP108", "OP109", "OP110", "OP111", "OP112"
        };

        using var anomalyCmd = connection.CreateCommand();
        anomalyCmd.Transaction = tx;
        anomalyCmd.CommandText = @"
            INSERT INTO Anomalies (DateHeure, TypeAnomalie, ClasseYolo, Confiance, ImagePreuve, Statut, IdPoste, MatriculeOp)
            VALUES ($date, $type, $classe, $confiance, $image, $statut, $poste, $matricule)
        ";
        anomalyCmd.Parameters.Add(new SqliteParameter("$date", DbType.DateTime));
        anomalyCmd.Parameters.Add(new SqliteParameter("$type", DbType.String));
        anomalyCmd.Parameters.Add(new SqliteParameter("$classe", DbType.String));
        anomalyCmd.Parameters.Add(new SqliteParameter("$confiance", DbType.Double));
        anomalyCmd.Parameters.Add(new SqliteParameter("$image", DbType.String));
        anomalyCmd.Parameters.Add(new SqliteParameter("$statut", DbType.Int32));
        anomalyCmd.Parameters.Add(new SqliteParameter("$poste", DbType.String));
        anomalyCmd.Parameters.Add(new SqliteParameter("$matricule", DbType.String));

        for (int id = 1; id <= 420; id++)
        {
            int joursEcart = rnd.Next(0, 30);
            int heure = 6 + rnd.Next(0, 16);
            int minute = rnd.Next(0, 60);
            var date = DateTime.Now.AddDays(-joursEcart).Date.AddHours(heure).AddMinutes(minute);

            string type = TirageSelonPoids(rnd, typesPonderes);
            var classes = classesParType[type];
            string classe = classes[rnd.Next(classes.Length)];
            double confiance = Math.Round(0.62 + rnd.NextDouble() * (0.98 - 0.62), 2);
            string poste = postes[rnd.Next(postes.Length)];
            string matricule = operateurs[rnd.Next(operateurs.Length)];
            double probaCorrigee = joursEcart > 2 ? 0.95 : 0.55;
            var statut = rnd.NextDouble() < probaCorrigee ? StatutAnomalie.Corrigee : StatutAnomalie.NonTraitee;

            anomalyCmd.Parameters["$date"].Value = date;
            anomalyCmd.Parameters["$type"].Value = type;
            anomalyCmd.Parameters["$classe"].Value = classe;
            anomalyCmd.Parameters["$confiance"].Value = confiance;
            anomalyCmd.Parameters["$image"].Value = $"captures/anomalie_{id:D4}.jpg";
            anomalyCmd.Parameters["$statut"].Value = (int)statut;
            anomalyCmd.Parameters["$poste"].Value = poste;
            anomalyCmd.Parameters["$matricule"].Value = matricule;
            anomalyCmd.ExecuteNonQuery();
        }

        using var seedMarkerInsert = connection.CreateCommand();
        seedMarkerInsert.Transaction = tx;
        seedMarkerInsert.CommandText = @"
            INSERT OR REPLACE INTO SeedState (Key, Value)
            VALUES ('DemoData', '1');
        ";
        seedMarkerInsert.ExecuteNonQuery();

        tx.Commit();
    }

    private static string TirageSelonPoids(Random rnd, List<(string type, int poids)> options)
    {
        int total = options.Sum(o => o.poids);
        int tirage = rnd.Next(total);
        int cumul = 0;
        foreach (var (type, poids) in options)
        {
            cumul += poids;
            if (tirage < cumul) return type;
        }
        return options[^1].type;
    }
}
