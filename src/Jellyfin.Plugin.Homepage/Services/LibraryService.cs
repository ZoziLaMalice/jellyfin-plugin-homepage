using System.Collections.Concurrent;
using System.Globalization;
using Jellyfin.Plugin.Homepage.Configuration;
using Jellyfin.Plugin.Homepage.Models;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.TV;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Homepage.Services;

/// <summary>
/// Everything that reads the Jellyfin library: owned-title index, section queries, and conversion to cards.
/// </summary>
public class LibraryService
{
    private static readonly TimeSpan IndexLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan GenreLifetime = TimeSpan.FromMinutes(30);

    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly IDtoService _dtoService;
    private readonly ITVSeriesManager _tvSeriesManager;
    private readonly ILogger<LibraryService> _logger;
    private readonly object _indexLock = new();
    private readonly ConcurrentDictionary<string, (DateTime At, List<string> Genres)> _genreCache = new();

    private Dictionary<string, Guid> _index = new(StringComparer.Ordinal);
    private DateTime _indexBuiltAt = DateTime.MinValue;

    public LibraryService(
        ILibraryManager libraryManager,
        IUserManager userManager,
        IDtoService dtoService,
        ITVSeriesManager tvSeriesManager,
        ILogger<LibraryService> logger)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
        _dtoService = dtoService;
        _tvSeriesManager = tvSeriesManager;
        _logger = logger;
    }

    private static PluginConfiguration Config => Plugin.Instance.Configuration;

    public User? GetUser(Guid userId) => _userManager.GetUserById(userId);

    public static DtoOptions CardDtoOptions() => new()
    {
        Fields = new List<ItemFields>
        {
            ItemFields.Path,
            ItemFields.ProviderIds,
            ItemFields.Overview,
            ItemFields.Genres,
            ItemFields.DateCreated,
            ItemFields.PrimaryImageAspectRatio,
        },
        ImageTypeLimit = 1,
        ImageTypes = new List<ImageType> { ImageType.Primary, ImageType.Backdrop, ImageType.Thumb, ImageType.Logo },
        EnableUserData = true,
    };

    /// <summary>Returns "movies" or "tvshows" for a library id, null when unknown.</summary>
    public string? LibraryKind(string libraryId)
    {
        VirtualFolderInfo? folder = _libraryManager.GetVirtualFolders()
            .FirstOrDefault(f => string.Equals(f.ItemId?.Replace("-", string.Empty, StringComparison.Ordinal), libraryId, StringComparison.OrdinalIgnoreCase));
        return folder?.CollectionType switch
        {
            CollectionTypeOptions.movies => "movies",
            CollectionTypeOptions.tvshows => "tvshows",
            _ => null,
        };
    }

    // ----- Owned-title index ---------------------------------------------------------------

    /// <summary>Returns true when the path belongs to a placeholder library (JellyNext...).</summary>
    public static bool IsVirtualPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        return DefaultsFactory.VirtualMarkers(Config).Any(m => path.Contains(m, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Finds a real (non placeholder) movie or series by TMDB/TVDB id.</summary>
    public Guid? FindLocal(string mediaType, int? tmdbId, int? tvdbId)
    {
        Dictionary<string, Guid> index = GetIndex();
        string kind = mediaType == "movie" ? "movie" : "tv";

        if (tmdbId is > 0 && index.TryGetValue($"{kind}:tmdb:{tmdbId}", out Guid id))
        {
            return id;
        }

        if (kind == "tv" && tvdbId is > 0 && index.TryGetValue($"tv:tvdb:{tvdbId}", out id))
        {
            return id;
        }

        return null;
    }

    public void InvalidateIndex()
    {
        lock (_indexLock)
        {
            _indexBuiltAt = DateTime.MinValue;
        }
    }

    private Dictionary<string, Guid> GetIndex()
    {
        lock (_indexLock)
        {
            if (DateTime.UtcNow - _indexBuiltAt < IndexLifetime)
            {
                return _index;
            }

            Dictionary<string, Guid> index = new(StringComparer.Ordinal);
            try
            {
                IReadOnlyList<BaseItem> items = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
                    Recursive = true,
                    IsVirtualItem = false,
                    DtoOptions = new DtoOptions(false),
                });

                foreach (BaseItem item in items)
                {
                    if (IsVirtualPath(item.Path))
                    {
                        continue;
                    }

                    string kind = item is MediaBrowser.Controller.Entities.Movies.Movie ? "movie" : "tv";
                    if (item.TryGetProviderId(MetadataProvider.Tmdb, out string? tmdb))
                    {
                        index.TryAdd($"{kind}:tmdb:{tmdb}", item.Id);
                    }

                    if (kind == "tv" && item.TryGetProviderId(MetadataProvider.Tvdb, out string? tvdb))
                    {
                        index.TryAdd($"tv:tvdb:{tvdb}", item.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Homepage: failed to build the owned-title index");
            }

            _index = index;
            _indexBuiltAt = DateTime.UtcNow;
            return _index;
        }
    }

    // ----- Section queries -----------------------------------------------------------------

    public List<HomeCard> ContinueWatching(User user, SectionConfig section)
    {
        DtoOptions options = CardDtoOptions();
        List<BaseItem> resume = _libraryManager.GetItemList(new InternalItemsQuery(user)
        {
            OrderBy = new[] { (ItemSortBy.DatePlayed, SortOrder.Descending) },
            IsResumable = true,
            Limit = section.Limit,
            Recursive = true,
            MediaTypes = new[] { MediaType.Video },
            IsVirtualItem = false,
            DtoOptions = options,
            EnableTotalRecordCount = false,
        }).Where(i => !IsVirtualPath(i.Path)).ToList();

        List<BaseItem> items = new(resume);

        if (section.IncludeNextUp)
        {
            HashSet<Guid> seriesInResume = resume
                .OfType<MediaBrowser.Controller.Entities.TV.Episode>()
                .Select(e => e.SeriesId)
                .ToHashSet();

            QueryResult<BaseItem> nextUp = _tvSeriesManager.GetNextUp(
                new NextUpQuery
                {
                    User = user,
                    Limit = section.Limit,
                    EnableTotalRecordCount = false,
                    NextUpDateCutoff = DateTime.UtcNow.AddDays(-365),
                },
                options);

            items.AddRange(nextUp.Items
                .Where(i => !IsVirtualPath(i.Path))
                .Where(i => i is not MediaBrowser.Controller.Entities.TV.Episode ep || !seriesInResume.Contains(ep.SeriesId)));
        }

        return ToCards(items.Take(section.Limit).ToList(), user, landscape: true);
    }

    public List<HomeCard> LibraryItems(User user, IReadOnlyList<string> libraryIds, string itemTypes, string sort, bool hideWatched, int limit, bool interleave, bool requireBackdrop = false)
    {
        BaseItemKind[] kinds = ParseKinds(itemTypes);
        (ItemSortBy, SortOrder)[] order = ParseSort(sort);
        DtoOptions options = CardDtoOptions();

        List<BaseItem> owned = new();
        List<BaseItem> placeholders = new();
        int fetch = requireBackdrop ? limit * 3 : limit;

        foreach (Folder folder in ResolveLibraries(user, libraryIds, kinds))
        {
            IReadOnlyList<BaseItem> found = folder.GetItems(new InternalItemsQuery(user)
            {
                IncludeItemTypes = kinds,
                OrderBy = order,
                IsPlayed = hideWatched ? false : null,
                Limit = fetch,
                Recursive = true,
                ParentId = folder.Id,
                DtoOptions = options,
                EnableTotalRecordCount = false,
            }).Items;

            foreach (BaseItem item in found)
            {
                if (requireBackdrop && item.GetImages(ImageType.Backdrop).FirstOrDefault() == null)
                {
                    continue;
                }

                (IsVirtualPath(item.Path) ? placeholders : owned).Add(item);
            }
        }

        // Several libraries were queried separately: restore the global order.
        owned = Reorder(owned, sort);
        placeholders = Reorder(placeholders, sort);

        List<HomeCard> ownedCards = ToCards(owned, user, landscape: false);
        List<HomeCard> placeholderCards = ToCards(placeholders, user, landscape: false);

        // A recommendation for something already owned shows as the owned copy.
        HashSet<string> ownedKeys = ownedCards.Select(c => c.Key).ToHashSet(StringComparer.Ordinal);
        placeholderCards = placeholderCards.Where(c => !ownedKeys.Contains(c.Key)).ToList();

        IEnumerable<HomeCard> merged = interleave
            ? Interleave(ownedCards, placeholderCards)
            : Reorder(ownedCards.Concat(placeholderCards).ToList(), sort);

        return merged.DistinctBy(c => c.Key).Take(limit).ToList();
    }

    public List<string> PickGenres(User user, IReadOnlyList<string> libraryIds, string itemTypes, string configured, int count)
    {
        List<string> explicitGenres = configured
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (explicitGenres.Count > 0)
        {
            return explicitGenres.Take(count).ToList();
        }

        BaseItemKind[] kinds = ParseKinds(itemTypes);
        string cacheKey = user.Id.ToString("N") + "|" + string.Join(',', libraryIds) + "|" + itemTypes;
        List<string> genres;
        if (_genreCache.TryGetValue(cacheKey, out var cached) && DateTime.UtcNow - cached.At < GenreLifetime)
        {
            genres = cached.Genres;
        }
        else
        {
            Dictionary<string, int> counts = new(StringComparer.OrdinalIgnoreCase);
            foreach (Folder folder in ResolveLibraries(user, libraryIds, kinds))
            {
                foreach (BaseItem item in folder.GetItems(new InternalItemsQuery(user)
                {
                    IncludeItemTypes = kinds,
                    Recursive = true,
                    ParentId = folder.Id,
                    DtoOptions = new DtoOptions(false),
                    EnableTotalRecordCount = false,
                }).Items)
                {
                    foreach (string genre in item.Genres ?? Array.Empty<string>())
                    {
                        counts[genre] = counts.GetValueOrDefault(genre) + 1;
                    }
                }
            }

            genres = counts.Where(kv => kv.Value >= 6).OrderByDescending(kv => kv.Value).Take(16).Select(kv => kv.Key).ToList();
            _genreCache[cacheKey] = (DateTime.UtcNow, genres);
        }

        // Rotate daily, differently for each user.
        int seed = HashCode.Combine(DateTime.UtcNow.Date.DayOfYear, user.Id);
        Random random = new(seed);
        return genres.OrderBy(_ => random.Next()).Take(count).ToList();
    }

    public List<HomeCard> GenreItems(User user, IReadOnlyList<string> libraryIds, string itemTypes, string genre, int limit, bool hideWatched)
    {
        BaseItemKind[] kinds = ParseKinds(itemTypes);
        DtoOptions options = CardDtoOptions();
        List<BaseItem> items = new();

        foreach (Folder folder in ResolveLibraries(user, libraryIds, kinds))
        {
            items.AddRange(folder.GetItems(new InternalItemsQuery(user)
            {
                IncludeItemTypes = kinds,
                Genres = new[] { genre },
                OrderBy = new[] { (ItemSortBy.Random, SortOrder.Ascending) },
                IsPlayed = hideWatched ? false : null,
                Limit = limit,
                Recursive = true,
                ParentId = folder.Id,
                DtoOptions = options,
                EnableTotalRecordCount = false,
            }).Items);
        }

        Random random = new();
        List<HomeCard> cards = ToCards(items.OrderBy(_ => random.Next()).ToList(), user, landscape: false);
        HashSet<string> owned = cards.Where(c => c.Kind == "local").Select(c => c.Key).ToHashSet();
        return cards.Where(c => c.Kind == "local" || !owned.Contains(c.Key)).DistinctBy(c => c.Key).Take(limit).ToList();
    }

    public List<HomeCard> Search(User user, string term, int limit)
    {
        IReadOnlyList<BaseItem> items = _libraryManager.GetItemList(new InternalItemsQuery(user)
        {
            SearchTerm = term,
            IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
            Recursive = true,
            Limit = limit * 2,
            DtoOptions = CardDtoOptions(),
            EnableTotalRecordCount = false,
        });

        List<HomeCard> cards = ToCards(items.ToList(), user, landscape: false);
        HashSet<string> owned = cards.Where(c => c.Kind == "local").Select(c => c.Key).ToHashSet();
        return cards.Where(c => c.Kind == "local" || !owned.Contains(c.Key)).DistinctBy(c => c.Key).Take(limit).ToList();
    }

    /// <summary>Series ids the user has watched at least one episode of.</summary>
    public HashSet<Guid> WatchedSeries(User user)
    {
        return _libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.Episode },
                IsPlayed = true,
                Recursive = true,
                DtoOptions = new DtoOptions(false),
                EnableTotalRecordCount = false,
            })
            .OfType<MediaBrowser.Controller.Entities.TV.Episode>()
            .Select(e => e.SeriesId)
            .ToHashSet();
    }

    public HomeCard? CardForItem(User user, Guid itemId)
    {
        BaseItem? item = _libraryManager.GetItemById(itemId);
        return item == null ? null : ToCards(new List<BaseItem> { item }, user, landscape: false).FirstOrDefault();
    }

    // ----- Conversion ----------------------------------------------------------------------

    public List<HomeCard> ToCards(List<BaseItem> items, User user, bool landscape)
    {
        if (items.Count == 0)
        {
            return new List<HomeCard>();
        }

        IReadOnlyList<BaseItemDto> dtos = _dtoService.GetBaseItemDtos(items, CardDtoOptions(), user);
        List<HomeCard> cards = new(dtos.Count);
        for (int i = 0; i < dtos.Count; i++)
        {
            HomeCard? card = ToCard(dtos[i], items[i], user);
            if (card != null)
            {
                cards.Add(card);
            }
        }

        return cards;
    }

    private HomeCard? ToCard(BaseItemDto dto, BaseItem item, User user)
    {
        bool isVirtual = IsVirtualPath(dto.Path);
        string id = dto.Id.ToString("N");
        HomeCard card = new()
        {
            ItemId = id,
            DetailsId = id,
            ItemType = dto.Type.ToString(),
            IsFolder = dto.IsFolder == true,
            Title = dto.Name ?? string.Empty,
            Year = dto.ProductionYear,
            Overview = dto.Overview,
            Rating = dto.CommunityRating.HasValue ? Math.Round(dto.CommunityRating.Value, 1) : null,
            OfficialRating = dto.OfficialRating,
            RunTimeTicks = dto.RunTimeTicks,
            Genres = (dto.Genres ?? Array.Empty<string>()).Take(3).ToList(),
            IsVirtual = isVirtual,
            AddedAt = item.DateCreated,
            ContentAddedAt = (item as Folder)?.DateLastMediaAdded ?? item.DateCreated,
            PremieredAt = item.PremiereDate,
            Played = dto.UserData?.Played == true,
            Progress = dto.UserData?.PlayedPercentage,
            PositionTicks = dto.UserData is { PlaybackPositionTicks: > 0 } ? dto.UserData.PlaybackPositionTicks : null,
        };

        card.TmdbId = ParseId(dto.ProviderIds, "Tmdb");
        card.TvdbId = ParseId(dto.ProviderIds, "Tvdb");

        switch (dto.Type)
        {
            case BaseItemKind.Movie:
                card.MediaType = "movie";
                break;
            case BaseItemKind.Series:
                card.MediaType = "tv";
                break;
            case BaseItemKind.Episode:
                card.MediaType = "episode";
                card.Title = dto.SeriesName ?? dto.Name ?? string.Empty;
                card.Subtitle = FormatEpisode(dto);
                card.DetailsId = dto.SeriesId?.ToString("N") ?? id;
                break;
            case BaseItemKind.Season:
                card.MediaType = "tv";
                card.Title = dto.SeriesName ?? dto.Name ?? string.Empty;
                card.Subtitle = dto.Name;
                card.DetailsId = dto.SeriesId?.ToString("N") ?? id;
                break;
            default:
                card.MediaType = "movie";
                break;
        }

        // Images, all relative to the server root; the client adds sizing.
        string? primaryTag = dto.ImageTags != null && dto.ImageTags.TryGetValue(ImageType.Primary, out string? p) ? p : null;
        string? thumbTag = dto.ImageTags != null && dto.ImageTags.TryGetValue(ImageType.Thumb, out string? t) ? t : null;
        string? logoTag = dto.ImageTags != null && dto.ImageTags.TryGetValue(ImageType.Logo, out string? l) ? l : null;
        string? backdropTag = dto.BackdropImageTags?.FirstOrDefault();

        if (dto.Type == BaseItemKind.Episode || dto.Type == BaseItemKind.Season)
        {
            card.Poster = dto.SeriesId.HasValue && !string.IsNullOrEmpty(dto.SeriesPrimaryImageTag)
                ? Image(dto.SeriesId.Value, "Primary", dto.SeriesPrimaryImageTag)
                : primaryTag != null ? Image(dto.Id, "Primary", primaryTag) : null;

            if (dto.Type == BaseItemKind.Episode && primaryTag != null)
            {
                card.Thumb = Image(dto.Id, "Primary", primaryTag);
            }
            else if (dto.ParentThumbItemId.HasValue && !string.IsNullOrEmpty(dto.ParentThumbImageTag))
            {
                card.Thumb = Image(dto.ParentThumbItemId.Value, "Thumb", dto.ParentThumbImageTag);
            }

            if (dto.ParentBackdropItemId.HasValue && dto.ParentBackdropImageTags is { Length: > 0 })
            {
                card.Backdrop = Image(dto.ParentBackdropItemId.Value, "Backdrop/0", dto.ParentBackdropImageTags[0]);
                card.Thumb ??= card.Backdrop;
            }

            if (dto.ParentLogoItemId.HasValue && !string.IsNullOrEmpty(dto.ParentLogoImageTag))
            {
                card.Logo = Image(dto.ParentLogoItemId.Value, "Logo", dto.ParentLogoImageTag);
            }
        }
        else
        {
            card.Poster = primaryTag != null ? Image(dto.Id, "Primary", primaryTag) : null;
            card.Backdrop = backdropTag != null ? Image(dto.Id, "Backdrop/0", backdropTag) : null;
            card.Thumb = thumbTag != null ? Image(dto.Id, "Thumb", thumbTag) : card.Backdrop;
            card.Logo = logoTag != null ? Image(dto.Id, "Logo", logoTag) : null;
        }

        if (!isVirtual)
        {
            card.Kind = "local";
            card.Key = KeyFor(card);
            return card;
        }

        // Placeholder item: playable copy if we own it, otherwise a request card.
        string requestType = card.MediaType == "movie" ? "movie" : "tv";
        if (dto.Type == BaseItemKind.Episode || dto.Type == BaseItemKind.Season)
        {
            // Episodes/seasons of placeholder series: identify the series instead.
            BaseItem? series = dto.SeriesId.HasValue ? _libraryManager.GetItemById(dto.SeriesId.Value) : null;
            card.TmdbId = series != null && series.TryGetProviderId(MetadataProvider.Tmdb, out string? st) && int.TryParse(st, out int sti) ? sti : null;
            card.TvdbId = series != null && series.TryGetProviderId(MetadataProvider.Tvdb, out string? sv) && int.TryParse(sv, out int svi) ? svi : null;
            card.RequestSeasons = dto.ParentIndexNumber.HasValue ? new List<int> { dto.ParentIndexNumber.Value }
                : dto.IndexNumber.HasValue && dto.Type == BaseItemKind.Season ? new List<int> { dto.IndexNumber.Value } : null;
        }

        card.MediaType = requestType;
        Guid? owned = FindLocal(requestType, card.TmdbId, card.TvdbId);

        if (requestType == "tv" && item is MediaBrowser.Controller.Entities.TV.Series placeholderSeries && card.RequestSeasons == null)
        {
            List<int> seasons = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    ParentId = placeholderSeries.Id,
                    IncludeItemTypes = new[] { BaseItemKind.Season },
                    DtoOptions = new DtoOptions(false),
                })
                .Where(s => s.IndexNumber.HasValue && s.IndexNumber.Value > 0)
                .Select(s => s.IndexNumber!.Value)
                .ToList();

            // JellyNext "next season" folders only hold the season(s) to come, owned or not.
            bool nextSeasonFolder = dto.Path?.Contains("nextseason", StringComparison.OrdinalIgnoreCase) == true;
            if ((owned.HasValue || nextSeasonFolder) && seasons.Count > 0)
            {
                card.RequestSeasons = seasons;
            }
        }

        if (owned.HasValue && (card.RequestSeasons == null || card.RequestSeasons.Count == 0))
        {
            // We already have it: show the real item instead.
            return CardForItem(user, owned.Value) is { } ownedCard ? ownedCard : null;
        }

        card.Kind = "request";
        card.Progress = null;
        card.PositionTicks = null;
        card.Played = false;
        if (owned.HasValue)
        {
            card.DetailsId = owned.Value.ToString("N");
        }

        if (card.RequestSeasons is { Count: > 0 })
        {
            card.Badge = card.RequestSeasons.Count == 1
                ? string.Create(CultureInfo.InvariantCulture, $"Saison {card.RequestSeasons[0]}")
                : "Nouvelles saisons";
        }

        card.Key = KeyFor(card);
        return card;
    }

    public static string KeyFor(HomeCard card)
    {
        if (card.MediaType == "episode" || card.TmdbId is null or 0)
        {
            return "jf:" + card.ItemId;
        }

        string key = $"{card.MediaType}:{card.TmdbId}";
        return card.RequestSeasons is { Count: > 0 } ? key + ":s" + string.Join('-', card.RequestSeasons) : key;
    }

    private static string Image(Guid itemId, string type, string tag) =>
        $"/Items/{itemId:N}/Images/{type}?tag={Uri.EscapeDataString(tag)}";

    private static int? ParseId(Dictionary<string, string>? ids, string name) =>
        ids != null && ids.TryGetValue(name, out string? value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) ? id : null;

    private static string? FormatEpisode(BaseItemDto dto)
    {
        if (dto.ParentIndexNumber.HasValue && dto.IndexNumber.HasValue)
        {
            return string.Create(CultureInfo.InvariantCulture, $"S{dto.ParentIndexNumber:00}E{dto.IndexNumber:00} · {dto.Name}");
        }

        return dto.Name;
    }

    private static BaseItemKind[] ParseKinds(string itemTypes)
    {
        List<BaseItemKind> kinds = new();
        foreach (string part in (itemTypes ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse(part, true, out BaseItemKind kind) && (kind == BaseItemKind.Movie || kind == BaseItemKind.Series))
            {
                kinds.Add(kind);
            }
        }

        return kinds.Count > 0 ? kinds.ToArray() : new[] { BaseItemKind.Movie, BaseItemKind.Series };
    }

    private static (ItemSortBy, SortOrder)[] ParseSort(string sort) => sort switch
    {
        "Random" => new[] { (ItemSortBy.Random, SortOrder.Ascending) },
        "CommunityRating" => new[] { (ItemSortBy.CommunityRating, SortOrder.Descending) },
        "PremiereDate" => new[] { (ItemSortBy.PremiereDate, SortOrder.Descending) },
        "Name" => new[] { (ItemSortBy.SortName, SortOrder.Ascending) },
        "DateLastContentAdded" => new[] { (ItemSortBy.DateLastContentAdded, SortOrder.Descending), (ItemSortBy.DateCreated, SortOrder.Descending) },
        _ => new[] { (ItemSortBy.DateCreated, SortOrder.Descending) },
    };

    private static List<BaseItem> Reorder(List<BaseItem> items, string sort) => sort switch
    {
        "Random" => items.OrderBy(_ => Random.Shared.Next()).ToList(),
        "CommunityRating" => items.OrderByDescending(i => i.CommunityRating ?? 0).ToList(),
        "PremiereDate" => items.OrderByDescending(i => i.PremiereDate ?? DateTime.MinValue).ToList(),
        "Name" => items.OrderBy(i => i.SortName, StringComparer.CurrentCultureIgnoreCase).ToList(),
        "DateLastContentAdded" => items.OrderByDescending(i => (i as Folder)?.DateLastMediaAdded ?? i.DateCreated).ToList(),
        _ => items.OrderByDescending(i => i.DateCreated).ToList(),
    };

    private static List<HomeCard> Reorder(List<HomeCard> cards, string sort) => sort switch
    {
        "Random" => cards.OrderBy(_ => Random.Shared.Next()).ToList(),
        "CommunityRating" => cards.OrderByDescending(c => c.Rating ?? 0).ToList(),
        "PremiereDate" => cards.OrderByDescending(c => c.PremieredAt ?? DateTime.MinValue).ToList(),
        "Name" => cards.OrderBy(c => c.Title, StringComparer.CurrentCultureIgnoreCase).ToList(),
        "DateLastContentAdded" => cards.OrderByDescending(c => c.ContentAddedAt).ToList(),
        _ => cards.OrderByDescending(c => c.AddedAt).ToList(),
    };

    private static IEnumerable<HomeCard> Interleave(List<HomeCard> first, List<HomeCard> second)
    {
        int max = Math.Max(first.Count, second.Count);
        for (int i = 0; i < max; i++)
        {
            if (i < first.Count)
            {
                yield return first[i];
            }

            if (i < second.Count)
            {
                yield return second[i];
            }
        }
    }

    private IEnumerable<Folder> ResolveLibraries(User user, IReadOnlyList<string> libraryIds, BaseItemKind[] kinds)
    {
        IEnumerable<Guid> ids;
        if (libraryIds.Count > 0)
        {
            ids = libraryIds.Select(x => Guid.TryParse(x, out Guid g) ? g : Guid.Empty).Where(g => g != Guid.Empty);
        }
        else
        {
            // No explicit choice: every movie/series library matching the requested kinds.
            ids = _libraryManager.GetVirtualFolders()
                .Where(f => (kinds.Contains(BaseItemKind.Movie) && f.CollectionType == CollectionTypeOptions.movies)
                            || (kinds.Contains(BaseItemKind.Series) && f.CollectionType == CollectionTypeOptions.tvshows))
                .Select(f => Guid.TryParse(f.ItemId, out Guid g) ? g : Guid.Empty)
                .Where(g => g != Guid.Empty);
        }

        foreach (Guid id in ids)
        {
            if (_libraryManager.GetItemById(id) is Folder folder && folder.IsVisible(user))
            {
                yield return folder;
            }
        }
    }
}
