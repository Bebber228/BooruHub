using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using Microsoft.Phone.Net.NetworkInformation;
using System.Windows.Threading;
using Microsoft.Phone.Controls;
using Microsoft.Phone.Shell;

namespace ArtHub
{
    public partial class MainPage : PhoneApplicationPage
    {
        static readonly Feed Posts = new Feed("");
        static int _seenVersion = -1;
        static int _seenAi = -1;
        static bool _trayShown;      // верхняя панель показана после первого запуска
        static double _cell = 150;   // размер плитки
        static double _postsOffset;  // положение прокрутки при уходе на пост
        static double _favOffset;

        // история: предыдущие запросы и страницы, как в браузере
        static readonly List<KeyValuePair<string, int>> History = new List<KeyValuePair<string, int>>();

        private readonly ProgressIndicator _progress =
            new ProgressIndicator { IsIndeterminate = true, IsVisible = false };
        private readonly DispatcherTimer _timer =
            new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        private string _skipText;
        private bool _scrollTop;
        private bool _netError;
        private bool _lostShown;

        private readonly DispatcherTimer _lostTimer =
            new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };

        public MainPage()
        {
            InitializeComponent();
            SystemTray.SetProgressIndicator(this, _progress);

            // при запуске верхняя панель скрыта, появляется после загрузки страницы
            if (!_trayShown) SystemTray.SetIsVisible(this, false);

            ApplyGrid();

            Favorites.Load();
            PostsGrid.ItemsSource = Posts.Items;
            FavGrid.ItemsSource = Favorites.Items;

            _skipText = Posts.Query.Length > 0 ? Posts.Query + " " : "";
            SearchBox.Text = _skipText;
            _timer.Tick += Timer_Tick;

            _lostTimer.Tick += (s, e) =>
            {
                _lostTimer.Stop();
                _netError = false;
                _lostShown = false;
                UpdateTray();
            };

            Loaded += MainPage_Loaded;
        }

        // размер плиток по настройке: от 2 до 4 колонок, сетка по центру без полосы справа
        private void ApplyGrid()
        {
            double w = Application.Current.Host.Content.ActualWidth;
            int cols = Settings.GridColumns;
            _cell = Math.Floor((w - 12) / cols);

            PostsGrid.Width = _cell * cols;
            FavGrid.Width = _cell * cols;
        }

        private void Tile_Loaded(object sender, RoutedEventArgs e)
        {
            var g = (Grid)sender;
            g.Width = _cell;
            g.Height = _cell;
        }

        private void Tile_Tap(object sender, System.Windows.Input.GestureEventArgs e)
        {
            var item = ((FrameworkElement)sender).DataContext as ArtItem;
            if (item == null) return;

            _postsOffset = PostsScroll.VerticalOffset;
            _favOffset = FavScroll.VerticalOffset;

            // из какого списка открыли пост, по нему и будет работать свайп
            IList<ArtItem> list = MainPivot.SelectedItem == FavItem
                ? (IList<ArtItem>)Favorites.Items
                : (IList<ArtItem>)Posts.Items;

            Nav.List = list;
            Nav.Index = list.IndexOf(item);
            Nav.Selected = item;
            NavigationService.Navigate(new Uri("/DetailPage.xaml", UriKind.Relative));
        }

        private void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            _trayShown = true;
            SystemTray.SetIsVisible(this, true);

            UpdateBusy();
            UpdateFavEmpty();

