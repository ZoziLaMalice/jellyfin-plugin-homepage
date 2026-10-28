using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Homepage.Models;

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}

/// <summary>A user's personal choices; null means "use the admin default".</summary>
public class UserSettings
{
    public bool? Enabled { get; set; }

    public bool? HeroEnabled { get; set; }

    public bool? HeaderEnabled { get; set; }

    /// <summary>Gets or sets the user's section order and visibility. Sections missing here keep the admin order.</summary>
    public List<UserSectionPreference> Sections { get; set; } = new();

    /// <summary>
    /// Gets or sets the libraries (N format) this user does not want on their home: hero, library and genre sections, search.
    /// New libraries are included until the user excludes them.
    /// </summary>
    public List<string> ExcludedLibraryIds { get; set; } = new();
}

/// <summary>A movie or series library the user can see, offered in the "my libraries" choice.</summary>
public class LibraryChoice
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets movies or tvshows.</summary>
    public string Type { get; set; } = "movies";

    /// <summary>Gets or sets a value indicating whether the library holds placeholders (JellyNext recommendations...).</summary>
    public bool IsVirtual { get; set; }
}

/// <summary>Admin view of one user's library choice.</summary>
public class UserLibrariesDto
{
    public List<LibraryChoice> Libraries { get; set; } = new();

    public List<string> ExcludedLibraryIds { get; set; } = new();
}

public class UserSectionPreference
{
    public string Id { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;
}

/// <summary>A single tile: either something the user owns, or something that can be requested from Seerr.</summary>
public class HomeCard
{
    /// <summary>Gets or sets a stable key for list rendering.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Gets or sets "local" (playable here) or "request" (asked for through Seerr).</summary>
    public string Kind { get; set; } = "local";

    /// <summary>Gets or sets movie or tv (the requestable media type), episode for episodes.</summary>
    public string MediaType { get; set; } = "movie";

    /// <summary>Gets or sets the Jellyfin item id (N format) for local items, or the placeholder item for virtual ones.</summary>
    public string? ItemId { get; set; }

    /// <summary>Gets or sets the Jellyfin item kind (Movie, Series, Episode...).</summary>
    public string? ItemType { get; set; }

    /// <summary>Gets or sets the item to open in Jellyfin's details page (the series for an episode).</summary>
    public string? DetailsId { get; set; }

    public bool IsFolder { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Subtitle { get; set; }

    public int? Year { get; set; }

    public string? Overview { get; set; }

    public double? Rating { get; set; }

    public string? OfficialRating { get; set; }

    public long? RunTimeTicks { get; set; }

    public List<string> Genres { get; set; } = new();

    /// <summary>Gets or sets the 2:3 image; relative (/Items/...) for Jellyfin, absolute for TMDB.</summary>
    public string? Poster { get; set; }

    /// <summary>Gets or sets the 16:9 image used by landscape cards.</summary>
    public string? Thumb { get; set; }

    /// <summary>Gets or sets the full-size backdrop used by the hero.</summary>
    public string? Backdrop { get; set; }

    public string? Logo { get; set; }

    public int? TmdbId { get; set; }

    public int? TvdbId { get; set; }

    /// <summary>Gets or sets playback progress in percent.</summary>
    public double? Progress { get; set; }

    public long? PositionTicks { get; set; }

    public bool Played { get; set; }

    /// <summary>Gets or sets a short label shown on the card (S02E03, an air date...).</summary>
    public string? Badge { get; set; }

    /// <summary>Gets or sets the Seerr media status: 1 unknown, 2 pending, 3 processing, 4 partially available, 5 available, 6 blocklisted, 7 deleted.</summary>
    public int? SeerrStatus { get; set; }

    /// <summary>Gets or sets the seasons to request (empty means all) for "new season" cards.</summary>
    public List<int>? RequestSeasons { get; set; }

    /// <summary>Gets or sets a date shown by date-based sections (air date, release date).</summary>
    public DateTime? Date { get; set; }

    [JsonIgnore]
    public bool IsVirtual { get; set; }

    [JsonIgnore]
    public DateTime AddedAt { get; set; }

    [JsonIgnore]
    public DateTime ContentAddedAt { get; set; }

    [JsonIgnore]
    public DateTime? PremieredAt { get; set; }
}

public class SectionRow
{
    public string Title { get; set; } = string.Empty;

