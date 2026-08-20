using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SebnWeb.Data;

namespace SebnWeb.Pages;

public class DashboardModel : PageModel
{
    private readonly AppDataStore _store;
    public DashboardModel(AppDataStore store) => _store = store;

    public (int total, int nonTraitees, int aujourdhui, double tauxConformite) Stats { get; set; }
    public Dictionary<string, int> RepartitionType { get; set; } = new();
    public Dictionary<string, int> RepartitionPoste { get; set; } = new();
    public Dictionary<string, int> Evolution { get; set; } = new();
    public List<(string classe, int total)> TopDefauts { get; set; } = new();

    public IActionResult OnGet()
    {
        if (HttpContext.Session.GetString("NomAffichage") == null)
            return RedirectToPage("/Index");

        Stats = _store.StatsGenerales();
        RepartitionType = _store.RepartitionParType();
        RepartitionPoste = _store.RepartitionParPoste();
        Evolution = _store.EvolutionJournaliere(14);
        TopDefauts = _store.TopDefauts(5);
        return Page();
    }
}
