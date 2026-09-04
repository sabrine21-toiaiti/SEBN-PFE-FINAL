using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using SebnWeb.Models;

namespace SebnWeb.Data;

public class LoginResult
{
    public bool EstReussi { get; private set; }
    public Utilisateur? Utilisateur { get; private set; }
    public string? MessageErreur { get; private set; }

    public static LoginResult Succes(Utilisateur u) => new() { EstReussi = true, Utilisateur = u };
    public static LoginResult Echec(string message) => new() { EstReussi = false, MessageErreur = message };
}

public class NotificationRecord
{
    public int IdNotification { get; set; }
    public string Message { get; set; } = "";
    public string IdPoste { get; set; } = "";
    public DateTime DateCreation { get; set; }
}

public class UtilisateurRecord
{
    public int IdUtilisateur { get; set; }
    public string Login { get; set; } = "";
    public string MotDePasseHash { get; set; } = "";
    public RoleUtilisateur Role { get; set; }
    public string NomAffichage { get; set; } = "";
}

public class AppDataStore
{
    private readonly string _connectionString;
    private readonly string _contentRootPath;
    private readonly string _dataDirectory;
    private readonly object _verrou = new();

    public AppDataStore(IWebHostEnvironment env)
    {
        _contentRootPath = env.ContentRootPath;
        _dataDirectory = Environment.GetEnvironmentVariable("SEBN_DATA_DIR")
            ?? Path.Combine(env.ContentRootPath, "Data");
        Directory.CreateDirectory(_dataDirectory);
        var dbPath = Path.Combine(_dataDirectory, "sebn.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        InitialiserSchema();
    }

    private SqliteConnection CreateConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    private void InitialiserSchema()
    {
        var dbPath = Path.Combine(_dataDirectory, "sebn.db");
        if (File.Exists(dbPath))
        {
            try
            {
                using var probe = new SqliteConnection(_connectionString);
                probe.Open();
                using var schemaCheck = probe.CreateCommand();
                schemaCheck.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'Utilisateurs';";
                var tableSql = schemaCheck.ExecuteScalar()?.ToString();
                var contientAncienRole = !string.IsNullOrEmpty(tableSql) && (tableSql.Contains("SuperviseurProd") || tableSql.Contains("PitAdmin") || tableSql.Contains("'Direction'"));

                var contientColonnesVerrouillage = false;
                using (var pragmaCheck = probe.CreateCommand())
                {
                    pragmaCheck.CommandText = "PRAGMA table_info(Utilisateurs);";
                    using var pragmaReader = pragmaCheck.ExecuteReader();
                    while (pragmaReader.Read())
                    {
                        if (string.Equals(pragmaReader.GetString(1), "NbTentatives", StringComparison.OrdinalIgnoreCase))
                        {
                            contientColonnesVerrouillage = true;
                            break;
                        }
                    }
                }

                if (contientAncienRole || !contientColonnesVerrouillage)
                {
                    File.Delete(dbPath);
                }
            }
            catch
            {
                // Ignoré : le fichier peut être corrompu ou verrouillé au tout premier démarrage.
            }
        }

        using var connection = CreateConnection();

        var schema = File.ReadAllText(Path.Combine(_contentRootPath, "Data", "schema.sql"));
        using var command = connection.CreateCommand();
        command.CommandText = schema;
        command.ExecuteNonQuery();

        SeedGenerator.Initialiser(_connectionString);
    }

    public Utilisateur? VerifierUtilisateur(string login, string mdpHash)
    {
        lock (_verrou)
        {
            using var connection = CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT IdUtilisateur, Login, MotDePasseHash, Role, NomAffichage FROM Utilisateurs WHERE Login = $login AND MotDePasseHash = $mdpHash";
            cmd.Parameters.AddWithValue("$login", login);
            cmd.Parameters.AddWithValue("$mdpHash", mdpHash);

            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;

            var role = Enum.Parse<RoleUtilisateur>(reader.GetString(3));
            return UtilisateurFactory.Creer(role, reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(4));
        }
    }

    private const int MaxTentatives = 3;
    private static readonly TimeSpan DureeVerrouillage = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Authentification complète avec verrouillage temporaire après 3 échecs,
    /// conforme au scénario nominal d'authentification décrit au Chapitre 4 du rapport.
    /// </summary>
    public LoginResult TenterConnexion(string login, string motDePasseEnClair)
    {
        lock (_verrou)
        {
            using var connection = CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"SELECT IdUtilisateur, Login, MotDePasseHash, Role, NomAffichage, NbTentatives, VerrouJusqua
                                 FROM Utilisateurs WHERE Login = $login";
            cmd.Parameters.AddWithValue("$login", login);

            int id; string mdpHash; string roleStr; string nom; int tentatives; string? verrouStr;
            using (var reader = cmd.ExecuteReader())
            {
                if (!reader.Read())
                    return LoginResult.Echec("Login ou mot de passe incorrect.");

                id = reader.GetInt32(0);
                mdpHash = reader.GetString(2);
                roleStr = reader.GetString(3);
                nom = reader.GetString(4);
                tentatives = reader.GetInt32(5);
                verrouStr = reader.IsDBNull(6) ? null : reader.GetString(6);
            }

            if (verrouStr != null && DateTime.TryParse(verrouStr, out var verrouJusqua) && verrouJusqua > DateTime.Now)
            {
                var minutesRestantes = Math.Ceiling((verrouJusqua - DateTime.Now).TotalMinutes);
                return LoginResult.Echec($"Compte temporairement verrouillé suite à plusieurs échecs. Réessayez dans {minutesRestantes} min.");
            }

            if (Utilisateur.Hacher(motDePasseEnClair) != mdpHash)
            {
                var nouveauNbTentatives = tentatives + 1;
                using (var echecCmd = connection.CreateCommand())
                {
                    if (nouveauNbTentatives >= MaxTentatives)
                    {
                        echecCmd.CommandText = "UPDATE Utilisateurs SET NbTentatives = $n, VerrouJusqua = $verrou WHERE Login = $login";
                        echecCmd.Parameters.AddWithValue("$verrou", DateTime.Now.Add(DureeVerrouillage).ToString("s"));
                    }
                    else
                    {
                        echecCmd.CommandText = "UPDATE Utilisateurs SET NbTentatives = $n WHERE Login = $login";
                    }
                    echecCmd.Parameters.AddWithValue("$n", nouveauNbTentatives);
                    echecCmd.Parameters.AddWithValue("$login", login);
                    echecCmd.ExecuteNonQuery();
                }

                var restantes = MaxTentatives - nouveauNbTentatives;
                return restantes > 0
                    ? LoginResult.Echec($"Login ou mot de passe incorrect. ({restantes} tentative(s) restante(s))")
                    : LoginResult.Echec($"Login ou mot de passe incorrect. Compte verrouillé {DureeVerrouillage.TotalMinutes} min suite à {MaxTentatives} échecs.");
            }

            using (var resetCmd = connection.CreateCommand())
            {
                resetCmd.CommandText = "UPDATE Utilisateurs SET NbTentatives = 0, VerrouJusqua = NULL WHERE Login = $login";
                resetCmd.Parameters.AddWithValue("$login", login);
                resetCmd.ExecuteNonQuery();
            }

            var role = Enum.Parse<RoleUtilisateur>(roleStr);
            var utilisateur = UtilisateurFactory.Creer(role, id, login, mdpHash, nom);
            return LoginResult.Succes(utilisateur);
        }
    }

    public void EnregistrerParametre(string cle, string valeur)
    {
        lock (_verrou)
        {
            using var connection = CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO ParametresSysteme (Cle, Valeur)
                VALUES ($cle, $valeur)
                ON CONFLICT(Cle) DO UPDATE SET Valeur = excluded.Valeur
            ";
            cmd.Parameters.AddWithValue("$cle", cle);
            cmd.Parameters.AddWithValue("$valeur", valeur);
            cmd.ExecuteNonQuery();
        }
    }

    public string ObtenirParametre(string cle, string valeurParDefaut)
    {
        lock (_verrou)
        {
            using var connection = CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT Valeur FROM ParametresSysteme WHERE Cle = $cle";
            cmd.Parameters.AddWithValue("$cle", cle);
            var valeur = cmd.ExecuteScalar();
            return valeur?.ToString() ?? valeurParDefaut;
        }
    }

    public double ObtenirSeuilConfianceMinimale()
    {
        var valeur = ObtenirParametre("SeuilConfianceMinimale", "0.50");
        return double.TryParse(valeur, CultureInfo.InvariantCulture, out var seuil)
            ? Math.Clamp(seuil, 0.05, 0.99)
            : 0.50d;
    }

    public int ObtenirTimeoutPerteCameraSecondes()
    {
        var valeur = ObtenirParametre("TimeoutPerteCameraSecondes", "5");
        return int.TryParse(valeur, out var timeout)
            ? Math.Clamp(timeout, 1, 120)
            : 5;
    }

    public string? EnregistrerImagePreuve(byte[] imageBytes, string sousDossier = "captures")
    {
        if (imageBytes == null || imageBytes.Length == 0)
            return null;

        var repWebRoot = Path.Combine(_contentRootPath, "wwwroot");
        var dossier = Path.Combine(repWebRoot, sousDossier.TrimStart('/', '\\'));
        Directory.CreateDirectory(dossier);

        var nomFichier = $"{DateTime.UtcNow:yyyyMMdd_HHmmssfff}_{Guid.NewGuid():N}.jpg";
        var cheminComplet = Path.Combine(dossier, nomFichier);
        File.WriteAllBytes(cheminComplet, imageBytes);

        return Path.Combine(sousDossier.TrimStart('/', '\\'), nomFichier).Replace('\\', '/');
    }

    public string? EnregistrerImagePreuveDepuisBase64(string? imageBase64, string sousDossier = "captures")
    {
        if (string.IsNullOrWhiteSpace(imageBase64))
            return null;

        var base64 = imageBase64.Trim();
        if (base64.Contains(',') && base64.IndexOf(',') >= 0)
            base64 = base64.Split(',', 2)[1];

        base64 = Regex.Replace(base64, @"\s+", "");
        while (base64.Length % 4 != 0)
            base64 += "=";

        try
        {
            var bytes = Convert.FromBase64String(base64);
            return EnregistrerImagePreuve(bytes, sousDossier);
        }
        catch
        {
            return null;
        }
    }

    public List<UtilisateurRecord> ListeUtilisateurs()
    {
        lock (_verrou)
        {
            using var connection = CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT IdUtilisateur, Login, MotDePasseHash, Role, NomAffichage FROM Utilisateurs";
            using var reader = cmd.ExecuteReader();
            var result = new List<UtilisateurRecord>();
            while (reader.Read())
            {
                result.Add(new UtilisateurRecord
                {
                    IdUtilisateur = reader.GetInt32(0),
                    Login = reader.GetString(1),
                    MotDePasseHash = reader.GetString(2),
                    Role = Enum.Parse<RoleUtilisateur>(reader.GetString(3)),
                    NomAffichage = reader.GetString(4)
                });
            }
            return result;
        }
    }

    public bool CreerUtilisateur(string login, string motDePasse, RoleUtilisateur role, string nomAffichage)
    {
        lock (_verrou)
        {
            using var connection = CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"INSERT INTO Utilisateurs (Login, MotDePasseHash, Role, NomAffichage)
                                VALUES ($login, $hash, $role, $nom)";
            cmd.Parameters.AddWithValue("$login", login);
            cmd.Parameters.AddWithValue("$hash", Utilisateur.Hacher(motDePasse));
            cmd.Parameters.AddWithValue("$role", role.ToString());
            cmd.Parameters.AddWithValue("$nom", nomAffichage);
            try
            {
                cmd.ExecuteNonQuery();
                return true;
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
            {
                return false;
            }
        }
    }

    public bool ModifierUtilisateur(int idUtilisateur, string login, string nomAffichage,
        RoleUtilisateur role, string? motDePasse)
    {
        lock (_verrou)
        {
            using var connection = CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = string.IsNullOrWhiteSpace(motDePasse)
                ? @"UPDATE Utilisateurs SET Login = $login, NomAffichage = $nom, Role = $role
                   WHERE IdUtilisateur = $id"
                : @"UPDATE Utilisateurs SET Login = $login, NomAffichage = $nom, Role = $role,
                   MotDePasseHash = $hash, NbTentatives = 0, VerrouJusqua = NULL
                   WHERE IdUtilisateur = $id";
            cmd.Parameters.AddWithValue("$login", login);
            cmd.Parameters.AddWithValue("$nom", nomAffichage);
            cmd.Parameters.AddWithValue("$role", role.ToString());
            cmd.Parameters.AddWithValue("$id", idUtilisateur);
            if (!string.IsNullOrWhiteSpace(motDePasse))
                cmd.Parameters.AddWithValue("$hash", Utilisateur.Hacher(motDePasse));
            try
            {
                return cmd.ExecuteNonQuery() == 1;
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
            {
                return false;
            }
        }
    }

    public bool SupprimerUtilisateur(int idUtilisateur)
    {
        lock (_verrou)
        {
            using var connection = CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM Utilisateurs WHERE IdUtilisateur = $id";
            cmd.Parameters.AddWithValue("$id", idUtilisateur);
            return cmd.ExecuteNonQuery() == 1;
        }
    }

    public int InsererAnomalie(string typeAnomalie, string classeYolo, double confiance,
        string imagePreuve, string idPoste, string matriculeOp)
    {
        lock (_verrou)
        {
            using var connection = CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO Anomalies (DateHeure, TypeAnomalie, ClasseYolo, Confiance, ImagePreuve, Statut, IdPoste, MatriculeOp)
                VALUES ($date, $type, $classe, $confiance, $image, $statut, $poste, $matricule)
            ";
            cmd.Parameters.AddWithValue("$date", DateTime.Now);
            cmd.Parameters.AddWithValue("$type", typeAnomalie);
            cmd.Parameters.AddWithValue("$classe", classeYolo);
            cmd.Parameters.AddWithValue("$confiance", confiance);
            cmd.Parameters.AddWithValue("$image", imagePreuve);
            cmd.Parameters.AddWithValue("$statut", (int)StatutAnomalie.NonTraitee);
            cmd.Parameters.AddWithValue("$poste", idPoste);
            cmd.Parameters.AddWithValue("$matricule", matriculeOp);
            cmd.ExecuteNonQuery();
            using var lastIdCmd = connection.CreateCommand();
            lastIdCmd.CommandText = "SELECT last_insert_rowid();";
            return Convert.ToInt32(lastIdCmd.ExecuteScalar());
        }
    }

    public int CompterHistorique(string? type = null, StatutAnomalie? statut = null,
        string? idPoste = null)
    {
        lock (_verrou)
        {
            using var connection = CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT COUNT(*)
                FROM Anomalies
                WHERE ($type IS NULL OR TypeAnomalie = $type)
                  AND ($statut IS NULL OR Statut = $statut)
                  AND ($poste IS NULL OR IdPoste = $poste)
            ";
            cmd.Parameters.AddWithValue("$type", (object?)type ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$statut", statut.HasValue ? (object)(int)statut.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("$poste", (object?)idPoste ?? DBNull.Value);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
    }

    public List<Anomalie> RecupererHistorique(string? type = null, StatutAnomalie? statut = null,
        string? idPoste = null, int pageSize = 50, int offset = 0)
    {
        lock (_verrou)
        {
            using var connection = CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT IdAnomalie, DateHeure, TypeAnomalie, ClasseYolo, Confiance, ImagePreuve, Statut, IdPoste, MatriculeOp
                FROM Anomalies
                WHERE ($type IS NULL OR TypeAnomalie = $type)
                  AND ($statut IS NULL OR Statut = $statut)
                  AND ($poste IS NULL OR IdPoste = $poste)
                ORDER BY DateHeure DESC
                LIMIT $pageSize OFFSET $offset
            ";
            cmd.Parameters.AddWithValue("$type", (object?)type ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$statut", statut.HasValue ? (object)(int)statut.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("$poste", (object?)idPoste ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$pageSize", pageSize);
            cmd.Parameters.AddWithValue("$offset", offset);
            using var reader = cmd.ExecuteReader();
            var result = new List<Anomalie>();
            while (reader.Read())
            {
                result.Add(new Anomalie
                {
                    IdAnomalie = reader.GetInt32(0),
                    DateHeure = reader.GetDateTime(1),
                    TypeAnomalie = reader.GetString(2),
                    ClasseYolo = reader.GetString(3),
                    Confiance = reader.GetDouble(4),
                    ImagePreuve = reader.IsDBNull(5) ? "" : reader.GetString(5),
                    Statut = (StatutAnomalie)reader.GetInt32(6),
                    IdPoste = reader.GetString(7),
                    MatriculeOp = reader.GetString(8)
                });
            }
            return result;
        }
    }

    public void CloturerAnomalie(int idAnomalie)
    {
        lock (_verrou)
        {
            using var connection = CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE Anomalies SET Statut = $statut WHERE IdAnomalie = $id";
            cmd.Parameters.AddWithValue("$statut", (int)StatutAnomalie.Corrigee);
            cmd.Parameters.AddWithValue("$id", idAnomalie);
            cmd.ExecuteNonQuery();
        }
    }

    public List<Poste> ListePostes()
    {
        lock (_verrou)
        {
            using var connection = CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT IdPoste, LigneProduction, COALESCE(IdCamera, '') FROM Postes ORDER BY IdPoste";
            using var reader = cmd.ExecuteReader();
            var result = new List<Poste>();
            while (reader.Read())
            {
                result.Add(new Poste { IdPoste = reader.GetString(0), LigneProduction = reader.GetString(1), IdCamera = reader.GetString(2) });
            }
            return result;
        }
    }

    public List<Camera> ListeCameras()
    {
        lock (_verrou)
        {
            using var connection = CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT IdCamera, StatutConnexion FROM Cameras ORDER BY IdCamera";
            using var reader = cmd.ExecuteReader();
            var result = new List<Camera>();
            while (reader.Read())
                result.Add(new Camera { IdCamera = reader.GetString(0), StatutConnexion = Enum.Parse<StatutConnexion>(reader.GetString(1)) });
            return result;
        }
    }

    public bool CreerCamera(string idCamera)
    {
        if (string.IsNullOrWhiteSpace(idCamera)) return false;
        lock (_verrou)
        {
            using var connection = CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT INTO Cameras (IdCamera, StatutConnexion) VALUES ($id, 'Active')";
            cmd.Parameters.AddWithValue("$id", idCamera.Trim());
            try { return cmd.ExecuteNonQuery() == 1; } catch (SqliteException) { return false; }
        }
    }

    public bool SupprimerCamera(string idCamera)
    {
        lock (_verrou)
        {
            using var connection = CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM Cameras WHERE IdCamera = $id AND NOT EXISTS (SELECT 1 FROM Postes WHERE IdCamera = $id)";
            cmd.Parameters.AddWithValue("$id", idCamera);
            return cmd.ExecuteNonQuery() == 1;
        }
    }

    public bool CreerPoste(string idPoste, string ligneProduction, string? idCamera)
    {
        if (string.IsNullOrWhiteSpace(idPoste) || string.IsNullOrWhiteSpace(ligneProduction)) return false;
        lock (_verrou)
        {
            using var connection = CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT INTO Postes (IdPoste, LigneProduction, IdCamera) VALUES ($id, $ligne, $camera)";
            cmd.Parameters.AddWithValue("$id", idPoste.Trim());
            cmd.Parameters.AddWithValue("$ligne", ligneProduction.Trim());
            cmd.Parameters.AddWithValue("$camera", string.IsNullOrWhiteSpace(idCamera) ? DBNull.Value : idCamera.Trim());
            try { return cmd.ExecuteNonQuery() == 1; } catch (SqliteException) { return false; }
        }
    }

    public bool SupprimerPoste(string idPoste)
    {
        lock (_verrou)
        {
            using var connection = CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM Postes WHERE IdPoste = $id";
            cmd.Parameters.AddWithValue("$id", idPoste);
            return cmd.ExecuteNonQuery() == 1;
        }
    }

    public bool PosteExiste(string idPoste)
    {
        lock (_verrou)
        {
            using var connection = CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT EXISTS(SELECT 1 FROM Postes WHERE IdPoste = $id)";
            cmd.Parameters.AddWithValue("$id", idPoste);
            return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
        }
    }

    public Operateur? TrouverOperateur(string matricule)
    {
        lock (_verrou)
        {
            using var connection = CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT MatriculeOp FROM Operateurs WHERE MatriculeOp = $matricule";
            cmd.Parameters.AddWithValue("$matricule", matricule);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;
            return new Operateur { MatriculeOp = reader.GetString(0) };
        }
    }

    public List<Operateur> ListeOperateurs()
    {
        lock (_verrou)
        {
            using var connection = CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT MatriculeOp, NomOp, PrenomOp, Equipe FROM Operateurs ORDER BY MatriculeOp";
            using var reader = cmd.ExecuteReader();
            var result = new List<Operateur>();
            while (reader.Read())
            {
                result.Add(new Operateur
                {
                    MatriculeOp = reader.GetString(0),
                    NomOp = reader.GetString(1),
                    PrenomOp = reader.GetString(2),
                    Equipe = reader.GetString(3)
                });
            }
            return result;
        }
    }

    public string LigneProduction(string idPoste)
    {
        lock (_verrou)
        {
            using var connection = CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT LigneProduction FROM Postes WHERE IdPoste = $id";
            cmd.Parameters.AddWithValue("$id", idPoste);
            var result = cmd.ExecuteScalar();
            return result?.ToString() ?? idPoste;
        }
    }

    public (int total, int nonTraitees, int aujourdhui, double tauxConformite) StatsGenerales()
    {
        lock (_verrou)
        {
            using var connection = CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT COUNT(*), COALESCE(SUM(CASE WHEN Statut = $nonTraitee THEN 1 ELSE 0 END), 0), COALESCE(SUM(CASE WHEN DateHeure >= $today THEN 1 ELSE 0 END), 0)
                FROM Anomalies
            ";
            cmd.Parameters.AddWithValue("$today", DateTime.Today);
            cmd.Parameters.AddWithValue("$nonTraitee", (int)StatutAnomalie.NonTraitee);
            using var reader = cmd.ExecuteReader();
            reader.Read();
            int total = reader.GetInt32(0);
            int nonTraitees = reader.GetInt32(1);
            int aujourdhui = reader.GetInt32(2);
            double taux = total == 0 ? 100.0 : Math.Round(100.0 - (nonTraitees * 100.0 / total), 1);
            return (total, nonTraitees, aujourdhui, taux);
        }
    }

    public Dictionary<string, int> RepartitionParType()
    {
        lock (_verrou)
        {
            using var connection = CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT TypeAnomalie, COUNT(*) FROM Anomalies GROUP BY TypeAnomalie";
            using var reader = cmd.ExecuteReader();
            var result = new Dictionary<string, int>();
            while (reader.Read()) result[reader.GetString(0)] = reader.GetInt32(1);
            return result;
        }
    }

    public Dictionary<string, int> RepartitionParPoste()
    {
        lock (_verrou)
        {
            using var connection = CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT IdPoste, COUNT(*) FROM Anomalies GROUP BY IdPoste";
            using var reader = cmd.ExecuteReader();
            var result = new Dictionary<string, int>();
            while (reader.Read()) result[reader.GetString(0)] = reader.GetInt32(1);
            return result;
        }
    }

    public Dictionary<string, int> EvolutionJournaliere(int jours = 14)
    {
        lock (_verrou)
        {
            using var connection = CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT strftime('%Y-%m-%d', DateHeure), COUNT(*)
                FROM Anomalies
                WHERE DateHeure >= $since
                GROUP BY strftime('%Y-%m-%d', DateHeure)
                ORDER BY 1
            ";
            cmd.Parameters.AddWithValue("$since", DateTime.Today.AddDays(-jours));
            using var reader = cmd.ExecuteReader();
            var result = new Dictionary<string, int>();
            while (reader.Read()) result[reader.GetString(0)] = reader.GetInt32(1);
            return result;
        }
    }

    public List<(string classe, int total)> TopDefauts(int limite = 5)
    {
        lock (_verrou)
        {
            using var connection = CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT ClasseYolo, COUNT(*) FROM Anomalies GROUP BY ClasseYolo ORDER BY 2 DESC LIMIT $limite";
            cmd.Parameters.AddWithValue("$limite", limite);
            using var reader = cmd.ExecuteReader();
            var result = new List<(string classe, int total)>();
            while (reader.Read()) result.Add((reader.GetString(0), reader.GetInt32(1)));
            return result;
        }
    }

    /// <summary>
    /// Enregistre une notification (ex : perte du flux caméra détectée après timeout),
    /// conforme au scénario d'exception du Chapitre 4.IV.4 du rapport.
    /// </summary>
    public void SignalerPerteFluxCamera(string idPoste)
    {
        lock (_verrou)
        {
            using var connection = CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"INSERT INTO Notifications (Message, IdPoste, DateCreation, Lue)
                                 VALUES ($message, $poste, $date, 0)";
            cmd.Parameters.AddWithValue("$message", $"Perte du flux caméra détectée sur le poste {idPoste} après expiration du délai d'attente.");
            cmd.Parameters.AddWithValue("$poste", idPoste);
            cmd.Parameters.AddWithValue("$date", DateTime.Now.ToString("s"));
            cmd.ExecuteNonQuery();
        }
    }

    public List<NotificationRecord> ListeNotificationsNonLues()
    {
        lock (_verrou)
        {
            using var connection = CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"SELECT IdNotification, Message, IdPoste, DateCreation
                                 FROM Notifications WHERE Lue = 0 ORDER BY DateCreation DESC";
            using var reader = cmd.ExecuteReader();
            var result = new List<NotificationRecord>();
            while (reader.Read())
            {
                result.Add(new NotificationRecord
                {
                    IdNotification = reader.GetInt32(0),
                    Message = reader.GetString(1),
                    IdPoste = reader.GetString(2),
                    DateCreation = DateTime.Parse(reader.GetString(3))
                });
            }
            return result;
        }
    }

    public void MarquerNotificationLue(int idNotification)
    {
        lock (_verrou)
        {
            using var connection = CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE Notifications SET Lue = 1 WHERE IdNotification = $id";
            cmd.Parameters.AddWithValue("$id", idNotification);
            cmd.ExecuteNonQuery();
        }
    }
}
