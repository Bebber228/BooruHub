using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using Microsoft.Phone.Controls;

namespace ArtHub
{
    public partial class SourcesPage : PhoneApplicationPage
    {
        private bool _pick;
        private Action _afterMenu;   // действие из меню, выполняется, когда меню закроется

        public SourcesPage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            _pick = NavigationContext.QueryString.ContainsKey("pick");
            TitleText.Text = _pick ? "choose source" : "sources";
            AddRow.Visibility = _pick ? Visibility.Collapsed : Visibility.Visible;
            RefreshList();
        }

        private void RefreshList()
        {
            Source current = Sources.Current;
            foreach (Source s in Sources.All)
            {
                s.IsActive = (s == current);
                s.ShowTools = !_pick;
            }

            SourceList.ItemsSource = null;
            SourceList.ItemsSource = Sources.All;
        }

        private static Source FromSender(object sender)
        {
            return ((FrameworkElement)sender).DataContext as Source;
        }

        // тап по названию: выбрать источник
        private void Row_Tap(object sender, System.Windows.Input.GestureEventArgs e)
        {
            Source s = FromSender(sender);
            if (s == null) return;

            Sources.Select(s);

            if (_pick) NavigationService.GoBack();
            else RefreshList();
        }

        // меню сначала закрывается, и только потом выполняем пункт:
        // иначе переход на другую страницу идёт без стандартной анимации
        private void RunAfterMenu(object sender, Action action)
        {
            var menu = ItemsControl.ItemsControlFromItemContainer(sender as Microsoft.Phone.Controls.MenuItem)
                as Microsoft.Phone.Controls.ContextMenu;
            if (menu != null && menu.IsOpen)
            {
                _afterMenu = action;            // выполнится в Menu_Closed
                return;
            }
            Dispatcher.BeginInvoke(action);     // меню уже закрыто — следующим кадром
        }

        private void Menu_Closed(object sender, RoutedEventArgs e)
        {
            Action a = _afterMenu;
            _afterMenu = null;
            if (a != null) Dispatcher.BeginInvoke(a);
        }

        // меню по удержанию: edit — отдельная страница
        private void Edit_Click(object sender, RoutedEventArgs e)
        {
            Source s = FromSender(sender);
            if (s == null) return;

            int idx = Sources.All.IndexOf(s);
            if (idx < 0) return;

            RunAfterMenu(sender, () => NavigationService.Navigate(
                new Uri("/SourceEditPage.xaml?idx=" + idx, UriKind.Relative)));
        }

        // меню по удержанию: delete с подтверждением
        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            Source s = FromSender(sender);
            if (s == null) return;

            RunAfterMenu(sender, () => ConfirmDelete(s));
        }

        private void ConfirmDelete(Source s)
        {
            if (Sources.All.Count <= 1)
            {
                MessageBox.Show("At least one source is required.");
                return;
            }

            string text = string.Format("Delete \"{0}\"?", s.Name);
            if (MessageBox.Show(text, "delete", MessageBoxButton.OKCancel) != MessageBoxResult.OK)
                return;

            Sources.Remove(s);
            RefreshList();
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            NavigationService.Navigate(new Uri("/SourceEditPage.xaml", UriKind.Relative));
        }
    }
}