using System.Reflection;
using System.Text.Json;
using Jellyfin.Plugin.Homepage.Configuration;
using Jellyfin.Plugin.Homepage.Models;
using Jellyfin.Plugin.Homepage.Services;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Homepage.Controllers;

[ApiController]
[Route("Homepage")]
public class HomepageController : ControllerBase
{
    private static readonly string[] Assets = { "homepage.js", "homepage.css", "admin.js", "admin.css" };

    private readonly LayoutService _layout;
    private readonly LibraryService _library;
    private readonly SeerrService _seerr;
    private readonly SonarrService _sonarr;
    private readonly UserSettingsStore _userSettings;
    private readonly ILibraryManager _libraryManager;

    public HomepageController(
        LayoutService layout,
        LibraryService library,
        SeerrService seerr,
        SonarrService sonarr,
        UserSettingsStore userSettings,
        ILibraryManager libraryManager)
    {
        _layout = layout;
        _library = library;
        _seerr = seerr;
        _sonarr = sonarr;
        _userSettings = userSettings;
        _libraryManager = libraryManager;
    }

    // ----- Client assets -------------------------------------------------------------------

    [HttpGet("{file}")]
    [AllowAnonymous]
    public IActionResult GetAsset([FromRoute] string file)
    {
        if (!Assets.Contains(file, StringComparer.Ordinal))
        {
            return NotFound();
        }

        Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"{typeof(Plugin).Namespace}.Web.{file}");
        if (stream == null)
        {
            return NotFound();
        }

