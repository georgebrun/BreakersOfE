using BreakersOfE.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace BreakersOfE.Windows
{
    // ── View model for each value row ─────────────────────────────────────────
    public class ValueItem
    {
        public string DisplayValue { get; set; } = string.Empty;
        public string ActualValue { get; set; } = string.Empty;
        public bool IsChecked { get; set; } = true;
        public bool IsAll { get; set; } = false;
    }

    public partial class ColumnFilterPopup : Window
    {
        // ── State ─────────────────────────────────────────────────────────────
        private ColumnFilterState _filterState;
        private List<string> _allValues;
        private ObservableCollection<ValueItem> _displayItems = new();
        private bool _suppressEvents = false;

        // ── Event fired when filter changes ──────────────────────────────────
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
            _filterState = existingState;
            _allValues = allValues
                .OrderBy(v => v)
                .ToList();

            PopulateOperatorCombo();
            PopulateValuesList(string.Empty);
            RestoreState();

            Deactivated += (s, e) =>
            {
                // Auto-close when user clicks away
                // Don't close — let user close manually
            };
        }

        // ════════════════════════════════════════════════════════════════════
        // POPULATE
        // ════════════════════════════════════════════════════════════════════
        private void PopulateValuesList(string searchText)
        {
            _suppressEvents = true;
            _displayItems.Clear();

            // (All) item always first
            var allItem = new ValueItem
            {
                DisplayValue = "(All)",
                ActualValue = "__ALL__",
                IsChecked = _filterState.AllSelected,
                IsAll = true
            };
            _displayItems.Add(allItem);

            // Filter by search text
            var filtered = string.IsNullOrWhiteSpace(searchText)
                ? _allValues
                : _allValues.Where(v =>
                    v.Contains(searchText,
                        StringComparison.OrdinalIgnoreCase)).ToList();

            foreach (var val in filtered)
            {
                _displayItems.Add(new ValueItem
                {
                    DisplayValue = string.IsNullOrEmpty(val) ? "(blank)" : val,
                    ActualValue = val,
                    IsChecked = _filterState.AllSelected ||
                                   _filterState.SelectedValues.Contains(val)
                });
            }

            ValuesListBox.ItemsSource = _displayItems;
            _suppressEvents = false;
        }

        private void PopulateOperatorCombo()
        {
            OperatorCombo.Items.Clear();
            foreach (ColumnFilterOperator op in
                Enum.GetValues<ColumnFilterOperator>())
            {
                OperatorCombo.Items.Add(
                    ColumnFilterState.GetOperatorLabel(op));
            }
            OperatorCombo.SelectedIndex =
                (int)_filterState.TextOperator;
        }

        private void RestoreState()
        {
            _suppressEvents = true;

            // Restore text filter
            OperatorCombo.SelectedIndex = (int)_filterState.TextOperator;
            TextFilterBox.Text = _filterState.TextValue;

            // Show/hide text box based on operator
            UpdateTextBoxVisibility();

            // Switch to correct tab
            if (_filterState.UseTextFilter)
                MainTabControl.SelectedIndex = 1;

            _suppressEvents = false;
        }

        private void UpdateTextBoxVisibility()
        {
            var op = (ColumnFilterOperator)OperatorCombo.SelectedIndex;
            TextFilterBox.Visibility =
                op == ColumnFilterOperator.IsBlank ||
                op == ColumnFilterOperator.IsNotBlank
                    ? Visibility.Collapsed
                    : Visibility.Visible;
        }

        // ════════════════════════════════════════════════════════════════════
        // VALUES TAB HANDLERS
        // ════════════════════════════════════════════════════════════════════
        private void ValueSearchBox_TextChanged(object sender,
            TextChangedEventArgs e)
        {
            PopulateValuesList(ValueSearchBox.Text);
        }

        private void ValueCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressEvents) return;
            if (sender is not CheckBox cb) return;
            if (cb.DataContext is not ValueItem item) return;

            _suppressEvents = true;

            if (item.IsAll)
            {
                // (All) toggled — set all items to match
                bool check = cb.IsChecked == true;
                foreach (var vi in _displayItems)
                    vi.IsChecked = check;

                _filterState.AllSelected = check;
                if (check)
                    _filterState.SelectedValues.Clear();
                else
                    _filterState.SelectedValues.Clear();

                // Refresh list
                ValuesListBox.Items.Refresh();
            }
            else
            {
                // Individual item toggled
                bool check = cb.IsChecked == true;

                if (check)
                {
                    if (!_filterState.SelectedValues
                            .Contains(item.ActualValue))
                        _filterState.SelectedValues
                            .Add(item.ActualValue);
                }
                else
                {
                    _filterState.SelectedValues
                        .Remove(item.ActualValue);
                    _filterState.AllSelected = false;

                    // Uncheck (All)
                    var allItem = _displayItems
                        .FirstOrDefault(x => x.IsAll);
                    if (allItem != null)
                        allItem.IsChecked = false;
                    ValuesListBox.Items.Refresh();
                }

                // Check if all individual items are checked
                var nonAllItems = _displayItems
                    .Where(x => !x.IsAll).ToList();
                bool allChecked = nonAllItems.All(x => x.IsChecked);
                _filterState.AllSelected = allChecked;

                var allItemRef = _displayItems
                    .FirstOrDefault(x => x.IsAll);
                if (allItemRef != null)
                {
                    allItemRef.IsChecked = allChecked;
                    ValuesListBox.Items.Refresh();
                }
            }

            _filterState.UseTextFilter = false;
            _suppressEvents = false;

            FilterChanged?.Invoke(this, EventArgs.Empty);
        }

        // ════════════════════════════════════════════════════════════════════
        // TEXT FILTER TAB HANDLERS
        // ════════════════════════════════════════════════════════════════════
        private void OperatorCombo_SelectionChanged(object sender,
            SelectionChangedEventArgs e)
        {
            if (_suppressEvents) return;

            _filterState.TextOperator =
                (ColumnFilterOperator)OperatorCombo.SelectedIndex;
            UpdateTextBoxVisibility();

            _filterState.UseTextFilter =
                !string.IsNullOrEmpty(_filterState.TextValue) ||
                _filterState.TextOperator ==
                    ColumnFilterOperator.IsBlank ||
                _filterState.TextOperator ==
                    ColumnFilterOperator.IsNotBlank;

            FilterChanged?.Invoke(this, EventArgs.Empty);
        }

        private void TextFilterBox_TextChanged(object sender,
            TextChangedEventArgs e)
        {
            if (_suppressEvents) return;

            _filterState.TextValue = TextFilterBox.Text;
            _filterState.UseTextFilter =
                !string.IsNullOrEmpty(_filterState.TextValue);

            FilterChanged?.Invoke(this, EventArgs.Empty);
        }

        private void BtnClearTextFilter_Click(object sender,
            RoutedEventArgs e)
        {
            _suppressEvents = true;
            TextFilterBox.Text = string.Empty;
            OperatorCombo.SelectedIndex = 0;
            _suppressEvents = false;

            _filterState.TextValue = string.Empty;
            _filterState.UseTextFilter = false;
            _filterState.TextOperator = ColumnFilterOperator.Contains;

            FilterChanged?.Invoke(this, EventArgs.Empty);
        }

        // ════════════════════════════════════════════════════════════════════
        // BOTTOM BUTTONS
        // ════════════════════════════════════════════════════════════════════
        private void BtnClearFilter_Click(object sender, RoutedEventArgs e)
        {
            _filterState.Clear();
            _suppressEvents = true;
            TextFilterBox.Text = string.Empty;
            OperatorCombo.SelectedIndex = 0;
            _suppressEvents = false;
            PopulateValuesList(ValueSearchBox.Text);
            FilterChanged?.Invoke(this, EventArgs.Empty);
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}