using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Antiforgery;
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
builder.Services.AddAntiforgery(options => options.HeaderName = "RequestVerificationToken");
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
    client.Timeout = TimeSpan.FromSeconds(60);
});

var app = builder.Build();
var antiforgery = app.Services.GetRequiredService<IAntiforgery>();

async Task<bool> RequeteApiProtegeeAsync(HttpContext ctx)
{
    try
    {
        await antiforgery.ValidateRequestAsync(ctx);
        return true;
    }
    catch (AntiforgeryValidationException)
    {
        return false;
    }
}

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
    if (!RoleAccess.HasRole(ctx, RoleUtilisateur.SuperviseurQualite) &&
        !RoleAccess.HasRole(ctx, RoleUtilisateur.SuperviseurPit) &&
        !RoleAccess.HasRole(ctx, RoleUtilisateur.OperateurProduction) &&
        !RoleAccess.HasRole(ctx, RoleUtilisateur.Administrateur))
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    if (!await RequeteApiProtegeeAsync(ctx))
        return Results.BadRequest(new { error = "Jeton de sécurité invalide ou manquant." });

    var body = await ctx.Request.ReadFromJsonAsync<PhotoRequest>();
    if (body == null || string.IsNullOrEmpty(body.ImageBase64))
        return Results.BadRequest(new { error = "Image manquante." });

    // "data:image/jpeg;base64,...." -> ne garder que la partie base64
    var base64 = body.ImageBase64.Contains(',') ? body.ImageBase64.Split(',')[1] : body.ImageBase64;
    byte[] imageBytes;
    try { imageBytes = Convert.FromBase64String(base64); }
    catch { return Results.BadRequest(new { error = "Image invalide." }); }
    if (imageBytes.Length == 0 || imageBytes.Length > 10 * 1024 * 1024)
        return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

    ResultatDetectionDto? resultat;
    try
    {
        resultat = await api.DetecterImageAsync(imageBytes);
    }
    catch (DetectionApiException ex)
    {
        var statusCode = ex.StatusCode is >= 400 and <= 599 ? ex.StatusCode.Value : StatusCodes.Status503ServiceUnavailable;
        var message = ex.StatusCode == StatusCodes.Status429TooManyRequests
            ? "Le service IA limite temporairement les requêtes. Veuillez patienter quelques secondes puis réessayer."
            : ex.StatusCode.HasValue
                ? $"Le service IA a refusé la requête (HTTP {ex.StatusCode.Value})."
                : "Le service IA est temporairement indisponible.";
        return Results.Json(new { erreur = message, code = ex.StatusCode.HasValue ? $"IA_HTTP_{ex.StatusCode.Value}" : "IA_NETWORK_ERROR" }, statusCode: statusCode);
    }
    if (resultat == null)
        return Results.Json(new { erreur = "Microservice IA indisponible." }, statusCode: 503);

    if (resultat.Status == "hors_domaine")
    {
        return Results.Json(new
        {
            imageBase64 = resultat.ImageBase64,
            anomalie = (object?)null,
            status = "hors_domaine",
            domainValid = false,
            message = string.IsNullOrWhiteSpace(resultat.Message) ? "Image hors domaine : ce n'est pas un poste industriel valide." : resultat.Message
        });
    }

    var idPoste = string.IsNullOrWhiteSpace(body.IdPoste) ? "P01" : body.IdPoste.Trim();
    if (!store.PosteExiste(idPoste))
        return Results.BadRequest(new { error = "Poste sélectionné invalide." });

    if (resultat.Anomalie != null)
    {
        var seuil = store.ObtenirSeuilConfianceMinimale();
        if (resultat.Anomalie.Confiance >= seuil)
        {
            var imagePreuve = store.EnregistrerImagePreuveDepuisBase64(resultat.ImageBase64, "captures");
            if (string.IsNullOrWhiteSpace(imagePreuve))
                imagePreuve = store.EnregistrerImagePreuveDepuisBase64(body.ImageBase64, "captures");

            store.InsererAnomalie(
                resultat.Anomalie.TypeAnomalie,
                resultat.Anomalie.Classe,
                resultat.Anomalie.Confiance,
                imagePreuve ?? "captures/camera-navigateur.jpg",
                idPoste,
                "OP101"
            );
        }
    }

    return Results.Json(new
    {
        imageBase64 = resultat.ImageBase64,
        anomalie = resultat.Anomalie == null ? null : new
        {
            type = resultat.Anomalie.TypeAnomalie,
            classe = resultat.Anomalie.Classe,
            confiance = resultat.Anomalie.Confiance
        },
        status = resultat.Status ?? (resultat.Anomalie == null ? "conforme" : "anomalie"),
        domainValid = resultat.DomainValid ?? true,
        message = resultat.Message
    });
});

