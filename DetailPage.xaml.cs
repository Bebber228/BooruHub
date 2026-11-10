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

        private ArtItem _item;
        private BitmapImage _fullImage;
        private bool _downloading;
        private bool _gone;               // страницу закрыли (ушли назад в ленту)
        private bool _suspended;          // приложение свернули или открыли браузер / плеер
        private bool _imageReady;         // средний размер картинки показан
        private bool _fullReady;          // оригинал показан
        private int _gen;                 // номер показанного поста, отсекает устаревшие задачи

        private DispatcherTimer _rowTimer;
        private List<Func<UIElement>> _rows;
        private int _rowPos;

        private DispatcherTimer _gifTimer;
        private string _gifHtml;

        // нижние панели: обычная и полупрозрачная для полноэкранного просмотра
        private IApplicationBar _mainBar;
        private IApplicationBar _zoomBar;
        private Storyboard _zoomSb;      // текущая анимация масштаба

        // перелистывание свайпом
        private bool _drag;
        private bool _animating;
        private double _off;

        private bool IsMedia
        {
            get { return _item != null && !string.IsNullOrEmpty(_item.Kind); }
        }

        public DetailPage()
        {
            InitializeComponent();
            _mainBar = ApplicationBar;
            BuildZoomBar();

            _item = Nav.Selected;
            if (_item == null) return;

            Fill();
            Loaded += DetailPage_Loaded;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            // приложение выгружалось из памяти: данных поста уже нет — возвращаемся в ленту,
            // она сама восстановит последнюю страницу
            if (_item == null)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (NavigationService.CanGoBack) NavigationService.GoBack();
                });
                return;
            }

            if (!_suspended) return;
            _suspended = false;

            // пока приложение было свёрнуто, загрузки могли оборваться — запускаем заново
            if (!_imageReady)
            {
                LoadItem(_item);
            }
            else if (!_fullReady && !IsMedia && !IsHuge())
            {
                _fullImage = null;
                StartFull(_item.SampleUrl ?? _item.ImageUrl, _gen);
            }

            if (_tagsShown && _rows != null && _rowPos < _rows.Count && _rowTimer != null)
                _rowTimer.Start();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            // свернули приложение, открыли браузер или плеер: страница остаётся,
            // ничего не очищаем, просто ставим на паузу
            if (e.NavigationMode != NavigationMode.Back)
            {
                _suspended = true;
                if (_rowTimer != null) _rowTimer.Stop();
                if (_gifTimer != null) _gifTimer.Stop();
                base.OnNavigatedFrom(e);
                return;
            }

            // ушли назад в ленту: страница больше не нужна
            _gone = true;
            if (_rowTimer != null) _rowTimer.Stop();
            if (_gifTimer != null) _gifTimer.Stop();

            // освобождаем память
            try
            {
                MainImage.Source = null;
                ZoomImage.Source = null;
                _fullImage = null;
                TagsPanel.Children.Clear();
                GifBrowser.Visibility = Visibility.Collapsed;
            }
            catch { }
            GC.Collect();

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

        private void DetailPage_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= DetailPage_Loaded;
            if (_item == null) return;
            Begin();
        }

        private void Fill()
        {
            TitleText.Text = _item.Title;

            int tagCount = (_item.Tags ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
            InfoText.Text = string.Join("  •  ", new[]
            {
                _item.Rating, "score " + _item.Score, _item.Size, _item.Author, _item.Date,
                tagCount + " tags"
            }.Where(s => !string.IsNullOrEmpty(s)).ToArray());

            // теги грузятся только по кнопке
            TagsButton.Visibility = tagCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // показ текущего поста: картинка и звезда; теги — только по кнопке
        private void Begin()
        {
            UpdateStar(Favorites.Contains(_item));
            LoadImage(_gen);
            if (IsMedia) PlayMark.Visibility = Visibility.Visible;
        }

        // ---------- теги по кнопке ----------
        private bool _tagsShown;     // пользователь нажал «show tags»
        private bool _tagsLoading;   // типы тегов ещё грузятся
        private bool _tagsBuilt;     // строки для этого поста уже подготовлены

        private void ResetTags()
        {
            if (_rowTimer != null) _rowTimer.Stop();
            _rows = null;
            _rowPos = 0;
            _tagsShown = false;
            _tagsLoading = false;
            _tagsBuilt = false;

            TagsPanel.Children.Clear();
            TagsPanel.Visibility = Visibility.Collapsed;
            TagsStatus.Visibility = Visibility.Collapsed;
            TagsButton.Content = "show tags";
        }

        private void TagsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_item == null) return;

            if (_tagsShown)
            {
                // скрыть: строки остаются в памяти, повторный показ мгновенный
                _tagsShown = false;
                TagsButton.Content = "show tags";
                TagsPanel.Visibility = Visibility.Collapsed;
                TagsStatus.Visibility = Visibility.Collapsed;
                if (_rowTimer != null) _rowTimer.Stop();
                return;
            }

            _tagsShown = true;
            TagsButton.Content = "hide tags";
            TagsPanel.Visibility = Visibility.Visible;

            if (_tagsLoading)
            {
                TagsStatus.Text = "loading tags...";
                TagsStatus.Visibility = Visibility.Visible;
                return;
            }

            if (_tagsBuilt)
            {
                // если до скрытия успели вывести не все строки — продолжаем
                if (_rows != null && _rowPos < _rows.Count && _rowTimer != null) _rowTimer.Start();
                return;
            }

            ShowTags(_gen);
        }

        // смена поста внутри той же страницы
        private void LoadItem(ArtItem item)
        {
            _gen++;
            _item = item;
            Nav.Selected = item;

            if (_rowTimer != null) _rowTimer.Stop();
            if (_gifTimer != null) _gifTimer.Stop();
            _fullImage = null;
            _imageReady = false;
            _fullReady = false;

            try { GifBrowser.NavigateToString("<html><body style=\"background:#000\"></body></html>"); }
            catch { }
            GifBrowser.Visibility = Visibility.Collapsed;
            MainImage.Visibility = Visibility.Visible;
            MainImage.Source = null;
            PlayMark.Visibility = Visibility.Collapsed;

            ResetTags();
            DetailScroll.ScrollToVerticalOffset(0);

            var dl = (ApplicationBarIconButton)_mainBar.Buttons[1];
            dl.Text = "download";
            dl.IconUri = new Uri("/Assets/Icons/save.png", UriKind.Relative);
            dl.IsEnabled = true;

            Fill();
            Begin();
        }

        // ---------- перелистывание свайпом: пост едет за пальцем ----------
        private bool HasNeighbor(int dir)
        {
            var list = Nav.List;
            if (list == null || _item == null) return false;
            int i = list.IndexOf(_item);
            if (i < 0) return false;
            int n = i + dir;
            return n >= 0 && n < list.Count;
        }

        private void Drag_Started(object sender, DragStartedGestureEventArgs e)
        {
            _drag = false;
            if (_animating || _item == null || Nav.List == null) return;
            if (ZoomOverlay.Visibility == Visibility.Visible) return;

            _drag = e.Direction == System.Windows.Controls.Orientation.Horizontal;
            _off = 0;
        }

        private void Drag_Delta(object sender, DragDeltaGestureEventArgs e)
        {
            if (!_drag) return;

            _off += e.HorizontalChange;

            // на краю списка тянется с сопротивлением
            double shown = HasNeighbor(_off < 0 ? 1 : -1) ? _off : _off * 0.3;
            SlideT.X = shown;
        }

        private void Drag_Completed(object sender, DragCompletedGestureEventArgs e)
        {
            if (!_drag) return;
            _drag = false;

            int dir = _off < 0 ? 1 : -1;      // 1 = следующий пост, -1 = предыдущий
            double w = LayoutRoot.ActualWidth;

            bool far = Math.Abs(_off) > w * 0.25;
            bool fast = e.HorizontalVelocity * _off > 0 && Math.Abs(e.HorizontalVelocity) > 600;
            bool commit = HasNeighbor(dir) && (far || fast);

            if (!commit)
            {
                SlideTo(0, 200, null);
                return;
            }

            _animating = true;
            SlideTo(-dir * w, 180, () =>
            {
                var list = Nav.List;
                int i = list.IndexOf(_item);
                Nav.Index = i + dir;
                LoadItem(list[i + dir]);

                // новый пост въезжает с противоположной стороны
                SlideT.X = dir * w;
                SlideTo(0, 220, () => { _animating = false; });
            });
        }

        private void SlideTo(double to, int ms, Action done)
        {
            var a = new DoubleAnimation
            {
                To = to,
                Duration = new Duration(TimeSpan.FromMilliseconds(ms)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                // HoldEnd: анимация держит конечное значение, пока мы его не зафиксируем.
                // Со Stop на один кадр возвращалось положение пальца и старый арт мелькал.
                FillBehavior = FillBehavior.HoldEnd
            };
            Storyboard.SetTarget(a, SlideT);
            Storyboard.SetTargetProperty(a, new PropertyPath("X"));

            var sb = new Storyboard();
            sb.Children.Add(a);
            sb.Completed += (s, e) =>
            {
                // сначала записываем конечное значение, потом снимаем анимацию — без промежуточного кадра
                SlideT.X = to;
                sb.Stop();
                if (done != null) done();
            };
            sb.Begin();
        }

        // ---------- GIF с повтором ----------
        private async void StartGif()
        {
            int gen = _gen;
            try
            {
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

                // длительность считаем только для небольших файлов, чтобы не съесть память
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

                if (_gone || gen != _gen || infinite || durationMs <= 0) return;

                if (_gifTimer != null) _gifTimer.Stop();
                _gifTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(Math.Max(600, durationMs + 250))
                };
                _gifTimer.Tick += (s, e) =>
                {
                    if (_gone || gen != _gen) { _gifTimer.Stop(); return; }
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
                    total += delay < 2 ? 100 : delay * 10;
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
        private void BuildZoomBar()
        {
            var bar = new ApplicationBar
            {
                Mode = ApplicationBarMode.Default,
                Opacity = 0.5,
                BackgroundColor = Colors.Black,
                ForegroundColor = Colors.White
            };

            var close = new ApplicationBarIconButton(new Uri("/Assets/Icons/close.png", UriKind.Relative))
            {
                Text = "close"
            };
            close.Click += (s, e) => CloseZoom();
            bar.Buttons.Add(close);

            _zoomBar = bar;
        }

        // панель с часами и батареей: в просмотре полупрозрачная чёрная, иначе цвета шапки
        private void SetTrayZoom(bool zoom)
        {
            SystemTray.SetBackgroundColor(this, zoom ? Colors.Black : Color.FromArgb(255, 0x4E, 0x62, 0x52));
            SystemTray.SetOpacity(this, zoom ? 0.5 : 0.99);
        }

        private void MainImage_Tap(object sender, System.Windows.Input.GestureEventArgs e)
        {
            if (MainImage.Source == null) return;

            if (_item != null && _item.Kind == "gif") { StartGif(); return; }
            if (_item != null && _item.Kind == "video") { OpenPlayer(); return; }

            ZoomImage.Source = MainImage.Source;
            ResetZoom();
            DetailScroll.IsHitTestVisible = false;
            ZoomOverlay.Visibility = Visibility.Visible;
            ApplicationBar = _zoomBar;
            SetTrayZoom(true);

            // огромный оригинал грузим только сейчас
            if (_fullImage == null && IsHuge() && !string.IsNullOrEmpty(_item.FullImageUrl))
            {
                int gen = _gen;
                _fullImage = MakeBitmap(_item.FullImageUrl, 1600);
                _fullImage.ImageOpened += (s2, e2) =>
                {
                    if (_gone || gen != _gen) return;
                    _imageReady = true;
                    _fullReady = true;
                    MainImage.Source = _fullImage;
                    if (ZoomOverlay.Visibility == Visibility.Visible)
                        ZoomImage.Source = _fullImage;
                };
            }
        }

        private void CloseZoom()
        {
            StopZoomAnim();
            ZoomOverlay.Visibility = Visibility.Collapsed;
            ZoomImage.Source = null;
            DetailScroll.IsHitTestVisible = true;
            ApplicationBar = _mainBar;
            SetTrayZoom(false);
        }

        private void SetZoom(double s, double tx, double ty)
        {
            ZoomTransform.ScaleX = s;
            ZoomTransform.ScaleY = s;
            ZoomTransform.TranslateX = tx;
            ZoomTransform.TranslateY = ty;
        }

        private void ResetZoom()
        {
            StopZoomAnim();
            SetZoom(1, 0, 0);
        }

        // останавливает анимацию масштаба, оставляя картинку там, где она сейчас
        private void StopZoomAnim()
        {
            if (_zoomSb == null) return;

            double s = ZoomTransform.ScaleX;
            double tx = ZoomTransform.TranslateX;
            double ty = ZoomTransform.TranslateY;

            Storyboard sb = _zoomSb;
            _zoomSb = null;
            SetZoom(s, tx, ty);
            sb.Stop();
        }

        private void AnimateTo(double scale, double tx, double ty)
        {
            StopZoomAnim();

            var sb = new Storyboard();
            AddAnim(sb, "ScaleX", scale);
            AddAnim(sb, "ScaleY", scale);
            AddAnim(sb, "TranslateX", tx);
            AddAnim(sb, "TranslateY", ty);

            sb.Completed += (s, e) =>
            {
                if (_zoomSb != sb) return;
                _zoomSb = null;

                // как и при перелистывании: сначала значения, потом снимаем анимацию
                SetZoom(scale, tx, ty);
                sb.Stop();
            };

            _zoomSb = sb;
            sb.Begin();
        }

        private void AddAnim(Storyboard sb, string prop, double to)
        {
            var a = new DoubleAnimation
            {
                To = to,
                Duration = new Duration(TimeSpan.FromMilliseconds(220)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.HoldEnd
            };
            Storyboard.SetTarget(a, ZoomTransform);
            Storyboard.SetTargetProperty(a, new PropertyPath(prop));
            sb.Children.Add(a);
        }

        // размер картинки на экране при масштабе 1 (Stretch=Uniform вписывает её в область)
        private Size FitSize()
        {
            double aw = ZoomArea.ActualWidth;
            double ah = ZoomArea.ActualHeight;

            var bmp = ZoomImage.Source as BitmapSource;
            if (bmp == null || bmp.PixelWidth <= 0 || bmp.PixelHeight <= 0 || aw <= 0 || ah <= 0)
                return new Size(aw, ah);

            double k = Math.Min(aw / bmp.PixelWidth, ah / bmp.PixelHeight);
            return new Size(bmp.PixelWidth * k, bmp.PixelHeight * k);
        }

        // насколько можно сдвинуть картинку, чтобы её край не уходил дальше края экрана
        private double LimitX(double s)
        {
            return Math.Max(0, (FitSize().Width * s - ZoomArea.ActualWidth) / 2);
        }

        private double LimitY(double s)
        {
            return Math.Max(0, (FitSize().Height * s - ZoomArea.ActualHeight) / 2);
        }

        private void Zoom_ManipulationStarted(object sender, ManipulationStartedEventArgs e)
        {
            e.Handled = true;
            StopZoomAnim();   // палец перехватывает картинку прямо во время анимации
        }

        private void Zoom_ManipulationDelta(object sender, ManipulationDeltaEventArgs e)
        {
            e.Handled = true;

            double s = ZoomTransform.ScaleX;
            double tx = ZoomTransform.TranslateX;
            double ty = ZoomTransform.TranslateY;

            // ZoomImage не принимает касания (IsHitTestVisible=False), поэтому жест считается
            // в координатах экрана: картинка движется ровно за пальцем при любом масштабе
            tx += e.DeltaManipulation.Translation.X;
            ty += e.DeltaManipulation.Translation.Y;

            if (e.PinchManipulation != null)
            {
                double ns = Math.Max(0.8, Math.Min(MaxScale, s * e.PinchManipulation.DeltaScale));

                // точка между пальцами остаётся на месте
                Point c = e.PinchManipulation.Current.Center;
                double cx = c.X - ZoomArea.ActualWidth / 2;
                double cy = c.Y - ZoomArea.ActualHeight / 2;

                tx = cx - (cx - tx) * ns / s;
                ty = cy - (cy - ty) * ns / s;
                s = ns;
            }

            double mx = LimitX(s);
            double my = LimitY(s);
            tx = Math.Max(-mx, Math.Min(mx, tx));
            ty = Math.Max(-my, Math.Min(my, ty));

            SetZoom(s, tx, ty);
        }

        private void Zoom_ManipulationCompleted(object sender, ManipulationCompletedEventArgs e)
        {
            e.Handled = true;

            if (ZoomTransform.ScaleX < 1.0)
                AnimateTo(1, 0, 0);
        }

        // двойной тап: приближение к тому месту, куда нажали; повторный — обратно
        private void Zoom_DoubleTap(object sender, Microsoft.Phone.Controls.GestureEventArgs e)
        {
            if (ZoomTransform.ScaleX > 1.05)
            {
                AnimateTo(1, 0, 0);
                return;
            }

            const double s = 2.5;
            Point p = e.GetPosition(ZoomArea);
            double cx = p.X - ZoomArea.ActualWidth / 2;
            double cy = p.Y - ZoomArea.ActualHeight / 2;

            // точка под пальцем остаётся на месте
            double tx = -cx * (s - 1);
            double ty = -cy * (s - 1);

            double mx = LimitX(s);
            double my = LimitY(s);
            tx = Math.Max(-mx, Math.Min(mx, tx));
            ty = Math.Max(-my, Math.Min(my, ty));

            AnimateTo(s, tx, ty);
        }

        // ---------- избранное ----------
        private void UpdateStar(bool fav)
        {
            var btn = (ApplicationBarIconButton)_mainBar.Buttons[0];
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

        private void LoadImage(int gen)
        {
            string sample = _item.SampleUrl ?? _item.ImageUrl;

            // сразу ставим превью из сетки (оно уже скачано), чтобы пост въезжал не пустым
            bool hasThumb = !string.IsNullOrEmpty(_item.ImageUrl) && _item.ImageUrl != sample;
            if (hasThumb)
                MainImage.Source = MakeBitmap(_item.ImageUrl, 400);

            var first = MakeBitmap(sample, 800);
            first.ImageOpened += (s, e) =>
            {
                if (_gone || gen != _gen) return;

                _imageReady = true;

                // подменяем превью на средний размер, если оригинал ещё не показан
                if (_fullImage == null || MainImage.Source != _fullImage)
                    MainImage.Source = first;
                if (ZoomOverlay.Visibility == Visibility.Visible && ZoomImage.Source != _fullImage)
                    ZoomImage.Source = first;

                StartFull(sample, gen);
            };
            first.ImageFailed += (s, e) => StartFull(sample, gen);

            if (!hasThumb)
                MainImage.Source = first;
        }

        private async void StartFull(string sample, int gen)
        {
            if (gen != _gen) return;
            if (IsMedia) return;
            if (_fullImage != null) return;
            if (string.IsNullOrEmpty(_item.FullImageUrl) || _item.FullImageUrl == sample) return;
            if (IsHuge()) return;

            await Task.Delay(800);
            if (_gone || gen != _gen) return;

            _fullImage = MakeBitmap(_item.FullImageUrl, 1600);
            _fullImage.ImageOpened += (s, e) =>
            {
                if (_gone || gen != _gen) return;
                _imageReady = true;
                _fullReady = true;
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
                case 1: return new SolidColorBrush(Color.FromArgb(255, 0xB0, 0x00, 0x00));
                case 3: return new SolidColorBrush(Color.FromArgb(255, 0xA0, 0x00, 0xA0));
                case 4: return new SolidColorBrush(Color.FromArgb(255, 0x00, 0x88, 0x00));
                case 5: return new SolidColorBrush(Color.FromArgb(255, 0xE0, 0x80, 0x00));
                default: return new SolidColorBrush(Color.FromArgb(255, 0x00, 0x00, 0xAA));
            }
        }

        private async void ShowTags(int gen)
        {
            string[] tags = (_item.Tags ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (tags.Length == 0)
            {
                TagsStatus.Visibility = Visibility.Collapsed;
                return;
            }

            _tagsLoading = true;
            TagsStatus.Text = "loading tags...";
            TagsStatus.Visibility = Visibility.Visible;

            try { await Api.LoadTags(tags); }
            catch { }
            if (_gone || gen != _gen) return;

            _tagsLoading = false;
            _tagsBuilt = true;

            bool failed = tags.All(t => !Api.HasInfo(t));
            TagsStatus.Text = "Couldn't load tag types, all tags are shown as general.";
            TagsStatus.Visibility = (failed && _tagsShown) ? Visibility.Visible : Visibility.Collapsed;

            BuildRows(tags);
        }

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

            // пока грузилось, теги могли скрыть — тогда выведем строки при следующем показе
            if (_tagsShown) _rowTimer.Start();
        }

        private void RowTimer_Tick(object sender, EventArgs e)
        {
            if (_gone || _rows == null || !_tagsShown)
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
            int gen = _gen;

            var btn = (ApplicationBarIconButton)_mainBar.Buttons[1];
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

            _downloading = false;

            // пока качали, пост перелистнули: кнопка уже сброшена для нового поста
            if (_gone || gen != _gen) return;

            btn.IsEnabled = true;

            if (saved)
            {
                btn.Text = "saved";
                btn.IconUri = new Uri("/Assets/Icons/done.png", UriKind.Relative);
                return;
            }

            btn.Text = "download";
            btn.IconUri = new Uri("/Assets/Icons/save.png", UriKind.Relative);
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