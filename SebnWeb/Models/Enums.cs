namespace SebnWeb.Models;

public enum RoleUtilisateur
{
    OperateurProduction,
    SuperviseurQualite,
    SuperviseurPit,
    Administrateur
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
        RoleUtilisateur.OperateurProduction => "Opérateur de Production",
        RoleUtilisateur.SuperviseurQualite => "Superviseur Qualité",
        RoleUtilisateur.SuperviseurPit => "Superviseur PIT",
        RoleUtilisateur.Administrateur => "Administrateur",
        _ => role.ToString()
    };

    public static string Libelle(this StatutAnomalie statut) => statut switch
    {
        StatutAnomalie.NonTraitee => "Non traitée",
        StatutAnomalie.Corrigee => "Corrigée",
        _ => statut.ToString()
    };
}