        Response.Headers["Cache-Control"] = "no-cache";
        return File(stream, file.EndsWith(".css", StringComparison.Ordinal) ? "text/css; charset=utf-8" : "application/javascript; charset=utf-8");
    }

    // ----- User-facing API -----------------------------------------------------------------

    [HttpGet("Layout")]
    [Authorize]
    public IActionResult GetLayout()
    {
        User? user = CurrentUser();
        return user == null ? Unauthorized() : Json(_layout.GetLayout(user));
    }

    [HttpGet("Hero")]
    [Authorize]
    public async Task<IActionResult> GetHero(CancellationToken ct)
    {
        User? user = CurrentUser();
        return user == null ? Unauthorized() : Json(await _layout.GetHeroAsync(user, ct).ConfigureAwait(false));
    }

    [HttpGet("Sections/{sectionId}")]
    [Authorize]
    public async Task<IActionResult> GetSection([FromRoute] string sectionId, CancellationToken ct)
    {
        User? user = CurrentUser();
        return user == null ? Unauthorized() : Json(await _layout.GetSectionAsync(user, sectionId, ct).ConfigureAwait(false));
    }

    [HttpGet("Seerr/Page")]
    [Authorize]
    public async Task<IActionResult> GetSeerrPage([FromQuery] string source, [FromQuery] int page = 1, CancellationToken ct = default)
    {
        User? user = CurrentUser();
        if (user == null)
        {
            return Unauthorized();
        }

        try
        {
            return Json(await _layout.GetSeerrPageAsync(user, source, Math.Clamp(page, 1, 500), ct).ConfigureAwait(false));
        }
        catch (SeerrException ex)
        {
            return Problem(ex.Message, statusCode: 502);
        }
    }

    [HttpGet("Search")]
    [Authorize]
    public async Task<IActionResult> Search([FromQuery] string q, CancellationToken ct)
    {
        User? user = CurrentUser();
        if (user == null)
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
        {
            return Json(new SearchResult());
        }

        return Json(await _layout.SearchAsync(user, q.Trim(), ct).ConfigureAwait(false));
    }

    [HttpGet("MyRequests")]
    [Authorize]
    public async Task<IActionResult> GetMyRequests(CancellationToken ct)
    {
        User? user = CurrentUser();
        if (user == null)
        {
            return Unauthorized();
        }

        try
        {
            int? seerrUser = await _seerr.ResolveUserAsync(user, ct).ConfigureAwait(false);
            if (seerrUser == null)
            {
                return Problem($"Aucun compte Seerr n'est lié à « {user.Username} ».", statusCode: 404);
            }

            return Json(await _seerr.MyRequestsAsync(seerrUser.Value, 40, ct).ConfigureAwait(false));
        }
        catch (SeerrException ex)
        {
            return Problem(ex.Message, statusCode: 502);
        }
    }

    [HttpGet("Details")]
    [Authorize]
    public async Task<IActionResult> GetDetails([FromQuery] string mediaType, [FromQuery] int tmdbId, CancellationToken ct)
    {
        User? user = CurrentUser();
        if (user == null)
        {
            return Unauthorized();
        }

        try
        {
            int? seerrUser = await _seerr.ResolveUserAsync(user, ct).ConfigureAwait(false);
            return Json(await _seerr.DetailsAsync(mediaType, tmdbId, seerrUser, ct).ConfigureAwait(false));
        }
        catch (SeerrException ex)
        {
            return Problem(ex.Message, statusCode: 502);
        }
    }

    [HttpPost("Request")]
    [Authorize]
    public async Task<IActionResult> CreateRequest([FromBody] RequestPayload payload, CancellationToken ct)
    {
        User? user = CurrentUser();
        if (user == null)
        {
            return Unauthorized();
        }

        if (payload.TmdbId <= 0 || payload.MediaType is not ("movie" or "tv"))
        {
            return BadRequest(new RequestOutcome { Message = "Demande invalide." });
        }

        int? seerrUser;
        try
        {
            seerrUser = await _seerr.ResolveUserAsync(user, ct).ConfigureAwait(false);
        }
        catch (SeerrException ex)
        {
            return Json(new RequestOutcome { Message = ex.Message });
        }

        if (seerrUser == null)
        {
            return Json(new RequestOutcome
            {
                Message = $"Aucun compte Seerr n'est lié à « {user.Username} ». Importe l'utilisateur dans Seerr ou ajoute une correspondance dans la configuration du plugin.",
            });
        }

        return Json(await _seerr.RequestAsync(payload, seerrUser.Value, ct).ConfigureAwait(false));
    }

    [HttpGet("UserSettings")]
    [Authorize]
    public IActionResult GetUserSettings()
    {
        User? user = CurrentUser();
        return user == null ? Unauthorized() : Json(_userSettings.Get(user.Id));
    }

    [HttpPost("UserSettings")]
    [Authorize]
    public IActionResult SaveUserSettings([FromBody] JsonElement body)
    {
        User? user = CurrentUser();
        if (user == null)
        {
            return Unauthorized();
        }

        UserSettings? settings = body.Deserialize<UserSettings>(JsonDefaults.Options);
        if (settings == null)
        {
            return BadRequest();
        }

        _userSettings.Save(user.Id, settings);
        return Json(_layout.GetLayout(user));
    }

    // ----- Admin ---------------------------------------------------------------------------

    [HttpGet("Admin/Test")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public async Task<IActionResult> Test([FromQuery] string service, CancellationToken ct)
    {
        try
        {
            string message = service == "sonarr"
                ? await _sonarr.TestAsync(ct).ConfigureAwait(false)
                : await _seerr.TestAsync(ct).ConfigureAwait(false);
            _seerr.ForgetUsers();
            _seerr.InvalidateCache();
            return Json(new { ok = true, message });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, message = ex.Message });
        }
    }

    [HttpGet("Admin/SeerrUsers")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public async Task<IActionResult> SeerrUsers(CancellationToken ct)
    {
        try
        {
            var users = await _seerr.ListUsersAsync(ct).ConfigureAwait(false);
            return Json(users.Select(u => new { id = u.Id, name = u.Name }));
        }
        catch (SeerrException ex)
        {
            return Problem(ex.Message, statusCode: 502);
        }
    }

    [HttpGet("Admin/SeerrSliders")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public async Task<IActionResult> SeerrSliders(CancellationToken ct)
    {
        try
        {
            return Json(await _seerr.SlidersAsync(ct).ConfigureAwait(false));
        }
        catch (SeerrException ex)
        {
            return Problem(ex.Message, statusCode: 502);
        }
    }

    [HttpGet("Admin/Libraries")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public IActionResult Libraries()
    {
        return Json(_libraryManager.GetVirtualFolders()
            .Where(f => f.CollectionType is CollectionTypeOptions.movies or CollectionTypeOptions.tvshows)
            .Select(f => new
            {
                id = f.ItemId,
                name = f.Name,
                type = f.CollectionType == CollectionTypeOptions.movies ? "movies" : "tvshows",
                isVirtual = f.Locations.Any(LibraryService.IsVirtualPath),
            }));
    }

    [HttpPost("Admin/ResetSections")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public IActionResult ResetSections()
    {
        PluginConfiguration config = Plugin.Instance.Configuration;
        config.Initialized = false;
        DefaultsFactory.EnsureSections(config, _libraryManager.GetVirtualFolders());
        config.DiscoverRows.Clear();
        config.Header.Tabs.Clear();
        DefaultsFactory.EnsureDefaults(config);
        Plugin.Instance.UpdateConfiguration(config);
        _library.InvalidateIndex();
        return Json(new { ok = true });
    }

    // ----- Helpers -------------------------------------------------------------------------

    private User? CurrentUser()
    {
        string? id = User.Claims.FirstOrDefault(c => c.Type.Equals("Jellyfin-UserId", StringComparison.OrdinalIgnoreCase))?.Value;
        return Guid.TryParse(id, out Guid userId) ? _library.GetUser(userId) : null;
    }

    private ContentResult Json(object value) => new()
    {
        Content = JsonSerializer.Serialize(value, JsonDefaults.Options),
        ContentType = "application/json; charset=utf-8",
        StatusCode = StatusCodes.Status200OK,
    };
}
