using Microsoft.AspNetCore.DataProtection;
using SebnWeb.Data;
using SebnWeb.Models;
using SebnWeb.Services;

var builder = WebApplication.CreateBuilder(args);

// Cloud platforms (Render, Railway, Azure...) fournissent le port via la variable
// d'environnement PORT. En local, on garde le port par défaut (5000/launchSettings).
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

builder.Services.AddRazorPages();
builder.Services.AddSingleton<AppDataStore>();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "Data", "DataProtectionKeys")))
    .SetApplicationName("SEBN");
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(4);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

builder.Services.AddHttpClient<DetectionApiClient>(client =>
{
    var baseUrl = builder.Configuration["DetectionApi:BaseUrl"];
    if (string.IsNullOrWhiteSpace(baseUrl))
    {
        baseUrl = builder.Environment.IsDevelopment()
            ? "http://localhost:8000"
            : "https://sebn-pfe-ia.onrender.com";
    }
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(90);
});

var app = builder.Build();

// Forcer l'initialisation de la base de données au démarrage
try
{
    _ = app.Services.GetRequiredService<AppDataStore>();
    Console.WriteLine("✓ AppDataStore initialisé");
}
catch (Exception ex)
{
    Console.WriteLine($"✗ Erreur lors de l'initialisation d'AppDataStore: {ex.Message}");
    Console.WriteLine($"  Détails: {ex.InnerException?.Message}");
    throw;
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthorization();
app.MapRazorPages();

app.MapPost("/api/detect-photo", async (HttpContext ctx, DetectionApiClient api, AppDataStore store) =>
{
    // Sécurité minimale : exige une session active (utilisateur connecté)
    if (ctx.Session.GetString("NomAffichage") == null)
        return Results.Unauthorized();
    if (!RoleAccess.HasRole(ctx, RoleUtilisateur.SuperviseurPit) &&
        !RoleAccess.HasRole(ctx, RoleUtilisateur.OperateurProduction))
        return Results.StatusCode(StatusCodes.Status403Forbidden);

    var body = await ctx.Request.ReadFromJsonAsync<PhotoRequest>();
    if (body == null || string.IsNullOrEmpty(body.ImageBase64))
        return Results.BadRequest(new { error = "Image manquante." });

    // "data:image/jpeg;base64,...." -> ne garder que la partie base64
    var base64 = body.ImageBase64.Contains(',') ? body.ImageBase64.Split(',')[1] : body.ImageBase64;
    byte[] imageBytes;
    try { imageBytes = Convert.FromBase64String(base64); }
    catch { return Results.BadRequest(new { error = "Image invalide." }); }

    var resultat = await api.DetecterImageAsync(imageBytes);
    if (resultat == null)
        return Results.Json(new { erreur = "Microservice IA indisponible." }, statusCode: 503);

    if (resultat.Anomalie != null)
    {
        store.InsererAnomalie(
            resultat.Anomalie.TypeAnomalie,
            resultat.Anomalie.Classe,
            resultat.Anomalie.Confiance,
            "captures/camera-navigateur.jpg",
            string.IsNullOrEmpty(body.IdPoste) ? "P01" : body.IdPoste,
            "OP101"
        );
    }

    return Results.Json(new
    {
        imageBase64 = resultat.ImageBase64,
        anomalie = resultat.Anomalie == null ? null : new
        {
            type = resultat.Anomalie.TypeAnomalie,
            classe = resultat.Anomalie.Classe,
            confiance = resultat.Anomalie.Confiance
        }
    });
});

app.MapPost("/api/camera-signal-perdu", (HttpContext ctx, AppDataStore store, SignalPerteRequest body) =>
{
    // Sécurité minimale : exige une session active (utilisateur connecté)
    if (ctx.Session.GetString("NomAffichage") == null)
        return Results.Unauthorized();
    if (!RoleAccess.HasRole(ctx, RoleUtilisateur.SuperviseurPit) &&
        !RoleAccess.HasRole(ctx, RoleUtilisateur.OperateurProduction))
        return Results.StatusCode(StatusCodes.Status403Forbidden);

    var idPoste = string.IsNullOrEmpty(body.IdPoste) ? "P01" : body.IdPoste;
    store.SignalerPerteFluxCamera(idPoste);
    return Results.Ok();
});

app.MapGet("/api/notifications", (HttpContext ctx, AppDataStore store) =>
{
    if (ctx.Session.GetString("NomAffichage") == null)
        return Results.Unauthorized();
    if (!RoleAccess.HasRole(ctx, RoleUtilisateur.SuperviseurPit))
        return Results.StatusCode(StatusCodes.Status403Forbidden);

    var notifications = store.ListeNotificationsNonLues()
        .Select(n => new { n.IdNotification, n.Message, n.IdPoste, DateCreation = n.DateCreation.ToString("dd/MM HH:mm") });
    return Results.Json(notifications);
});

app.MapPost("/api/notifications/{id:int}/lue", (int id, HttpContext ctx, AppDataStore store) =>
{
    if (ctx.Session.GetString("NomAffichage") == null)
        return Results.Unauthorized();
    if (!RoleAccess.HasRole(ctx, RoleUtilisateur.SuperviseurPit))
        return Results.StatusCode(StatusCodes.Status403Forbidden);

    store.MarquerNotificationLue(id);
    return Results.Ok();
});

app.Run();

record PhotoRequest(string ImageBase64, string? IdPoste);
record SignalPerteRequest(string? IdPoste);