            // возвращаемся к месту в ленте, где открывали пост
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    PostsScroll.ScrollToVerticalOffset(_postsOffset);
                    FavScroll.ScrollToVerticalOffset(_favOffset);
                }
                catch { }
            });
        }

        // ---------- жизненный цикл ----------
        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            ApplyGrid();

            Posts.Changed += UpdateBusy;
            Posts.PageLoaded += OnPageLoaded;
            Posts.Error += ShowError;
            DeviceNetworkInformation.NetworkAvailabilityChanged += Network_Changed;
            Favorites.Items.CollectionChanged += Fav_Changed;

            SourceText.Text = Sources.Current.Name;
            UpdateBlacklistItem();

            bool sourcesChanged = _seenVersion != Sources.Version;
            bool aiChanged = _seenAi != Settings.Version;
            _seenVersion = Sources.Version;
            _seenAi = Settings.Version;

            if (sourcesChanged)
            {
                History.Clear();
                Api.ClearCache();

                _skipText = "";
                SearchBox.Text = "";
                MainPivot.SelectedItem = PostsItem;
                Nav.Query = null;

                _postsOffset = 0;
                _scrollTop = true;
                Posts.Open("", 1);
            }
            else if (aiChanged)
            {
                _postsOffset = 0;
                _scrollTop = true;
                Posts.Reload();
            }

            if (Nav.Query != null)
            {
                string q = Nav.Query;
                Nav.Query = null;
                StartSearch(q);
            }
            else if (Posts.Page == 0)
            {
                // новый запуск или восстановление после сворачивания
                string q0 = Settings.LastQuery;
                _skipText = q0.Length > 0 ? q0 + " " : "";
                SearchBox.Text = _skipText;

                _scrollTop = true;
                Posts.Open(q0, Settings.LastPage);
            }

            BuildPager();
            UpdateBusy();
            UpdateFavEmpty();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            Posts.Changed -= UpdateBusy;
            Posts.PageLoaded -= OnPageLoaded;
            Posts.Error -= ShowError;
            DeviceNetworkInformation.NetworkAvailabilityChanged -= Network_Changed;
            Favorites.Items.CollectionChanged -= Fav_Changed;
            _progress.IsVisible = false;

            base.OnNavigatedFrom(e);
        }

        // кнопка «Назад»: вкладка posts, потом история запросов, потом выход
        protected override void OnBackKeyPress(CancelEventArgs e)
        {
            if (MainPivot.SelectedItem != PostsItem)
            {
                e.Cancel = true;
                MainPivot.SelectedItem = PostsItem;
                return;
            }

            if (History.Count > 0)
            {
                e.Cancel = true;

                var prev = History[History.Count - 1];
                History.RemoveAt(History.Count - 1);

                _skipText = prev.Key.Length > 0 ? prev.Key + " " : "";
                SearchBox.Text = _skipText;

                _scrollTop = true;
                Posts.Open(prev.Key, prev.Value);
                return;
            }

            if (MessageBox.Show("Exit the app?", "exit", MessageBoxButton.OKCancel) != MessageBoxResult.OK)
            {
                e.Cancel = true;
                return;
            }

            // выход подтверждён: сессию не сохраняем
            Settings.ClearSession();
        }

        // запоминаем текущее состояние, прежде чем уйти на другой запрос или страницу
        private void PushHistory(string newQuery, int newPage)
        {
            if (Posts.Page < 1) return;
            if (Posts.Query == newQuery && Posts.Page == newPage) return;

            History.Add(new KeyValuePair<string, int>(Posts.Query, Posts.Page));
            if (History.Count > 50) History.RemoveAt(0);
        }

        // ---------- загрузка, сообщения и пустое избранное ----------
        private void UpdateBusy()
        {
            bool loading = Posts.IsLoading;
            if (loading) _netError = false;

            bool opened = !loading && Posts.Page >= 1;
            bool lost = opened && Posts.Failed;
            bool empty = opened && !lost && Posts.Items.Count == 0;

            // нет связи: сетку и страницы прячем, показываем сообщение
            LostPanel.Visibility = lost ? Visibility.Visible : Visibility.Collapsed;
            PostsGrid.Visibility = lost ? Visibility.Collapsed : Visibility.Visible;

            // пусто: если сайт посты вернул, значит их скрыли фильтры или блэклист
            EmptyPanel.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            if (empty)
            {
                EmptyText.Text = Posts.RawCount > 0
                    ? "All posts on this page are hidden by the blacklist or filters"
                    : (Settings.UseBlacklist && Settings.Blacklist.Count > 0
                        ? "Nothing found. The blacklist is on and may hide some posts"
                        : "Nothing found");
            }

            // страницы остаются, если есть куда листать
            bool pager = opened && !lost && (!empty || Posts.HasNext || Posts.Page > 1);
            PagerRoot.Visibility = pager ? Visibility.Visible : Visibility.Collapsed;
            PlacePager(empty);

            UpdateTray();
        }

        // пустая страница: переключатель внизу вкладки, под сообщением;
        // обычная: в ленте после сетки
        private void PlacePager(bool bottom)
        {
            Panel target = bottom ? (Panel)PostsRoot : PostsStack;
            var parent = PagerRoot.Parent as Panel;
            if (parent == target) return;

            if (parent != null) parent.Children.Remove(PagerRoot);
            PagerRoot.VerticalAlignment = bottom ? VerticalAlignment.Bottom : VerticalAlignment.Top;
            target.Children.Add(PagerRoot);   // в ленте он последний, в Grid — поверх всего
        }

        // тап по сообщению «нет связи»: попробовать ещё раз
        private void Lost_Tap(object sender, System.Windows.Input.GestureEventArgs e)
        {
            if (Posts.IsLoading) return;
            _netError = false;
            _lostShown = false;
            Posts.Reload();
        }

        private void UpdateTray()
        {
            bool lost = !DeviceNetworkInformation.IsNetworkAvailable || _netError;

            if (lost && !_lostShown)
            {
                // показываем сообщение один раз на 3 секунды
                _lostShown = true;
                _lostTimer.Stop();
                _lostTimer.Start();
            }

            if (lost && _lostTimer.IsEnabled)
            {
                _progress.IsIndeterminate = false;
                _progress.Text = "connection lost";
                _progress.IsVisible = true;
            }
            else if (Posts.IsLoading)
            {
                _progress.IsIndeterminate = true;
                _progress.Text = "loading posts...";
                _progress.IsVisible = true;
            }
            else
            {
                _progress.IsVisible = false;
            }
        }

        private void Fav_Changed(object sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateFavEmpty();
        }

        private void UpdateFavEmpty()
        {
            FavEmpty.Visibility = Favorites.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnPageLoaded()
        {
            Settings.SaveSession(Posts.Query, Posts.Page);

            BuildPager();
            UpdateBusy();

            if (_scrollTop)
            {
                _scrollTop = false;
                Dispatcher.BeginInvoke(() =>
                {
                    try { PostsScroll.ScrollToVerticalOffset(0); }
                    catch { }
                });
            }
        }

        private void ShowError(string message)
        {
            _netError = true;
            UpdateTray();
        }

        private void Network_Changed(object sender, NetworkNotificationEventArgs e)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (DeviceNetworkInformation.IsNetworkAvailable) _lostShown = false;
                bool online = DeviceNetworkInformation.IsNetworkAvailable;

                if (online && _netError && !Posts.IsLoading)
                {
                    _netError = false;
                    Posts.Reload();
                }
                UpdateTray();
            });
        }

        // ---------- переключатель страниц ----------
        private void GoPage(int page)
        {
            if (page < 1 || Posts.IsLoading) return;

            Focus();   // убирает клавиатуру
            MainPivot.SelectedItem = PostsItem;

            PushHistory(Posts.Query, page);
            _scrollTop = true;
            Posts.Open(Posts.Query, page);
        }

        // кнопка в стиле Windows: рамка, при нажатии заливается
        private UIElement PagerButton(string text, bool current, bool enabled, int target)
        {
            var dark = (Brush)Application.Current.Resources["TextDark"];
            var header = (Brush)Application.Current.Resources["HeaderBg"];
            var white = new SolidColorBrush(Colors.White);

            var label = new TextBlock
            {
                Text = text,
                FontSize = 24,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = current ? white : dark
            };

            var box = new Border
            {
                Child = label,
                Height = 56,
                Margin = new Thickness(2),
                BorderThickness = new Thickness(2),
                BorderBrush = current ? header : dark,
                Background = current ? header : new SolidColorBrush(Colors.Transparent),
                Opacity = enabled ? 1.0 : 0.35
            };

            if (enabled && !current)
            {
                box.MouseLeftButtonDown += (s, e) =>
                {
                    box.Background = dark;
                    label.Foreground = white;
                };
                Action restore = () =>
                {
                    box.Background = new SolidColorBrush(Colors.Transparent);
                    label.Foreground = dark;
                };
                box.MouseLeftButtonUp += (s, e) => restore();
                box.MouseLeave += (s, e) => restore();

                int t = target;
                box.Tap += (s, e) => GoPage(t);
            }

            return box;
        }

        private static void ResetGrid(Grid g, int columns)
        {
            g.Children.Clear();
            g.ColumnDefinitions.Clear();
            for (int i = 0; i < columns; i++)
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        private void BuildPager()
        {
            int cur = Posts.Page;
            ResetGrid(PagerGrid, 5);
            ResetGrid(NavGrid, 4);
            if (cur < 1) return;

            // номера: 2 назад, текущая, 2 вперёд
            int start = Math.Max(1, cur - 2);
            int end = Posts.HasNext ? start + 4 : cur;
            for (int p = start; p <= end; p++)
            {
                UIElement b = PagerButton(p.ToString(), p == cur, true, p);
                Grid.SetColumn((FrameworkElement)b, p - start);
                PagerGrid.Children.Add(b);
            }

            // << < > >>
            UIElement[] nav =
            {
                PagerButton("<<", false, cur > 1, Math.Max(1, cur - 10)),
                PagerButton("<", false, cur > 1, cur - 1),
                PagerButton(">", false, Posts.HasNext, cur + 1),
                PagerButton(">>", false, Posts.HasNext, cur + 10)
            };
            for (int i = 0; i < nav.Length; i++)
            {
                Grid.SetColumn((FrameworkElement)nav[i], i);
                NavGrid.Children.Add(nav[i]);
            }
        }

        private void Go_Click(object sender, RoutedEventArgs e)
        {
            int p;
            if (int.TryParse((PageBox.Text ?? "").Trim(), out p) && p >= 1)
                GoPage(p);
        }

        // ---------- поиск ----------
        private void StartSearch(string q)
        {
            _timer.Stop();
            Suggestions.Visibility = Visibility.Collapsed;

            string text = (q ?? "").Replace(',', ' ').Trim().ToLowerInvariant();
            while (text.Contains("  ")) text = text.Replace("  ", " ");

            _skipText = text.Length > 0 ? text + " " : "";
            SearchBox.Text = _skipText;
            Focus();

            MainPivot.SelectedItem = PostsItem;

            PushHistory(text, 1);
            _scrollTop = true;
            Posts.Open(text, 1);
        }

        private void Search_Click(object sender, RoutedEventArgs e)
        {
            StartSearch(SearchBox.Text);
        }

        private void SearchBox_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) StartSearch(SearchBox.Text);
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (SearchBox.Text == _skipText) return;
            _timer.Stop();
            _timer.Start();
        }

        private async void Timer_Tick(object sender, EventArgs e)
        {
            _timer.Stop();

            string text = SearchBox.Text ?? "";
            int sp = text.LastIndexOf(' ');
            string word = sp >= 0 ? text.Substring(sp + 1) : text;
            string neg = word.StartsWith("-") ? "-" : "";
            string term = word.TrimStart('-');

            if (term.Length < 2)
            {
                Suggestions.Visibility = Visibility.Collapsed;
                return;
            }

            List<TagSuggestion> list = await Api.Suggest(term);

            if ((SearchBox.Text ?? "") != text) return;

            if (list.Count == 0)
            {
                Suggestions.Visibility = Visibility.Collapsed;
                return;
            }

            foreach (TagSuggestion s in list) s.Value = neg + s.Value;
            Suggestions.ItemsSource = list;
            Suggestions.Visibility = Visibility.Visible;
        }

        private void Suggestions_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var s = Suggestions.SelectedItem as TagSuggestion;
            if (s == null) return;
            Suggestions.SelectedItem = null;

            string text = SearchBox.Text ?? "";
            int sp = text.LastIndexOf(' ');
            string newText = (sp >= 0 ? text.Substring(0, sp + 1) : "") + s.Value + " ";

            _skipText = newText;
            SearchBox.Text = newText;
            SearchBox.SelectionStart = newText.Length;
            Suggestions.Visibility = Visibility.Collapsed;
            SearchBox.Focus();
        }

        // ---------- меню ----------
        private void Refresh_Click(object sender, EventArgs e)
        {
            if (MainPivot.SelectedItem == FavItem) return;

            _scrollTop = true;
            Posts.Reload();
        }

        private void ClearSearch_Click(object sender, EventArgs e)
        {
            StartSearch("");
        }

        private void SwitchSource_Click(object sender, EventArgs e)
        {
            NavigationService.Navigate(new Uri("/SourcesPage.xaml?pick=1", UriKind.Relative));
        }

        private void Settings_Click(object sender, EventArgs e)
        {
            NavigationService.Navigate(new Uri("/SettingsPage.xaml", UriKind.Relative));
        }

        // пункт меню «blacklist» — четвёртый (индекс 3)
        private void UpdateBlacklistItem()
        {
            var item = ApplicationBar.MenuItems[3] as ApplicationBarMenuItem;
            if (item != null)
                item.Text = Settings.UseBlacklist ? "blacklist: on" : "blacklist: off";
        }

        private void Blacklist_Click(object sender, EventArgs e)
        {
            Settings.UseBlacklist = !Settings.UseBlacklist;
            _seenAi = Settings.Version;   // ленту перезагружаем сразу, при возврате повторно не нужно
            UpdateBlacklistItem();

            if (Posts.Page >= 1)
            {
                _scrollTop = true;
                Posts.Reload();
            }
        }
    }
}