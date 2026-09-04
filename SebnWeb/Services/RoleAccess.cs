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
        nameof(RoleUtilisateur.AdminPit) => "/Administration",
        nameof(RoleUtilisateur.SuperviseurProduction) => "/Operateur",
        nameof(RoleUtilisateur.AuditeurQualite) => "/Dashboard",
        nameof(RoleUtilisateur.Direction) => "/DashboardPit",
        _ => "/"
    };
}
