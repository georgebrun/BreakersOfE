using System.Windows;
using System.Windows.Controls;

namespace BreakersOfE.Windows
{
    public partial class ColumnChooserPopup : Window
    {
        private readonly DataGrid _grid;

        public ColumnChooserPopup(DataGrid grid)
        {
            InitializeComponent();
            _grid = grid;
            BuildColumnList();
        }

        private void BuildColumnList()
        {
            ColumnPanel.Children.Clear();

            foreach (var col in _grid.Columns)
            {
                string header = col.Header?.ToString() ?? string.Empty;
                if (string.IsNullOrEmpty(header)) continue;

                var cb = new CheckBox
                {
                    Content = header,
                    IsChecked = col.Visibility == Visibility.Visible,
                    FontSize = 13,
                    Padding = new Thickness(4, 3, 0, 3),
                    Tag = col
                };
                cb.Checked += ColCheckBox_Changed;
                cb.Unchecked += ColCheckBox_Changed;
                ColumnPanel.Children.Add(cb);
            }
        }

        private void ColCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox cb) return;
            if (cb.Tag is not DataGridColumn col) return;

            col.Visibility = cb.IsChecked == true
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}