using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Jellyfin.Plugin.Homepage.Configuration;
using Jellyfin.Plugin.Homepage.Models;
using MediaBrowser.Common.Net;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Homepage.Services;

public class SeerrException : Exception
{
    public SeerrException(string message, HttpStatusCode? status = null)
        : base(message)
    {
        Status = status;
    }

    public HttpStatusCode? Status { get; }
}

/// <summary>
/// Talks to Seerr (Overseerr/Jellyseerr API). Uses the admin API key server-side and acts on behalf
/// of the Jellyfin user through the X-Api-User header, so Seerr applies that user's permissions and quotas.
/// </summary>
public class SeerrService
{
    private const string TmdbImage = "https://image.tmdb.org/t/p/";
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan UserLifetime = TimeSpan.FromMinutes(30);

    /// <summary>Only these read-only Seerr paths can be reached through the generic discover proxy.</summary>
    private static readonly string[] AllowedPrefixes = { "discover/", "search" };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly LibraryService _library;
    private readonly ILogger<SeerrService> _logger;
    private readonly ConcurrentDictionary<string, (DateTime At, string Body)> _cache = new();
    private readonly ConcurrentDictionary<Guid, (DateTime At, int? Id)> _users = new();

    public SeerrService(IHttpClientFactory httpClientFactory, LibraryService library, ILogger<SeerrService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _library = library;
        _logger = logger;
    }

    private static PluginConfiguration Config => Plugin.Instance.Configuration;

    public static bool IsConfigured => !string.IsNullOrWhiteSpace(Config.SeerrUrl) && !string.IsNullOrWhiteSpace(Config.SeerrApiKey);

