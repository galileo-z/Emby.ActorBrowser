using System;
using MediaBrowser.Common.Plugins;

namespace Emby.ActorBrowser
{
    public sealed class Plugin : BasePlugin
    {
        public override string Name => "Actor Browser";

        public override string Description => "Browse all actors and their movies/series in a standalone Emby-hosted page.";

        public override Guid Id => Guid.Parse("a42f689b-d234-40ca-a36b-e9266925b845");
    }
}
