using System;
using System.Collections.Generic;
using System.IO;
using System.IO.IsolatedStorage;
using System.Net.Browser;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;

namespace ArtHub
{
    public class PostPage
    {
        public List<ArtItem> Items = new List<ArtItem>();
        public int Raw;   // сколько постов вернул сайт до фильтрации
    }

    public static class Api
    {
        public const int PageSize = 82;

        // всё, что связано с кэшем тегов, трогается из разных потоков, поэтому под замком
        static readonly object L = new object();
        static readonly Dictionary<string, TagInfo> TagCache = new Dictionary<string, TagInfo>();
        static readonly HashSet<string> Missing = new HashSet<string>();   // теги, которые сайт не отдал
        static readonly HashSet<string> Bad = new HashSet<string>();       // способы, которые у сайта не работают
        static string _cacheHost;
        static bool _dirty;

        static readonly string[] Black =
            Config.Blacklist.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        static readonly string[] AiTags =
        {
            "ai_generated", "ai_assisted", "ai_art", "ai-generated", "stable_diffusion",
            "novelai", "nai_diffusion", "midjourney", "dall-e", "comfyui", "pony_diffusion"
        };

        // вызывается при смене источника
        public static void ClearCache()
        {
            lock (L)
            {
                TagCache.Clear();
                Missing.Clear();
                _cacheHost = null;
                _dirty = false;
            }
        }

        public static async Task<byte[]> DownloadLimited(string url, int maxBytes)
        {
            var req = (HttpWebRequest)WebRequest.Create(new Uri(url));
            req.Method = "GET";

            using (var resp = await Task.Factory.FromAsync<WebResponse>(
                req.BeginGetResponse, req.EndGetResponse, null))
            {
                if (resp.ContentLength > maxBytes) { req.Abort(); return null; }

                using (var stream = resp.GetResponseStream())
                {
                    return await Task.Run<byte[]>(() =>
                    {
                        var ms = new MemoryStream();
                        var buf = new byte[16384];
                        int n;
                        while ((n = stream.Read(buf, 0, buf.Length)) > 0)
                        {
                            ms.Write(buf, 0, n);
                            if (ms.Length > maxBytes) return null;
                        }
                        return ms.ToArray();
                    });
                }
            }
        }

        public static async Task<long> GetSize(string url)
        {
            try
            {
                var req = (HttpWebRequest)WebRequestCreator.ClientHttp.Create(new Uri(url));
                req.Method = "HEAD";

                using (var resp = await Task.Factory.FromAsync<WebResponse>(
                    req.BeginGetResponse, req.EndGetResponse, null))
                {
                    return resp.ContentLength;
                }
            }
            catch
            {
                return -1;
            }
        }

        static string Host
        {
            get { return Sources.Current.Url; }
        }

        static string TagBase
        {
            get { return Host + "/index.php?page=dapi&s=tag&q=index"; }
        }

        static string Auth
        {
            get
            {
                Source s = Sources.Current;
                string r = "";
                if (!string.IsNullOrEmpty(s.ApiKey)) r += "&api_key=" + Uri.EscapeDataString(s.ApiKey);
                if (!string.IsNullOrEmpty(s.UserId)) r += "&user_id=" + Uri.EscapeDataString(s.UserId);
                return r;
            }
        }

        // ---------- HTTP (продолжения идут в фоновых потоках, главный поток свободен) ----------
        static async Task<string> GetCore(string url)
        {
            var req = (HttpWebRequest)WebRequest.Create(new Uri(url));
            req.Method = "GET";

            string body = null;
            string error = null;
            try
            {
                using (var resp = await Task.Factory.FromAsync<WebResponse>(
                    req.BeginGetResponse, req.EndGetResponse, null).ConfigureAwait(false))
                using (var sr = new StreamReader(resp.GetResponseStream()))
                {
                    body = sr.ReadToEnd();
                }
            }
            catch (WebException ex)
            {
                error = ex.Message;
                try
                {
                    if (ex.Response != null)
                        using (var sr = new StreamReader(ex.Response.GetResponseStream()))
                            error += " " + sr.ReadToEnd();
                }
                catch { }
            }

            if (body == null) throw new Exception(error ?? "нет ответа");
            return body;
        }

