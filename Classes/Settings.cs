using System.IO.IsolatedStorage;

namespace ArtHub
{
    public static class Settings
    {
        // растёт при каждом изменении, по нему главная страница перезагружает ленту
        public static int Version;

        static IsolatedStorageSettings S { get { return IsolatedStorageSettings.ApplicationSettings; } }

        public static bool HideAi
        {
            get
            {
                bool v;
                return S.TryGetValue("hideAi", out v) ? v : true;
            }
            set
            {
                S["hideAi"] = value;
                S.Save();
                Version++;
            }
        }

        // прозрачная плитка (версия ленты не меняется)
        public static bool TransparentTile
        {
            get
            {
                bool v;
                return S.TryGetValue("tileTransparent", out v) ? v : false;
            }
            set
            {
                S["tileTransparent"] = value;
                S.Save();
            }
        }

        // крупные картинки в сетке: красивее, но заметно медленнее
        public static bool HighQualityGrid
        {
            get
            {
                bool v;
                return S.TryGetValue("hqGrid", out v) ? v : false;
            }
            set
            {
                S["hqGrid"] = value;
                S.Save();
                Version++;
            }
        }
    }
}