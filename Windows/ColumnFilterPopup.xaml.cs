using BreakersOfE.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace BreakersOfE.Windows
{
    // ── One row in the values list ────────────────────────────────────────────
    public class ValueItem : INotifyPropertyChanged
    {
        private bool _isChecked;

        public string DisplayValue { get; set; } = string.Empty;
        public string ActualValue { get; set; } = string.Empty;
        public bool IsAll { get; set; } = false;

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                _isChecked = value;
                PropertyChanged?.Invoke(this,
                    new PropertyChangedEventArgs(nameof(IsChecked)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public partial class ColumnFilterPopup : Window
    {
        private readonly ColumnFilterState _state;
        private readonly List<string> _allValues;
        private readonly List<ValueItem> _items = new();
        private bool _busy = false;

        // Fired whenever the filter changes so MainWindow can reload the grid
        public event EventHandler? FilterChanged;

        // ════════════════════════════════════════════════════════════════════
        // CONSTRUCTOR
        // ════════════════════════════════════════════════════════════════════
        public ColumnFilterPopup(
            string columnName,
            string propertyName,
            List<string> allValues,
            ColumnFilterState existingState)
        {
            InitializeComponent();

            Title = $"Filter: {columnName}";
            _state = existingState;
            _allValues = allValues.OrderBy(v => v).ToList();

            BuildItemList();
            PopulateOperatorCombo();
            RestoreTextState();
        }

        // ════════════════════════════════════════════════════════════════════
        // BUILD ITEM LIST
        // Built once on open. Never rebuilt. Search only scrolls.
        // ════════════════════════════════════════════════════════════════════
        private void BuildItemList()
        {
            _items.Clear();

            // Add every unique value — Select All is now a pinned control above
            foreach (var v in _allValues)
            {
                _items.Add(new ValueItem
                {
                    DisplayValue = string.IsNullOrEmpty(v) ? "(blank)" : v,
                    ActualValue = v,
                    IsAll = false,
                    IsChecked = _state.AllSelected ||
                                   _state.SelectedValues.Contains(v)
                });
            }

            // Store total count so summary can compute exclusions
            _state.TotalValueCount = _allValues.Count;

            ValuesListBox.ItemsSource = _items;

            // Sync the pinned Select All checkbox
            _busy = true;
            ChkSelectAll.IsChecked = _state.AllSelected;
            _busy = false;
        }

        // ════════════════════════════════════════════════════════════════════
        // SEARCH BOX
        // Scrolls to the first item that starts with the typed text.
        // Does NOT remove any items from the list.
        // ════════════════════════════════════════════════════════════════════
        private void ValueSearchBox_TextChanged(object sender,
            TextChangedEventArgs e)
        {
            string txt = ValueSearchBox.Text;
            if (string.IsNullOrEmpty(txt)) return;

            var match = _items.FirstOrDefault(x =>
                !x.IsAll &&
                x.DisplayValue.StartsWith(txt,
                    StringComparison.OrdinalIgnoreCase));

            if (match == null) return;

            // Scroll matched item to top of visible area
            ValuesListBox.ScrollIntoView(_items.Last());
            ValuesListBox.UpdateLayout();
            ValuesListBox.ScrollIntoView(match);
        }

        // ════════════════════════════════════════════════════════════════════
        // PINNED SELECT ALL CHECKBOX
        // ════════════════════════════════════════════════════════════════════
        private void SelectAll_Changed(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            _busy = true;

            bool check = ChkSelectAll.IsChecked == true;
            foreach (var vi in _items)
                vi.IsChecked = check;

            _state.AllSelected = check;
            _state.SelectedValues.Clear();
            _state.UseTextFilter = false;

            _busy = false;
            FilterChanged?.Invoke(this, EventArgs.Empty);
        }

        // ════════════════════════════════════════════════════════════════════
        // CHECKBOX CHANGED
        // ════════════════════════════════════════════════════════════════════
        private void ValueCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            if (sender is not CheckBox cb) return;
            if (cb.DataContext is not ValueItem item) return;

            _busy = true;

            if (item.IsChecked)
            {
                if (!_state.SelectedValues.Contains(item.ActualValue))
                    _state.SelectedValues.Add(item.ActualValue);
            }
            else
            {
                _state.SelectedValues.Remove(item.ActualValue);
                _state.AllSelected = false;
            }

            // If every value row is now checked → treat as AllSelected
            bool allNowChecked = _items.All(x => x.IsChecked);
            if (allNowChecked)
            {
                _state.AllSelected = true;
                _state.SelectedValues.Clear();
            }

            // Sync pinned checkbox
            ChkSelectAll.IsChecked = _state.AllSelected;

            _state.UseTextFilter = false;
            _busy = false;

            FilterChanged?.Invoke(this, EventArgs.Empty);
        }

        // ════════════════════════════════════════════════════════════════════
        // TEXT FILTER TAB
        // ════════════════════════════════════════════════════════════════════
        private void PopulateOperatorCombo()
        {
            OperatorCombo.Items.Clear();
            foreach (ColumnFilterOperator op in
                Enum.GetValues<ColumnFilterOperator>())
                OperatorCombo.Items.Add(
                    ColumnFilterState.GetOperatorLabel(op));
            OperatorCombo.SelectedIndex = (int)_state.TextOperator;
        }

        private void RestoreTextState()
        {
            _busy = true;
            OperatorCombo.SelectedIndex = (int)_state.TextOperator;
            TextFilterBox.Text = _state.TextValue;
            UpdateTextBoxVisibility();
            if (_state.UseTextFilter)
                MainTabControl.SelectedIndex = 1;
            _busy = false;
        }

        private void UpdateTextBoxVisibility()
        {
            var op = (ColumnFilterOperator)OperatorCombo.SelectedIndex;
            TextFilterBox.Visibility =
                op == ColumnFilterOperator.IsBlank ||
                op == ColumnFilterOperator.IsNotBlank
                    ? Visibility.Collapsed : Visibility.Visible;
        }

        private void OperatorCombo_SelectionChanged(object sender,
            SelectionChangedEventArgs e)
        {
            if (_busy) return;
            _state.TextOperator =
                (ColumnFilterOperator)OperatorCombo.SelectedIndex;
            UpdateTextBoxVisibility();
            _state.UseTextFilter =
                !string.IsNullOrEmpty(_state.TextValue) ||
                _state.TextOperator == ColumnFilterOperator.IsBlank ||
                _state.TextOperator == ColumnFilterOperator.IsNotBlank;
            FilterChanged?.Invoke(this, EventArgs.Empty);
        }

        private void TextFilterBox_TextChanged(object sender,
            TextChangedEventArgs e)
        {
            if (_busy) return;
            // Only update state, don't fire FilterChanged yet —
            // firing on every keystroke causes grid reload which steals focus
            _state.TextValue = TextFilterBox.Text;
            _state.UseTextFilter = !string.IsNullOrEmpty(_state.TextValue);
        }

        private void TextFilterBox_KeyDown(object sender,
            System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                _state.TextValue = TextFilterBox.Text;
                _state.UseTextFilter = !string.IsNullOrEmpty(_state.TextValue);
                FilterChanged?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
        }

        private void BtnApplyTextFilter_Click(object sender, RoutedEventArgs e)
        {
            _state.TextValue = TextFilterBox.Text;
            _state.UseTextFilter = !string.IsNullOrEmpty(_state.TextValue) ||
                _state.TextOperator == ColumnFilterOperator.IsBlank ||
                _state.TextOperator == ColumnFilterOperator.IsNotBlank;
            FilterChanged?.Invoke(this, EventArgs.Empty);
        }

        private void BtnClearTextFilter_Click(object sender, RoutedEventArgs e)
        {
            _busy = true;
            TextFilterBox.Text = string.Empty;
            OperatorCombo.SelectedIndex = 0;
            _busy = false;
            _state.TextValue = string.Empty;
            _state.UseTextFilter = false;
            _state.TextOperator = ColumnFilterOperator.Contains;
            FilterChanged?.Invoke(this, EventArgs.Empty);
        }

        // ════════════════════════════════════════════════════════════════════
        // CLEAR FILTER BUTTON
        // Clears THIS column's filter only.
        // Other column filters are untouched.
        // Funnel for this column goes gray.
        // Funnels for other active columns stay blue.
        // Popup closes.
        // ════════════════════════════════════════════════════════════════════
        private void BtnClearFilter_Click(object sender, RoutedEventArgs e)
        {
            _state.Clear();

            _busy = true;
            foreach (var item in _items)
                item.IsChecked = true;
            ChkSelectAll.IsChecked = true;
            TextFilterBox.Text = string.Empty;
            OperatorCombo.SelectedIndex = 0;
            _busy = false;

            FilterChanged?.Invoke(this, EventArgs.Empty);

            Dispatcher.BeginInvoke(new Action(() => Close()),
                System.Windows.Threading.DispatcherPriority.ContextIdle);
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
            => Close();
    }
}