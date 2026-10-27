using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Homepage.Configuration;

/// <summary>
/// Builds the initial layout and keeps configurations consistent after edits.
/// </summary>
public static class DefaultsFactory
{
    public static readonly string[] SectionTypes =
    {
        "ContinueWatching", "Library", "Genres", "SeerrDiscover", "RecentDigital", "Upcoming", "MyRequests",
    };

    public static readonly string[] TabTypes =
    {
        "Home", "Favorites", "Discover", "DiscoverMovies", "DiscoverTv", "Search", "Requests", "Library", "Link",
    };

    /// <summary>
    /// Adds the parts of the configuration that do not depend on libraries (header tabs, discover rows).
    /// </summary>
    /// <returns>True when something changed.</returns>
    public static bool EnsureDefaults(PluginConfiguration config)
    {
        bool changed = false;

        if (config.Header.Tabs.Count == 0)
        {
            config.Header.Tabs = DefaultTabs();
            changed = true;
        }

        if (config.DiscoverRows.Count == 0)
        {
            config.DiscoverRows = DefaultDiscoverRows();
            changed = true;
        }

        return Normalize(config) || changed;
    }

    /// <summary>
    /// Creates the default home sections from the server's libraries. Only runs once per install.
    /// </summary>
    /// <returns>True when sections were created.</returns>
    public static bool EnsureSections(PluginConfiguration config, IReadOnlyList<VirtualFolderInfo> folders)
    {
        if (config.Initialized)
        {
            return false;
        }

        string[] markers = VirtualMarkers(config);
        bool IsVirtual(VirtualFolderInfo f) => f.Locations.Any(l => markers.Any(m => l.Contains(m, StringComparison.OrdinalIgnoreCase)));
        bool IsNextSeasons(VirtualFolderInfo f) => f.Locations.Any(l => l.Contains("nextseason", StringComparison.OrdinalIgnoreCase));

        List<VirtualFolderInfo> movies = folders.Where(f => f.CollectionType == CollectionTypeOptions.movies).ToList();
        List<VirtualFolderInfo> shows = folders.Where(f => f.CollectionType == CollectionTypeOptions.tvshows).ToList();

        List<string> movieIds = movies.Select(f => f.ItemId).ToList();
        List<string> showIds = shows.Where(f => !IsNextSeasons(f)).Select(f => f.ItemId).ToList();
        List<string> newSeasonIds = shows.Where(f => !IsVirtual(f) || IsNextSeasons(f)).Select(f => f.ItemId).ToList();
        List<string> localIds = folders
            .Where(f => (f.CollectionType == CollectionTypeOptions.movies || f.CollectionType == CollectionTypeOptions.tvshows) && !IsVirtual(f))
            .Select(f => f.ItemId)
            .ToList();

        config.Sections = new List<SectionConfig>
        {
            new() { Type = "ContinueWatching", Title = "Continuer à regarder", Display = "Landscape", IncludeNextUp = true, Limit = 20 },
            new() { Type = "Library", Title = "Films", LibraryIds = movieIds, ItemTypes = "Movie", Sort = "DateCreated", Interleave = true, HideWatched = true },
            new() { Type = "Library", Title = "Séries", LibraryIds = showIds, ItemTypes = "Series", Sort = "DateLastContentAdded", Interleave = true },
            new() { Type = "RecentDigital", Title = "Sorties digitales récentes", DaysBack = 90 },
            new() { Type = "Library", Title = "Nouveaux épisodes et saisons", LibraryIds = newSeasonIds, ItemTypes = "Series", Sort = "DateLastContentAdded", Interleave = false },
            new() { Type = "Upcoming", Title = "Prochainement dans tes séries", Display = "Landscape", DaysAhead = 30 },
            new() { Type = "Genres", Title = "{genre}", LibraryIds = localIds, ItemTypes = "Movie,Series", Sort = "Random", GenreRows = 3 },
            new() { Type = "SeerrDiscover", Title = "Tendances", SeerrPath = "discover/trending", Enabled = false },
        };

        config.Hero.LibraryIds = movies.Concat(shows.Where(f => !IsNextSeasons(f))).Select(f => f.ItemId).ToList();
        config.Initialized = true;
        Normalize(config);
        return true;
    }

    /// <summary>Fixes ids, bounds and unknown values after an admin edit.</summary>
    /// <returns>True when something changed.</returns>
    public static bool Normalize(PluginConfiguration config)
    {
        bool changed = false;
        HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);

