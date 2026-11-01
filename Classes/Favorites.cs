using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.IsolatedStorage;
using System.Linq;
using Newtonsoft.Json;

namespace ArtHub
{
    public static class Favorites
    {
        const string FileName = "favorites.json";

        public static readonly ObservableCollection<ArtItem> Items = new ObservableCollection<ArtItem>();
        static bool _loaded;

        static string Key(ArtItem a)
        {
            return (a.SourceUrl ?? "") + "|" + a.Id;
        }

        public static void Load()
        {
            if (_loaded) return;
            _loaded = true;

            try
            {
                using (var store = IsolatedStorageFile.GetUserStoreForApplication())
                {
                    if (!store.FileExists(FileName)) return;

                    using (var s = store.OpenFile(FileName, FileMode.Open, FileAccess.Read))
                    using (var r = new StreamReader(s))
                    {
                        var list = JsonConvert.DeserializeObject<List<ArtItem>>(r.ReadToEnd());
                        if (list != null)
                            foreach (ArtItem a in list) Items.Add(a);
                    }
                }
            }
            catch { }
        }

        public static bool Contains(ArtItem item)
        {
            Load();
            string k = Key(item);
            return Items.Any(x => Key(x) == k);
        }

        // возвращает true, если пост теперь в избранном
        public static bool Toggle(ArtItem item)
        {
            Load();
            string k = Key(item);
            ArtItem existing = Items.FirstOrDefault(x => Key(x) == k);

            bool added;
            if (existing != null)
            {
                Items.Remove(existing);
                added = false;
            }
            else
            {
                Items.Insert(0, item);
                added = true;
            }

            Save();
            return added;
        }

        static void Save()
        {
            try
            {
                using (var store = IsolatedStorageFile.GetUserStoreForApplication())
                using (var s = store.OpenFile(FileName, FileMode.Create, FileAccess.Write))
                using (var w = new StreamWriter(s))
                {
                    w.Write(JsonConvert.SerializeObject(Items.ToList()));
                }
            }
            catch { }
        }
    }
}