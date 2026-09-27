using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.SortTitleUpdater
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;

            // Debug log to output available manifest resource names during server build/startup
            var resources = typeof(Plugin).Assembly.GetManifestResourceNames();
            Console.WriteLine($"[SortTitleUpdater] Available embedded resources: {string.Join(", ", resources)}");
        }

        public override string Name => "Sort Title Updater";

        public override Guid Id => Guid.Parse("a1b2c3d4-e5f6-7a8b-9c0d-1e2f3a4b5c6d");

        public static Plugin? Instance { get; private set; }

        // Registers the plugin configuration webpage within the Jellyfin dashboard
        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = "Sort Title Updater",
                    // Explicitly maps to the embedded HTML resource
                    EmbeddedResourcePath = GetType().Namespace + ".configpage.html",
                    EnableInMainMenu = false
                }
            };
        }
    }

    public class PluginConfiguration : BasePluginConfiguration
    {
        // Stores the list of selected library IDs (Virtual Folders) to process
        public List<Guid> TargetLibraryIds { get; set; } = new();
    }
}
