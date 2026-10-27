using Jellyfin.Plugin.Homepage.Configuration;
using Jellyfin.Plugin.Homepage.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Homepage.Services;

/// <summary>
/// Merges admin configuration and user choices, and computes section contents.
/// </summary>
public class LayoutService
{
    private readonly LibraryService _library;
    private readonly SeerrService _seerr;
    private readonly SonarrService _sonarr;
    private readonly UserSettingsStore _userSettings;
    private readonly ILogger<LayoutService> _logger;

    public LayoutService(LibraryService library, SeerrService seerr, SonarrService sonarr, UserSettingsStore userSettings, ILogger<LayoutService> logger)
    {
        _library = library;
        _seerr = seerr;
        _sonarr = sonarr;
        _userSettings = userSettings;
        _logger = logger;
    }

    private static PluginConfiguration Config => Plugin.Instance.Configuration;

    public ClientLayout GetLayout(User user)
    {
        PluginConfiguration config = Config;
        UserSettings settings = _userSettings.Get(user.Id);
        bool forcedOff = config.DisabledUserIds.Any(id => string.Equals(id.Replace("-", string.Empty, StringComparison.Ordinal), user.Id.ToString("N"), StringComparison.OrdinalIgnoreCase));
        bool enabled = !forcedOff && (config.AllowUserToggle ? settings.Enabled ?? config.EnabledByDefault : config.EnabledByDefault);

        ClientLayout layout = new()
        {
            Enabled = enabled,
            CanToggle = config.AllowUserToggle && !forcedOff,
            CanCustomize = config.AllowUserSections,
            IsAdmin = user.HasPermission(PermissionKind.IsAdministrator),
            Generation = Plugin.Instance.ConfigurationGeneration,
            SeerrConfigured = SeerrService.IsConfigured,
            SonarrConfigured = SonarrService.IsConfigured,
            ConfirmRequests = config.ConfirmRequests,
            Header = new ClientHeader
            {
                Enabled = config.Header.Enabled && (settings.HeaderEnabled ?? true),
                ReplaceSearch = config.Header.ReplaceSearch,
                HideNativeLinks = config.Header.HideNativeLinks,
                Tabs = config.Header.Tabs
                    .Where(t => t.Enabled)
                    .Where(t => SeerrService.IsConfigured || t.Type is not ("Discover" or "DiscoverMovies" or "DiscoverTv" or "Requests"))
                    .Select(t => new ClientTab { Type = t.Type, Title = string.IsNullOrWhiteSpace(t.Title) ? t.Type : t.Title, Target = TabTarget(t) })
                    .Where(t => t.Type != "Library" || t.Target != null)
                    .ToList(),
            },
            Hero = new ClientHero
            {
                Enabled = config.Hero.Enabled && (settings.HeroEnabled ?? true),
                IntervalSeconds = config.Hero.IntervalSeconds,
            },
            DiscoverRows = config.DiscoverRows
                .Where(r => r.Enabled)
                .Select(r => new ClientDiscoverRow { Id = r.Id, Page = r.Page, Title = r.Title })
                .ToList(),
        };

        // Sections: admin order, then the user's own order for the ones they placed.
        List<SectionConfig> available = config.Sections
            .Where(s => s.Enabled)
            .Where(s => SeerrService.IsConfigured || s.Type is not ("SeerrDiscover" or "RecentDigital" or "MyRequests"))
            .Where(s => SonarrService.IsConfigured || s.Type != "Upcoming")
            .ToList();

        Dictionary<string, int> userOrder = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, bool> userEnabled = new(StringComparer.OrdinalIgnoreCase);
        if (config.AllowUserSections)
        {
            for (int i = 0; i < settings.Sections.Count; i++)
            {
                userOrder[settings.Sections[i].Id] = i;
                userEnabled[settings.Sections[i].Id] = settings.Sections[i].Enabled;
            }
        }

        layout.Sections = available
            .Select((s, adminIndex) => (Section: s, Order: userOrder.TryGetValue(s.Id, out int u) ? u : 1000 + adminIndex))
            .OrderBy(x => x.Order)
            .Select(x => new ClientSection
            {
                Id = x.Section.Id,
                Type = x.Section.Type,
                Title = x.Section.Title,
                Display = x.Section.Display,
                UserCanHide = x.Section.UserCanHide,
                Enabled = !x.Section.UserCanHide || !userEnabled.TryGetValue(x.Section.Id, out bool on) || on,
            })
            .ToList();

        return layout;
    }

