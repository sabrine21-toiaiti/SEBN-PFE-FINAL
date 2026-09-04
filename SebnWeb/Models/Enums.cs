namespace SebnWeb.Models;

public enum RoleUtilisateur
{
    SuperviseurProduction,
    AuditeurQualite,
    AdminPit,
    Direction
}

public enum StatutAnomalie
{
    NonTraitee,
    Corrigee
}

public enum StatutConnexion
{
    Active,
    HorsLigne
}

public static class EnumLabels
{
    public static string Libelle(this RoleUtilisateur role) => role switch
    {
        RoleUtilisateur.SuperviseurProduction => "Superviseur Production",
        RoleUtilisateur.AuditeurQualite => "Auditeur Qualité",
        RoleUtilisateur.AdminPit => "Admin PIT",
        RoleUtilisateur.Direction => "Direction",
        _ => role.ToString()
    };

    public static string Libelle(this StatutAnomalie statut) => statut switch
    {
        StatutAnomalie.NonTraitee => "Non traitée",
        StatutAnomalie.Corrigee => "Corrigée",
        _ => statut.ToString()
    };
}
