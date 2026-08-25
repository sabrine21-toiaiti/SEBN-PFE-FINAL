using System.Text.Json.Serialization;
using System.Diagnostics;

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

public sealed class DetectionApiException : Exception
{
    public int? StatusCode { get; }
    public string ResponseBody { get; }

    public DetectionApiException(string message, int? statusCode = null, string responseBody = "", Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}

/// <summary>
/// Client HTTP vers le microservice IA Python (couche Traitement & Logique).
/// </summary>
public class DetectionApiClient
{
    private static readonly SemaphoreSlim HealthProbeLock = new(1, 1);
    private static EtatDetectionDto? _cachedHealth;
    private static DateTimeOffset _healthCacheUntil;
    private static bool _healthCacheInitialized;
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
        return await ObtenirEtatAsync() != null;
    }

    public async Task<EtatDetectionDto?> ObtenirEtatAsync()
    {
        await HealthProbeLock.WaitAsync();
        try
        {
            if (_healthCacheInitialized && DateTimeOffset.UtcNow < _healthCacheUntil)
                return _cachedHealth;

            for (var tentative = 0; tentative < 2; tentative++)
            {
                var rep = await _http.GetAsync("/health");
                var contenu = await rep.Content.ReadAsStringAsync();
                _logger.LogInformation("IA health {Url} returned HTTP {StatusCode}: {ResponseBody}", new Uri(_http.BaseAddress!, "/health"), (int)rep.StatusCode, contenu);

                if (rep.IsSuccessStatusCode)
                {
                    _cachedHealth = System.Text.Json.JsonSerializer.Deserialize<EtatDetectionDto>(contenu);
                    CacheHealth(TimeSpan.FromSeconds(30));
                    return _cachedHealth;
                }

                if ((int)rep.StatusCode != StatusCodes.Status429TooManyRequests || tentative == 1)
                    break;

                var delai = rep.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(tentative + 1);
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(delai.TotalSeconds, 1, 10)));
            }

            CacheHealth(TimeSpan.FromSeconds(10));
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IA health request or JSON parsing failed for {Url}", new Uri(_http.BaseAddress!, "/health"));
            CacheHealth(TimeSpan.FromSeconds(10));
            return null;
        }
        finally
        {
            HealthProbeLock.Release();
        }
    }

    private static void CacheHealth(TimeSpan duration)
    {
        _healthCacheInitialized = true;
        _healthCacheUntil = DateTimeOffset.UtcNow.Add(duration);
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
        var chronometre = Stopwatch.StartNew();
        try
        {
            using var content = new MultipartFormDataContent();
            using var imageContent = new ByteArrayContent(imageBytes);
            imageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            content.Add(imageContent, "file", nomFichier);

            var rep = await _http.PostAsync("/detect-image", content);
            var contenu = await rep.Content.ReadAsStringAsync();
            _logger.LogInformation("IA image detection {Url} returned HTTP {StatusCode} in {ElapsedMs} ms", new Uri(_http.BaseAddress!, "/detect-image"), (int)rep.StatusCode, chronometre.ElapsedMilliseconds);
            if (!rep.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "IA image detection {Url} returned non-success HTTP {StatusCode}. Response body: {ResponseBody}",
                    new Uri(_http.BaseAddress!, "/detect-image"),
                    (int)rep.StatusCode,
                    contenu);
                throw new DetectionApiException(
                    $"Le microservice IA a retourné HTTP {(int)rep.StatusCode}.",
                    (int)rep.StatusCode,
                    contenu);
            }
            return System.Text.Json.JsonSerializer.Deserialize<ResultatDetectionDto>(contenu);
        }
        catch (DetectionApiException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IA image detection request failed for {Url} after {ElapsedMs} ms", new Uri(_http.BaseAddress!, "/detect-image"), chronometre.ElapsedMilliseconds);
            throw new DetectionApiException(
                "La requête vers le microservice IA a échoué.",
                innerException: ex);
        }
    }
}
