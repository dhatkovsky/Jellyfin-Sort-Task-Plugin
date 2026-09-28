using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Tasks;
using MediaBrowser.Model.Entities;
using Jellyfin.Data.Enums;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SortTitleUpdater
{
    public class UpdateSortTitleTask : IScheduledTask, IDisposable
    {
        private ILibraryManager _libraryManager;
        private IProviderManager _providerManager;
        private ILogger<UpdateSortTitleTask> _logger;

        public UpdateSortTitleTask(
            ILibraryManager libraryManager,
            IProviderManager providerManager,
            ILogger<UpdateSortTitleTask> logger)
        {
            _libraryManager = libraryManager;
            _providerManager = providerManager;
            _logger = logger;
        }

        public string Name => "Update Library Sort Titles";

        public string Key => "UpdateSortTitleLibraryTask";

        public string Description => "Bypasses disk locks, encodes Sort Titles to UTF-16, and forces <sorttitle> updates in NFO files.";

        public string Category => "Library";

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return new[]
            {
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfoType.DailyTrigger,
                    TimeOfDayTicks = TimeSpan.FromHours(2).Ticks
                }
            };
        }

        public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            // Load plugin configuration
            var config = Plugin.Instance?.Configuration;
            if (config == null || config.TargetLibraryIds == null || config.TargetLibraryIds.Count == 0)
            {
                _logger.LogWarning("Task cancelled: No libraries selected in plugin settings.");
                progress.Report(100);
                return;
            }

            var savedKinds = config.TargetMediaKinds;
            if (savedKinds == null || savedKinds.Count == 0)
            {
                _logger.LogInformation("No media types found in config file. Falling back to all default media types.");
                savedKinds = new List<string> { "Movie", "Series", "MusicArtist", "MusicAlbum", "Audio", "Book", "AudioBook", "Video", "BoxSet" };
            }

            // CRITICAL FIX: Explicitly map strings to avoid modern Jellyfin 12 Enum.TryParse failures
            var selectedMediaKinds = new List<BaseItemKind>();
            foreach (var kindStr in savedKinds)
            {
                if (string.IsNullOrEmpty(kindStr)) continue;

                switch (kindStr.Trim().ToLowerInvariant())
                {
                    case "movie": selectedMediaKinds.Add(BaseItemKind.Movie); break;
                    case "series": selectedMediaKinds.Add(BaseItemKind.Series); break;
                    case "musicartist": selectedMediaKinds.Add(BaseItemKind.MusicArtist); break;
                    case "musicalbum": selectedMediaKinds.Add(BaseItemKind.MusicAlbum); break;
                    case "audio": selectedMediaKinds.Add(BaseItemKind.Audio); break;
                    case "book": selectedMediaKinds.Add(BaseItemKind.Book); break;
                    case "audiobook": selectedMediaKinds.Add(BaseItemKind.AudioBook); break;
                    case "video": selectedMediaKinds.Add(BaseItemKind.Video); break;
                    case "boxset": selectedMediaKinds.Add(BaseItemKind.BoxSet); break;
                    default:
                        _logger.LogWarning("Unknown media type string in configuration: {KindStr}", kindStr);
                        break;
                }
            }

            // Fallback just in case everything failed
            if (selectedMediaKinds.Count == 0)
            {
                _logger.LogWarning("Mapping failed. Forcing all default media kinds to prevent empty task execution.");
                selectedMediaKinds.AddRange(new[] {
                    BaseItemKind.Movie, BaseItemKind.Series, BaseItemKind.MusicArtist,
                    BaseItemKind.MusicAlbum, BaseItemKind.Audio, BaseItemKind.Book,
                    BaseItemKind.AudioBook, BaseItemKind.Video, BaseItemKind.BoxSet
                });
            }

            // Log verified target kinds to logs for debugging
            _logger.LogInformation("SortTitleUpdater is starting query for kinds: {Kinds}",
                string.Join(", ", selectedMediaKinds.Select(k => k.ToString())));

            var query = new InternalItemsQuery
            {
                IncludeItemTypes = selectedMediaKinds.ToArray(),
                Recursive = true,
                AncestorIds = config.TargetLibraryIds.ToArray()
            };

            var query = new InternalItemsQuery
            {
                IncludeItemTypes = selectedMediaKinds.ToArray(), // Array loaded dynamically from web config
                Recursive = true,
                AncestorIds = config.TargetLibraryIds.ToArray()
            };

            var items = _libraryManager.GetItemList(query);

            if (items.Count == 0)
            {
                _logger.LogInformation("No items found to update in the selected libraries.");
                progress.Report(100);
                return;
            }

            double total = items.Count;
            double current = 0;
            var savers = new[] { "Nfo" };

            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (item != null && !string.IsNullOrEmpty(item.Name))
                {
                    string normalizedName = item.Name.Trim().ToUpperInvariant();
                    var sb = new StringBuilder();

                    foreach (char c in normalizedName)
                    {
                        ushort code = c;

                        int digit1 = (code >> 12) & 0xF;
                        int digit2 = (code >> 8) & 0xF;
                        int digit3 = (code >> 4) & 0xF;
                        int digit4 = code & 0xF;

                        sb.Append((char)('a' + digit1));
                        sb.Append((char)('a' + digit2));
                        sb.Append((char)('a' + digit3));
                        sb.Append((char)('a' + digit4));
                        sb.Append('-');
                    }

                    string newSortTitle = sb.ToString();

                    if (item.ForcedSortName != newSortTitle)
                    {
                        item.ForcedSortName = newSortTitle;
                        item.IsLocked = false;

                        var originalLockedFields = item.LockedFields.ToArray();
                        item.LockedFields = Array.Empty<MetadataField>();

                        await _providerManager.SaveMetadataAsync(item, ItemUpdateType.MetadataEdit, savers)
                                              .ConfigureAwait(false);

                        item.IsLocked = true;

                        var lockedFieldsList = originalLockedFields.ToList();
                        if (!lockedFieldsList.Contains(MetadataField.Name))
                        {
                            lockedFieldsList.Add(MetadataField.Name);
                        }
                        item.LockedFields = lockedFieldsList.ToArray();

                        await _libraryManager.UpdateItemAsync(
                            item,
                            item.GetParent(),
                            ItemUpdateType.MetadataEdit,
                            cancellationToken
                        ).ConfigureAwait(false);

                        _logger.LogInformation("Successfully updated NFO and DB Sort Title for [{Kind}]: {ItemName}",
                            item.GetType().Name, item.Name);
                    }
                }

                current++;
                progress.Report((current / total) * 100);
            }

            _logger.LogInformation("Sort Title encoding task completed successfully.");
        }

        public void Dispose()
        {
            _libraryManager = null!;
            _providerManager = null!;
            _logger = null!;
        }
    }
}
