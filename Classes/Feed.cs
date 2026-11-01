using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace ArtHub
{
    // Лента с постраничным переключением
    public class Feed
    {
        public readonly ObservableCollection<ArtItem> Items = new ObservableCollection<ArtItem>();
        public string Query { get; private set; }
        public int Page { get; private set; }      // 1, 2, 3...; 0 = ещё ничего не открывали
        public bool HasNext { get; private set; }
        public bool IsLoading { get; private set; }
        public ArtItem Anchor;                     // последняя открытая работа

        public event Action Changed;
        public event Action PageLoaded;
        public event Action<string> Error;

        const int Chunk = 8;
        int _gen;

        public Feed(string query)
        {
            Query = query ?? "";
        }

        public void Open(string query, int page)
        {
            _gen++;
            Query = query ?? "";
            Page = Math.Max(1, page);
            Items.Clear();
            Anchor = null;
            HasNext = false;
            IsLoading = true;
            Notify();
            Load(0, _gen);
        }

        public void Reload()
        {
            Open(Query, Page);
        }

        static bool ValidUrl(string u)
        {
            Uri uri;
            return !string.IsNullOrEmpty(u) && Uri.TryCreate(u, UriKind.Absolute, out uri)
                && (uri.Scheme == "http" || uri.Scheme == "https");
        }

        async void Load(int attempt, int gen)
        {
            try
            {
                PostPage page = null;
                string err = null;
                try { page = await Api.Posts(Query, Page - 1); }
                catch (Exception ex) { err = ex.Message; }

                if (gen != _gen) return;

                if (err != null)
                {
                    // тихие повторы
                    if (attempt < 2)
                    {
                        await Task.Delay(1500 * (attempt + 1));
                        if (gen != _gen) return;
                        Load(attempt + 1, gen);
                        return;
                    }

                    IsLoading = false;
                    HasNext = true;   // чтобы можно было перейти на другую страницу
                    Notify();
                    var h = Error;
                    if (h != null) h(err);
                    var pl = PageLoaded;
                    if (pl != null) pl();
                    return;
                }

                HasNext = page.Raw >= Api.PageSize;

                // добавляем порциями, чтобы сетка успевала перерисовываться
                int inChunk = 0;
                foreach (ArtItem it in page.Items)
                {
                    if (!ValidUrl(it.ImageUrl)) continue;

                    Items.Add(it);
                    inChunk++;

                    if (inChunk >= Chunk)
                    {
                        inChunk = 0;
                        await Task.Delay(30);
                        if (gen != _gen) return;
                    }
                }

                IsLoading = false;
                Notify();
                var done = PageLoaded;
                if (done != null) done();
            }
            catch
            {
                if (gen != _gen) return;
                IsLoading = false;
                HasNext = true;
                Notify();
                var pl = PageLoaded;
                if (pl != null) pl();
            }
        }

        void Notify()
        {
            var h = Changed;
            if (h != null) h();
        }
    }
}