        // запрос с таймаутом, чтобы медленный ответ не вешал загрузку
        static async Task<string> GetAsync(string url, int timeoutMs = 20000)
        {
            Task<string> work = GetCore(url);
            Task first = await Task.WhenAny(work, Task.Delay(timeoutMs)).ConfigureAwait(false);
            if (first != work)
            {
                work.ContinueWith(t => { var ignored = t.Exception; });
                throw new Exception("Сайт не ответил вовремя.");
            }
            return await work.ConfigureAwait(false);
        }

        public static async Task<byte[]> Download(string url)
        {
            // сначала обычным способом, потом с заголовком Referer, как у браузера
            byte[] data = null;

            try
            {
                data = await DownloadCore(url, false);
            }
            catch
            {
                data = null;
            }

            if (data == null)
                data = await DownloadCore(url, true);

            return data;
        }

        static async Task<byte[]> DownloadCore(string url, bool withReferer)
        {
            HttpWebRequest req;
            if (withReferer)
            {
                req = (HttpWebRequest)WebRequestCreator.ClientHttp.Create(new Uri(url));
                req.Headers["Referer"] = Sources.Current.Url.Replace("//api.", "//") + "/";
                req.Headers["User-Agent"] = "Mozilla/5.0 (Windows Phone 8.1; BooruHub)";
            }
            else
            {
                req = (HttpWebRequest)WebRequest.Create(new Uri(url));
            }
            req.Method = "GET";

            using (var resp = await Task.Factory.FromAsync<WebResponse>(
                req.BeginGetResponse, req.EndGetResponse, null))
            using (var stream = resp.GetResponseStream())
            {
                return await Task.Run(() =>
                {
                    var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    return ms.ToArray();
                });
            }
        }

        // ---------- фильтры ----------
        static bool Blocked(string tags)
        {
            foreach (string tag in tags.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (tag.Contains("loli") || tag.Contains("shota")) return true;
                foreach (string b in Black)
                    if (tag == b) return true;
            }
            return false;
        }

        static bool IsAi(string tags)
        {
            foreach (string tag in tags.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (tag.StartsWith("ai_generated") || tag.StartsWith("ai_assisted")) return true;
                foreach (string a in AiTags)
                    if (tag == a) return true;
            }
            return false;
        }

        // ---------- адреса картинок ----------
        static string Abs(string u, string host)
        {
            if (string.IsNullOrEmpty(u)) return u;
            if (u.StartsWith("//")) return "https:" + u;
            if (u.StartsWith("/")) return host + u;
            return u;
        }

        static bool Truthy(string s)
        {
            return s == "1" || string.Equals(s, "true", StringComparison.OrdinalIgnoreCase);
        }

        // ---------- посты ----------
        public static async Task<PostPage> Posts(string tags, int page)
        {
            // всё, что зависит от настроек, читаем здесь, в главном потоке
            string host = Host;
            string auth = Auth;
            bool hideAi = Settings.HideAi;
            bool hq = Settings.HighQualityGrid;

            string url = host + "/index.php?page=dapi&s=post&q=index&json=1&limit=" + PageSize
                + "&pid=" + page
                + "&tags=" + Uri.EscapeDataString((tags ?? "").Trim()) + auth;

            string body = (await GetAsync(url)).Trim();
            if (body.Length == 0) return new PostPage();
            if (!body.StartsWith("["))
                throw new Exception("Ответ сайта: " + (body.Length > 200 ? body.Substring(0, 200) : body));

            // разбор в фоне
            return await Task.Run(() => ParsePosts(body, host, hideAi, hq));
        }

