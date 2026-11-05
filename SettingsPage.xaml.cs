using System;
using System.Windows;
using System.Windows.Controls;
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

            TilePicker.ItemsSource = new[] { "Green", "System" };
            TilePicker.SelectedIndex = Settings.TransparentTile ? 1 : 0;

            GridPicker.ItemsSource = new[] { "2 columns", "3 columns", "4 columns" };
            GridPicker.SelectedIndex = Settings.GridColumns - 2;

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

            if (ai != Settings.HideAi) Settings.HideAi = ai;
            if (hq != Settings.HighQualityGrid) Settings.HighQualityGrid = hq;
        }

        private void Tile_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_init || TilePicker.SelectedIndex < 0) return;

            bool system = TilePicker.SelectedIndex == 1;
            if (system != Settings.TransparentTile)
            {
                Settings.TransparentTile = system;
                Tiles.Apply(system);
            }
        }

        private void Grid_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_init || GridPicker.SelectedIndex < 0) return;
            Settings.GridColumns = GridPicker.SelectedIndex + 2;
        }
    }
}