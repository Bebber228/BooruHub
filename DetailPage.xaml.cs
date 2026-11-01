using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Threading;
using Microsoft.Phone.Controls;
using Microsoft.Phone.Shell;
using Microsoft.Phone.Tasks;

namespace ArtHub
{
    public partial class DetailPage : PhoneApplicationPage
    {
        // порядок групп как на сайте: copyright, character, artist, general, meta
        private static readonly int[] Order = { 3, 4, 1, 0, 5 };

        private const double MaxScale = 6.0;

        private readonly ArtItem _item;
        private BitmapImage _fullImage;   // держим ссылку, пока грузится оригинал
        private bool _downloading;
        private bool _gone;               // страницу закрыли, дальше ничего не делаем

        private DispatcherTimer _rowTimer;
        private List<Func<UIElement>> _rows;
        private int _rowPos;

        // GIF: перезапуск по кругу
        private DispatcherTimer _gifTimer;
        private string _gifHtml;

        private bool IsMedia
        {
            get { return _item != null && !string.IsNullOrEmpty(_item.Kind); }
        }

        public DetailPage()
        {
            InitializeComponent();
            _item = Nav.Selected;
            if (_item == null) return;

            TitleText.Text = _item.Title;
            InfoText.Text = string.Join("  •  ", new[]
            {
                _item.Rating, "score " + _item.Score, _item.Size, _item.Author, _item.Date
            }.Where(s => !string.IsNullOrEmpty(s)).ToArray());

            Loaded += DetailPage_Loaded;
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            _gone = true;
            if (_rowTimer != null) _rowTimer.Stop();
            if (_gifTimer != null) _gifTimer.Stop();
            base.OnNavigatedFrom(e);
        }

        // кнопка «Назад» сначала закрывает просмотр картинки
        protected override void OnBackKeyPress(CancelEventArgs e)
        {
            if (ZoomOverlay.Visibility == Visibility.Visible)
            {
                e.Cancel = true;
                CloseZoom();
                return;
            }
            base.OnBackKeyPress(e);
        }

        private async void DetailPage_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= DetailPage_Loaded;
            if (_item == null) return;

            UpdateStar(Favorites.Contains(_item));

            // картинка из кэша показывается сразу, она уже загружена в сетке
            LoadImage();
            if (IsMedia) PlayMark.Visibility = Visibility.Visible;

            // тяжёлое начинаем после того, как анимация перехода доиграла
            await Task.Delay(350);
            if (_gone) return;
            ShowTags();
        }

