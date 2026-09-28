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

            var selectedMediaKinds = new List<BaseItemKind>();
            foreach (var kindStr in savedKinds)
            {
                if (string.IsNullOrEmpty(kindStr)) continue;

                switch (kindStr.Trim().ToLowerInvariant())
                {
                    case "movie": selectedMediaKinds.Add(BaseItemKind.Movie); break;
                    case "series": selectedMediaKinds.Add(BaseItemKind.Series); break;
                    case "season": selectedMediaKinds.Add(BaseItemKind.Season); break;   // Added
                    case "episode": selectedMediaKinds.Add(BaseItemKind.Episode); break; // Added
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

            if (selectedMediaKinds.Count == 0)
            {
                _logger.LogWarning("Mapping failed. Forcing all default media kinds to prevent empty task execution.");
                selectedMediaKinds.AddRange(new[] {
                    BaseItemKind.Movie, BaseItemKind.Series, BaseItemKind.Season, BaseItemKind.Episode,
                    BaseItemKind.MusicArtist, BaseItemKind.MusicAlbum, BaseItemKind.Audio, BaseItemKind.Book,
                    BaseItemKind.AudioBook, BaseItemKind.Video, BaseItemKind.BoxSet
                });
            }

            _logger.LogInformation("SortTitleUpdater is starting query for kinds: {Kinds}",
                string.Join(", ", selectedMediaKinds.Select(k => k.ToString())));


            // Load selected library IDs into a HashSet for high-performance memory lookup
            var targetLibraryGuids = new HashSet<Guid>(config.TargetLibraryIds ?? new List<Guid>());

            // Query ALL matching items globally across the server
            var query = new InternalItemsQuery
            {
                IncludeItemTypes = selectedMediaKinds.ToArray(),
                Recursive = true               
            };

            var allItems = _libraryManager.GetItemList(query);

            // Filter items in memory to include only those belonging to the selected target libraries
            var items = allItems.Where(item =>
                item != null &&
                item.GetAncestorIds().Any(ancestorId => targetLibraryGuids.Contains(ancestorId))
            ).ToList();

            if (items.Count == 0)
            {
                _logger.LogInformation("No items matched the selected libraries criteria. Total global items checked: {Count}", allItems.Count);
                progress.Report(100);
                return;
            }

            _logger.LogInformation("Successfully found {Count} media items to process inside selected libraries.", items.Count);

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
