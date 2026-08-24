using Microsoft.AspNetCore.Http;
using SebnWeb.Models;

namespace SebnWeb.Services;

public static class RoleAccess
{
    public static string? Role(HttpContext context) => context.Session.GetString("Role");

    public static bool HasRole(HttpContext context, RoleUtilisateur role) =>
        Role(context) == role.ToString();

    public static string PageAccueil(HttpContext context) => Role(context) switch
    {
        nameof(RoleUtilisateur.Administrateur) => "/Administration",
        nameof(RoleUtilisateur.SuperviseurPit) => "/DashboardPit",
        nameof(RoleUtilisateur.OperateurProduction) => "/Operateur",
        _ => "/Dashboard"
    };
}
