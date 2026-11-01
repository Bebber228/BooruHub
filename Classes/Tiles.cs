using System;
using System.Linq;
using Microsoft.Phone.Shell;

namespace ArtHub
{
    public static class Tiles
    {
        // папка с плитками внутри проекта
        const string Folder = "/Assets/Tiles/";

        public static void Apply(bool transparent)
        {
            try
            {
                string sfx = transparent ? "_nocolor" : "";

                var data = new FlipTileData
                {
                    SmallBackgroundImage = new Uri(Folder + "small" + sfx + ".png", UriKind.Relative),
                    BackgroundImage = new Uri(Folder + "orig" + sfx + ".png", UriKind.Relative),
                    WideBackgroundImage = new Uri(Folder + "wide" + sfx + ".png", UriKind.Relative)
                };

                ShellTile tile = ShellTile.ActiveTiles.FirstOrDefault();
                if (tile != null) tile.Update(data);
            }
            catch { }
        }
    }
}