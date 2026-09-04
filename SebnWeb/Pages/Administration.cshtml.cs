using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SebnWeb.Data;
using SebnWeb.Models;
using SebnWeb.Services;

namespace SebnWeb.Pages;

public class AdministrationModel : PageModel
{
    private readonly AppDataStore _store;
    public AdministrationModel(AppDataStore store) => _store = store;

    public List<SebnWeb.Models.Poste> Postes { get; set; } = new();
    public List<SebnWeb.Models.Camera> Cameras { get; set; } = new();
    public List<UtilisateurRecord> Utilisateurs { get; set; } = new();

    [BindProperty] public double SeuilConfiance { get; set; } = 0.50;
    [BindProperty] public int TimeoutPerteCamera { get; set; } = 5;
    [BindProperty] public int IdUtilisateur { get; set; }
    [BindProperty] public string LoginUtilisateur { get; set; } = "";
    [BindProperty] public string MotDePasseUtilisateur { get; set; } = "";
    [BindProperty] public string NomUtilisateur { get; set; } = "";
    [BindProperty] public RoleUtilisateur RoleUtilisateur { get; set; }
    [BindProperty] public string IdPoste { get; set; } = "";
    [BindProperty] public string LigneProduction { get; set; } = "";
    [BindProperty] public string IdCamera { get; set; } = "";

    public IActionResult OnGet()
    {
        if (HttpContext.Session.GetString("NomAffichage") == null)
            return RedirectToPage("/Index");
        if (HttpContext.Session.GetString("Role") != nameof(RoleUtilisateur.AdminPit))
            return Redirect(RoleAccess.PageAccueil(HttpContext));

        Postes = _store.ListePostes();
        Cameras = _store.ListeCameras();
        Utilisateurs = _store.ListeUtilisateurs();
        SeuilConfiance = _store.ObtenirSeuilConfianceMinimale();
        TimeoutPerteCamera = _store.ObtenirTimeoutPerteCameraSecondes();
        return Page();
    }

    public IActionResult OnPostEnregistrer()
    {
        if (HttpContext.Session.GetString("NomAffichage") == null)
            return RedirectToPage("/Index");
        if (HttpContext.Session.GetString("Role") != nameof(RoleUtilisateur.AdminPit))
            return Redirect(RoleAccess.PageAccueil(HttpContext));

        var seuil = Math.Clamp(SeuilConfiance, 0.05, 0.99);
        var timeout = Math.Clamp(TimeoutPerteCamera, 1, 120);

        _store.EnregistrerParametre("SeuilConfianceMinimale", seuil.ToString("0.##", CultureInfo.InvariantCulture));
        _store.EnregistrerParametre("TimeoutPerteCameraSecondes", timeout.ToString(CultureInfo.InvariantCulture));

        TempData["MessageAdmin"] = "Paramètres IA enregistrés avec succès.";
        return RedirectToPage();
    }

    public IActionResult OnPostAjouterUtilisateur()
    {
        if (!EstAdministrateur()) return Redirect(RoleAccess.PageAccueil(HttpContext));
        if (!FormulaireUtilisateurValide(true, out var erreur))
        {
            TempData["ErreurAdmin"] = erreur;
            return RedirectToPage();
        }

        var cree = _store.CreerUtilisateur(LoginUtilisateur.Trim(), MotDePasseUtilisateur,
            RoleUtilisateur, NomUtilisateur.Trim());
        TempData[cree ? "MessageAdmin" : "ErreurAdmin"] = cree
            ? "Acteur ajouté avec succès."
            : "Ce login existe déjà ou les données sont invalides.";
        return RedirectToPage();
    }

    public IActionResult OnPostAjouterPoste()
    {
        if (!EstAdministrateur()) return Redirect(RoleAccess.PageAccueil(HttpContext));
        var ajoute = _store.CreerPoste(IdPoste, LigneProduction, IdCamera);
        TempData[ajoute ? "MessageAdmin" : "ErreurAdmin"] = ajoute
            ? "Poste ajouté avec succès."
            : "Impossible d'ajouter le poste ou la caméra est invalide.";
        return RedirectToPage();
    }

    public IActionResult OnPostSupprimerPoste()
    {
        if (!EstAdministrateur()) return Redirect(RoleAccess.PageAccueil(HttpContext));
        TempData[_store.SupprimerPoste(IdPoste) ? "MessageAdmin" : "ErreurAdmin"] = "Poste supprimé avec succès.";
        return RedirectToPage();
    }

    public IActionResult OnPostAjouterCamera()
    {
        if (!EstAdministrateur()) return Redirect(RoleAccess.PageAccueil(HttpContext));
        TempData[_store.CreerCamera(IdCamera) ? "MessageAdmin" : "ErreurAdmin"] = "Caméra ajoutée avec succès.";
        return RedirectToPage();
    }

    public IActionResult OnPostSupprimerCamera()
    {
        if (!EstAdministrateur()) return Redirect(RoleAccess.PageAccueil(HttpContext));
        TempData[_store.SupprimerCamera(IdCamera) ? "MessageAdmin" : "ErreurAdmin"] = "Caméra supprimée ou encore utilisée par un poste.";
        return RedirectToPage();
    }

    public IActionResult OnPostModifierUtilisateur()
    {
        if (!EstAdministrateur()) return Redirect(RoleAccess.PageAccueil(HttpContext));
        if (!FormulaireUtilisateurValide(false, out var erreur))
        {
            TempData["ErreurAdmin"] = erreur;
            return RedirectToPage();
        }

        var modifie = _store.ModifierUtilisateur(IdUtilisateur, LoginUtilisateur.Trim(),
            NomUtilisateur.Trim(), RoleUtilisateur, MotDePasseUtilisateur);
        TempData[modifie ? "MessageAdmin" : "ErreurAdmin"] = modifie
            ? "Acteur modifié avec succès."
            : "Modification impossible : login déjà utilisé ou acteur introuvable.";
        return RedirectToPage();
    }

    public IActionResult OnPostSupprimerUtilisateur()
    {
        if (!EstAdministrateur()) return Redirect(RoleAccess.PageAccueil(HttpContext));
        if (IdUtilisateur == HttpContext.Session.GetInt32("IdUtilisateur"))
        {
            TempData["ErreurAdmin"] = "Vous ne pouvez pas supprimer votre propre compte.";
            return RedirectToPage();
        }

        TempData[_store.SupprimerUtilisateur(IdUtilisateur) ? "MessageAdmin" : "ErreurAdmin"] =
            "Acteur supprimé avec succès.";
        return RedirectToPage();
    }

    private bool EstAdministrateur() =>
        HttpContext.Session.GetString("NomAffichage") != null &&
        HttpContext.Session.GetString("Role") == nameof(RoleUtilisateur.AdminPit);

    private bool FormulaireUtilisateurValide(bool motDePasseObligatoire, out string erreur)
    {
        erreur = "";
        if (string.IsNullOrWhiteSpace(LoginUtilisateur) || string.IsNullOrWhiteSpace(NomUtilisateur))
            erreur = "Le login et le nom affiché sont obligatoires.";
        else if (motDePasseObligatoire && string.IsNullOrWhiteSpace(MotDePasseUtilisateur))
            erreur = "Le mot de passe est obligatoire pour un nouvel acteur.";
        else if (!Enum.IsDefined(RoleUtilisateur))
            erreur = "Le rôle sélectionné est invalide.";
        else if (!string.IsNullOrWhiteSpace(MotDePasseUtilisateur) && MotDePasseUtilisateur.Length < 6)
            erreur = "Le mot de passe doit contenir au moins 6 caractères.";
        return erreur.Length == 0;
    }
}