        static PostPage ParsePosts(string body, string host, bool hideAi, bool hq)
        {
            string site = host.Replace("//api.", "//");   // страница работы на основном сайте

            var result = new PostPage();
            JArray arr = JArray.Parse(body);
            result.Raw = arr.Count;

            foreach (JToken d in arr)
            {
                string tagStr = System.Net.HttpUtility.HtmlDecode(((string)d["tags"] ?? "").Trim());
                if (Blocked(tagStr)) continue;
                if (hideAi && IsAi(tagStr)) continue;

                string dir = (string)d["directory"];
                string image = (string)d["image"];
                string hash = (string)d["hash"];

                string file = Abs((string)d["file_url"], host);
                string sample = Abs((string)d["sample_url"], host);
                string preview = Abs((string)d["preview_url"], host);

                // формат Safebooru и похожих: адреса собираются из каталога и имени файла
                if (string.IsNullOrEmpty(file) && !string.IsNullOrEmpty(dir) && !string.IsNullOrEmpty(image))
                    file = host + "/images/" + dir + "/" + image;
                if (string.IsNullOrEmpty(sample) && Truthy((string)d["sample"]) && !string.IsNullOrEmpty(hash))
                    sample = host + "/samples/" + dir + "/sample_" + hash + ".jpg";
                if (string.IsNullOrEmpty(preview) && !string.IsNullOrEmpty(hash))
                    preview = host + "/thumbnails/" + dir + "/thumbnail_" + hash + ".jpg";

                if (string.IsNullOrEmpty(file) && string.IsNullOrEmpty(preview)) continue;

                string lower = (file ?? "").ToLowerInvariant();
                if (lower.EndsWith(".webm")) continue;   // телефон не воспроизводит

                string kind = null;
                if (lower.EndsWith(".gif")) kind = "gif";
                else if (lower.EndsWith(".mp4")) kind = "video";

                string best;
                string grid;
                if (kind != null)
                {
                    // для GIF и видео показываем неподвижный кадр
                    best = !string.IsNullOrEmpty(sample) ? sample : preview;
                    if (string.IsNullOrEmpty(best)) continue;
                    grid = !string.IsNullOrEmpty(preview) ? preview : best;
                }
                else
                {
                    best = !string.IsNullOrEmpty(sample) ? sample : (file ?? preview);
                    grid = !hq ? (preview ?? best) : best;   // по умолчанию лёгкие превью
                }

                string date = "";
                long secs;
                if (long.TryParse((string)d["change"], out secs))
                {
                    date = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                        .AddSeconds(secs).ToLocalTime().ToString("dd MMM yyyy HH:mm");
                }

                string id = (string)d["id"];
                result.Items.Add(new ArtItem
                {
                    Id = id,
                    Title = "#" + id,
                    Author = (string)d["owner"],
                    Rating = (string)d["rating"],
                    Score = (string)d["score"],
                    Size = (string)d["width"] + "×" + (string)d["height"],
                    Date = date,
                    Tags = tagStr,
                    SourceUrl = host,
                    ImageUrl = grid,
                    SampleUrl = best,
                    FullImageUrl = file ?? best,
                    Kind = kind,
                    Url = site + "/index.php?page=post&s=view&id=" + id
                });
            }
            return result;
        }

        // ---------- кэш типов тегов (хранится на телефоне) ----------
        static string CacheFile(string host)
        {
            return "tags_" + Regex.Replace(host, "[^a-zA-Z0-9]", "_") + ".txt";
        }

        // читает файл вне замка, подставляет данные под замком
        static void EnsureCache(string host)
        {
            lock (L)
            {
                if (_cacheHost == host) return;
            }

            var loaded = new Dictionary<string, TagInfo>();
            try
            {
                using (var store = IsolatedStorageFile.GetUserStoreForApplication())
                {
                    string f = CacheFile(host);
                    if (store.FileExists(f))
                    {
                        using (var s = store.OpenFile(f, FileMode.Open, FileAccess.Read))
                        using (var r = new StreamReader(s))
                        {
                            string line;
                            while ((line = r.ReadLine()) != null)
                            {
                                string[] p = line.Split('\t');
                                if (p.Length != 3) continue;
                                int type, count;
                                int.TryParse(p[1], out type);
                                int.TryParse(p[2], out count);
                                loaded[p[0]] = new TagInfo { Type = type, Count = count };
                            }
                        }
                    }
                }
            }
            catch { }

            lock (L)
            {
                if (_cacheHost == host) return;
                TagCache.Clear();
                Missing.Clear();
                foreach (KeyValuePair<string, TagInfo> kv in loaded) TagCache[kv.Key] = kv.Value;
                _cacheHost = host;
                _dirty = false;
            }
        }

