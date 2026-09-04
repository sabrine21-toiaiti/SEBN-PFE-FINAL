using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using SebnWeb.Data;
using SebnWeb.Models;
using SebnWeb.Services;

namespace SebnWeb.Pages;

public class HistoriqueModel : PageModel
{
    private readonly AppDataStore _store;
    private readonly IWebHostEnvironment _environment;

    public HistoriqueModel(AppDataStore store, IWebHostEnvironment environment)
    {
        _store = store;
        _environment = environment;
    }

    public List<Anomalie> Historique { get; set; } = new();
    public List<Poste> Postes { get; set; } = new();
    public Dictionary<string, string> NomsOperateurs { get; set; } = new();
    public Dictionary<string, string> LignesPostes { get; set; } = new();

    [BindProperty(SupportsGet = true)] public string TypeFiltre { get; set; } = "Tous";
    [BindProperty(SupportsGet = true)] public string StatutFiltre { get; set; } = "Tous";
    [BindProperty(SupportsGet = true)] public string PosteFiltre { get; set; } = "Tous";
    [BindProperty(SupportsGet = true)] public int? IdAnomalie { get; set; }
    [BindProperty(SupportsGet = true, Name = "pageNumber")] public int PageNumber { get; set; } = 1;
    public new int Page { get; private set; } = 1;

    public int PageSize { get; } = 50;
    public int TotalItems { get; private set; }
    public int TotalPages { get; private set; }

    public string? RoleActuel { get; set; }
    public Anomalie? AnomalieSelectionnee { get; set; }

    public bool PeutCloturer => RoleActuel == nameof(RoleUtilisateur.SuperviseurProduction) ||
                                RoleActuel == nameof(RoleUtilisateur.AuditeurQualite) ||
                                RoleActuel == nameof(RoleUtilisateur.AdminPit);

    public bool ImagePreuveDisponible => AnomalieSelectionnee != null &&
        !string.IsNullOrWhiteSpace(AnomalieSelectionnee.ImagePreuve) &&
        System.IO.File.Exists(Path.Combine(_environment.WebRootPath, AnomalieSelectionnee.ImagePreuve.Replace('/', Path.DirectorySeparatorChar)));

    public string? ImagePreuveUrl => ImagePreuveDisponible
        ? "/" + AnomalieSelectionnee!.ImagePreuve.Replace('\\', '/')
        : null;

    public string LienHistorique(int page, int? idAnomalie = null)
    {
        var valeurs = new Dictionary<string, string?>
        {
            ["pageNumber"] = page.ToString(),
            ["TypeFiltre"] = TypeFiltre,
            ["StatutFiltre"] = StatutFiltre,
            ["PosteFiltre"] = PosteFiltre
        };
        if (idAnomalie.HasValue)
            valeurs["IdAnomalie"] = idAnomalie.Value.ToString();
        return QueryHelpers.AddQueryString("/Historique", valeurs);
    }

    public IActionResult OnGet()
    {
        if (HttpContext.Session.GetString("NomAffichage") == null)
            return RedirectToPage("/Index");
        if (!RolesAutorises())
            return Redirect(RoleAccess.PageAccueil(HttpContext));

        RoleActuel = HttpContext.Session.GetString("Role");
        Postes = _store.ListePostes();
        foreach (var p in Postes) LignesPostes[p.IdPoste] = p.LigneProduction;

        StatutAnomalie? statut = StatutFiltre switch
        {
            "NON_TRAITEE" => StatutAnomalie.NonTraitee,
            "CORRIGEE" => StatutAnomalie.Corrigee,
            _ => null
        };

        var type = TypeFiltre == "Tous" ? null : TypeFiltre;
        var poste = PosteFiltre == "Tous" ? null : PosteFiltre;
        TotalItems = _store.CompterHistorique(type, statut, poste);
        TotalPages = Math.Max(1, (int)Math.Ceiling(TotalItems / (double)PageSize));
        Page = Math.Clamp(PageNumber, 1, TotalPages);
        var offset = (Page - 1) * PageSize;
        Historique = _store.RecupererHistorique(type, statut, poste, PageSize, offset);
        AnomalieSelectionnee = IdAnomalie.HasValue
            ? Historique.FirstOrDefault(a => a.IdAnomalie == IdAnomalie.Value)
            : null;

        foreach (var a in Historique)
        {
            var op = _store.TrouverOperateur(a.MatriculeOp);
            NomsOperateurs[a.MatriculeOp] = op?.NomComplet ?? a.MatriculeOp;
        }
        return Page();
    }

    public IActionResult OnPostCloturer(int id)
    {
        if (HttpContext.Session.GetString("NomAffichage") == null)
            return RedirectToPage("/Index");
        if (!PeutCloturer)
            return Redirect(RoleAccess.PageAccueil(HttpContext));

        _store.CloturerAnomalie(id);
        return RedirectToPage(new { TypeFiltre, StatutFiltre, PosteFiltre, pageNumber = PageNumber });
    }

    private bool RolesAutorises()
    {
        var role = HttpContext.Session.GetString("Role");
         return role == nameof(RoleUtilisateur.SuperviseurProduction) ||
             role == nameof(RoleUtilisateur.AuditeurQualite) ||
             role == nameof(RoleUtilisateur.AdminPit) ||
             role == nameof(RoleUtilisateur.Direction);
    }
}
