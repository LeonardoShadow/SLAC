using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SLAC.Core.Configuration;

namespace SLAC.Infrastructure.Data;

public class DatabaseHealthService : IDatabaseHealthService
{
    private readonly HttpClient _httpClient;
    private readonly SlacSupabaseOptions _options;
    private readonly ILogger<DatabaseHealthService> _logger;
    private bool _isHealthy = true;
    private DateTimeOffset _ultimaComprobacion = DateTimeOffset.MinValue;
    private string? _errorMessage;

    public bool IsHealthy => _isHealthy;
    public DateTimeOffset UltimaComprobacion => _ultimaComprobacion;
    public string? ErrorMessage => _errorMessage;

    public DatabaseHealthService(
        HttpClient httpClient,
        IOptions<SlacSupabaseOptions> options,
        ILogger<DatabaseHealthService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<bool> VerificarSaludAsync(bool forzar = false)
    {
        if (!forzar && DateTimeOffset.UtcNow - _ultimaComprobacion < TimeSpan.FromSeconds(30))
        {
            return _isHealthy;
        }

        if (string.IsNullOrWhiteSpace(_options.Url) || string.IsNullOrWhiteSpace(_options.Key) || _options.Url.Contains("placeholder"))
        {
            _isHealthy = false;
            _errorMessage = "Credenciales de Supabase no configuradas.";
            return false;
        }

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{_options.Url.TrimEnd('/')}/rest/v1/institucion?select=id&limit=1");
            req.Headers.Add("apikey", _options.Key);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.Key);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            var response = await _httpClient.SendAsync(req, cts.Token);

            if (response.IsSuccessStatusCode)
            {
                _isHealthy = true;
                _errorMessage = null;
            }
            else
            {
                _isHealthy = false;
                _errorMessage = $"Supabase respondió con código HTTP {(int)response.StatusCode}";
                _logger.LogWarning("Supabase health check falló con status {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _isHealthy = false;
            _errorMessage = ex.Message;
            _logger.LogWarning(ex, "Supabase health check detectó base de datos no disponible o pausada.");
        }
        finally
        {
            _ultimaComprobacion = DateTimeOffset.UtcNow;
        }

        return _isHealthy;
    }
}

