using System.Globalization;
using System.Text.Json;
using Jellyfin.Plugin.Homepage.Configuration;
using Jellyfin.Plugin.Homepage.Models;
using MediaBrowser.Common.Net;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Homepage.Services;

/// <summary>
/// Reads Sonarr's calendar to show the next episodes of the series you follow.
/// </summary>
public class SonarrService
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(15);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly LibraryService _library;
    private readonly ILogger<SonarrService> _logger;
    private readonly object _lock = new();
    private (DateTime At, int Days, List<CalendarEntry> Entries)? _cache;

    public SonarrService(IHttpClientFactory httpClientFactory, LibraryService library, ILogger<SonarrService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _library = library;
        _logger = logger;
    }

    private static PluginConfiguration Config => Plugin.Instance.Configuration;

    public static bool IsConfigured => !string.IsNullOrWhiteSpace(Config.SonarrUrl) && !string.IsNullOrWhiteSpace(Config.SonarrApiKey);

    public async Task<string> TestAsync(CancellationToken ct)
    {
        using JsonDocument doc = JsonDocument.Parse(await GetAsync("system/status", ct).ConfigureAwait(false));
        return "Sonarr " + SeerrService.Str(doc.RootElement, "version") + " — clé valide";
    }

    /// <summary>One card per series: its next episode airing in the window.</summary>
    public async Task<List<HomeCard>> UpcomingAsync(User user, SectionConfig section, CancellationToken ct)
    {
        List<CalendarEntry> entries = await CalendarAsync(section.DaysAhead, ct).ConfigureAwait(false);
        HashSet<Guid>? watched = section.OnlyWatched ? _library.WatchedSeries(user) : null;
        List<HomeCard> cards = new();

        foreach (IGrouping<int, CalendarEntry> series in entries.GroupBy(e => e.TvdbId).OrderBy(g => g.Min(e => e.AirDateUtc)))
        {
            CalendarEntry next = series.OrderBy(e => e.AirDateUtc).First();
            Guid? localId = _library.FindLocal("tv", next.TmdbId, next.TvdbId);

            // "Series I have or watch": the series must exist in Jellyfin.
            if (localId == null || (watched != null && !watched.Contains(localId.Value)))
            {
                continue;
            }

            HomeCard? card = _library.CardForItem(user, localId.Value);
            if (card == null)
            {
                continue;
            }

            int count = series.Count();
            card.Subtitle = string.Create(CultureInfo.InvariantCulture, $"S{next.Season:00}E{next.Episode:00}")
                + (string.IsNullOrWhiteSpace(next.Title) ? string.Empty : " · " + next.Title)
                + (count > 1 ? string.Create(CultureInfo.InvariantCulture, $" (+{count - 1})") : string.Empty);
            card.Date = next.AirDateUtc;
            card.Badge = null; // formatted by the client in the viewer's language and time zone
            card.Progress = null;
            card.Key = "upcoming:" + card.Key;
            cards.Add(card);

            if (cards.Count >= section.Limit)
            {
                break;
            }
        }

        return cards;
    }

    private async Task<List<CalendarEntry>> CalendarAsync(int days, CancellationToken ct)
    {
        lock (_lock)
        {
            if (_cache is { } c && c.Days == days && DateTime.UtcNow - c.At < CacheLifetime)
            {
                return c.Entries;
            }
        }

        string start = DateTime.UtcNow.AddHours(-6).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        string end = DateTime.UtcNow.AddDays(days).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        string body = await GetAsync($"calendar?start={start}&end={end}&includeSeries=true&unmonitored=false", ct).ConfigureAwait(false);

        List<CalendarEntry> entries = new();
        using (JsonDocument doc = JsonDocument.Parse(body))
        {
            foreach (JsonElement e in doc.RootElement.EnumerateArray())
            {
                if (!e.TryGetProperty("series", out JsonElement series))
                {
                    continue;
                }

                string? air = SeerrService.Str(e, "airDateUtc");
                if (air == null || !DateTime.TryParse(air, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime airDate))
                {
                    continue;
                }

                entries.Add(new CalendarEntry
                {
                    TvdbId = SeerrService.Int(series, "tvdbId") ?? 0,
                    TmdbId = SeerrService.Int(series, "tmdbId"),
                    Season = SeerrService.Int(e, "seasonNumber") ?? 0,
                    Episode = SeerrService.Int(e, "episodeNumber") ?? 0,
                    Title = SeerrService.Str(e, "title"),
                    AirDateUtc = airDate,
                });
            }
        }

        entries = entries.Where(e => e.TvdbId > 0 && e.Season > 0).ToList();
        lock (_lock)
        {
            _cache = (DateTime.UtcNow, days, entries);
        }

        return entries;
    }

    private async Task<string> GetAsync(string path, CancellationToken ct)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Sonarr n'est pas configuré.");
        }

        using HttpRequestMessage request = new(HttpMethod.Get, Config.SonarrUrl.TrimEnd('/') + "/api/v3/" + path);
        request.Headers.Add("X-Api-Key", Config.SonarrApiKey);
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using HttpResponseMessage response = await _httpClientFactory.CreateClient(NamedClient.Default).SendAsync(request, timeout.Token).ConfigureAwait(false);
        string text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Homepage: Sonarr answered {Status} for {Path}", (int)response.StatusCode, path);
            throw new InvalidOperationException($"Sonarr a répondu {(int)response.StatusCode}");
        }

        return text;
    }

    private sealed class CalendarEntry
    {
        public int TvdbId { get; init; }

        public int? TmdbId { get; init; }

        public int Season { get; init; }

        public int Episode { get; init; }

        public string? Title { get; init; }

        public DateTime AirDateUtc { get; init; }
    }
}