    public static bool IsAllowedPath(string path) =>
        AllowedPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase))
        && !path.Contains("..", StringComparison.Ordinal)
        && !path.Contains("://", StringComparison.Ordinal);

    // ----- Users ---------------------------------------------------------------------------

    /// <summary>Finds the Seerr account of a Jellyfin user: explicit mapping, then Jellyfin link, then name.</summary>
    public async Task<int?> ResolveUserAsync(User user, CancellationToken ct)
    {
        string jfId = user.Id.ToString("N");
        UserMapping? mapping = Config.UserMappings.FirstOrDefault(m =>
            string.Equals(m.JellyfinUserId.Replace("-", string.Empty, StringComparison.Ordinal), jfId, StringComparison.OrdinalIgnoreCase));
        if (mapping is { SeerrUserId: > 0 })
        {
            return mapping.SeerrUserId;
        }

        if (_users.TryGetValue(user.Id, out var cached) && DateTime.UtcNow - cached.At < UserLifetime)
        {
            return cached.Id;
        }

        int? found = null;
        try
        {
            using JsonDocument linked = await SendAsync(HttpMethod.Get, $"user/jellyfin/{jfId}", null, null, ct).ConfigureAwait(false);
            if (linked.RootElement.TryGetProperty("id", out JsonElement idElement) && idElement.ValueKind == JsonValueKind.Number)
            {
                found = idElement.GetInt32();
            }
        }
        catch (SeerrException ex) when (ex.Status is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
        {
            // Older Seerr versions have no direct lookup; fall back to the user list.
        }

        if (found == null)
        {
            using JsonDocument list = await SendAsync(HttpMethod.Get, "user?take=1000&skip=0", null, null, ct).ConfigureAwait(false);
            if (list.RootElement.TryGetProperty("results", out JsonElement results))
            {
                foreach (JsonElement u in results.EnumerateArray())
                {
                    string? linkedId = Str(u, "jellyfinUserId")?.Replace("-", string.Empty, StringComparison.Ordinal);
                    if (string.Equals(linkedId, jfId, StringComparison.OrdinalIgnoreCase))
                    {
                        found = u.GetProperty("id").GetInt32();
                        break;
                    }
                }

                if (found == null)
                {
                    foreach (JsonElement u in results.EnumerateArray())
                    {
                        if (new[] { Str(u, "jellyfinUsername"), Str(u, "username"), Str(u, "displayName") }
                            .Any(n => string.Equals(n, user.Username, StringComparison.OrdinalIgnoreCase)))
                        {
                            found = u.GetProperty("id").GetInt32();
                            break;
                        }
                    }
                }
            }
        }

        _users[user.Id] = (DateTime.UtcNow, found);
        return found;
    }

    public async Task<List<(int Id, string Name)>> ListUsersAsync(CancellationToken ct)
    {
        using JsonDocument list = await SendAsync(HttpMethod.Get, "user?take=1000&skip=0", null, null, ct).ConfigureAwait(false);
        List<(int, string)> users = new();
        if (list.RootElement.TryGetProperty("results", out JsonElement results))
        {
            foreach (JsonElement u in results.EnumerateArray())
            {
                string name = Str(u, "displayName") ?? Str(u, "jellyfinUsername") ?? Str(u, "username") ?? Str(u, "email") ?? "?";
                users.Add((u.GetProperty("id").GetInt32(), name));
            }
        }

        return users;
    }

    public void ForgetUsers() => _users.Clear();

    // ----- Reading -------------------------------------------------------------------------

    public async Task<PagedCards> DiscoverAsync(string path, string? query, int page, string? mediaTypeFilter, int? seerrUserId, CancellationToken ct)
    {
        if (!IsAllowedPath(path))
        {
            throw new SeerrException("Chemin Seerr non autorisé : " + path);
        }

        string url = BuildUrl(path, query, page);
        using JsonDocument doc = await GetCachedAsync(url, seerrUserId, ct).ConfigureAwait(false);
        return ToPaged(doc.RootElement, page, mediaTypeFilter);
    }

    public Task<PagedCards> SearchAsync(string term, int page, int? seerrUserId, CancellationToken ct)
    {
        // Seerr rejects '+' for spaces; Uri.EscapeDataString encodes them as %20.
        return DiscoverAsync("search", "query=" + Uri.EscapeDataString(term), page, null, seerrUserId, ct);
    }

    /// <summary>Movies released recently that are already on a streaming/VOD service in the configured region.</summary>
    public async Task<PagedCards> RecentDigitalAsync(int daysBack, int page, int? seerrUserId, CancellationToken ct)
    {
        string providers = await StreamingProvidersAsync(ct).ConfigureAwait(false);
        string today = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string from = DateTime.UtcNow.AddDays(-daysBack).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        StringBuilder query = new($"primaryReleaseDateGte={from}&primaryReleaseDateLte={today}&sortBy=popularity.desc&watchRegion={Uri.EscapeDataString(Config.Region)}");
        if (!string.IsNullOrEmpty(providers))
        {
            query.Append("&watchProviders=").Append(Uri.EscapeDataString(providers));
        }

        return await DiscoverAsync("discover/movies", query.ToString(), page, null, seerrUserId, ct).ConfigureAwait(false);
    }

    public async Task<List<HomeCard>> MyRequestsAsync(int seerrUserId, int limit, CancellationToken ct)
    {
        using JsonDocument doc = await SendAsync(HttpMethod.Get, $"request?take={limit}&skip=0&filter=all&sort=added&requestedBy={seerrUserId}", null, seerrUserId, ct).ConfigureAwait(false);
        List<HomeCard> cards = new();
        if (!doc.RootElement.TryGetProperty("results", out JsonElement results))
        {
            return cards;
        }

        foreach (JsonElement request in results.EnumerateArray())
        {
            if (!request.TryGetProperty("media", out JsonElement media))
            {
                continue;
            }

            string type = Str(media, "mediaType") ?? "movie";
            int tmdbId = Int(media, "tmdbId") ?? 0;
            if (tmdbId == 0)
            {
                continue;
            }

            try
            {
                MediaDetails details = await DetailsAsync(type, tmdbId, seerrUserId, ct).ConfigureAwait(false);
                HomeCard card = new()
                {
                    Kind = details.LocalItemId != null ? "local" : "request",
                    MediaType = type,
                    TmdbId = tmdbId,
                    ItemId = details.LocalItemId,
                    DetailsId = details.LocalItemId,
                    ItemType = details.LocalItemId != null ? (type == "movie" ? "Movie" : "Series") : null,
                    IsFolder = type == "tv",
                    Title = details.Title,
                    Year = details.Year,
                    Overview = details.Overview,
                    Rating = details.Rating,
                    Genres = details.Genres.Take(3).ToList(),
                    Poster = details.Poster,
                    Backdrop = details.Backdrop,
                    Thumb = details.Backdrop,
                    SeerrStatus = Int(media, "status"),
                    Badge = RequestStatusLabel(Int(request, "status")),
                };
                card.Key = LibraryService.KeyFor(card);
                cards.Add(card);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Homepage: could not load request {Type} {Id}", type, tmdbId);
            }
        }

        return cards;
    }

    public async Task<MediaDetails> DetailsAsync(string mediaType, int tmdbId, int? seerrUserId, CancellationToken ct)
    {
        string type = mediaType == "tv" ? "tv" : "movie";
        string url = $"{type}/{tmdbId}?language={Uri.EscapeDataString(Config.Language)}";
        using JsonDocument doc = await GetCachedAsync(url, seerrUserId, ct, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
        JsonElement root = doc.RootElement;

        MediaDetails details = new()
        {
            MediaType = type,
            TmdbId = tmdbId,
            Title = Str(root, "title") ?? Str(root, "name") ?? string.Empty,
            Tagline = Str(root, "tagline"),
            Overview = Str(root, "overview"),
            Year = Year(Str(root, "releaseDate") ?? Str(root, "firstAirDate")),
            Rating = Dbl(root, "voteAverage") is double r && r > 0 ? Math.Round(r, 1) : null,
            Runtime = Int(root, "runtime") ?? (root.TryGetProperty("episodeRunTime", out JsonElement ert) && ert.ValueKind == JsonValueKind.Array && ert.GetArrayLength() > 0 ? ert[0].GetInt32() : null),
            Poster = Img(Str(root, "posterPath"), "w500"),
            Backdrop = Img(Str(root, "backdropPath"), "w1280"),
            SeerrLink = SeerrLink(type, tmdbId),
        };

        if (root.TryGetProperty("genres", out JsonElement genres) && genres.ValueKind == JsonValueKind.Array)
        {
            details.Genres = genres.EnumerateArray().Select(g => Str(g, "name")).Where(n => n != null).Select(n => n!).ToList();
        }

        Dictionary<int, int> seasonStatus = new();
        int? tvdbId = null;
        if (root.TryGetProperty("mediaInfo", out JsonElement mediaInfo) && mediaInfo.ValueKind == JsonValueKind.Object)
        {
            details.SeerrStatus = Int(mediaInfo, "status");
            tvdbId = Int(mediaInfo, "tvdbId");
            if (mediaInfo.TryGetProperty("seasons", out JsonElement mis) && mis.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement s in mis.EnumerateArray())
                {
                    if (Int(s, "seasonNumber") is int n)
                    {
                        seasonStatus[n] = Int(s, "status") ?? 1;
                    }
                }
            }

            // Pending/approved requests that are not yet downloading also block a new request for those seasons.
            if (mediaInfo.TryGetProperty("requests", out JsonElement reqs) && reqs.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement req in reqs.EnumerateArray())
                {
                    int reqStatus = Int(req, "status") ?? 0; // 1 pending, 2 approved, 3 declined
                    if (reqStatus == 3 || !req.TryGetProperty("seasons", out JsonElement rs) || rs.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (JsonElement s in rs.EnumerateArray())
                    {
                        if (Int(s, "seasonNumber") is int n && seasonStatus.GetValueOrDefault(n, 1) < 2)
                        {
                            seasonStatus[n] = 2;
                        }
                    }
                }
            }
        }

        if (root.TryGetProperty("externalIds", out JsonElement ext) && ext.ValueKind == JsonValueKind.Object)
        {
            tvdbId ??= Int(ext, "tvdbId");
        }

        if (type == "tv" && root.TryGetProperty("seasons", out JsonElement seasons) && seasons.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement s in seasons.EnumerateArray())
            {
                int number = Int(s, "seasonNumber") ?? 0;
                if (number <= 0)
                {
                    continue;
                }

                details.Seasons.Add(new SeasonInfo
                {
                    Number = number,
                    Name = Str(s, "name") ?? $"Saison {number}",
                    EpisodeCount = Int(s, "episodeCount") ?? 0,
                    Status = seasonStatus.GetValueOrDefault(number, 1),
                });
            }
        }

        if (root.TryGetProperty("credits", out JsonElement credits) && credits.ValueKind == JsonValueKind.Object)
        {
            if (credits.TryGetProperty("cast", out JsonElement cast) && cast.ValueKind == JsonValueKind.Array)
            {
                details.Cast = cast.EnumerateArray().Take(6).Select(c => Str(c, "name")).Where(n => n != null).Select(n => n!).ToList();
            }

            if (credits.TryGetProperty("crew", out JsonElement crew) && crew.ValueKind == JsonValueKind.Array)
            {
                details.Director = crew.EnumerateArray().Where(c => Str(c, "job") == "Director").Select(c => Str(c, "name")).FirstOrDefault();
            }
        }

        if (type == "tv" && root.TryGetProperty("createdBy", out JsonElement createdBy) && createdBy.ValueKind == JsonValueKind.Array)
        {
            details.Director ??= createdBy.EnumerateArray().Select(c => Str(c, "name")).FirstOrDefault();
        }

        if (root.TryGetProperty("relatedVideos", out JsonElement videos) && videos.ValueKind == JsonValueKind.Array)
        {
            details.TrailerUrl = videos.EnumerateArray()
                .Where(v => Str(v, "type") == "Trailer" && Str(v, "site") == "YouTube")
                .Select(v => Str(v, "url"))
                .FirstOrDefault();
        }

        details.LocalItemId = _library.FindLocal(type, tmdbId, tvdbId)?.ToString("N");

        if (type == "movie")
        {
            details.CanRequest = details.SeerrStatus is null or 1 or 7 && details.LocalItemId == null;
            if (!details.CanRequest)
            {
                details.CannotRequestReason = StatusLabel(details.SeerrStatus) ?? "Déjà disponible";
            }
        }
        else
        {
            details.CanRequest = details.Seasons.Any(s => s.Status is 1 or 7);
            if (!details.CanRequest)
            {
                details.CannotRequestReason = details.Seasons.Count == 0 ? "Aucune saison à demander" : "Toutes les saisons sont déjà demandées ou disponibles";
            }
        }

        return details;
    }

    // ----- Requests ------------------------------------------------------------------------

    public async Task<RequestOutcome> RequestAsync(RequestPayload payload, int seerrUserId, CancellationToken ct)
    {
        string type = payload.MediaType == "tv" ? "tv" : "movie";
        Dictionary<string, object> body = new()
        {
            ["mediaType"] = type,
            ["mediaId"] = payload.TmdbId,
        };

        if (type == "tv")
        {
            List<int> wanted = payload.Seasons?.Where(s => s > 0).Distinct().ToList() ?? new List<int>();
            if (wanted.Count == 0)
            {
                // "all" would also re-ask for seasons already requested; send only the missing ones.
                MediaDetails details = await DetailsAsync("tv", payload.TmdbId, seerrUserId, ct).ConfigureAwait(false);
                wanted = details.Seasons.Where(s => s.Status is 1 or 7).Select(s => s.Number).ToList();
                if (wanted.Count == 0)
                {
                    return new RequestOutcome { Success = false, Message = "Toutes les saisons sont déjà demandées ou disponibles." };
                }
            }

            body["seasons"] = wanted;
        }

        try
        {
            using JsonDocument result = await SendAsync(HttpMethod.Post, "request", JsonSerializer.Serialize(body), seerrUserId, ct).ConfigureAwait(false);
            InvalidateCache();
            int? mediaStatus = result.RootElement.TryGetProperty("media", out JsonElement media) ? Int(media, "status") : null;
            int requestStatus = Int(result.RootElement, "status") ?? 1;
            return new RequestOutcome
            {
                Success = true,
                SeerrStatus = mediaStatus ?? 2,
                Message = requestStatus == 2 ? "Demande envoyée et approuvée." : "Demande envoyée, en attente de validation.",
            };
        }
        catch (SeerrException ex)
        {
            return new RequestOutcome { Success = false, Message = ex.Message };
        }
    }

    public async Task<string> TestAsync(CancellationToken ct)
    {
        using JsonDocument status = await SendAsync(HttpMethod.Get, "status", null, null, ct).ConfigureAwait(false);
        using JsonDocument me = await SendAsync(HttpMethod.Get, "auth/me", null, null, ct).ConfigureAwait(false);
        return $"Seerr {Str(status.RootElement, "version")} — clé valide ({Str(me.RootElement, "displayName") ?? Str(me.RootElement, "email")})";
    }

    public async Task<List<Dictionary<string, object?>>> SlidersAsync(CancellationToken ct)
    {
        using JsonDocument doc = await SendAsync(HttpMethod.Get, "settings/discover", null, null, ct).ConfigureAwait(false);
        List<Dictionary<string, object?>> sliders = new();
        foreach (JsonElement s in doc.RootElement.EnumerateArray())
        {
            (string? path, string? query) = SliderSource(Int(s, "type") ?? 0, Str(s, "data"));
            if (path == null)
            {
                continue;
            }

            sliders.Add(new Dictionary<string, object?>
            {
                ["title"] = Str(s, "title") ?? DefaultSliderTitle(Int(s, "type") ?? 0),
                ["enabled"] = s.TryGetProperty("enabled", out JsonElement e) && e.ValueKind == JsonValueKind.True,
                ["seerrPath"] = path,
                ["seerrQuery"] = query ?? string.Empty,
            });
        }

        return sliders;
    }

    public void InvalidateCache() => _cache.Clear();

    // ----- Plumbing ------------------------------------------------------------------------

    private async Task<string> StreamingProvidersAsync(CancellationToken ct)
    {
        try
        {
            using JsonDocument doc = await GetCachedAsync($"watchproviders/movies?watchRegion={Uri.EscapeDataString(Config.Region)}", null, ct, TimeSpan.FromHours(24)).ConfigureAwait(false);
            return string.Join('|', doc.RootElement.EnumerateArray()
                .OrderBy(p => Int(p, "displayPriority") ?? 999)
                .Take(20)
                .Select(p => Int(p, "id"))
                .Where(id => id.HasValue)
                .Select(id => id!.Value.ToString(CultureInfo.InvariantCulture)));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Homepage: streaming providers unavailable");
            return string.Empty;
        }
    }

    private PagedCards ToPaged(JsonElement root, int page, string? mediaTypeFilter)
    {
        PagedCards paged = new()
        {
            Page = Int(root, "page") ?? page,
            TotalPages = Int(root, "totalPages") ?? 1,
        };

        if (!root.TryGetProperty("results", out JsonElement results) || results.ValueKind != JsonValueKind.Array)
        {
            return paged;
        }

        foreach (JsonElement r in results.EnumerateArray())
        {
            HomeCard? card = ToCard(r);
            if (card != null && (string.IsNullOrEmpty(mediaTypeFilter) || card.MediaType == mediaTypeFilter))
            {
                paged.Items.Add(card);
            }
        }

        return paged;
    }

    private HomeCard? ToCard(JsonElement r)
    {
        string? mediaType = Str(r, "mediaType");
        if (mediaType is not ("movie" or "tv"))
        {
            return null;
        }

        int tmdbId = Int(r, "id") ?? 0;
        if (tmdbId == 0)
        {
            return null;
        }

        HomeCard card = new()
        {
            MediaType = mediaType,
            TmdbId = tmdbId,
            Title = Str(r, "title") ?? Str(r, "name") ?? string.Empty,
            Year = Year(Str(r, "releaseDate") ?? Str(r, "firstAirDate")),
            Overview = Str(r, "overview"),
            Rating = Dbl(r, "voteAverage") is double v && v > 0 ? Math.Round(v, 1) : null,
            Poster = Img(Str(r, "posterPath"), "w342"),
            Backdrop = Img(Str(r, "backdropPath"), "w1280"),
            Thumb = Img(Str(r, "backdropPath"), "w780"),
            IsFolder = mediaType == "tv",
        };

        if (DateTime.TryParse(Str(r, "releaseDate") ?? Str(r, "firstAirDate"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTime date))
        {
            card.Date = date;
        }

        if (r.TryGetProperty("mediaInfo", out JsonElement mediaInfo) && mediaInfo.ValueKind == JsonValueKind.Object)
        {
            card.SeerrStatus = Int(mediaInfo, "status");
            card.TvdbId = Int(mediaInfo, "tvdbId");
        }

        Guid? local = _library.FindLocal(mediaType, tmdbId, card.TvdbId);
        if (local.HasValue)
        {
            card.Kind = "local";
            card.ItemId = local.Value.ToString("N");
            card.DetailsId = card.ItemId;
            card.ItemType = mediaType == "movie" ? "Movie" : "Series";
        }
        else
        {
            card.Kind = "request";
        }

        card.Key = LibraryService.KeyFor(card);
        return card;
    }

    private async Task<JsonDocument> GetCachedAsync(string url, int? seerrUserId, CancellationToken ct, TimeSpan? lifetime = null)
    {
        string key = (seerrUserId?.ToString(CultureInfo.InvariantCulture) ?? "-") + "|" + url;
        if (_cache.TryGetValue(key, out var entry) && DateTime.UtcNow - entry.At < (lifetime ?? CacheLifetime))
        {
            return JsonDocument.Parse(entry.Body);
        }

        string body = await SendRawAsync(HttpMethod.Get, url, null, seerrUserId, ct).ConfigureAwait(false);
        _cache[key] = (DateTime.UtcNow, body);
        if (_cache.Count > 500)
        {
            foreach (var old in _cache.OrderBy(kv => kv.Value.At).Take(250))
            {
                _cache.TryRemove(old.Key, out _);
            }
        }

        return JsonDocument.Parse(body);
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string url, string? jsonBody, int? seerrUserId, CancellationToken ct)
    {
        string body = await SendRawAsync(method, url, jsonBody, seerrUserId, ct).ConfigureAwait(false);
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
    }

    private async Task<string> SendRawAsync(HttpMethod method, string url, string? jsonBody, int? seerrUserId, CancellationToken ct)
    {
        if (!IsConfigured)
        {
            throw new SeerrException("Seerr n'est pas configuré.");
        }

        using HttpRequestMessage request = new(method, Config.SeerrUrl.TrimEnd('/') + "/api/v1/" + url.TrimStart('/'));
        request.Headers.Add("X-Api-Key", Config.SeerrApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (seerrUserId.HasValue)
        {
            request.Headers.Add("X-Api-User", seerrUserId.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (jsonBody != null)
        {
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

        HttpClient client = _httpClientFactory.CreateClient(NamedClient.Default);
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SeerrException("Seerr injoignable : " + ex.Message);
        }

        using (response)
        {
            string text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                string message = text;
                try
                {
                    using JsonDocument error = JsonDocument.Parse(text);
                    message = Str(error.RootElement, "message") ?? text;
                }
                catch (JsonException)
                {
                }

                if (message.Length > 300)
                {
                    message = message[..300];
                }

                throw new SeerrException($"Seerr a répondu {(int)response.StatusCode} : {message}", response.StatusCode);
            }

            return text;
        }
    }

    private static string BuildUrl(string path, string? query, int page)
    {
        List<string> parts = new() { "page=" + Math.Max(1, page).ToString(CultureInfo.InvariantCulture) };
        if (!path.StartsWith("search", StringComparison.OrdinalIgnoreCase))
        {
            parts.Add("language=" + Uri.EscapeDataString(Config.Language));
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            // Drop paging/language from configured queries: they are controlled here.
            parts.AddRange(query.TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Where(p => !p.StartsWith("page=", StringComparison.OrdinalIgnoreCase) && !p.StartsWith("language=", StringComparison.OrdinalIgnoreCase)));
        }

        return path.Trim('/') + "?" + string.Join('&', parts);
    }

    private string? SeerrLink(string type, int tmdbId)
    {
        string baseUrl = string.IsNullOrWhiteSpace(Config.SeerrPublicUrl) ? Config.SeerrUrl : Config.SeerrPublicUrl;
        return string.IsNullOrWhiteSpace(baseUrl) ? null : $"{baseUrl.TrimEnd('/')}/{type}/{tmdbId}";
    }

    public static string? StatusLabel(int? status) => status switch
    {
        2 => "Demandé",
        3 => "En cours",
        4 => "Partiellement disponible",
        5 => "Disponible",
        6 => "Bloqué",
        _ => null,
    };

    private static string? RequestStatusLabel(int? status) => status switch
    {
        1 => "En attente",
        2 => "Approuvé",
        3 => "Refusé",
        4 => "Échec",
        5 => "Terminé",
        _ => null,
    };

    private static (string? Path, string? Query) SliderSource(int type, string? data)
    {
        string d = Uri.EscapeDataString(data ?? string.Empty);
        string upcoming = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return type switch
        {
            4 => ("discover/trending", null),
            5 => ("discover/movies", null),
            7 => ("discover/movies", "primaryReleaseDateGte=" + upcoming),
            9 => ("discover/tv", null),
            11 => ("discover/tv", "firstAirDateGte=" + upcoming),
            13 => ("discover/movies", "keywords=" + d),
            14 => ("discover/tv", "keywords=" + d),
            15 => ("discover/movies", "genre=" + d),
            16 => ("discover/tv", "genre=" + d),
            17 => ("search", "query=" + d),
            18 => ($"discover/movies/studio/{d}", null),
            19 => ($"discover/tv/network/{d}", null),
            20 => ("discover/movies", "watchRegion=" + Uri.EscapeDataString((data ?? string.Empty).Split(',')[0]) + "&watchProviders=" + Uri.EscapeDataString((data ?? string.Empty).Split(',').ElementAtOrDefault(1) ?? string.Empty)),
            21 => ("discover/tv", "watchRegion=" + Uri.EscapeDataString((data ?? string.Empty).Split(',')[0]) + "&watchProviders=" + Uri.EscapeDataString((data ?? string.Empty).Split(',').ElementAtOrDefault(1) ?? string.Empty)),
            _ => (null, null),
        };
    }

    private static string DefaultSliderTitle(int type) => type switch
    {
        4 => "Tendances",
        5 => "Films populaires",
        7 => "Films à venir",
        9 => "Séries populaires",
        11 => "Séries à venir",
        _ => "Seerr",
    };

    private static string? Img(string? path, string size) => string.IsNullOrEmpty(path) ? null : TmdbImage + size + path;

    private static int? Year(string? date) =>
        date != null && date.Length >= 4 && int.TryParse(date.AsSpan(0, 4), NumberStyles.Integer, CultureInfo.InvariantCulture, out int y) ? y : null;

    internal static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    internal static int? Int(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int i) ? i : null;

    internal static double? Dbl(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
}
