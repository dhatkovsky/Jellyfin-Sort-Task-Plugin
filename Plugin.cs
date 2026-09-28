using System;
using System.Collections.Generic;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.SortTitleUpdater
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages, IDisposable
    {
        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;

            var resources = typeof(Plugin).Assembly.GetManifestResourceNames();
            Console.WriteLine($"[SortTitleUpdater] Available embedded resources: {string.Join(", ", resources)}");
        }

        public override string Name => "SortTitleUpdater";

        public override Guid Id => Guid.Parse("a1b2c3d4-e5f6-7a8b-9c0d-1e2f3a4b5c6d");

        public static Plugin? Instance { get; private set; }

        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = "SortTitleUpdater",
                    EmbeddedResourcePath = GetType().Namespace + ".configpage.html",
                    EnableInMainMenu = false
                }
            };
        }

        public void Dispose()
        {
            Instance = null;
        }
    }

    public class PluginConfiguration : BasePluginConfiguration
    {
        // Selected Library IDs (Virtual Folders)
        public List<Guid> TargetLibraryIds { get; set; } = new();

        // Selected Media Types (Stores string representation of BaseItemKind)
        public List<string> TargetMediaKinds { get; set; } = new()
        {
            "Movie", "Series", "MusicArtist", "MusicAlbum", "Audio", "Book", "AudioBook", "Video", "BoxSet"
        };
    }
}