        foreach (SectionConfig section in config.Sections)
        {
            if (string.IsNullOrWhiteSpace(section.Id) || !ids.Add(section.Id))
            {
                section.Id = Guid.NewGuid().ToString("N");
                ids.Add(section.Id);
                changed = true;
            }

            if (!SectionTypes.Contains(section.Type, StringComparer.Ordinal))
            {
                section.Type = "Library";
                changed = true;
            }

            section.Limit = Math.Clamp(section.Limit, 4, 60);
            section.GenreRows = Math.Clamp(section.GenreRows, 1, 10);
            section.DaysBack = Math.Clamp(section.DaysBack, 7, 365);
            section.DaysAhead = Math.Clamp(section.DaysAhead, 1, 120);
            section.SeerrPath = (section.SeerrPath ?? string.Empty).Trim().TrimStart('/');
            if (section.SeerrPath.StartsWith("api/v1/", StringComparison.OrdinalIgnoreCase))
            {
                section.SeerrPath = section.SeerrPath[7..];
            }

            section.LibraryIds = section.LibraryIds.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant()).Distinct().ToList();
        }

        ids.Clear();
        foreach (DiscoverRowConfig row in config.DiscoverRows)
        {
            if (string.IsNullOrWhiteSpace(row.Id) || !ids.Add(row.Id))
            {
                row.Id = Guid.NewGuid().ToString("N");
                ids.Add(row.Id);
                changed = true;
            }

            row.SeerrPath = (row.SeerrPath ?? string.Empty).Trim().TrimStart('/');
        }

        config.Hero.Count = Math.Clamp(config.Hero.Count, 1, 30);
        config.Hero.IntervalSeconds = Math.Clamp(config.Hero.IntervalSeconds, 3, 60);
        config.Hero.LibraryIds = config.Hero.LibraryIds.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant()).Distinct().ToList();
        config.SeerrUrl = (config.SeerrUrl ?? string.Empty).Trim().TrimEnd('/');
        config.SeerrPublicUrl = (config.SeerrPublicUrl ?? string.Empty).Trim().TrimEnd('/');
        config.SonarrUrl = (config.SonarrUrl ?? string.Empty).Trim().TrimEnd('/');
        config.AssetBaseUrl = (config.AssetBaseUrl ?? string.Empty).Trim().TrimEnd('/');
        config.Region = string.IsNullOrWhiteSpace(config.Region) ? "FR" : config.Region.Trim().ToUpperInvariant();
        config.Language = string.IsNullOrWhiteSpace(config.Language) ? "fr" : config.Language.Trim();

        return changed;
    }

    public static string[] VirtualMarkers(PluginConfiguration config) =>
        (config.VirtualPathMarkers ?? string.Empty)
            .Split(new[] { '\n', '\r', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length > 0)
            .ToArray();

    private static List<HeaderTab> DefaultTabs() => new()
    {
        new() { Type = "Home", Title = "Accueil" },
        new() { Type = "Discover", Title = "Découvrir" },
        new() { Type = "DiscoverMovies", Title = "Films" },
        new() { Type = "DiscoverTv", Title = "Séries" },
        new() { Type = "Favorites", Title = "Favoris" },
        new() { Type = "Requests", Title = "Mes demandes", Enabled = false },
        new() { Type = "Search", Title = "Rechercher" },
    };

    private static List<DiscoverRowConfig> DefaultDiscoverRows() => new()
    {
        new() { Page = "Discover", Title = "Tendances", SeerrPath = "discover/trending" },
        new() { Page = "Discover", Title = "Films populaires", SeerrPath = "discover/movies" },
        new() { Page = "Discover", Title = "Séries populaires", SeerrPath = "discover/tv" },
        new() { Page = "Discover", Title = "Films à venir", SeerrPath = "discover/movies/upcoming" },
        new() { Page = "Discover", Title = "Séries à venir", SeerrPath = "discover/tv/upcoming" },
        new() { Page = "Movies", Title = "Tendances", SeerrPath = "discover/trending", MediaType = "movie" },
        new() { Page = "Movies", Title = "Populaires", SeerrPath = "discover/movies" },
        new() { Page = "Movies", Title = "Mieux notés", SeerrPath = "discover/movies", SeerrQuery = "sortBy=vote_average.desc&voteCountGte=2000" },
        new() { Page = "Movies", Title = "À venir", SeerrPath = "discover/movies/upcoming" },
        new() { Page = "Movies", Title = "Action", SeerrPath = "discover/movies", SeerrQuery = "genre=28" },
        new() { Page = "Movies", Title = "Comédie", SeerrPath = "discover/movies", SeerrQuery = "genre=35" },
        new() { Page = "Movies", Title = "Science-fiction", SeerrPath = "discover/movies", SeerrQuery = "genre=878" },
        new() { Page = "Movies", Title = "Animation", SeerrPath = "discover/movies", SeerrQuery = "genre=16" },
        new() { Page = "Tv", Title = "Tendances", SeerrPath = "discover/trending", MediaType = "tv" },
        new() { Page = "Tv", Title = "Populaires", SeerrPath = "discover/tv" },
        new() { Page = "Tv", Title = "Mieux notées", SeerrPath = "discover/tv", SeerrQuery = "sortBy=vote_average.desc&voteCountGte=1000" },
        new() { Page = "Tv", Title = "À venir", SeerrPath = "discover/tv/upcoming" },
        new() { Page = "Tv", Title = "Crime", SeerrPath = "discover/tv", SeerrQuery = "genre=80" },
        new() { Page = "Tv", Title = "Comédie", SeerrPath = "discover/tv", SeerrQuery = "genre=35" },
        new() { Page = "Tv", Title = "Science-fiction & Fantastique", SeerrPath = "discover/tv", SeerrQuery = "genre=10765" },
        new() { Page = "Tv", Title = "Animation", SeerrPath = "discover/tv", SeerrQuery = "genre=16" },
    };
}
