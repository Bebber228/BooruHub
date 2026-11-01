using System;
using System.Collections.Generic;
using System.IO.IsolatedStorage;
using System.Windows;
using Newtonsoft.Json;

namespace ArtHub
{
    public class Source
    {
        public string Name { get; set; }
        public string Url { get; set; }
        public string ApiKey { get; set; }
        public string UserId { get; set; }

        [JsonIgnore]
        public bool IsActive { get; set; }

        [JsonIgnore]
        public bool ShowTools { get; set; }

        [JsonIgnore]
        public string Title
        {
            get { return (IsActive ? "● " : "") + Name; }
        }

        [JsonIgnore]
        public Visibility ToolsVisibility
        {
            get { return ShowTools ? Visibility.Visible : Visibility.Collapsed; }
        }
    }

    public static class Sources
    {
        const string ListKey = "sources";
        const string IdxKey = "sourceIdx";

        static List<Source> _list;
        static int _idx;

        // растёт при любом изменении, по нему главная страница понимает, что надо перезагрузить ленту
        public static int Version;

        static IsolatedStorageSettings S { get { return IsolatedStorageSettings.ApplicationSettings; } }

        static void Load()
        {
            if (_list != null) return;

            try
            {
                string json;
                if (S.TryGetValue(ListKey, out json))
                    _list = JsonConvert.DeserializeObject<List<Source>>(json);
            }
            catch { }

            if (_list == null || _list.Count == 0)
            {
                _list = new List<Source>();
                _list.Add(new Source { Name = "Safebooru", Url = "https://safebooru.org", ApiKey = "", UserId = "" });
            }

            int i;
            if (S.TryGetValue(IdxKey, out i)) _idx = i;
            if (_idx < 0 || _idx >= _list.Count) _idx = 0;
        }

        public static List<Source> All
        {
            get { Load(); return _list; }
        }

        public static Source Current
        {
            get { Load(); return _list[_idx]; }
        }

        public static void Save()
        {
            Load();
            S[ListKey] = JsonConvert.SerializeObject(_list);
            S[IdxKey] = _idx;
            S.Save();
            Version++;
        }

        public static void Select(Source s)
        {
            Load();
            int i = _list.IndexOf(s);
            if (i < 0 || i == _idx) return;
            _idx = i;
            Save();
        }

        public static void Add(Source s)
        {
            Load();
            _list.Add(s);
            Save();
        }

        public static void Remove(Source s)
        {
            Load();
            int i = _list.IndexOf(s);
            if (i < 0 || _list.Count <= 1) return;

            _list.RemoveAt(i);
            if (_idx == i) _idx = 0;
            else if (_idx > i) _idx--;
            Save();
        }

        public static string NormalizeUrl(string input)
        {
            string u = (input ?? "").Trim();
            if (u.Length == 0) return null;

            if (!u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !u.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                u = "https://" + u;

            u = u.TrimEnd('/');

            Uri uri;
            if (!Uri.TryCreate(u, UriKind.Absolute, out uri)) return null;
            return u;
        }
    }
}