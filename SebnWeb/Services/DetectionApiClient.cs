using System.Text.Json.Serialization;

namespace SebnWeb.Services;

public class AnomalieDetecteeDto
{
    [JsonPropertyName("type_anomalie")] public string TypeAnomalie { get; set; } = "";
    [JsonPropertyName("classe")] public string Classe { get; set; } = "";
    [JsonPropertyName("confiance")] public double Confiance { get; set; }
}

public class EtatDetectionDto
{
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("mode")] public string Mode { get; set; } = "";
}

public class ResultatDetectionDto
{
    [JsonPropertyName("image_base64")] public string ImageBase64 { get; set; } = "";
    [JsonPropertyName("anomalie")] public AnomalieDetecteeDto? Anomalie { get; set; }
}

/// <summary>
/// Client HTTP vers le microservice IA Python (couche Traitement & Logique).
/// </summary>
public class DetectionApiClient
{
    private readonly HttpClient _http;
    private readonly ILogger<DetectionApiClient> _logger;

    public DetectionApiClient(HttpClient http, ILogger<DetectionApiClient> logger)
    {
        _http = http;
        _logger = logger;
        _logger.LogInformation("IA client configured with base URL {BaseUrl}", _http.BaseAddress);
    }

    public async Task<bool> EstDisponibleAsync()
    {
        try
        {
            var rep = await _http.GetAsync("/health");
            _logger.LogInformation("IA health {Url} returned HTTP {StatusCode}", new Uri(_http.BaseAddress!, "/health"), (int)rep.StatusCode);
            return rep.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IA health request failed for {Url}", new Uri(_http.BaseAddress!, "/health"));
            return false;
        }
    }

    public async Task<EtatDetectionDto?> ObtenirEtatAsync()
    {
        try
        {
            var rep = await _http.GetAsync("/health");
            var contenu = await rep.Content.ReadAsStringAsync();
            _logger.LogInformation("IA health {Url} returned HTTP {StatusCode}: {ResponseBody}", new Uri(_http.BaseAddress!, "/health"), (int)rep.StatusCode, contenu);
            if (!rep.IsSuccessStatusCode) return null;
            return System.Text.Json.JsonSerializer.Deserialize<EtatDetectionDto>(contenu);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IA health request or JSON parsing failed for {Url}", new Uri(_http.BaseAddress!, "/health"));
            return null;
        }
    }

    public async Task<ResultatDetectionDto?> DetecterAsync()
    {
        try
        {
            var rep = await _http.GetAsync("/detect");
            var contenu = await rep.Content.ReadAsStringAsync();
            _logger.LogInformation("IA detect {Url} returned HTTP {StatusCode}", new Uri(_http.BaseAddress!, "/detect"), (int)rep.StatusCode);
            if (!rep.IsSuccessStatusCode) return null;
            return System.Text.Json.JsonSerializer.Deserialize<ResultatDetectionDto>(contenu);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IA camera detection request failed for {Url}", new Uri(_http.BaseAddress!, "/detect"));
            return null;
        }
    }

    public async Task<ResultatDetectionDto?> DetecterImageAsync(byte[] imageBytes, string nomFichier = "photo.jpg")
    {
        try
        {
            using var content = new MultipartFormDataContent();
            using var imageContent = new ByteArrayContent(imageBytes);
            imageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            content.Add(imageContent, "file", nomFichier);

            var rep = await _http.PostAsync("/detect-image", content);
            var contenu = await rep.Content.ReadAsStringAsync();
            _logger.LogInformation("IA image detection {Url} returned HTTP {StatusCode}", new Uri(_http.BaseAddress!, "/detect-image"), (int)rep.StatusCode);
            if (!rep.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "IA image detection {Url} returned non-success HTTP {StatusCode}. Response body: {ResponseBody}",
                    new Uri(_http.BaseAddress!, "/detect-image"),
                    (int)rep.StatusCode,
                    contenu);
                return null;
            }
            return System.Text.Json.JsonSerializer.Deserialize<ResultatDetectionDto>(contenu);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IA image detection request failed for {Url}", new Uri(_http.BaseAddress!, "/detect-image"));
            return null;
        }
    }
}
