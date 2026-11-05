using System.IO.IsolatedStorage;

namespace ArtHub
{
    public static class Settings
    {
        // растёт при каждом изменении, по нему главная страница перезагружает ленту
        public static int Version;

        static IsolatedStorageSettings S { get { return IsolatedStorageSettings.ApplicationSettings; } }

        static bool GetBool(string key, bool def)
        {
            bool v;
            return S.TryGetValue(key, out v) ? v : def;
        }

        public static bool HideAi
        {
            get { return GetBool("hideAi", true); }
            set { S["hideAi"] = value; S.Save(); Version++; }
        }

        public static bool HighQualityGrid
        {
            get { return GetBool("hqGrid", false); }
            set { S["hqGrid"] = value; S.Save(); Version++; }
        }

        // false = Green (плитки с фоном), true = System (прозрачные плитки)
        public static bool TransparentTile
        {
            get { return GetBool("tileTransparent", false); }
            set { S["tileTransparent"] = value; S.Save(); }
        }

        // число колонок в сетке: 2..4
        public static int GridColumns
        {
            get
            {
                int v;
                if (!S.TryGetValue("gridCols", out v)) return 3;
                return v < 2 ? 2 : (v > 4 ? 4 : v);
            }
            set { S["gridCols"] = value; S.Save(); }
        }

        // ---------- сессия ----------
        public static string LastQuery
        {
            get { string v; return S.TryGetValue("lastQuery", out v) ? v : ""; }
        }

        public static int LastPage
        {
            get { int v; return S.TryGetValue("lastPage", out v) && v >= 1 ? v : 1; }
        }

        public static void SaveSession(string query, int page)
        {
            S["lastQuery"] = query ?? "";
            S["lastPage"] = page;
            S.Save();
        }

        public static void ClearSession()
        {
            S.Remove("lastQuery");
            S.Remove("lastPage");
            S.Save();
        }
    }
}