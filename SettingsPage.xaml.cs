using System;
using System.Windows;
using System.Windows.Navigation;
using Microsoft.Phone.Controls;

namespace ArtHub
{
    public partial class SettingsPage : PhoneApplicationPage
    {
        private bool _init;

        public SettingsPage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            SourceName.Text = Sources.Current.Name + "  •  " + Sources.Current.Url;

            // чтобы начальная установка не считалась действием пользователя
            _init = true;
            HideAiSwitch.IsChecked = Settings.HideAi;
            HqSwitch.IsChecked = Settings.HighQualityGrid;
            TileSwitch.IsChecked = Settings.TransparentTile;
            _init = false;
        }

        private void Sources_Tap(object sender, System.Windows.Input.GestureEventArgs e)
        {
            NavigationService.Navigate(new Uri("/SourcesPage.xaml", UriKind.Relative));
        }

        private void Switch_Changed(object sender, RoutedEventArgs e)
        {
            if (_init) return;

            bool ai = HideAiSwitch.IsChecked == true;
            bool hq = HqSwitch.IsChecked == true;
            bool tile = TileSwitch.IsChecked == true;

            if (ai != Settings.HideAi) Settings.HideAi = ai;
            if (hq != Settings.HighQualityGrid) Settings.HighQualityGrid = hq;

            if (tile != Settings.TransparentTile)
            {
                Settings.TransparentTile = tile;
                Tiles.Apply(tile);
            }
        }
    }
}