using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace ArtHub
{
    // Загружает картинку, сразу уменьшая её при декодировании (экономит память)
    public class ImageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                string url = value as string;
                Uri uri;
                if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out uri))
                    return null;

                int width = 0;
                if (parameter != null) int.TryParse(parameter.ToString(), out width);

                var bmp = new BitmapImage();
                bmp.CreateOptions = BitmapCreateOptions.BackgroundCreation;
                if (width > 0) bmp.DecodePixelWidth = width;
                bmp.UriSource = uri;
                return bmp;
            }
            catch
            {
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}