        static void SaveCache(string host)
        {
            List<KeyValuePair<string, TagInfo>> snapshot;
            lock (L)
            {
                if (!_dirty || _cacheHost != host) return;
                _dirty = false;
                snapshot = TagCache.ToList();
            }

            try
            {
                using (var store = IsolatedStorageFile.GetUserStoreForApplication())
                using (var s = store.OpenFile(CacheFile(host), FileMode.Create, FileAccess.Write))
                using (var w = new StreamWriter(s))
                {
                    foreach (KeyValuePair<string, TagInfo> kv in snapshot)
                        w.WriteLine(kv.Key + "\t" + kv.Value.Type + "\t" + kv.Value.Count);
                }
            }
            catch { }
        }

        static void Put(string host, string name, int type, int count)
        {
            lock (L)
            {
                if (_cacheHost != host) return;   // за это время сменили источник
                TagCache[name] = new TagInfo { Type = type, Count = count };
                _dirty = true;
            }
        }

        static bool IsBad(string key)
        {
            lock (L) return Bad.Contains(key);
        }

        static void SetBad(string key)
        {
            lock (L) Bad.Add(key);
        }

        static List<string> Pending(List<string> items)
        {
            lock (L) return items.Where(t => !TagCache.ContainsKey(t)).ToList();
        }

        // ---------- типы тегов ----------
        public static bool HasInfo(string tag)
        {
            EnsureCache(Host);
            lock (L) return TagCache.ContainsKey(tag);
        }

        public static TagInfo Info(string tag)
        {
            EnsureCache(Host);
            lock (L)
            {
                TagInfo i;
                return TagCache.TryGetValue(tag, out i) ? i : new TagInfo();
            }
        }

        static async Task<int> FetchTagXml(string host, string url)
        {
            int found = 0;
            try
            {
                XDocument doc = XDocument.Parse(await GetAsync(url, 7000).ConfigureAwait(false));
                foreach (XElement e in doc.Descendants("tag"))
                {
                    string name = (string)e.Attribute("name");
                    int type, count;
                    int.TryParse((string)e.Attribute("type"), out type);
                    int.TryParse((string)e.Attribute("count"), out count);
                    if (name == null) continue;
                    Put(host, name, type, count);
                    found++;
                }
            }
            catch { }
            return found;
        }

        static int TypeFromName(string t)
        {
            switch ((t ?? "").ToLowerInvariant())
            {
                case "artist": return 1;
                case "copyright": return 3;
                case "character": return 4;
                case "metadata":
                case "meta": return 5;
                default: return 0;
            }
        }

        static async Task<int> FetchViaAutocomplete(string host, string tag)
        {
            try
            {
                string body = (await GetAsync(
                    host + "/autocomplete.php?q=" + Uri.EscapeDataString(tag), 7000).ConfigureAwait(false)).Trim();
                if (!body.StartsWith("[")) return 0;

                foreach (JToken d in JArray.Parse(body))
                {
                    if ((string)d["value"] != tag) continue;

                    int count = 0;
                    string label = (string)d["label"] ?? "";
                    int a = label.LastIndexOf('(');
                    int b = label.LastIndexOf(')');
                    if (a >= 0 && b > a) int.TryParse(label.Substring(a + 1, b - a - 1), out count);

                    Put(host, tag, TypeFromName((string)d["type"]), count);
                    return 1;
                }
            }
            catch { }
            return 0;
        }