app.MapPost("/api/camera-signal-perdu", async (HttpContext ctx, AppDataStore store, SignalPerteRequest body) =>
{
    // Sécurité minimale : exige une session active (utilisateur connecté)
    if (ctx.Session.GetString("NomAffichage") == null)
        return Results.Unauthorized();
    if (!RoleAccess.HasRole(ctx, RoleUtilisateur.SuperviseurQualite) &&
        !RoleAccess.HasRole(ctx, RoleUtilisateur.SuperviseurPit) &&
        !RoleAccess.HasRole(ctx, RoleUtilisateur.OperateurProduction) &&
        !RoleAccess.HasRole(ctx, RoleUtilisateur.Administrateur))
        return Results.StatusCode(StatusCodes.Status403Forbidden);

    if (!await RequeteApiProtegeeAsync(ctx))
        return Results.BadRequest(new { error = "Jeton de sécurité invalide ou manquant." });

    var idPoste = string.IsNullOrWhiteSpace(body.IdPoste) ? "P01" : body.IdPoste.Trim();
    if (!store.PosteExiste(idPoste))
        return Results.BadRequest(new { error = "Poste sélectionné invalide." });
    store.SignalerPerteFluxCamera(idPoste);
    return Results.Ok();
});

app.MapGet("/api/notifications", (HttpContext ctx, AppDataStore store) =>
{
    if (ctx.Session.GetString("NomAffichage") == null)
        return Results.Unauthorized();
    if (!RoleAccess.HasRole(ctx, RoleUtilisateur.SuperviseurQualite) &&
        !RoleAccess.HasRole(ctx, RoleUtilisateur.SuperviseurPit) &&
        !RoleAccess.HasRole(ctx, RoleUtilisateur.Administrateur))
        return Results.StatusCode(StatusCodes.Status403Forbidden);

    var notifications = store.ListeNotificationsNonLues()
        .Select(n => new { n.IdNotification, n.Message, n.IdPoste, DateCreation = n.DateCreation.ToString("dd/MM HH:mm") });
    return Results.Json(notifications);
});

app.MapPost("/api/notifications/{id:int}/lue", async (int id, HttpContext ctx, AppDataStore store) =>
{
    if (ctx.Session.GetString("NomAffichage") == null)
        return Results.Unauthorized();
    if (!RoleAccess.HasRole(ctx, RoleUtilisateur.SuperviseurQualite) &&
        !RoleAccess.HasRole(ctx, RoleUtilisateur.SuperviseurPit) &&
        !RoleAccess.HasRole(ctx, RoleUtilisateur.Administrateur))
        return Results.StatusCode(StatusCodes.Status403Forbidden);

    if (!await RequeteApiProtegeeAsync(ctx))
        return Results.BadRequest(new { error = "Jeton de sécurité invalide ou manquant." });

    store.MarquerNotificationLue(id);
    return Results.Ok();
});

app.Run();

record PhotoRequest(string ImageBase64, string? IdPoste);
record SignalPerteRequest(string? IdPoste);
