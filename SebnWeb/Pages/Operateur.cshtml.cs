using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SebnWeb.Models;
using SebnWeb.Services;

namespace SebnWeb.Pages;

public class OperateurModel : PageModel
{
    public IActionResult OnGet()
    {
        if (HttpContext.Session.GetString("NomAffichage") == null)
            return RedirectToPage("/Index");
        if (HttpContext.Session.GetString("Role") != nameof(RoleUtilisateur.OperateurProduction))
            return Redirect(RoleAccess.PageAccueil(HttpContext));

        return Page();
    }
}
