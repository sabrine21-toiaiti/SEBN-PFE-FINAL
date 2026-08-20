using System.Text.Json.Serialization;

namespace SebnWeb.Services;

public class AnomalieDetecteeDto
{
    [JsonPropertyName("type_anomalie")] public string TypeAnomalie { get; set; } = "";
    [JsonPropertyName("classe")] public string Classe { get; set; } = "";
    [JsonPropertyName("confiance")] public double Confiance { get; set; }
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

    public DetectionApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<bool> EstDisponibleAsync()
    {
        try
        {
            var rep = await _http.GetAsync("/health");
            return rep.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<ResultatDetectionDto?> DetecterAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<ResultatDetectionDto>("/detect");
        }
        catch
        {
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
            if (!rep.IsSuccessStatusCode) return null;
            return await rep.Content.ReadFromJsonAsync<ResultatDetectionDto>();
        }
        catch
        {
            return null;
        }
    }
}
