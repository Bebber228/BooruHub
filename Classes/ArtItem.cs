using System.Windows;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace ArtHub
{
    public class ArtItem
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Author { get; set; }
        public string Rating { get; set; }
        public string Score { get; set; }
        public string Size { get; set; }
        public string Date { get; set; }
        public string Tags { get; set; }
        public string SourceUrl { get; set; }      // откуда пост, нужно для избранного
        public string ImageUrl { get; set; }       // для сетки
        public string SampleUrl { get; set; }      // средний размер
        public string FullImageUrl { get; set; }   // оригинал
        public string Url { get; set; }
        public string Kind { get; set; }           // null = картинка, "gif", "video"

        // синяя рамка в ленте у GIF и видео
        [JsonIgnore]
        public Visibility MediaVisible
        {
            get { return string.IsNullOrEmpty(Kind) ? Visibility.Collapsed : Visibility.Visible; }
        }
    }

    public class TagSuggestion
    {
        public string Value { get; set; }
        public string Label { get; set; }
        public int Count { get; set; }
    }

    public class TagInfo
    {
        public int Type { get; set; }   // 0 general, 1 artist, 3 copyright, 4 character, 5 metadata
        public int Count { get; set; }
    }

    public static class Nav
    {
        public static ArtItem Selected;
        public static string Query;               // тег, по которому надо искать при возврате на главную
        public static IList<ArtItem> List;        // список, из которого открыт пост
        public static int Index;                  // позиция поста в списке
    }
}