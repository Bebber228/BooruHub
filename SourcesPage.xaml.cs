using System;
using System.Windows;
using System.Windows.Navigation;
using Microsoft.Phone.Controls;

namespace ArtHub
{
    public partial class SourcesPage : PhoneApplicationPage
    {
        private bool _pick;

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

        // кнопка изменить: отдельная страница
        private void Edit_Tap(object sender, System.Windows.Input.GestureEventArgs e)
        {
            e.Handled = true;
            Source s = FromSender(sender);
            if (s == null) return;

            int idx = Sources.All.IndexOf(s);
            NavigationService.Navigate(new Uri("/SourceEditPage.xaml?idx=" + idx, UriKind.Relative));
        }

        // кнопка удалить
        private void Delete_Tap(object sender, System.Windows.Input.GestureEventArgs e)
        {
            e.Handled = true;
            Source s = FromSender(sender);
            if (s == null) return;

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