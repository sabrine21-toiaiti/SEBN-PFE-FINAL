using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SebnWeb.Data;
using SebnWeb.Models;

namespace SebnWeb.Pages;

public class IndexModel : PageModel
{
    private readonly AppDataStore _store;

    public IndexModel(AppDataStore store)
    {
        _store = store;
    }

    [BindProperty] public string Login { get; set; } = "";
    [BindProperty] public string MotDePasse { get; set; } = "";
    public string? Erreur { get; set; }

    public void OnGet()
    {
        if (HttpContext.Session.GetString("NomAffichage") != null)
        {
            Response.Redirect("/Dashboard");
        }
    }

    public IActionResult OnPost()
    {
        var resultat = _store.TenterConnexion(Login, MotDePasse);

        if (!resultat.EstReussi || resultat.Utilisateur == null)
        {
            Erreur = resultat.MessageErreur ?? "Login ou mot de passe incorrect.";
            return Page();
        }

        var utilisateur = resultat.Utilisateur;
        HttpContext.Session.SetString("NomAffichage", utilisateur.NomAffichage);
        HttpContext.Session.SetString("Role", utilisateur.Role.Libelle());
        HttpContext.Session.SetInt32("IdUtilisateur", utilisateur.IdUtilisateur);

        return RedirectToPage("/Dashboard");
    }

    public IActionResult OnPostLogout()
    {
        HttpContext.Session.Clear();
        return RedirectToPage("/Index");
    }
}