        // по 6 запросов параллельно; если первая пачка ничего не дала, способ помечается нерабочим
        static async Task RunEach(List<string> items, Func<string, Task<int>> job, string badKey)
        {
            int total = 0;
            for (int i = 0; i < items.Count; i += 6)
            {
                Task<int>[] batch = items.Skip(i).Take(6).Select(job).ToArray();
                int[] counts = await Task.WhenAll(batch).ConfigureAwait(false);
                total += counts.Sum();

                if (i == 0 && total == 0)
                {
                    SetBad(badKey);
                    return;
                }
            }
        }

        // весь процесс идёт в фоновом потоке, главный поток не блокируется
        public static Task LoadTags(IEnumerable<string> tags)
        {
            string host = Host;
            string auth = Auth;
            List<string> list = tags.ToList();
            return Task.Run(() => LoadTagsCore(host, auth, list));
        }

        static async Task LoadTagsCore(string host, string auth, List<string> all)
        {
            EnsureCache(host);
            string tagBase = host + "/index.php?page=dapi&s=tag&q=index";

            List<string> need;
            lock (L)
            {
                need = all.Where(t => !TagCache.ContainsKey(t) && !Missing.Contains(t))
                          .Distinct().ToList();
            }
            if (need.Count == 0) return;

            // 1) пачками
            if (!IsBad(host + "|batch"))
            {
                var jobs = new List<Task<int>>();
                for (int i = 0; i < need.Count; i += 40)
                {
                    List<string> chunk = need.GetRange(i, Math.Min(40, need.Count - i));
                    jobs.Add(FetchTagXml(host, tagBase + "&limit=100&names="
                        + Uri.EscapeDataString(string.Join(" ", chunk.ToArray())) + auth));
                }

                int[] counts = await Task.WhenAll(jobs.ToArray()).ConfigureAwait(false);
                if (counts.Sum() == 0) SetBad(host + "|batch");
            }

            // 2) оставшиеся по одному
            need = Pending(need);
            if (need.Count > 0 && !IsBad(host + "|single"))
            {
                await RunEach(need,
                    t => FetchTagXml(host, tagBase + "&limit=5&name=" + Uri.EscapeDataString(t) + auth),
                    host + "|single").ConfigureAwait(false);
            }

            // 3) автодополнение, в нём тоже есть тип тега
            need = Pending(need);
            if (need.Count > 0 && !IsBad(host + "|auto"))
            {
                await RunEach(need, t => FetchViaAutocomplete(host, t), host + "|auto").ConfigureAwait(false);
            }

            // что не нашлось, больше не спрашиваем
            need = Pending(need);
            lock (L)
            {
                if (_cacheHost == host)
                    foreach (string t in need) Missing.Add(t);
            }
            SaveCache(host);
        }

        // ---------- подсказки при поиске ----------
        public static async Task<List<TagSuggestion>> Suggest(string term)
        {
            string host = Host;
            string tagBase = TagBase;
            string auth = Auth;
            var list = new List<TagSuggestion>();

            try
            {
                string body = (await GetAsync(
                    host + "/autocomplete.php?q=" + Uri.EscapeDataString(term), 6000)).Trim();
                if (body.StartsWith("["))
                {
                    foreach (JToken d in JArray.Parse(body))
                    {
                        string v = (string)d["value"];
                        if (string.IsNullOrEmpty(v)) continue;
                        list.Add(new TagSuggestion { Value = v, Label = (string)d["label"] ?? v });
                    }
                }
            }
            catch { }

            if (list.Count > 0) return list;

            try
            {
                string xml = await GetAsync(tagBase + "&limit=15&name_pattern="
                    + Uri.EscapeDataString(term + "%") + auth, 6000);
                foreach (XElement e in XDocument.Parse(xml).Descendants("tag"))
                {
                    string name = (string)e.Attribute("name");
                    int count;
                    int.TryParse((string)e.Attribute("count"), out count);
                    if (name == null) continue;
                    list.Add(new TagSuggestion { Value = name, Label = name + " (" + count + ")", Count = count });
                }
                list.Sort((a, b) => b.Count.CompareTo(a.Count));
            }
            catch { }

            return list;
        }
    }
}