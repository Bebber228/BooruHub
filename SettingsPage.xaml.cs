using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Xml.Linq;
using Microsoft.Phone.Controls;
using Microsoft.Phone.Shell;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;

namespace ArtHub
{
    public partial class SettingsPage : PhoneApplicationPage
    {
        private bool _init;

        // пасхалка
        private static SoundEffect _boom;
        private int _logoTaps;
        private DateTime _lastTap;

        public SettingsPage()
        {
            InitializeComponent();
            AboutTitle.Text = AppTitleAndVersion();
        }

        // название и версия из WMAppManifest.xml: меняются в одном месте, в свойствах проекта
        private static string AppTitleAndVersion()
        {
            string title = "BooruHub";
            string version = "";
            try
            {
                XElement app = XDocument.Load("WMAppManifest.xml").Root.Element("App");
                title = (string)app.Attribute("Title") ?? title;
                version = (string)app.Attribute("Version") ?? "";
            }
            catch { }

            return version.Length > 0 ? title + " " + version : title;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            SourceName.Text = Sources.Current.Name + "  •  " + Sources.Current.Url;

            int count = Settings.Blacklist.Count;
            BlacklistInfo.Text = (count == 0 ? "no tags" : count + (count == 1 ? " tag" : " tags"))
                + "  •  " + (Settings.UseBlacklist ? "on" : "off");

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

        private void Blacklist_Tap(object sender, System.Windows.Input.GestureEventArgs e)
        {
            NavigationService.Navigate(new Uri("/BlacklistPage.xaml", UriKind.Relative));
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

        // ---------- пасхалка: 5 нажатий на логотип подряд ----------
        private void AboutLogo_Tap(object sender, System.Windows.Input.GestureEventArgs e)
        {
            // если между нажатиями прошло больше 1,5 секунды, счёт начинается заново
            DateTime now = DateTime.Now;
            if ((now - _lastTap).TotalSeconds > 1.5) _logoTaps = 0;
            _lastTap = now;

            if (++_logoTaps < 5) return;
            _logoTaps = 0;

            ShowEasterEgg();
        }

        private const string EggImage = "Assets/dezhnuts.jpeg";

        private void ShowEasterEgg()
        {
            // картинку читаем из файла сразу, иначе во всплывающем окне она не успевает показаться
            var bmp = new BitmapImage();
            var res = Application.GetResourceStream(new Uri("Assets/dezhnuts.jpeg", UriKind.Relative));
            if (res != null) bmp.SetSource(res.Stream);

            var picture = new Image
            {
                Source = bmp,
                Width = 300,
                Height = 300,
                Stretch = System.Windows.Media.Stretch.Uniform,
                Margin = new Thickness(0, 12, 0, 0)
            };

            var box = new CustomMessageBox
            {
                Caption = "Deezhnuts",
                Content = picture,
                LeftButtonContent = "ok"
            };

            // панель с часами на время окна прячем: иначе между ней и окном видна полоска шапки
            SystemTray.SetIsVisible(this, false);
            box.Dismissed += (s, e) => SystemTray.SetIsVisible(this, true);
            box.Show();

            PlayBoom();
        }

        // SoundEffect не останавливает музыку, которая играет на телефоне
        private static void PlayBoom()
        {
            try
            {
                if (_boom == null)
                {
                    var res = Application.GetResourceStream(new Uri("Assets/vine_boom.wav", UriKind.Relative));
                    _boom = SoundEffect.FromStream(res.Stream);
                }

                FrameworkDispatcher.Update();   // без этого XNA-звук в Silverlight не играет
                _boom.Play();
            }
            catch { }
        }
    }
}