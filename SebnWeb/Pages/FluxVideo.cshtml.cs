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
    public List<Operateur> Operateurs { get; set; } = new();
    public bool ApiDisponible { get; set; }
    public string ModeIA { get; set; } = "";
    public string? ImageBase64 { get; set; }
    public AnomalieDetecteeDto? Anomalie { get; set; }
    public string? Statut { get; set; }
    public string? Message { get; set; }
    public int TimeoutPerteCameraSecondes { get; set; } = 5;
    [BindProperty] public string IdPoste { get; set; } = "";
    [BindProperty] public string MatriculeOp { get; set; } = "";

    public async Task<IActionResult> OnGet()
    {
        if (HttpContext.Session.GetString("NomAffichage") == null)
            return RedirectToPage("/Index");
        if (!RolesAutorises())
            return Redirect(RoleAccess.PageAccueil(HttpContext));

        Postes = _store.ListePostes();
        Operateurs = _store.ListeOperateurs();
        MatriculeOp = Operateurs.FirstOrDefault()?.MatriculeOp ?? "";
        TimeoutPerteCameraSecondes = _store.ObtenirTimeoutPerteCameraSecondes();
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
        Operateurs = _store.ListeOperateurs();
        TimeoutPerteCameraSecondes = _store.ObtenirTimeoutPerteCameraSecondes();
        ApiDisponible = await _api.EstDisponibleAsync();
        if (!ApiDisponible)
        {
            Statut = "indisponible";
            Message = "Le service IA est temporairement indisponible.";
            return Page();
        }

        var idPoste = string.IsNullOrWhiteSpace(IdPoste) ? "P01" : IdPoste.Trim();
        if (!_store.PosteExiste(idPoste))
        {
            Statut = "erreur";
            Message = "Poste sélectionné invalide.";
            return Page();
        }

        var matriculeOp = string.IsNullOrWhiteSpace(MatriculeOp)
            ? Operateurs.FirstOrDefault()?.MatriculeOp
            : MatriculeOp.Trim();
        if (matriculeOp == null || _store.TrouverOperateur(matriculeOp) == null)
        {
            Statut = "erreur";
            Message = "Opérateur sélectionné invalide.";
            return Page();
        }

        var resultat = await _api.DetecterAsync();
        if (resultat != null)
        {
            ImageBase64 = resultat.ImageBase64;
            Anomalie = resultat.Anomalie;
            Statut = resultat.Status ?? (resultat.Anomalie == null ? "conforme" : "anomalie");
            Message = resultat.Message;

            if (resultat.Status == "hors_domaine")
            {
                Anomalie = null;
                Statut = "hors_domaine";
            }
            else if (resultat.Anomalie != null)
            {
                var seuil = _store.ObtenirSeuilConfianceMinimale();
                if (resultat.Anomalie.Confiance >= seuil)
                {
                    var imagePreuve = _store.EnregistrerImagePreuveDepuisBase64(resultat.ImageBase64, "captures");
                    if (string.IsNullOrWhiteSpace(imagePreuve))
                        imagePreuve = _store.EnregistrerImagePreuveDepuisBase64(ImageBase64, "captures");

                    _store.InsererAnomalie(
                        resultat.Anomalie.TypeAnomalie,
                        resultat.Anomalie.Classe,
                        resultat.Anomalie.Confiance,
                        imagePreuve ?? "captures/live.jpg",
                        idPoste,
                        matriculeOp
                    );
                }
            }
        }
        else
        {
            ApiDisponible = false;
            Statut = "indisponible";
            Message = "La caméra industrielle est indisponible ou n'a fourni aucune image.";
        }

        return Page();
    }

    private bool RolesAutorises()
    {
        var role = HttpContext.Session.GetString("Role");
         return role == nameof(RoleUtilisateur.AuditeurQualite) ||
             role == nameof(RoleUtilisateur.AdminPit) ||
             role == nameof(RoleUtilisateur.SuperviseurProduction) ||
             role == nameof(RoleUtilisateur.Direction);
    }
}
