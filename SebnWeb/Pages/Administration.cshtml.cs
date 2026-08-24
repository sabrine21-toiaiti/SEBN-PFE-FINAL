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

    public IActionResult OnGet()
    {
        if (HttpContext.Session.GetString("NomAffichage") == null)
            return RedirectToPage("/Index");
        if (HttpContext.Session.GetString("Role") != nameof(RoleUtilisateur.Administrateur))
            return Redirect(RoleAccess.PageAccueil(HttpContext));

        Postes = _store.ListePostes();
        Utilisateurs = _store.ListeUtilisateurs();
        return Page();
    }
}
