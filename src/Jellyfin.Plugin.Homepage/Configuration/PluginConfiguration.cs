using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Homepage.Configuration;

/// <summary>
/// Server-wide (admin) configuration. Serialized to XML by Jellyfin, so only simple types and lists.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    // ----- General -------------------------------------------------------------------------

    /// <summary>Gets or sets a value indicating whether default sections were generated (only done once).</summary>
    public bool Initialized { get; set; }

    /// <summary>Gets or sets a value indicating whether the custom home is on for users who never chose.</summary>
    public bool EnabledByDefault { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether users can turn the custom home on/off for themselves.</summary>
    public bool AllowUserToggle { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether users can hide/reorder sections for themselves.</summary>
    public bool AllowUserSections { get; set; } = true;

    /// <summary>Gets or sets user ids (N format) for whom the plugin is forced off, whatever their choice.</summary>
    public List<string> DisabledUserIds { get; set; } = new();

    /// <summary>
    /// Gets or sets path fragments identifying "virtual" libraries whose items are placeholders
    /// (JellyNext recommendations, next seasons...). Their items are shown as requestable, not playable.
    /// One fragment per line.
    /// </summary>
    public string VirtualPathMarkers { get; set; } = "/jellynext-virtual/";

    /// <summary>Gets or sets an optional base URL to load client assets from instead of the embedded ones (development).</summary>
    public string AssetBaseUrl { get; set; } = string.Empty;

    // ----- Seerr ---------------------------------------------------------------------------

    /// <summary>Gets or sets the Seerr URL as reachable from the Jellyfin server.</summary>
    public string SeerrUrl { get; set; } = string.Empty;

    /// <summary>Gets or sets the Seerr URL used for "open in Seerr" links (defaults to <see cref="SeerrUrl"/>).</summary>
    public string SeerrPublicUrl { get; set; } = string.Empty;

    public string SeerrApiKey { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether a confirmation is shown before a request is sent.</summary>
    public bool ConfirmRequests { get; set; } = true;

    /// <summary>Gets or sets explicit Jellyfin user → Seerr user links, checked before automatic matching.</summary>
    public List<UserMapping> UserMappings { get; set; } = new();

    /// <summary>Gets or sets the region used for streaming availability (ISO 3166-1, e.g. FR).</summary>
    public string Region { get; set; } = "FR";

    /// <summary>Gets or sets the language passed to Seerr for titles and overviews (e.g. fr).</summary>
    public string Language { get; set; } = "fr";

    // ----- Sonarr --------------------------------------------------------------------------

    public string SonarrUrl { get; set; } = string.Empty;

    public string SonarrApiKey { get; set; } = string.Empty;

    // ----- Layout --------------------------------------------------------------------------

    public HeaderConfig Header { get; set; } = new();

    public HeroConfig Hero { get; set; } = new();

    public List<SectionConfig> Sections { get; set; } = new();

    /// <summary>Gets or sets the rows displayed on the Discover pages.</summary>
    public List<DiscoverRowConfig> DiscoverRows { get; set; } = new();
}

public class UserMapping
{
    /// <summary>Gets or sets the Jellyfin user id (N format).</summary>
    public string JellyfinUserId { get; set; } = string.Empty;

    public int SeerrUserId { get; set; }
}

public class HeaderConfig
{
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the header search button opens the combined (library + Seerr) search.</summary>
    public bool ReplaceSearch { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether Jellyfin's own top-bar links (favorites, libraries) are hidden, keeping only the logo.</summary>
    public bool HideNativeLinks { get; set; } = true;

    public List<HeaderTab> Tabs { get; set; } = new();
}

public class HeaderTab
{
    /// <summary>Gets or sets the kind: Home, Favorites, Discover, DiscoverMovies, DiscoverTv, Search, Requests, Library, Link.</summary>
    public string Type { get; set; } = "Home";

    public string Title { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the target: a library id for Library tabs, a URL or #/route for Link tabs.</summary>
    public string Target { get; set; } = string.Empty;
}

public class HeroConfig
{
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the libraries the hero picks from; empty means all movie and series libraries.</summary>
    public List<string> LibraryIds { get; set; } = new();

    public bool IncludeMovies { get; set; } = true;

    public bool IncludeSeries { get; set; } = true;

    public int Count { get; set; } = 10;

    /// <summary>Gets or sets the order: Random, DateCreated, PremiereDate, CommunityRating.</summary>
    public string Sort { get; set; } = "Random";

    public int IntervalSeconds { get; set; } = 8;

    public bool HideWatched { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether items without a logo are skipped.</summary>
    public bool RequireLogo { get; set; }

    /// <summary>Gets or sets a value indicating whether Seerr trending titles fill the hero too.</summary>
    public bool IncludeSeerrTrending { get; set; }
}

public class SectionConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Gets or sets the kind: ContinueWatching, Library, Genres, SeerrDiscover, RecentDigital, Upcoming, MyRequests.
    /// </summary>
    public string Type { get; set; } = "Library";

    public string Title { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether users may hide this section.</summary>
    public bool UserCanHide { get; set; } = true;

    /// <summary>Gets or sets the card shape: Poster or Landscape.</summary>
    public string Display { get; set; } = "Poster";

    public int Limit { get; set; } = 24;

    // Library / Genres / Hero-like sources

    public List<string> LibraryIds { get; set; } = new();

    /// <summary>Gets or sets item kinds, comma separated: Movie, Series.</summary>
    public string ItemTypes { get; set; } = "Movie,Series";

    /// <summary>Gets or sets the order: DateCreated, DateLastContentAdded, Random, CommunityRating, PremiereDate, Name.</summary>
    public string Sort { get; set; } = "DateCreated";

    /// <summary>Gets or sets a value indicating whether owned and requestable items alternate instead of being sorted together.</summary>
    public bool Interleave { get; set; } = true;

    public bool HideWatched { get; set; }

    // ContinueWatching

    public bool IncludeNextUp { get; set; } = true;

    // Genres

    /// <summary>Gets or sets genre names, comma separated; empty picks genres automatically.</summary>
    public string Genres { get; set; } = string.Empty;

    public int GenreRows { get; set; } = 3;

    // Seerr

    /// <summary>Gets or sets the Seerr API path below /api/v1, e.g. discover/movies or discover/trending.</summary>
    public string SeerrPath { get; set; } = string.Empty;

    /// <summary>Gets or sets extra Seerr query parameters, e.g. genre=18&amp;sortBy=popularity.desc.</summary>
    public string SeerrQuery { get; set; } = string.Empty;

    public bool HideAvailable { get; set; }

    // RecentDigital / Upcoming

    public int DaysBack { get; set; } = 90;

    public int DaysAhead { get; set; } = 30;

    /// <summary>Gets or sets a value indicating whether Upcoming only keeps series the user has started.</summary>
    public bool OnlyWatched { get; set; }
}

public class DiscoverRowConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Gets or sets the page the row appears on: Discover, Movies or Tv.</summary>
    public string Page { get; set; } = "Discover";

    public string Title { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public string SeerrPath { get; set; } = string.Empty;

    public string SeerrQuery { get; set; } = string.Empty;

    /// <summary>Gets or sets an optional media type filter applied to mixed results: movie or tv.</summary>
    public string MediaType { get; set; } = string.Empty;
}
