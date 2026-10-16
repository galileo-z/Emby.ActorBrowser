using System.Collections.Generic;

namespace Emby.ActorBrowser.Models
{
    public sealed class ActorDto
    {
        public string Id { get; set; }
        public long InternalId { get; set; }
        public string Name { get; set; }
        public string SortName { get; set; }
        public int WorkCount { get; set; }
    }

    public sealed class MediaItemDto
    {
        public string Id { get; set; }
        public long InternalId { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public int? ProductionYear { get; set; }
        public string Overview { get; set; }
    }

    public sealed class CastMemberDto
    {
        public string Id { get; set; }
        public long InternalId { get; set; }
        public string Name { get; set; }
        public string Role { get; set; }
    }

    public sealed class MediaLibraryDto
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public int Count { get; set; }
    }

    public sealed class PagedResult<T>
    {
        public int TotalRecordCount { get; set; }
        public int StartIndex { get; set; }
        public int Limit { get; set; }
        public List<T> Items { get; set; }
    }
}
