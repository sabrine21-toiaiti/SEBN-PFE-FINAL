using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SebnWeb.Data;
using SebnWeb.Models;

namespace SebnWeb.Pages;

public class HistoriqueModel : PageModel
{
    private readonly AppDataStore _store;
    public HistoriqueModel(AppDataStore store) => _store = store;

    public List<Anomalie> Historique { get; set; } = new();
    public List<Poste> Postes { get; set; } = new();
    public Dictionary<string, string> NomsOperateurs { get; set; } = new();
    public Dictionary<string, string> LignesPostes { get; set; } = new();

    [BindProperty(SupportsGet = true)] public string TypeFiltre { get; set; } = "Tous";
    [BindProperty(SupportsGet = true)] public string StatutFiltre { get; set; } = "Tous";
    [BindProperty(SupportsGet = true)] public string PosteFiltre { get; set; } = "Tous";

    public string? RoleActuel { get; set; }

    public IActionResult OnGet()
    {
        if (HttpContext.Session.GetString("NomAffichage") == null)
            return RedirectToPage("/Index");

        RoleActuel = HttpContext.Session.GetString("Role");
        Postes = _store.ListePostes();
        foreach (var p in Postes) LignesPostes[p.IdPoste] = p.LigneProduction;

        StatutAnomalie? statut = StatutFiltre switch
        {
            "NON_TRAITEE" => StatutAnomalie.NonTraitee,
            "CORRIGEE" => StatutAnomalie.Corrigee,
            _ => null
        };

        Historique = _store.RecupererHistorique(TypeFiltre, statut, PosteFiltre);

        foreach (var a in Historique)
        {
            var op = _store.TrouverOperateur(a.MatriculeOp);
            NomsOperateurs[a.MatriculeOp] = op?.NomComplet ?? a.MatriculeOp;
        }
        return Page();
    }

    public IActionResult OnPostCloturer(int id)
    {
        _store.CloturerAnomalie(id);
        return RedirectToPage(new { TypeFiltre, StatutFiltre, PosteFiltre });
    }
}
