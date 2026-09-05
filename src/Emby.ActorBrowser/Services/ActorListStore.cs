using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Serialization;
using Emby.ActorBrowser.Models;

namespace Emby.ActorBrowser.Services
{
    /// <summary>
    /// Reads/writes the favorites and custom actor lists as a single JSON file under the plugin's
    /// configuration directory. A process-wide lock guards every read-modify-write cycle since the
    /// underlying IService instances are created per request.
    /// </summary>
    public sealed class ActorListStore
    {
        public const string FavoritesId = "favorites";
        private const string FavoritesName = "收藏";

        private static readonly object Lock = new object();

        private readonly IJsonSerializer _jsonSerializer;
        private readonly string _filePath;

        public ActorListStore(IApplicationPaths appPaths, IJsonSerializer jsonSerializer)
        {
            if (appPaths == null) throw new ArgumentNullException(nameof(appPaths));
            _jsonSerializer = jsonSerializer ?? throw new ArgumentNullException(nameof(jsonSerializer));
            _filePath = Path.Combine(appPaths.PluginConfigurationsPath, "Emby.ActorBrowser.lists.json");
        }

        public ActorListStoreData Read()
        {
            lock (Lock)
            {
                return ReadUnlocked();
            }
        }

        public void Mutate(Action<ActorListStoreData> mutator)
        {
            Mutate<object>(data => { mutator(data); return null; });
        }

        public T Mutate<T>(Func<ActorListStoreData, T> mutator)
        {
            lock (Lock)
            {
                var data = ReadUnlocked();
                var result = mutator(data);
                Write(data);
                return result;
            }
        }

        private ActorListStoreData ReadUnlocked()
        {
            ActorListStoreData data = null;
            if (File.Exists(_filePath))
            {
                try
                {
                    data = _jsonSerializer.DeserializeFromFile<ActorListStoreData>(_filePath);
                }
                catch
                {
                    data = null;
                }
            }

            if (data == null) data = new ActorListStoreData();
            if (data.Lists == null) data.Lists = new List<ActorListRecord>();

            var favorites = data.Lists.FirstOrDefault(x => x.IsFavorites);
            if (favorites == null)
            {
                favorites = new ActorListRecord { Id = FavoritesId, Name = FavoritesName, IsFavorites = true, ActorIds = new List<long>() };
                data.Lists.Insert(0, favorites);
            }
            else
            {
                favorites.Id = FavoritesId;
                if (favorites.ActorIds == null) favorites.ActorIds = new List<long>();
            }

            foreach (var list in data.Lists)
            {
                if (list.ActorIds == null) list.ActorIds = new List<long>();
            }

            return data;
        }

        private void Write(ActorListStoreData data)
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            _jsonSerializer.SerializeToFile(data, _filePath);
        }
    }
}
