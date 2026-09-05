using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Services;
using Emby.ActorBrowser.Models;

namespace Emby.ActorBrowser.Services
{
    [Route("/ActorBrowser", "GET", Summary = "Actor Browser web page")]
    public sealed class GetActorBrowserPage
    {
    }

    [Route("/ActorBrowser/People", "GET", Summary = "List all actors")]
    public sealed class GetActorBrowserPeople
    {
        public string Search { get; set; }
        public string Group { get; set; }
        public int? StartIndex { get; set; }
        public int? Limit { get; set; }
    }

    [Route("/ActorBrowser/Items", "GET", Summary = "List movies and series for an actor")]
    public sealed class GetActorBrowserItems
    {
        public long PersonId { get; set; }
        public int? StartIndex { get; set; }
        public int? Limit { get; set; }
    }

    [Route("/ActorBrowser/Lists", "GET", Summary = "List the favorites list and all custom actor lists")]
    public sealed class GetActorLists
    {
    }

    [Route("/ActorBrowser/Lists", "POST", Summary = "Create a custom actor list")]
    public sealed class CreateActorList
    {
        public string Name { get; set; }
    }

    [Route("/ActorBrowser/Lists/{Id}", "POST", Summary = "Rename a custom actor list")]
    public sealed class RenameActorList
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }

    [Route("/ActorBrowser/Lists/{Id}", "DELETE", Summary = "Delete a custom actor list")]
    public sealed class DeleteActorList
    {
        public string Id { get; set; }
    }

    [Route("/ActorBrowser/Lists/{Id}/People", "GET", Summary = "List the actors that belong to a favorites/custom list")]
    public sealed class GetActorListPeople
    {
        public string Id { get; set; }
        public string Search { get; set; }
        public int? StartIndex { get; set; }
        public int? Limit { get; set; }
    }

    [Route("/ActorBrowser/Lists/{Id}/ActorIds", "GET", Summary = "Get the raw actor id set for a list")]
    public sealed class GetActorListIds
    {
        public string Id { get; set; }
    }

    [Route("/ActorBrowser/Lists/{Id}/Actors/{ActorId}", "POST", Summary = "Add an actor to a list")]
    public sealed class AddActorToList
    {
        public string Id { get; set; }
        public long ActorId { get; set; }
    }

    [Route("/ActorBrowser/Lists/{Id}/Actors/{ActorId}", "DELETE", Summary = "Remove an actor from a list")]
    public sealed class RemoveActorFromList
    {
        public string Id { get; set; }
        public long ActorId { get; set; }
    }

    [Route("/ActorBrowser/Actors/{ActorId}/Lists", "GET", Summary = "Get list membership flags for an actor")]
    public sealed class GetActorListMembership
    {
        public long ActorId { get; set; }
    }

    /// <summary>
    /// Exposes a small same-origin HTML app and JSON endpoints.
    /// Emby authentication remains enabled; the page propagates a dedicated api_key to its own requests.
    /// </summary>
    public sealed class ActorBrowserService : IService
    {
        private readonly ILibraryManager _libraryManager;
        private readonly IHttpResultFactory _resultFactory;
        private readonly ActorListStore _listStore;
        private static readonly StringComparer NameComparer =
            StringComparer.Create(CultureInfo.GetCultureInfo("zh-CN"), true);

        public ActorBrowserService(
            ILibraryManager libraryManager,
            IHttpResultFactory resultFactory,
            IApplicationPaths appPaths,
            IJsonSerializer jsonSerializer)
        {
            _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
            _resultFactory = resultFactory ?? throw new ArgumentNullException(nameof(resultFactory));
            _listStore = new ActorListStore(appPaths, jsonSerializer);
        }

        public object Get(GetActorBrowserPage request)
        {
            var assembly = typeof(ActorBrowserService).GetTypeInfo().Assembly;
            const string resourceName = "Emby.ActorBrowser.Web.actor-browser.html";

            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    return _resultFactory.GetResult("Actor Browser resource not found.".AsSpan(), "text/plain; charset=utf-8");
                }

                using (var reader = new StreamReader(stream))
                {
                    var html = reader.ReadToEnd();
                    return _resultFactory.GetResult(html.AsSpan(), "text/html; charset=utf-8");
                }
            }
        }

        public object Get(GetActorBrowserPeople request)
        {
            if (_libraryManager == null)
            {
                throw new InvalidOperationException("Actor Browser initialization error: ILibraryManager is null.");
            }

            var startIndex = Math.Max(0, request.StartIndex ?? 0);
            var limit = Clamp(request.Limit ?? 120, 1, 500);
            var search = (request.Search ?? string.Empty).Trim();

            var query = new InternalItemsQuery
            {
                Recursive = true,
                PersonTypes = new[] { PersonType.Actor }
            };

            var people = _libraryManager.GetPeople(query).Items
                .Where(x => x != null && x.Item1 != null)
                .Select(x => new ActorDto
                {
                    Id = x.Item1.Id.ToString("N"),
                    InternalId = x.Item1.InternalId,
                    Name = x.Item1.Name ?? string.Empty,
                    SortName = string.IsNullOrWhiteSpace(x.Item1.SortName) ? (x.Item1.Name ?? string.Empty) : x.Item1.SortName,
                    WorkCount = ReadWorkCount(x.Item2)
                });

            if (!string.IsNullOrWhiteSpace(search))
            {
                people = people.Where(x =>
                    Contains(x.Name, search) ||
                    Contains(x.SortName, search));
            }

            var group = (request.Group ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(group) && !string.Equals(group, "全部", StringComparison.OrdinalIgnoreCase))
            {
                people = people.Where(x => MatchesGroup(x.SortName, x.Name, group));
            }

            var ordered = people
                .OrderBy(x => x.SortName, NameComparer)
                .ThenBy(x => x.Name, NameComparer)
                .ToList();

            return new PagedResult<ActorDto>
            {
                TotalRecordCount = ordered.Count(),
                StartIndex = startIndex,
                Limit = limit,
                Items = ordered.Skip(startIndex).Take(limit).ToList()
            };
        }

        public object Get(GetActorBrowserItems request)
        {
            if (_libraryManager == null)
            {
                throw new InvalidOperationException("Actor Browser initialization error: ILibraryManager is null.");
            }

            var startIndex = Math.Max(0, request.StartIndex ?? 0);
            var limit = Clamp(request.Limit ?? 200, 1, 500);

            if (request.PersonId <= 0 || _libraryManager.GetItemById(request.PersonId) == null)
            {
                return new PagedResult<MediaItemDto>
                {
                    TotalRecordCount = 0,
                    StartIndex = startIndex,
                    Limit = limit,
                    Items = new List<MediaItemDto>()
                };
            }

            var query = new InternalItemsQuery
            {
                Recursive = true,
                PersonIds = new[] { request.PersonId },
                PersonTypes = new[] { PersonType.Actor },
                IncludeItemTypes = new[] { "Movie", "Series" }
            };

            var items = _libraryManager.GetItemsResult(query).Items
                .Where(x => x != null)
                .GroupBy(x => x.InternalId)
                .Select(x => x.First())
                .OrderByDescending(x => x.ProductionYear ?? 0)
                .ThenBy(x => x.SortName ?? x.Name, NameComparer)
                .Select(x => new MediaItemDto
                {
                    Id = x.Id.ToString("N"),
                    InternalId = x.InternalId,
                    Name = x.Name ?? string.Empty,
                    Type = x.GetType().Name,
                    ProductionYear = x.ProductionYear,
                    Overview = x.Overview ?? string.Empty
                })
                .ToList();

            return new PagedResult<MediaItemDto>
            {
                TotalRecordCount = items.Count(),
                StartIndex = startIndex,
                Limit = limit,
                Items = items.Skip(startIndex).Take(limit).ToList()
            };
        }

        public object Get(GetActorLists request)
        {
            var data = _listStore.Read();
            return data.Lists.Select(ToSummary).ToList();
        }

        public object Post(CreateActorList request)
        {
            var name = (request.Name ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("列表名称不能为空");
            }

            return _listStore.Mutate(data =>
            {
                if (data.Lists.Any(x => !x.IsFavorites && string.Equals(x.Name, name, StringComparison.CurrentCultureIgnoreCase)))
                {
                    throw new ArgumentException("已存在同名列表");
                }

                var record = new ActorListRecord { Id = Guid.NewGuid().ToString("N"), Name = name, IsFavorites = false, ActorIds = new List<long>() };
                data.Lists.Add(record);
                return ToSummary(record);
            });
        }

        public object Post(RenameActorList request)
        {
            var name = (request.Name ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("列表名称不能为空");
            }

            return _listStore.Mutate(data =>
            {
                var record = ResolveList(data, request.Id);
                if (record.IsFavorites)
                {
                    throw new ArgumentException("收藏列表不能重命名");
                }

                if (data.Lists.Any(x => x.Id != record.Id && !x.IsFavorites && string.Equals(x.Name, name, StringComparison.CurrentCultureIgnoreCase)))
                {
                    throw new ArgumentException("已存在同名列表");
                }

                record.Name = name;
                return ToSummary(record);
            });
        }

        public void Delete(DeleteActorList request)
        {
            _listStore.Mutate(data =>
            {
                var record = ResolveList(data, request.Id);
                if (record.IsFavorites)
                {
                    throw new ArgumentException("收藏列表不能删除");
                }

                data.Lists.Remove(record);
            });
        }

        public object Get(GetActorListIds request)
        {
            var data = _listStore.Read();
            var record = ResolveList(data, request.Id);
            return record.ActorIds.ToList();
        }

        public object Post(AddActorToList request)
        {
            return _listStore.Mutate(data =>
            {
                var record = ResolveList(data, request.Id);
                if (!record.ActorIds.Contains(request.ActorId)) record.ActorIds.Add(request.ActorId);
                return ToSummary(record);
            });
        }

        public object Delete(RemoveActorFromList request)
        {
            return _listStore.Mutate(data =>
            {
                var record = ResolveList(data, request.Id);
                record.ActorIds.Remove(request.ActorId);
                return ToSummary(record);
            });
        }

        public object Get(GetActorListMembership request)
        {
            var data = _listStore.Read();
            return data.Lists.Select(x => new ActorListMembershipDto
            {
                Id = x.Id,
                Name = x.Name,
                IsFavorites = x.IsFavorites,
                Contains = x.ActorIds.Contains(request.ActorId)
            }).ToList();
        }

        public object Get(GetActorListPeople request)
        {
            if (_libraryManager == null)
            {
                throw new InvalidOperationException("Actor Browser initialization error: ILibraryManager is null.");
            }

            var data = _listStore.Read();
            var record = ResolveList(data, request.Id);

            var startIndex = Math.Max(0, request.StartIndex ?? 0);
            var limit = Clamp(request.Limit ?? 120, 1, 500);
            var search = (request.Search ?? string.Empty).Trim();

            var actors = record.ActorIds
                .Select(internalId => _libraryManager.GetItemById(internalId))
                .Where(x => x != null)
                .Select(x => new ActorDto
                {
                    Id = x.Id.ToString("N"),
                    InternalId = x.InternalId,
                    Name = x.Name ?? string.Empty,
                    SortName = string.IsNullOrWhiteSpace(x.SortName) ? (x.Name ?? string.Empty) : x.SortName,
                    WorkCount = CountWorks(x.InternalId)
                });

            if (!string.IsNullOrWhiteSpace(search))
            {
                actors = actors.Where(x => Contains(x.Name, search) || Contains(x.SortName, search));
            }

            var ordered = actors
                .OrderBy(x => x.SortName, NameComparer)
                .ThenBy(x => x.Name, NameComparer)
                .ToList();

            return new PagedResult<ActorDto>
            {
                TotalRecordCount = ordered.Count,
                StartIndex = startIndex,
                Limit = limit,
                Items = ordered.Skip(startIndex).Take(limit).ToList()
            };
        }

        private int CountWorks(long personInternalId)
        {
            var query = new InternalItemsQuery
            {
                Recursive = true,
                PersonIds = new[] { personInternalId },
                PersonTypes = new[] { PersonType.Actor },
                IncludeItemTypes = new[] { "Movie", "Series" }
            };

            return _libraryManager.GetItemsResult(query).Items
                .Where(x => x != null)
                .Select(x => x.InternalId)
                .Distinct()
                .Count();
        }

        private static ActorListRecord ResolveList(ActorListStoreData data, string id)
        {
            var record = data.Lists.FirstOrDefault(x => x.Id == id);
            if (record == null)
            {
                throw new ResourceNotFoundException("列表不存在");
            }

            return record;
        }

        private static ActorListSummaryDto ToSummary(ActorListRecord record)
        {
            return new ActorListSummaryDto
            {
                Id = record.Id,
                Name = record.Name,
                IsFavorites = record.IsFavorites,
                Count = record.ActorIds?.Count ?? 0
            };
        }

        private static bool Contains(string value, string search)
        {
            return !string.IsNullOrEmpty(value) &&
                   value.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        private static bool MatchesGroup(string sortName, string name, string group)
        {
            var value = string.IsNullOrWhiteSpace(sortName) ? (name ?? string.Empty) : sortName;
            value = value.Trim();
            var isLatin = value.Length > 0 && ((value[0] >= 'A' && value[0] <= 'Z') || (value[0] >= 'a' && value[0] <= 'z'));

            if (string.Equals(group, "中文/其他", StringComparison.OrdinalIgnoreCase))
            {
                return !isLatin;
            }

            if (group.Length == 1 && group[0] >= 'A' && group[0] <= 'Z')
            {
                return isLatin && char.ToUpperInvariant(value[0]) == group[0];
            }

            return true;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static int ReadWorkCount(object itemCounts)
        {
            if (itemCounts == null) return 0;

            var direct = ReadIntProperty(itemCounts, "ItemCount") ??
                         ReadIntProperty(itemCounts, "TotalCount");
            if (direct.HasValue) return direct.Value;

            var movie = ReadIntProperty(itemCounts, "MovieCount") ?? 0;
            var series = ReadIntProperty(itemCounts, "SeriesCount") ?? 0;
            return movie + series;
        }

        private static int? ReadIntProperty(object value, string name)
        {
            var property = value.GetType().GetRuntimeProperty(name);
            if (property == null) return null;

            var raw = property.GetValue(value);
            if (raw == null) return null;

            try
            {
                return Convert.ToInt32(raw, CultureInfo.InvariantCulture);
            }
            catch
            {
                return null;
            }
        }
    }
}
