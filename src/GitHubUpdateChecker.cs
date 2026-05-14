using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SolarMonitorBrightness;

internal sealed class GitHubUpdateChecker : IDisposable
{
    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    public GitHubUpdateChecker()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DDC-CI-HA-Bridge", Form1.AppVersion));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<ReleaseInfo?> GetNewerReleaseAsync(string currentVersion, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync("https://api.github.com/repos/afaminx/DDC-CI-HA-Bridge/releases/latest", cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var tagName = root.TryGetProperty("tag_name", out var tagProperty) ? tagProperty.GetString() : null;
        if (!TryParseVersion(tagName, out var upstream) || !TryParseVersion(currentVersion, out var local))
        {
            return null;
        }

        if (upstream.CompareTo(local) <= 0)
        {
            return null;
        }

        var htmlUrl = root.TryGetProperty("html_url", out var urlProperty)
            ? urlProperty.GetString()
            : "https://github.com/afaminx/DDC-CI-HA-Bridge";

        return new ReleaseInfo(tagName ?? upstream.ToString(), htmlUrl ?? "https://github.com/afaminx/DDC-CI-HA-Bridge");
    }

    private static bool TryParseVersion(string? value, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var match = Regex.Match(value.Trim(), @"v?(?<version>\d+(?:\.\d+){0,3})", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return false;
        }

        var parts = match.Groups["version"].Value
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => int.Parse(part, CultureInfo.InvariantCulture))
            .ToList();

        while (parts.Count < 2)
        {
            parts.Add(0);
        }

        version = parts.Count switch
        {
            2 => new Version(parts[0], parts[1]),
            3 => new Version(parts[0], parts[1], parts[2]),
            _ => new Version(parts[0], parts[1], parts[2], parts[3])
        };
        return true;
    }

    public void Dispose() => _httpClient.Dispose();
}

internal sealed record ReleaseInfo(string TagName, string Url);
