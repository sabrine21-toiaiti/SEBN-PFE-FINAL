using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SebnWeb.Data;
using SebnWeb.Models;
using SebnWeb.Services;

namespace SebnWeb.Pages;

public class DashboardPitModel : PageModel
{
    private readonly AppDataStore _store;
    public DashboardPitModel(AppDataStore store) => _store = store;

    public (int total, int nonTraitees, int aujourdhui, double tauxConformite) Stats { get; set; }
    public Dictionary<string, int> RepartitionPoste { get; set; } = new();
    public Dictionary<string, int> Evolution { get; set; } = new();

    public IActionResult OnGet()
    {
        if (HttpContext.Session.GetString("NomAffichage") == null)
            return RedirectToPage("/Index");
        if (HttpContext.Session.GetString("Role") != nameof(RoleUtilisateur.SuperviseurPit))
            return Redirect(RoleAccess.PageAccueil(HttpContext));

        Stats = _store.StatsGenerales();
        RepartitionPoste = _store.RepartitionParPoste();
        Evolution = _store.EvolutionJournaliere(14);
        return Page();
    }
}
