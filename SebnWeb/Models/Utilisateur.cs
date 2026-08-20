using System.Security.Cryptography;
using System.Text;

namespace SebnWeb.Models;

/// <summary>
/// Classe abstraite - factorise le comportement commun des acteurs authentifiés
/// conformément au rapport PFE : Opérateur de Production, Superviseur Qualité,
/// Superviseur PIT et Administrateur.
/// </summary>
public abstract class Utilisateur
{
    public int IdUtilisateur { get; set; }
    public string Login { get; set; } = "";
    public string MotDePasseHash { get; set; } = "";
    public RoleUtilisateur Role { get; protected set; }
    public string NomAffichage { get; set; } = "";

    public static string Hacher(string motDePasse)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(motDePasse));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public bool VerifierMotDePasse(string mdpSaisi) => Hacher(mdpSaisi) == MotDePasseHash;

    public abstract string ConsulterDashboard();
}

public class OperateurProduction : Utilisateur
{
    public OperateurProduction() => Role = RoleUtilisateur.OperateurProduction;

    public override string ConsulterDashboard() => "Dashboard : flux de production";

    public bool ValiderRepriseDuTravail(Anomalie anomalie) => anomalie.Statut == StatutAnomalie.Corrigee;

    public void CloturerAnomalie(Anomalie anomalie) => anomalie.Cloturer();
}

public class SuperviseurQualite : Utilisateur
{
    public SuperviseurQualite() => Role = RoleUtilisateur.SuperviseurQualite;

    public override string ConsulterDashboard() => "Dashboard : indicateurs qualité (KPI)";
}

public class SuperviseurPIT : Utilisateur
{
    public SuperviseurPIT() => Role = RoleUtilisateur.SuperviseurPit;

    public override string ConsulterDashboard() => "Dashboard : supervision système";
}

public class Administrateur : Utilisateur
{
    public Administrateur() => Role = RoleUtilisateur.Administrateur;

    public override string ConsulterDashboard() => "Dashboard : administration système";
}

public static class UtilisateurFactory
{
    public static Utilisateur Creer(RoleUtilisateur role, int id, string login, string mdpHash, string nomAffichage)
    {
        Utilisateur u = role switch
        {
            RoleUtilisateur.OperateurProduction => new OperateurProduction(),
            RoleUtilisateur.SuperviseurQualite => new SuperviseurQualite(),
            RoleUtilisateur.SuperviseurPit => new SuperviseurPIT(),
            RoleUtilisateur.Administrateur => new Administrateur(),
            _ => throw new ArgumentOutOfRangeException(nameof(role))
        };
        u.IdUtilisateur = id;
        u.Login = login;
        u.MotDePasseHash = mdpHash;
        u.NomAffichage = nomAffichage;
        return u;
    }
}
