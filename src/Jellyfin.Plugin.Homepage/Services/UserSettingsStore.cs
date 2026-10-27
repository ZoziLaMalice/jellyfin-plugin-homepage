using System.Collections.Concurrent;
using System.Text.Json;
using Jellyfin.Plugin.Homepage.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Homepage.Services;

/// <summary>
/// Stores each user's personal choices as a small JSON file in the plugin data folder.
/// </summary>
public class UserSettingsStore
{
    private readonly ConcurrentDictionary<Guid, UserSettings> _cache = new();
    private readonly ILogger<UserSettingsStore> _logger;
    private readonly object _writeLock = new();

    public UserSettingsStore(ILogger<UserSettingsStore> logger)
    {
        _logger = logger;
    }

    private static string FolderPath
    {
        get
        {
            string path = Path.Combine(Plugin.Instance.DataDirectory, "users");
            Directory.CreateDirectory(path);
            return path;
        }
    }

    public UserSettings Get(Guid userId)
    {
        return _cache.GetOrAdd(userId, id =>
        {
            string file = Path.Combine(FolderPath, id.ToString("N") + ".json");
            if (!File.Exists(file))
            {
                return new UserSettings();
            }

            try
            {
                return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(file), JsonDefaults.Options) ?? new UserSettings();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Homepage: could not read settings for user {UserId}", id);
                return new UserSettings();
            }
        });
    }

    public void Save(Guid userId, UserSettings settings)
    {
        settings.Sections = settings.Sections
            .Where(s => !string.IsNullOrWhiteSpace(s.Id))
            .GroupBy(s => s.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        lock (_writeLock)
        {
            string file = Path.Combine(FolderPath, userId.ToString("N") + ".json");
            File.WriteAllText(file, JsonSerializer.Serialize(settings, JsonDefaults.Options));
            _cache[userId] = settings;
        }
    }
}
