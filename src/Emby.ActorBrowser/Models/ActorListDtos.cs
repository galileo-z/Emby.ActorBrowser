using System.Collections.Generic;

namespace Emby.ActorBrowser.Models
{
    /// <summary>
    /// Persisted record for a favorites/custom actor list. ActorIds stores library InternalId values.
    /// </summary>
    public sealed class ActorListRecord
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public bool IsFavorites { get; set; }
        public List<long> ActorIds { get; set; } = new List<long>();
    }

    public sealed class ActorListStoreData
    {
        public List<ActorListRecord> Lists { get; set; } = new List<ActorListRecord>();
    }

    public sealed class ActorListSummaryDto
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public bool IsFavorites { get; set; }
        public int Count { get; set; }
    }

    public sealed class ActorListMembershipDto
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public bool IsFavorites { get; set; }
        public bool Contains { get; set; }
    }
}