    public string Display { get; set; } = "Poster";

    /// <summary>Gets or sets an optional "see all" target understood by the client.</summary>
    public string? MoreLink { get; set; }

    public List<HomeCard> Items { get; set; } = new();
}

public class SectionResult
{
    public List<SectionRow> Rows { get; set; } = new();

    public string? Error { get; set; }
}

public class PagedCards
{
    public int Page { get; set; }

    public int TotalPages { get; set; }

    public List<HomeCard> Items { get; set; } = new();
}

public class SearchResult
{
    public List<HomeCard> Library { get; set; } = new();

    public List<HomeCard> Seerr { get; set; } = new();

    public string? SeerrError { get; set; }
}

public class RequestPayload
{
    public string MediaType { get; set; } = "movie";

    public int TmdbId { get; set; }

    /// <summary>Gets or sets the seasons to request; null or empty requests every season.</summary>
    public List<int>? Seasons { get; set; }
}

public class RequestOutcome
{
    public bool Success { get; set; }

    public string Message { get; set; } = string.Empty;

    public int? SeerrStatus { get; set; }
}

public class MediaDetails
{
    public string MediaType { get; set; } = "movie";

    public int TmdbId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Tagline { get; set; }

    public string? Overview { get; set; }

    public int? Year { get; set; }

    public double? Rating { get; set; }

    public int? Runtime { get; set; }

    public List<string> Genres { get; set; } = new();

    public string? Poster { get; set; }

    public string? Backdrop { get; set; }

    public string? Logo { get; set; }

    public int? SeerrStatus { get; set; }

    public string? LocalItemId { get; set; }

    public string? SeerrLink { get; set; }

    public string? TrailerUrl { get; set; }

    public List<SeasonInfo> Seasons { get; set; } = new();

    public List<string> Cast { get; set; } = new();

    public string? Director { get; set; }

    public bool CanRequest { get; set; }

    public string? CannotRequestReason { get; set; }
}

public class SeasonInfo
{
    public int Number { get; set; }

    public string Name { get; set; } = string.Empty;

    public int EpisodeCount { get; set; }

    /// <summary>Gets or sets the Seerr status of this season (same values as SeerrStatus).</summary>
    public int Status { get; set; } = 1;
}

/// <summary>Everything the client needs to draw the page, merged from admin config and the user's choices.</summary>
public class ClientLayout
{
    public bool Enabled { get; set; }

    public bool CanToggle { get; set; }

    public bool CanCustomize { get; set; }

    public bool IsAdmin { get; set; }

    public int Generation { get; set; }

    public bool SeerrConfigured { get; set; }

    public bool SonarrConfigured { get; set; }

    public bool ConfirmRequests { get; set; }

    public ClientHeader Header { get; set; } = new();

    public ClientHero Hero { get; set; } = new();

    public List<ClientSection> Sections { get; set; } = new();

    public List<ClientDiscoverRow> DiscoverRows { get; set; } = new();
}

public class ClientHeader
{
    public bool Enabled { get; set; }

    public bool ReplaceSearch { get; set; }

    public bool HideNativeLinks { get; set; }

    public List<ClientTab> Tabs { get; set; } = new();
}

public class ClientTab
{
    public string Type { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Target { get; set; }
}

public class ClientHero
{
    public bool Enabled { get; set; }

    public int IntervalSeconds { get; set; }
}

public class ClientSection
{
    public string Id { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Display { get; set; } = "Poster";

    public bool Enabled { get; set; } = true;

    public bool UserCanHide { get; set; } = true;
}

public class ClientDiscoverRow
{
    public string Id { get; set; } = string.Empty;

    public string Page { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
}
