using System;
using System.Windows;
using System.Windows.Navigation;
using Microsoft.Phone.Controls;

namespace ArtHub
{
    public partial class SourceEditPage : PhoneApplicationPage
    {
        private Source _editing;
        private bool _filled;

        public SourceEditPage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (_filled) return;
            _filled = true;

            string idxText;
            int idx;
            if (NavigationContext.QueryString.TryGetValue("idx", out idxText)
                && int.TryParse(idxText, out idx)
                && idx >= 0 && idx < Sources.All.Count)
            {
                _editing = Sources.All[idx];
                TitleText.Text = "edit source";
                NameBox.Text = _editing.Name ?? "";
                UrlBox.Text = _editing.Url ?? "";
                KeyBox.Text = _editing.ApiKey ?? "";
                UserBox.Text = _editing.UserId ?? "";
            }
            else
            {
                TitleText.Text = "new source";
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            string name = (NameBox.Text ?? "").Trim();
            string url = Sources.NormalizeUrl(UrlBox.Text);

            if (name.Length == 0 || url == null)
            {
                MessageBox.Show("Enter a name and a valid address, for example https://safebooru.org");
                return;
            }

            string key = (KeyBox.Text ?? "").Trim();
            string user = (UserBox.Text ?? "").Trim();

            if (_editing != null)
            {
                _editing.Name = name;
                _editing.Url = url;
                _editing.ApiKey = key;
                _editing.UserId = user;
                Sources.Save();
            }
            else
            {
                Sources.Add(new Source { Name = name, Url = url, ApiKey = key, UserId = user });
            }

            NavigationService.GoBack();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            NavigationService.GoBack();
        }
    }
}