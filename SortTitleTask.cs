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
    public class UpdateSortTitleTask : IScheduledTask
    {
        private readonly ILibraryManager _libraryManager;
        private readonly IProviderManager _providerManager;
        private readonly ILogger<UpdateSortTitleTask> _logger;

        // Dependency injection via constructor
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

        // Defines default execution trigger (e.g., daily at 2:00 AM)
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

            // Supported media kinds to search for (Seasons and Episodes excluded to preserve native numeric index sorting)
            var allMediaKinds = new[]
            {
                BaseItemKind.Movie,
                BaseItemKind.Series,
                BaseItemKind.MusicArtist,
                BaseItemKind.MusicAlbum,
                BaseItemKind.Audio,
                BaseItemKind.Book,
                BaseItemKind.AudioBook,
                BaseItemKind.Video,
                BaseItemKind.BoxSet
            };

            // Query items recursively filtering by selected ancestor library IDs
            var query = new InternalItemsQuery
            {
                IncludeItemTypes = allMediaKinds,
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
            var savers = new[] { "Nfo" }; // Identifies the local NFO metadata saver component

            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (item != null && !string.IsNullOrEmpty(item.Name))
                {
                    // 1 & 2: Trim leading/trailing spaces and transform string to Upper Case to normalize sorting
                    string normalizedName = item.Name.Trim().ToUpperInvariant();

                    var sb = new StringBuilder();

                    // Convert each character of the normalized Title to 4-digit hexadecimal UTF-16 representation
                    foreach (char c in normalizedName)
                    {
                        ushort code = c;

                        int digit1 = (code >> 12) & 0xF;
                        int digit2 = (code >> 8) & 0xF;
                        int digit3 = (code >> 4) & 0xF;
                        int digit4 = code & 0xF;

                        // Shift each hex digit value to latin characters 'a' through 'p'
                        sb.Append((char)('a' + digit1));
                        sb.Append((char)('a' + digit2));
                        sb.Append((char)('a' + digit3));
                        sb.Append((char)('a' + digit4));
                        sb.Append('-');
                    }

                    string newSortTitle = sb.ToString();

                    // Compare against the forced sort name to detect modifications
                    if (item.ForcedSortName != newSortTitle)
                    {
                        // Step 1: Assign the custom encoded string to ForcedSortName
                        item.ForcedSortName = newSortTitle;

                        // Step 2: Temporarily lift the main data lock so Jellyfin permits local file modification
                        item.IsLocked = false;

                        // Step 3: Clear individual field locks so the NfoSaver evaluates all properties
                        var originalLockedFields = item.LockedFields.ToArray();
                        item.LockedFields = Array.Empty<MetadataField>();

                        // Step 4: Flush data to the disk. 
                        // Since IsLocked is false, Jellyfin overrides the NFO file adding <sorttitle>
                        await _providerManager.SaveMetadataAsync(item, ItemUpdateType.MetadataEdit, savers)
                                              .ConfigureAwait(false);

                        // Step 5: Restore the global metadata lock state in memory
                        item.IsLocked = true;

                        // Step 6: Lock the name metadata field to protect it from future automated provider updates
                        var lockedFieldsList = originalLockedFields.ToList();
                        if (!lockedFieldsList.Contains(MetadataField.Name))
                        {
                            lockedFieldsList.Add(MetadataField.Name);
                        }
                        item.LockedFields = lockedFieldsList.ToArray();

                        // Step 7: Apply and commit the final database state to SQLite
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
    }
}