        // ---------- GIF с повтором ----------
        private async void StartGif()
        {
            try
            {
                // высота по пропорциям картинки
                double w = Application.Current.Host.Content.ActualWidth - 24;
                double h = w * 0.75;
                string[] p = (_item.Size ?? "").Split('×');
                double pw, ph;
                if (p.Length == 2 && double.TryParse(p[0], out pw) && double.TryParse(p[1], out ph) && pw > 0)
                    h = w * ph / pw;

                PlayMark.Visibility = Visibility.Collapsed;
                MainImage.Visibility = Visibility.Collapsed;

                GifBrowser.Height = Math.Max(120, h);
                GifBrowser.Visibility = Visibility.Visible;

                _gifHtml = "<html><head><meta name=\"viewport\" content=\"width=device-width\"/></head>"
                    + "<body style=\"margin:0;background:#000\">"
                    + "<img src=\"" + _item.FullImageUrl + "\" style=\"width:100%\"/></body></html>";
                GifBrowser.NavigateToString(_gifHtml);

                // длительность считаем только для небольших файлов (до 6 МБ), чтобы не съесть память
                int durationMs = 0;
                bool infinite = false;
                try
                {
                    byte[] data = await Api.DownloadLimited(_item.FullImageUrl, 6 * 1024 * 1024);
                    if (data != null)
                    {
                        var info = await Task.Run(() => ParseGif(data));
                        durationMs = info.Item1;
                        infinite = info.Item2;
                    }
                }
                catch { }

                if (_gone || infinite || durationMs <= 0) return;

                if (_gifTimer != null) _gifTimer.Stop();
                _gifTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(Math.Max(600, durationMs + 250))
                };
                _gifTimer.Tick += (s, e) =>
                {
                    if (_gone) { _gifTimer.Stop(); return; }
                    try { GifBrowser.NavigateToString(_gifHtml); }
                    catch { }
                };
                _gifTimer.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Can't play this GIF: " + ex.Message);
            }
        }

        // возвращает общую длительность кадров и признак бесконечного повтора
        private static Tuple<int, bool> ParseGif(byte[] d)
        {
            int total = 0;
            bool infinite = false;
            byte[] net = System.Text.Encoding.UTF8.GetBytes("NETSCAPE2.0");

            for (int i = 0; i + 5 < d.Length; i++)
            {
                if (d[i] == 0x21 && d[i + 1] == 0xF9 && d[i + 2] == 0x04)
                {
                    int delay = d[i + 4] | (d[i + 5] << 8);
                    total += delay < 2 ? 100 : delay * 10;   // браузеры считают 0 и 1 как 100 мс
                }

                if (!infinite && d[i] == net[0] && i + net.Length < d.Length)
                {
                    bool ok = true;
                    for (int k = 1; k < net.Length; k++)
                        if (d[i + k] != net[k]) { ok = false; break; }
                    if (ok) infinite = true;
                }
            }
            return Tuple.Create(total, infinite);
        }

        // ---------- видео: системный плеер ----------
        private void OpenPlayer()
        {
            int w, h;
            GetDims(out w, out h);

            if (Math.Max(w, h) > 1920)
            {
                var r = MessageBox.Show(
                    "This video is " + _item.Size + ". It may load for a long time and slow the phone down.\n\nPlay anyway?",
                    "big video", MessageBoxButton.OKCancel);

                if (r != MessageBoxResult.OK) return;
            }

            try
            {
                new MediaPlayerLauncher
                {
                    Media = new Uri(_item.FullImageUrl, UriKind.Absolute),
                    Controls = MediaPlaybackControls.All
                }.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Can't open this video: " + ex.Message);
            }
        }

        // ---------- просмотр картинки с масштабированием ----------
        private void MainImage_Tap(object sender, System.Windows.Input.GestureEventArgs e)
        {
            if (MainImage.Source == null) return;

            if (_item != null && _item.Kind == "gif") { StartGif(); return; }
            if (_item != null && _item.Kind == "video") { OpenPlayer(); return; }

            ZoomImage.Source = MainImage.Source;
            ResetZoom();
            DetailScroll.IsHitTestVisible = false;
            ZoomOverlay.Visibility = Visibility.Visible;
            ApplicationBar.IsVisible = false;

            // огромный оригинал грузим только сейчас
            if (_fullImage == null && IsHuge() && !string.IsNullOrEmpty(_item.FullImageUrl))
            {
                _fullImage = MakeBitmap(_item.FullImageUrl, 2000);
                _fullImage.ImageOpened += (s2, e2) =>
                {
                    if (_gone) return;
                    MainImage.Source = _fullImage;
                    if (ZoomOverlay.Visibility == Visibility.Visible)
                        ZoomImage.Source = _fullImage;
                };
            }
        }

        private void Close_Tap(object sender, System.Windows.Input.GestureEventArgs e)
        {
            CloseZoom();
        }

        private void CloseZoom()
        {
            ZoomOverlay.Visibility = Visibility.Collapsed;
            ZoomImage.Source = null;
            DetailScroll.IsHitTestVisible = true;
            ApplicationBar.IsVisible = true;
        }

        private void ResetZoom()
        {
            ZoomTransform.ScaleX = 1;
            ZoomTransform.ScaleY = 1;
            ZoomTransform.TranslateX = 0;
            ZoomTransform.TranslateY = 0;
        }

        // плавный переход к нужному масштабу и смещению
        private void AnimateTo(double scale, double tx, double ty)
        {
            var sb = new Storyboard();
            AddAnim(sb, "ScaleX", scale);
            AddAnim(sb, "ScaleY", scale);
            AddAnim(sb, "TranslateX", tx);
            AddAnim(sb, "TranslateY", ty);
            sb.Begin();
        }

        private void AddAnim(Storyboard sb, string prop, double to)
        {
            var a = new DoubleAnimation
            {
                To = to,
                Duration = new Duration(TimeSpan.FromMilliseconds(220)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };
            Storyboard.SetTarget(a, ZoomTransform);
            Storyboard.SetTargetProperty(a, new PropertyPath(prop));
            sb.Children.Add(a);

            // после анимации фиксируем итоговое значение, чтобы жесты шли от него
            a.Completed += (s, e) =>
            {
                switch (prop)
                {
                    case "ScaleX": ZoomTransform.ScaleX = to; break;
                    case "ScaleY": ZoomTransform.ScaleY = to; break;
                    case "TranslateX": ZoomTransform.TranslateX = to; break;
                    case "TranslateY": ZoomTransform.TranslateY = to; break;
                }
            };
        }

        private double LimitX(double s)
        {
            return Math.Max(0, (s - 1) * ZoomArea.ActualWidth / 2);
        }

        private double LimitY(double s)
        {
            return Math.Max(0, (s - 1) * ZoomArea.ActualHeight / 2);
        }

        private void Zoom_ManipulationStarted(object sender, ManipulationStartedEventArgs e)
        {
            e.Handled = true;   // касания не уходят в интерфейс под затемнением
        }

        private void Zoom_ManipulationDelta(object sender, ManipulationDeltaEventArgs e)
        {
            e.Handled = true;

            double s = ZoomTransform.ScaleX;
            double tx = ZoomTransform.TranslateX;
            double ty = ZoomTransform.TranslateY;

            // перетаскивание: чем больше увеличение, тем быстрее едет картинка
            double boost = 1.0;
            if (e.PinchManipulation == null)
                boost = Math.Min(3.0, 1.0 + 0.4 * (s - 1.0));

            tx += e.DeltaManipulation.Translation.X * boost;
            ty += e.DeltaManipulation.Translation.Y * boost;

            // щипок: масштаб вокруг точки между пальцами
            if (e.PinchManipulation != null)
            {
                double ns = Math.Max(0.8, Math.Min(MaxScale, s * e.PinchManipulation.DeltaScale));

                Point c = e.PinchManipulation.Current.Center;
                double cx = c.X - ZoomArea.ActualWidth / 2;
                double cy = c.Y - ZoomArea.ActualHeight / 2;

                tx = cx - (cx - tx) * ns / s;
                ty = cy - (cy - ty) * ns / s;
                s = ns;
            }

            // при масштабе около 1 картинка стоит по центру
            double mx = LimitX(s);
            double my = LimitY(s);
            tx = Math.Max(-mx, Math.Min(mx, tx));
            ty = Math.Max(-my, Math.Min(my, ty));

            ZoomTransform.ScaleX = s;
            ZoomTransform.ScaleY = s;
            ZoomTransform.TranslateX = tx;
            ZoomTransform.TranslateY = ty;
        }

        private void Zoom_ManipulationCompleted(object sender, ManipulationCompletedEventArgs e)
        {
            e.Handled = true;

            // если слегка уменьшили, мягко возвращаем к 1×
            if (ZoomTransform.ScaleX < 1.0)
                AnimateTo(1, 0, 0);
        }

        private void Zoom_DoubleTap(object sender, Microsoft.Phone.Controls.GestureEventArgs e)
        {
            if (ZoomTransform.ScaleX > 1.05) AnimateTo(1, 0, 0);
            else AnimateTo(2.5, 0, 0);
        }

        // ---------- избранное ----------
        private void UpdateStar(bool fav)
        {
            var btn = (ApplicationBarIconButton)ApplicationBar.Buttons[0];
            btn.IconUri = new Uri(fav ? "/Assets/Icons/removestar.png" : "/Assets/Icons/star.png",
                                  UriKind.Relative);
            btn.Text = fav ? "remove" : "favourite";
        }

        private void Fav_Click(object sender, EventArgs e)
        {
            if (_item == null) return;
            UpdateStar(Favorites.Toggle(_item));
        }

        // ---------- картинка: сначала средний размер, потом оригинал ----------
        private void GetDims(out int w, out int h)
        {
            w = 0; h = 0;
            string[] p = (_item.Size ?? "").Split('×');
            if (p.Length == 2)
            {
                int.TryParse(p[0], out w);
                int.TryParse(p[1], out h);
            }
        }

        private bool IsHuge()
        {
            int w, h;
            GetDims(out w, out h);
            return Math.Max(w, h) > 3000 || (long)w * h > 12000000L;
        }

        // декодирует так, чтобы длинная сторона не превышала maxSide
        private BitmapImage MakeBitmap(string url, int maxSide)
        {
            int w, h;
            GetDims(out w, out h);

            var bmp = new BitmapImage();
            bmp.CreateOptions = BitmapCreateOptions.BackgroundCreation;

            if (w > 0 && h > 0)
            {
                if (h > w) bmp.DecodePixelHeight = Math.Min(h, maxSide);
                else bmp.DecodePixelWidth = Math.Min(w, maxSide);
            }
            else
            {
                bmp.DecodePixelWidth = maxSide;
            }

            bmp.UriSource = new Uri(url, UriKind.Absolute);
            return bmp;
        }

        private void LoadImage()
        {
            string sample = _item.SampleUrl ?? _item.ImageUrl;

            var first = MakeBitmap(sample, 1000);
            first.ImageOpened += (s, e) => StartFull(sample);
            first.ImageFailed += (s, e) => StartFull(sample);
            MainImage.Source = first;
        }

        private async void StartFull(string sample)
        {
            if (IsMedia) return;   // GIF и видео оригиналом в картинку не грузим
            if (_fullImage != null) return;
            if (string.IsNullOrEmpty(_item.FullImageUrl) || _item.FullImageUrl == sample) return;
            if (IsHuge()) return;  // огромные оригиналы грузим только при открытии на весь экран

            // оригинал качаем после среднего размера и тегов, чтобы не занимать соединения
            await Task.Delay(800);
            if (_gone) return;

            _fullImage = MakeBitmap(_item.FullImageUrl, 2000);
            _fullImage.ImageOpened += (s, e) =>
            {
                if (_gone) return;
                MainImage.Source = _fullImage;

                if (ZoomOverlay.Visibility == Visibility.Visible)
                    ZoomImage.Source = _fullImage;
            };
        }

        // ---------- теги ----------
        private static int Normalize(int type)
        {
            return (type == 1 || type == 3 || type == 4 || type == 5) ? type : 0;
        }

        private static string GroupName(int type)
        {
            switch (type)
            {
                case 3: return "Copyright";
                case 4: return "Character";
                case 1: return "Artist";
                case 5: return "Meta";
                default: return "General";
            }
        }

        private static SolidColorBrush TagBrush(int type)
        {
            switch (type)
            {
                case 1: return new SolidColorBrush(Color.FromArgb(255, 0xB0, 0x00, 0x00));   // artist
                case 3: return new SolidColorBrush(Color.FromArgb(255, 0xA0, 0x00, 0xA0));   // copyright
                case 4: return new SolidColorBrush(Color.FromArgb(255, 0x00, 0x88, 0x00));   // character
                case 5: return new SolidColorBrush(Color.FromArgb(255, 0xE0, 0x80, 0x00));   // meta
                default: return new SolidColorBrush(Color.FromArgb(255, 0x00, 0x00, 0xAA));  // general
            }
        }

        private async void ShowTags()
        {
            string[] tags = (_item.Tags ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (tags.Length == 0)
            {
                TagsStatus.Visibility = Visibility.Collapsed;
                return;
            }

            // вся загрузка идёт в фоне, интерфейс остаётся живым
            try { await Api.LoadTags(tags); }
            catch { }
            if (_gone) return;

            if (tags.All(t => !Api.HasInfo(t)))
                TagsStatus.Text = "Типы тегов не загрузились, все показаны как general.";
            else
                TagsStatus.Visibility = Visibility.Collapsed;

            BuildRows(tags);
        }

        // строки добавляются небольшими порциями с паузой, чтобы успевала отрисовка
        private void BuildRows(string[] tags)
        {
            var dim = (Brush)Application.Current.Resources["TextDim"];
            var dark = (Brush)Application.Current.Resources["TextDark"];

            var rows = new List<Func<UIElement>>();

            foreach (int t in Order)
            {
                int type = t;
                string[] group = tags
                    .Where(x => Normalize(Api.Info(x).Type) == type)
                    .OrderBy(x => x)
                    .ToArray();
                if (group.Length == 0) continue;

                SolidColorBrush brush = TagBrush(type);
                string name = GroupName(type);

                rows.Add(() => new TextBlock
                {
                    Text = name,
                    FontSize = 26,
                    FontWeight = FontWeights.Bold,
                    Foreground = dark,
                    Margin = new Thickness(0, 16, 0, 2)
                });

                foreach (string tag in group)
                {
                    string tg = tag;
                    rows.Add(() => MakeRow(tg, brush, dim));
                }
            }

            TagsPanel.Children.Clear();
            _rows = rows;
            _rowPos = 0;

            if (_rowTimer != null) _rowTimer.Stop();
            _rowTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            _rowTimer.Tick += RowTimer_Tick;
            _rowTimer.Start();
        }

        private void RowTimer_Tick(object sender, EventArgs e)
        {
            if (_gone || _rows == null)
            {
                _rowTimer.Stop();
                return;
            }

            int end = Math.Min(_rowPos + 6, _rows.Count);
            for (int i = _rowPos; i < end; i++)
                TagsPanel.Children.Add(_rows[i]());
            _rowPos = end;

            if (end >= _rows.Count) _rowTimer.Stop();
        }

        private UIElement MakeRow(string tag, Brush brush, Brush dim)
        {
            var row = new TextBlock
            {
                FontSize = 24,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 5, 0, 5)
            };
            row.Inlines.Add(new System.Windows.Documents.Run { Text = "? ", FontSize = 20, Foreground = dim });
            row.Inlines.Add(new System.Windows.Documents.Run { Text = tag.Replace('_', ' '), Foreground = brush });
            row.Inlines.Add(new System.Windows.Documents.Run { Text = " " + Api.Info(tag).Count, FontSize = 20, Foreground = dim });

            row.Tap += (s, e) =>
            {
                Nav.Query = tag;
                NavigationService.GoBack();
            };
            return row;
        }

        // ---------- кнопки нижней панели ----------
        private async void Download_Click(object sender, EventArgs e)
        {
            if (_downloading || _item == null) return;

            if (IsMedia)
            {
                MessageBox.Show("Saving is available for images only. Use the site button to get this file.");
                return;
            }

            _downloading = true;

            var btn = (ApplicationBarIconButton)ApplicationBar.Buttons[1];
            btn.IsEnabled = false;
            btn.Text = "downloading";

            bool saved = false;
            try
            {
                string url = _item.FullImageUrl;
                int q = url.IndexOf('?');
                string path = q >= 0 ? url.Substring(0, q) : url;
                string ext = System.IO.Path.GetExtension(path);
                if (string.IsNullOrEmpty(ext)) ext = ".jpg";

                byte[] data = await Api.Download(url);

                using (var lib = new Microsoft.Xna.Framework.Media.MediaLibrary())
                    lib.SavePicture("booru_" + _item.Id + ext, data);

                saved = true;
            }
            catch
            {
                saved = false;
            }

            btn.IsEnabled = true;
            _downloading = false;

            if (saved)
            {
                btn.Text = "saved";
                return;
            }

            // обходной путь: открываем оригинал в браузере, там его можно сохранить долгим нажатием
            btn.Text = "download";
            if (MessageBox.Show("Couldn't save to the photo library.\n\nOpen the image in the browser instead? Press and hold it there to save.",
                "download", MessageBoxButton.OKCancel) == MessageBoxResult.OK)
            {
                try
                {
                    new WebBrowserTask { Uri = new Uri(_item.FullImageUrl, UriKind.Absolute) }.Show();
                }
                catch { }
            }
        }

        private void Open_Click(object sender, EventArgs e)
        {
            if (_item == null || string.IsNullOrEmpty(_item.Url)) return;
            new WebBrowserTask { Uri = new Uri(_item.Url, UriKind.Absolute) }.Show();
        }
    }
}