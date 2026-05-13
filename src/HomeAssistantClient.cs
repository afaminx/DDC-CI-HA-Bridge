using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace SolarMonitorBrightness;

internal sealed class HomeAssistantClient : IDisposable
{
    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    public async Task<decimal> GetSensorLuxAsync(string address, string entityId, string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            throw new InvalidOperationException("Home Assistant host (IP:port) is missing.");
        }

        if (string.IsNullOrWhiteSpace(entityId))
        {
            throw new InvalidOperationException("Sensor entity ID is missing.");
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Home Assistant token is missing.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, BuildSensorUri(address, entityId));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Home Assistant returned {(int)response.StatusCode}: {response.ReasonPhrase}");
        }

        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("state", out var stateProperty))
        {
            throw new InvalidOperationException("The Home Assistant response does not contain a sensor state.");
        }

        var state = stateProperty.GetString();
        if (!decimal.TryParse(state, NumberStyles.Float, CultureInfo.InvariantCulture, out var lux) &&
            !decimal.TryParse(state, NumberStyles.Float, CultureInfo.CurrentCulture, out lux))
        {
            throw new InvalidOperationException($"The sensor state is not a number: {state}");
        }

        return lux;
    }

    private static Uri BuildSensorUri(string address, string entityId)
    {
        var normalized = address.Trim();
        if (!normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "http://" + normalized;
        }

        normalized = normalized.TrimEnd('/');
        return new Uri($"{normalized}/api/states/{Uri.EscapeDataString(entityId.Trim())}");
    }

    public void Dispose() => _httpClient.Dispose();
}
