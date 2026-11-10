using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;
using System.Windows.Threading;
using Microsoft.Phone.Controls;

namespace ArtHub
{
    public partial class BlacklistPage : PhoneApplicationPage
    {
        static readonly char[] Separators = { ' ', ',' };

        private readonly DispatcherTimer _timer =
            new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        private string _skipText;   // текст, выставленный кодом: на него подсказки не ищем

        public BlacklistPage()
        {
            InitializeComponent();
            _timer.Tick += Timer_Tick;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            RefreshList();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            _timer.Stop();
            base.OnNavigatedFrom(e);
        }

        private void RefreshList()
        {
            List<string> list = Settings.Blacklist;

            TagList.ItemsSource = null;
            TagList.ItemsSource = list;
            EmptyText.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // ---------- добавление ----------
        private void Add_Click(object sender, RoutedEventArgs e)
        {
            AddFromBox();
        }

        private void TagBox_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) AddFromBox();
        }

        // пробел и запятая разделяют теги: добавляется каждый из введённых
        private void AddFromBox()
        {
            string text = TagBox.Text ?? "";
            string[] parts = text.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;

            foreach (string part in parts)
                Settings.AddToBlacklist(part);

            HideSuggestions();
            _skipText = "";
            TagBox.Text = "";
            RefreshList();
        }

        // ---------- удаление: кнопка-корзина в строке ----------
        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            string tag = ((FrameworkElement)sender).DataContext as string;
            if (tag == null) return;

            Settings.RemoveFromBlacklist(tag);
            Dispatcher.BeginInvoke(() => RefreshList());
        }

        // ---------- подсказки, как в поиске ----------
        private void TagBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (TagBox.Text == _skipText) return;
            _timer.Stop();
            _timer.Start();
        }

        // последнее слово в поле (после последнего пробела или запятой)
        private static int LastWordStart(string text)
        {
            return text.LastIndexOfAny(Separators) + 1;
        }

        private async void Timer_Tick(object sender, EventArgs e)
        {
            _timer.Stop();

            string text = TagBox.Text ?? "";
            string term = text.Substring(LastWordStart(text)).TrimStart('-');

            if (term.Length < 2)
            {
                HideSuggestions();
                return;
            }

            List<TagSuggestion> list = await Api.Suggest(term);

            // пока ждали ответ, текст поменялся
            if ((TagBox.Text ?? "") != text) return;

            if (list.Count == 0)
            {
                HideSuggestions();
                return;
            }

            Suggestions.ItemsSource = list;
            SuggestBox.Visibility = Visibility.Visible;
        }

        // выбранная подсказка заменяет последнее слово, после неё пробел для следующего тега
        private void Suggestion_Tap(object sender, System.Windows.Input.GestureEventArgs e)
        {
            var s = ((FrameworkElement)sender).DataContext as TagSuggestion;
            if (s == null) return;

            string text = TagBox.Text ?? "";
            string newText = text.Substring(0, LastWordStart(text)) + s.Value + " ";

            _skipText = newText;
            TagBox.Text = newText;
            TagBox.SelectionStart = newText.Length;
            HideSuggestions();
            TagBox.Focus();
        }

        private void HideSuggestions()
        {
            _timer.Stop();
            SuggestBox.Visibility = Visibility.Collapsed;
            Suggestions.ItemsSource = null;
        }
    }
}