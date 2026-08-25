using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SebnWeb.Data;
using SebnWeb.Models;
using SebnWeb.Services;

namespace SebnWeb.Pages;

public class FluxVideoModel : PageModel
{
    private readonly AppDataStore _store;
    private readonly DetectionApiClient _api;

    public FluxVideoModel(AppDataStore store, DetectionApiClient api)
    {
        _store = store;
        _api = api;
    }

    public List<SebnWeb.Models.Poste> Postes { get; set; } = new();
    public bool ApiDisponible { get; set; }
    public string ModeIA { get; set; } = "";
    public string? ImageBase64 { get; set; }
    public AnomalieDetecteeDto? Anomalie { get; set; }
    [BindProperty] public string IdPoste { get; set; } = "";

    public async Task<IActionResult> OnGet()
    {
        if (HttpContext.Session.GetString("NomAffichage") == null)
            return RedirectToPage("/Index");
        if (!RolesAutorises())
            return Redirect(RoleAccess.PageAccueil(HttpContext));

        Postes = _store.ListePostes();
        var etatIA = await _api.ObtenirEtatAsync();
        ApiDisponible = etatIA != null;
        ModeIA = etatIA?.Mode ?? "";
        return Page();
    }

    public async Task<IActionResult> OnPostCapturerAsync()
    {
        if (HttpContext.Session.GetString("NomAffichage") == null)
            return RedirectToPage("/Index");
        if (!RolesAutorises())
            return Redirect(RoleAccess.PageAccueil(HttpContext));

        Postes = _store.ListePostes();
        ApiDisponible = true;
        var resultat = await _api.DetecterAsync();
        if (resultat != null)
        {
            ImageBase64 = resultat.ImageBase64;
            Anomalie = resultat.Anomalie;

            if (resultat.Anomalie != null)
            {
                _store.InsererAnomalie(
                    resultat.Anomalie.TypeAnomalie,
                    resultat.Anomalie.Classe,
                    resultat.Anomalie.Confiance,
                    "captures/live.jpg",
                    string.IsNullOrEmpty(IdPoste) ? "P01" : IdPoste,
                    "OP101"
                );
            }
        }

        return Page();
    }

    private bool RolesAutorises()
    {
        var role = HttpContext.Session.GetString("Role");
        return role == nameof(RoleUtilisateur.SuperviseurPit) ||
               role == nameof(RoleUtilisateur.OperateurProduction);
    }
}