    public async Task<SectionResult> GetSectionAsync(User user, string sectionId, CancellationToken ct)
    {
        SectionConfig? section = Config.Sections.FirstOrDefault(s => string.Equals(s.Id, sectionId, StringComparison.OrdinalIgnoreCase));
        if (section == null)
        {
            return new SectionResult { Error = "Section inconnue" };
        }

        SectionResult result = new();
        try
        {
            switch (section.Type)
            {
                case "ContinueWatching":
                    result.Rows.Add(Row(section, _library.ContinueWatching(user, section)));
                    break;

                case "Library":
                    result.Rows.Add(Row(section, _library.LibraryItems(user, section.LibraryIds, section.ItemTypes, section.Sort, section.HideWatched, section.Limit, section.Interleave)));
                    break;

                case "Genres":
                    foreach (string genre in _library.PickGenres(user, section.LibraryIds, section.ItemTypes, section.Genres, section.GenreRows))
                    {
                        List<HomeCard> items = _library.GenreItems(user, section.LibraryIds, section.ItemTypes, genre, section.Limit, section.HideWatched);
                        if (items.Count > 0)
                        {
                            SectionRow row = Row(section, items);
                            row.Title = string.IsNullOrWhiteSpace(section.Title) || section.Title == "{genre}"
                                ? genre
                                : section.Title.Replace("{genre}", genre, StringComparison.OrdinalIgnoreCase);
                            result.Rows.Add(row);
                        }
                    }

                    break;

                case "SeerrDiscover":
                {
                    int? seerrUser = await _seerr.ResolveUserAsync(user, ct).ConfigureAwait(false);
                    PagedCards page = await _seerr.DiscoverAsync(section.SeerrPath, section.SeerrQuery, 1, null, seerrUser, ct).ConfigureAwait(false);
                    List<HomeCard> items = page.Items;
                    if (page.TotalPages > 1 && items.Count < section.Limit)
                    {
                        PagedCards next = await _seerr.DiscoverAsync(section.SeerrPath, section.SeerrQuery, 2, null, seerrUser, ct).ConfigureAwait(false);
                        items.AddRange(next.Items);
                    }

                    SectionRow row = Row(section, Filter(items, section));
                    row.MoreLink = "seerr:" + section.Id;
                    result.Rows.Add(row);
                    break;
                }

                case "RecentDigital":
                {
                    int? seerrUser = await _seerr.ResolveUserAsync(user, ct).ConfigureAwait(false);
                    PagedCards page = await _seerr.RecentDigitalAsync(section.DaysBack, 1, seerrUser, ct).ConfigureAwait(false);
                    List<HomeCard> items = page.Items;
                    if (page.TotalPages > 1 && items.Count < section.Limit)
                    {
                        items.AddRange((await _seerr.RecentDigitalAsync(section.DaysBack, 2, seerrUser, ct).ConfigureAwait(false)).Items);
                    }

                    SectionRow row = Row(section, Filter(items, section));
                    row.MoreLink = "seerr:" + section.Id;
                    result.Rows.Add(row);
                    break;
                }

                case "Upcoming":
                    result.Rows.Add(Row(section, await _sonarr.UpcomingAsync(user, section, ct).ConfigureAwait(false)));
                    break;

                case "MyRequests":
                {
                    int? seerrUser = await _seerr.ResolveUserAsync(user, ct).ConfigureAwait(false);
                    if (seerrUser.HasValue)
                    {
                        result.Rows.Add(Row(section, await _seerr.MyRequestsAsync(seerrUser.Value, section.Limit, ct).ConfigureAwait(false)));
                    }

                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Homepage: section {Title} ({Type}) failed", section.Title, section.Type);
            result.Error = ex.Message;
        }

        return result;
    }

    /// <summary>"See all" pages for Seerr-backed sections and discover rows.</summary>
    public async Task<PagedCards> GetSeerrPageAsync(User user, string sourceId, int page, CancellationToken ct)
    {
        int? seerrUser = await _seerr.ResolveUserAsync(user, ct).ConfigureAwait(false);

        SectionConfig? section = Config.Sections.FirstOrDefault(s => string.Equals(s.Id, sourceId, StringComparison.OrdinalIgnoreCase));
        if (section != null)
        {
            PagedCards cards = section.Type == "RecentDigital"
                ? await _seerr.RecentDigitalAsync(section.DaysBack, page, seerrUser, ct).ConfigureAwait(false)
                : await _seerr.DiscoverAsync(section.SeerrPath, section.SeerrQuery, page, null, seerrUser, ct).ConfigureAwait(false);
            cards.Items = Filter(cards.Items, section, applyLimit: false);
            return cards;
        }

        DiscoverRowConfig? row = Config.DiscoverRows.FirstOrDefault(r => string.Equals(r.Id, sourceId, StringComparison.OrdinalIgnoreCase));
        if (row != null)
        {
            return await _seerr.DiscoverAsync(row.SeerrPath, row.SeerrQuery, page, NullIfEmpty(row.MediaType), seerrUser, ct).ConfigureAwait(false);
        }

        throw new SeerrException("Source inconnue");
    }

    public async Task<List<HomeCard>> GetHeroAsync(User user, CancellationToken ct)
    {
        HeroConfig hero = Config.Hero;
        string kinds = string.Join(',', new[] { hero.IncludeMovies ? "Movie" : null, hero.IncludeSeries ? "Series" : null }.Where(k => k != null));
        if (kinds.Length == 0)
        {
            kinds = "Movie,Series";
        }

        List<HomeCard> cards = _library.LibraryItems(user, hero.LibraryIds, kinds, hero.Sort, hero.HideWatched, hero.Count * 2, interleave: true, requireBackdrop: true)
            .Where(c => !string.IsNullOrEmpty(c.Backdrop))
            .Where(c => !hero.RequireLogo || !string.IsNullOrEmpty(c.Logo))
            .ToList();

        if (hero.IncludeSeerrTrending && SeerrService.IsConfigured)
        {
            try
            {
                int? seerrUser = await _seerr.ResolveUserAsync(user, ct).ConfigureAwait(false);
                PagedCards trending = await _seerr.DiscoverAsync("discover/trending", null, 1, null, seerrUser, ct).ConfigureAwait(false);
                List<HomeCard> extra = trending.Items.Where(c => c.Kind == "request" && !string.IsNullOrEmpty(c.Backdrop)).Take(Math.Max(2, hero.Count / 3)).ToList();
                cards = Interleave(cards, extra).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Homepage: hero trending unavailable");
            }
        }

        return cards.DistinctBy(c => c.Key).Take(hero.Count).ToList();
    }

    public async Task<SearchResult> SearchAsync(User user, string term, CancellationToken ct)
    {
        SearchResult result = new() { Library = _library.Search(user, term, 30) };
        if (!SeerrService.IsConfigured)
        {
            return result;
        }

        try
        {
            int? seerrUser = await _seerr.ResolveUserAsync(user, ct).ConfigureAwait(false);
            PagedCards seerr = await _seerr.SearchAsync(term, 1, seerrUser, ct).ConfigureAwait(false);
            HashSet<string> inLibrary = result.Library.Select(c => c.Key).ToHashSet(StringComparer.Ordinal);
            result.Seerr = seerr.Items.Where(c => !inLibrary.Contains(c.Key)).ToList();
        }
        catch (Exception ex)
        {
            result.SeerrError = ex.Message;
        }

        return result;
    }

    private static List<HomeCard> Filter(List<HomeCard> items, SectionConfig section, bool applyLimit = true)
    {
        IEnumerable<HomeCard> filtered = items.DistinctBy(c => c.Key);
        if (section.HideAvailable)
        {
            filtered = filtered.Where(c => c.Kind != "local" && c.SeerrStatus != 5);
        }

        return applyLimit ? filtered.Take(section.Limit).ToList() : filtered.ToList();
    }

    private static SectionRow Row(SectionConfig section, List<HomeCard> items) => new()
    {
        Title = section.Title,
        Display = section.Display,
        Items = items,
    };

    /// <summary>Library tabs link to Jellyfin's own library page, which depends on the library kind.</summary>
    private string? TabTarget(HeaderTab tab)
    {
        if (string.IsNullOrWhiteSpace(tab.Target))
        {
            return null;
        }

        if (tab.Type != "Library")
        {
            return tab.Target;
        }

        string id = tab.Target.Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        string? kind = _library.LibraryKind(id);
        return kind == null ? null : $"#/{(kind == "tvshows" ? "tv" : "movies")}?topParentId={id}&collectionType={kind}";
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static IEnumerable<HomeCard> Interleave(List<HomeCard> first, List<HomeCard> second)
    {
        int step = second.Count == 0 ? int.MaxValue : Math.Max(1, first.Count / (second.Count + 1));
        int j = 0;
        for (int i = 0; i < first.Count; i++)
        {
            yield return first[i];
            if ((i + 1) % step == 0 && j < second.Count)
            {
                yield return second[j++];
            }
        }

        while (j < second.Count)
        {
            yield return second[j++];
        }
    }
}
