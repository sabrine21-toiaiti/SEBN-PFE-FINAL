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

    public IActionResult OnGet()
    {
        if (HttpContext.Session.GetString("NomAffichage") == null)
            return RedirectToPage("/Index");
        if (HttpContext.Session.GetString("Role") != nameof(RoleUtilisateur.Administrateur))
            return Redirect(RoleAccess.PageAccueil(HttpContext));

        Postes = _store.ListePostes();
        Utilisateurs = _store.ListeUtilisateurs();
        SeuilConfiance = _store.ObtenirSeuilConfianceMinimale();
        TimeoutPerteCamera = _store.ObtenirTimeoutPerteCameraSecondes();
        return Page();
    }

    public IActionResult OnPostEnregistrer()
    {
        if (HttpContext.Session.GetString("NomAffichage") == null)
            return RedirectToPage("/Index");
        if (HttpContext.Session.GetString("Role") != nameof(RoleUtilisateur.Administrateur))
            return Redirect(RoleAccess.PageAccueil(HttpContext));

        var seuil = Math.Clamp(SeuilConfiance, 0.05, 0.99);
        var timeout = Math.Clamp(TimeoutPerteCamera, 1, 120);

        _store.EnregistrerParametre("SeuilConfianceMinimale", seuil.ToString("0.##", CultureInfo.InvariantCulture));
        _store.EnregistrerParametre("TimeoutPerteCameraSecondes", timeout.ToString(CultureInfo.InvariantCulture));

        TempData["MessageAdmin"] = "Paramètres IA enregistrés avec succès.";
        return RedirectToPage();
    }
}
