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

            if (config.TargetMediaKinds == null || config.TargetMediaKinds.Count == 0)
            {
                _logger.LogWarning("Task cancelled: No target media types selected in plugin settings.");
                progress.Report(100);
                return;
            }

            // DYNAMIC FIX: Map saved config strings back into valid BaseItemKind enums
            var selectedMediaKinds = new List<BaseItemKind>();
            foreach (var kindStr in config.TargetMediaKinds)
            {
                if (Enum.TryParse<BaseItemKind>(kindStr, true, out var kindEnum))
                {
                    selectedMediaKinds.Add(kindEnum);
                }
            }

            if (selectedMediaKinds.Count == 0)
            {
                _logger.LogWarning("Task cancelled: Could not parse any valid media types from configuration.");
                progress.Report(100);
                return;
            }

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
