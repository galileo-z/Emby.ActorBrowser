using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Controller.Providers;
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
        public long? LibraryId { get; set; }
        public int? StartIndex { get; set; }
        public int? Limit { get; set; }
    }

    [Route("/ActorBrowser/Libraries", "GET", Summary = "List the server's media libraries with actor counts")]
    public sealed class GetActorBrowserLibraries
    {
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

    [Route("/ActorBrowser/Movies", "GET", Summary = "Search movies and series by title")]
    public sealed class SearchActorBrowserMovies
    {
        public string Search { get; set; }
        public int? Limit { get; set; }
    }

    [Route("/ActorBrowser/Movies/{ItemId}/Cast", "GET", Summary = "List the actors currently attached to a movie or series")]
    public sealed class GetMovieCast
    {
        public long ItemId { get; set; }
    }

    [Route("/ActorBrowser/Movies/{ItemId}/Cast", "POST", Summary = "Attach an actor (existing or new, matched by name) to a movie or series and write NFO (admin only)")]
    public sealed class AddMovieCast
    {
        public long ItemId { get; set; }
        public string Name { get; set; }
        public string Role { get; set; }
    }

    [Route("/ActorBrowser/Movies/{ItemId}/Cast", "DELETE", Summary = "Detach an actor from a movie or series and write NFO (admin only)")]
    public sealed class RemoveMovieCast
    {
        public long ItemId { get; set; }
        public string Name { get; set; }
    }

    [Route("/ActorBrowser/People/Duplicates", "GET", Summary = "Find Person entries that share the exact same name and likely need merging")]
    public sealed class GetDuplicatePeople
    {
    }

    [Route("/ActorBrowser/People/Merge", "POST", Summary = "Merge one or more Person entries into a single target: rewrites that name on every credited item (writing NFO), then deletes the merged-away Person entries (admin only)")]
    public sealed class MergePeople
    {
        public long KeepPersonId { get; set; }
        public List<long> MergePersonIds { get; set; }
    }

    /// <summary>
    /// Exposes a small same-origin HTML app and JSON endpoints.
    /// Emby authentication remains enabled; the page propagates a dedicated api_key to its own requests.
    /// </summary>
    public sealed class ActorBrowserService : IService, IRequiresRequest
    {
        private readonly ILibraryManager _libraryManager;
        private readonly IHttpResultFactory _resultFactory;
        private readonly IAuthorizationContext _authContext;
        private readonly IUserManager _userManager;
        private readonly IProviderManager _providerManager;
        private readonly ActorListStore _listStore;
        private static readonly StringComparer NameComparer =
            StringComparer.Create(CultureInfo.GetCultureInfo("zh-CN"), true);

        public ActorBrowserService(
            ILibraryManager libraryManager,
            IHttpResultFactory resultFactory,
            IApplicationPaths appPaths,
            IJsonSerializer jsonSerializer,
            IAuthorizationContext authContext,
            IUserManager userManager,
            IProviderManager providerManager)
        {
            _providerManager = providerManager ?? throw new ArgumentNullException(nameof(providerManager));
            _authContext = authContext ?? throw new ArgumentNullException(nameof(authContext));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
            _resultFactory = resultFactory ?? throw new ArgumentNullException(nameof(resultFactory));
            _listStore = new ActorListStore(appPaths, jsonSerializer);
        }

        public IRequest Request { get; set; }

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

            if (request.LibraryId.HasValue && request.LibraryId.Value > 0)
            {
                // GetPeople ignores TopParentIds; ParentIds combined with Recursive is what
                // actually scopes it to a library, matching Emby's own /Persons?ParentId= behavior.
                query.ParentIds = new[] { request.LibraryId.Value };
            }

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

        public object Get(GetActorBrowserLibraries request)
        {
            if (_libraryManager == null)
            {
                throw new InvalidOperationException("Actor Browser initialization error: ILibraryManager is null.");
            }

            var folders = _libraryManager.GetVirtualFolders() ?? new List<VirtualFolderInfo>();

            return folders
                .Where(x => x != null
                    && !string.Equals(x.CollectionType, "boxsets", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(x.CollectionType, "playlists", StringComparison.OrdinalIgnoreCase)
                    && long.TryParse(x.ItemId, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                .Select(x =>
                {
                    long.TryParse(x.ItemId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var internalId);
                    return new MediaLibraryDto
                    {
                        Id = internalId,
                        Name = x.Name ?? string.Empty,
                        Count = CountPeopleInLibrary(internalId)
                    };
                })
                .OrderBy(x => x.Name, NameComparer)
                .ToList();
        }

        public object Get(SearchActorBrowserMovies request)
        {
            var search = (request.Search ?? string.Empty).Trim();
            var limit = Clamp(request.Limit ?? 30, 1, 100);
            if (search.Length == 0) return new List<MediaItemDto>();

            var query = new InternalItemsQuery
            {
                Recursive = true,
                SearchTerm = search,
                IncludeItemTypes = new[] { "Movie", "Series" },
                // Query more than the public limit so collapsing merge-group duplicates below
                // doesn't leave fewer distinct titles than the caller asked for.
                Limit = Math.Min(limit * 4, 200)
            };

            var results = _libraryManager.GetItemList(query).Where(x => x != null).ToList();

            // A merged multi-version movie is still several separate library items under the
            // hood, one per version file, all sharing the same title - without this each version
            // shows up as its own row for what the user sees as a single movie. Series don't have
            // alternate versions, so GetAlternateVersionIds is only meaningful for Video items
            // (Movie) and naturally no-ops (via the "as Video" cast) for Series.
            var seenGroupKeys = new HashSet<long>();
            var deduped = new List<BaseItem>();
            foreach (var x in results)
            {
                var alternateIds = (x as Video)?.GetAlternateVersionIds();
                var groupKey = alternateIds == null || alternateIds.Count == 0
                    ? x.InternalId
                    : Math.Min(x.InternalId, alternateIds.Min());
                if (seenGroupKeys.Add(groupKey))
                {
                    deduped.Add(x);
                }
            }

            return deduped
                .Take(limit)
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
        }

        public object Get(GetMovieCast request)
        {
            return ReadCast(ResolveMediaItem(request.ItemId));
        }

        public object Post(AddMovieCast request)
        {
            RequireAdmin();
            var item = ResolveMediaItem(request.ItemId);
            var name = (request.Name ?? string.Empty).Trim();
            if (name.Length == 0) throw new ArgumentException("演员名称不能为空");
            if (name.Length > 100) throw new ArgumentException("演员名称过长");

            // Reuse the stored spelling when the actor already exists in the library.
            var existing = FindPerson(name);
            var resolvedName = existing != null ? existing.Name : name;
            var role = (request.Role ?? string.Empty).Trim();

            // A merged multi-version movie is still several separate library items, one per
            // version file, each with its own People record - Emby's detail page shows cast from
            // whichever version it defaults to in the version picker, not necessarily this one,
            // so writing the actor to only the item the caller picked can leave the movie looking
            // cast-less. Apply the same add to every version in the group.
            var addedAny = false;
            foreach (var target in CollectMergeGroup(item))
            {
                var people = _libraryManager.GetItemPeople(target) ?? new List<PersonInfo>();
                var alreadyHas = people.Any(x => x.Type == PersonType.Actor &&
                                                 string.Equals(x.Name, resolvedName, StringComparison.OrdinalIgnoreCase));
                if (!alreadyHas)
                {
                    people.Add(new PersonInfo { Name = resolvedName, Type = PersonType.Actor, Role = role });
                    addedAny = true;
                }

                // Always resave, even when this particular target already had the actor: that's
                // exactly the state a previous partial add can leave behind, and skipping the
                // save here would permanently prevent ever fixing it through this endpoint.
                SavePeople(target, people);
            }

            // A brand-new actor only becomes a browsable Person item after Emby validates people.
            if (addedAny && existing == null) ValidatePeopleInBackground();

            return ReadCast(item);
        }

        public object Delete(RemoveMovieCast request)
        {
            RequireAdmin();
            var item = ResolveMediaItem(request.ItemId);
            var name = (request.Name ?? string.Empty).Trim();
            if (name.Length == 0) throw new ArgumentException("演员名称不能为空");

            foreach (var target in CollectMergeGroup(item))
            {
                var people = _libraryManager.GetItemPeople(target) ?? new List<PersonInfo>();
                var removed = people.RemoveAll(x => x.Type == PersonType.Actor &&
                                                    string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (removed > 0) SavePeople(target, people);
            }

            return ReadCast(item);
        }

        public object Get(GetDuplicatePeople request)
        {
            if (_libraryManager == null)
            {
                throw new InvalidOperationException("Actor Browser initialization error: ILibraryManager is null.");
            }

            // Emby's own people-validation can end up creating two separate Person library
            // entries with the exact same Name (e.g. the same alias string scraped from two
            // different folders under two different disks), rather than reusing the one that
            // already exists. Grouping by exact name surfaces those cleanly and safely - it
            // can't produce a false positive, since it only ever groups entries whose names are
            // already character-for-character identical.
            var people = _libraryManager.GetPeople(new InternalItemsQuery
            {
                Recursive = true,
                PersonTypes = new[] { PersonType.Actor }
            }).Items.Where(x => x != null && x.Item1 != null).ToList();

            return people
                .GroupBy(x => (x.Item1.Name ?? string.Empty).Trim(), StringComparer.Ordinal)
                .Where(g => !string.IsNullOrEmpty(g.Key) && g.Count() > 1)
                .Select(g => new DuplicatePersonGroupDto
                {
                    Name = g.Key,
                    People = g.Select(x => new DuplicatePersonDto
                        {
                            InternalId = x.Item1.InternalId,
                            Name = x.Item1.Name ?? string.Empty,
                            WorkCount = ReadWorkCount(x.Item2)
                        })
                        .OrderByDescending(p => p.WorkCount)
                        .ToList()
                })
                .OrderBy(g => g.Name, NameComparer)
                .ToList();
        }

        public object Post(MergePeople request)
        {
            RequireAdmin();

            var keep = _libraryManager.GetItemById(request.KeepPersonId);
            if (keep == null || !(keep is Person))
            {
                throw new ResourceNotFoundException("要保留的演员条目不存在");
            }

            var mergeIds = (request.MergePersonIds ?? new List<long>())
                .Where(id => id != request.KeepPersonId)
                .Distinct()
                .ToList();
            if (mergeIds.Count == 0)
            {
                throw new ArgumentException("没有选择要合并掉的演员条目");
            }

            var keepName = keep.Name ?? string.Empty;
            var affectedItems = 0;
            var mergedPeople = 0;

            foreach (var mergeId in mergeIds)
            {
                var source = _libraryManager.GetItemById(mergeId);
                if (source == null || !(source is Person))
                {
                    continue;
                }

                var sourceName = source.Name ?? string.Empty;
                if (sourceName.Length == 0)
                {
                    continue;
                }

                var creditedItems = _libraryManager.GetItemsResult(new InternalItemsQuery
                {
                    Recursive = true,
                    PersonIds = new[] { mergeId },
                    IncludeItemTypes = new[] { "Movie", "Series" }
                }).Items ?? Array.Empty<BaseItem>();

                foreach (var creditedItem in creditedItems)
                {
                    if (creditedItem == null)
                    {
                        continue;
                    }

                    var people = _libraryManager.GetItemPeople(creditedItem) ?? new List<PersonInfo>();
                    var touched = false;
                    foreach (var p in people)
                    {
                        if (string.Equals(p.Name, sourceName, StringComparison.OrdinalIgnoreCase))
                        {
                            p.Name = keepName;
                            touched = true;
                        }
                    }

                    if (!touched)
                    {
                        continue;
                    }

                    // Renaming above can leave two entries for the same (name, type) on one item
                    // - one that already credited the keep name, one that just got renamed to
                    // it. Collapse those, preferring whichever already had a Role filled in.
                    var deduped = people
                        .GroupBy(p => ((p.Name ?? string.Empty).Trim().ToLowerInvariant(), p.Type))
                        .Select(g => g.OrderByDescending(p => !string.IsNullOrEmpty(p.Role)).First())
                        .ToList();

                    SavePeople(creditedItem, deduped);
                    affectedItems++;
                }

                // The merged-away Person has no backing file on disk (it's metadata-only), so
                // this only removes the library record - nothing is deleted from the filesystem.
                _libraryManager.DeleteItem(source, new DeleteOptions { DeleteFileLocation = false });
                mergedPeople++;
            }

            return new OperationResultDto
            {
                Success = true,
                Message = $"已将 {mergedPeople} 个重复演员条目合并到「{keepName}」，更新了 {affectedItems} 个作品的演员信息",
                AffectedCount = affectedItems
            };
        }

        private BaseItem ResolveMediaItem(long internalId)
        {
            var item = internalId > 0 ? _libraryManager.GetItemById(internalId) : null;
            if (item == null || !(item is Movie || item is Series))
            {
                throw new ResourceNotFoundException("电影不存在");
            }

            return item;
        }

        /// <summary>
        /// A movie plus every other version merged into the same group (self-inclusive). Series
        /// don't have alternate versions, so this is effectively a no-op single-item list for
        /// them - the "as Video" cast naturally comes back null.
        /// </summary>
        private List<BaseItem> CollectMergeGroup(BaseItem item)
        {
            var targets = new List<BaseItem> { item };
            var video = item as Video;
            if (video != null)
            {
                foreach (var altId in video.GetAlternateVersionIds())
                {
                    var alt = _libraryManager.GetItemById(altId);
                    if (alt != null)
                    {
                        targets.Add(alt);
                    }
                }
            }

            return targets;
        }

        private void SavePeople(BaseItem item, List<PersonInfo> people)
        {
            _libraryManager.UpdatePeople(item, people, false);
            item.UpdateToRepository(ItemUpdateType.MetadataEdit);

            // Explicitly write the .nfo so the change survives a refresh. This is a no-op for
            // libraries whose NFO saver is disabled.
            _providerManager.SaveMetadata(item, ItemUpdateType.MetadataEdit).GetAwaiter().GetResult();
        }

        private void ValidatePeopleInBackground()
        {
            Task.Run(async () =>
            {
                try
                {
                    await _libraryManager.ValidatePeople(CancellationToken.None, new Progress<double>()).ConfigureAwait(false);
                }
                catch
                {
                    // Best effort: the regular "scan people" task will pick the actor up later.
                }
            });
        }

        private List<CastMemberDto> ReadCast(BaseItem item)
        {
            var result = new List<CastMemberDto>();
            foreach (var p in _libraryManager.GetItemPeople(item) ?? new List<PersonInfo>())
            {
                if (p.Type != PersonType.Actor) continue;

                var person = FindPerson(p.Name);
                result.Add(new CastMemberDto
                {
                    Id = person != null ? person.Id.ToString("N") : string.Empty,
                    InternalId = person != null ? person.InternalId : 0,
                    Name = p.Name ?? string.Empty,
                    Role = p.Role ?? string.Empty
                });
            }

            return result;
        }

        private BaseItem FindPerson(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            return _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { "Person" },
                Name = name,
                Limit = 1
            }).FirstOrDefault();
        }

        private void RequireAdmin()
        {
            var auth = _authContext.GetAuthorizationInfo(Request);
            var userId = auth != null ? auth.UserId : 0;

            // An administrator-issued api_key carries no user; any real user must be an admin.
            if (userId <= 0)
            {
                if (auth != null && !string.IsNullOrEmpty(auth.Token)) return;
                throw new SecurityException("需要管理员权限");
            }

            var user = _userManager.GetUserById(userId);
            if (user == null || !user.Policy.IsAdministrator)
            {
                throw new SecurityException("需要管理员权限");
            }
        }

        private int CountPeopleInLibrary(long libraryInternalId)
        {
            var query = new InternalItemsQuery
            {
                Recursive = true,
                PersonTypes = new[] { PersonType.Actor },
                ParentIds = new[] { libraryInternalId }
            };

            return _libraryManager.GetPeople(query).TotalRecordCount;
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
