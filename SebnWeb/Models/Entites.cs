namespace SebnWeb.Models;

public class Operateur
{
    public string MatriculeOp { get; set; } = "";
    public string NomOp { get; set; } = "";
    public string PrenomOp { get; set; } = "";
    public string Equipe { get; set; } = "";

    public string NomComplet => $"{PrenomOp} {NomOp}";
}

public class Camera
{
    public string IdCamera { get; set; } = "";
    public StatutConnexion StatutConnexion { get; set; } = StatutConnexion.Active;

    public bool EstActive() => StatutConnexion == StatutConnexion.Active;
}

public class Poste
{
    public string IdPoste { get; set; } = "";
    public string LigneProduction { get; set; } = "";
    public string IdCamera { get; set; } = "";
}

public class Anomalie
{
    public int IdAnomalie { get; set; }
    public DateTime DateHeure { get; set; }
    public string TypeAnomalie { get; set; } = "";   // "Qualité" / "Production" / "5S"
    public string ClasseYolo { get; set; } = "";
    public double Confiance { get; set; }
    public string ImagePreuve { get; set; } = "";
    public StatutAnomalie Statut { get; set; } = StatutAnomalie.NonTraitee;
    public string IdPoste { get; set; } = "";
    public string MatriculeOp { get; set; } = "";

    public void Cloturer() => Statut = StatutAnomalie.Corrigee;
}
