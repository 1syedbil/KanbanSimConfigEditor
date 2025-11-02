using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Data;
using System.Data.SqlClient;
using System.Windows.Threading;
using System.Globalization;

namespace KanbanSimConfigEditor
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly Connection _connection = new Connection();
        private readonly ConfigurationTable _configTable = new ConfigurationTable();
        private System.Collections.Generic.List<ConfigurationEditor> _baselineRows;

        public MainWindow()
        {
            InitializeComponent();

            // Ensure the grid does NOT auto-generate extra columns
            dataGridConfigEditor.AutoGenerateColumns = false;

            dataGridConfigEditor.CanUserAddRows = false;

            // Wire the submit click if not wired in XAML
            btnSubmitSettings.Click += btnSubmitSettings_Click;
        }

        private void btnConnectToDB_Click(object sender, RoutedEventArgs e)
        {
            var cs = (txtConnectionString?.Text ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(cs))
            {
                MessageBox.Show("Please enter a connection string.", "Missing Input",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (IsAllDigits(cs))
            {
                MessageBox.Show("This doesn’t look like a valid connection string.", "Invalid Input",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var ok = _connection.TryOpen(cs);
            if (!ok)
            {
                var msg = string.IsNullOrWhiteSpace(_connection.LastError)
                    ? "Failed to connect."
                    : _connection.LastError;

                MessageBox.Show(msg, "Connection Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Success → flip visibility (use your actual names)
            lblEnterString.Visibility = Visibility.Collapsed;
            txtConnectionString.Visibility = Visibility.Collapsed;
            btnConnectToDB.Visibility = Visibility.Collapsed;

            dataGridConfigEditor.Visibility = Visibility.Visible;
            btnSubmitSettings.Visibility = Visibility.Visible;

            // Load rows (only description + value), bind to existing columns
            try
            {
                var rows = _configTable.LoadAll(_connection.Sql);
                dataGridConfigEditor.ItemsSource = rows;  // uses configSetting/configValue bindings you already have
                _baselineRows = CloneRows(rows);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load configuration settings.\n\n" + ex.Message,
                    "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnSubmitSettings_Click(object sender, RoutedEventArgs e)
        {
            // Commit any in-progress cell/row edit
            dataGridConfigEditor.CommitEdit(DataGridEditingUnit.Cell, true);
            dataGridConfigEditor.CommitEdit(DataGridEditingUnit.Row, true);

            var items = dataGridConfigEditor.ItemsSource as System.Collections.Generic.IEnumerable<ConfigurationEditor>;
            if (items == null)
            {
                MessageBox.Show("Nothing to submit.", "No Data",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 1) REVERT any edits to the left column (Configuration Setting / configSetting)
            if (_baselineRows == null || _baselineRows.Count != dataGridConfigEditor.Items.Count)
            {
                RestoreFromDatabase();
                MessageBox.Show("Configuration Setting names cannot be changed. Reverted to original values.",
                    "Edit Not Allowed", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool anyDescEdited = false;
            int dataRowIndex = 0;
            foreach (var obj in dataGridConfigEditor.Items)
            {
                if (obj == System.Windows.Data.CollectionView.NewItemPlaceholder) continue;
                var row = obj as ConfigurationEditor;
                if (row == null) continue;

                var baseline = _baselineRows[dataRowIndex];

                if (!string.Equals(row.configSetting, baseline.configSetting, StringComparison.Ordinal))
                {
                    row.configSetting = baseline.configSetting; // revert to original
                    anyDescEdited = true;
                }

                dataRowIndex++;
            }

            if (anyDescEdited)
            {
                dataGridConfigEditor.Items.Refresh();
                MessageBox.Show("Configuration Setting names cannot be changed. The edited cell(s) were reverted.",
                    "Edit Not Allowed", MessageBoxButton.OK, MessageBoxImage.Warning);
                return; // stop here; user can re-submit after seeing the revert
            }

            // 2) Ensure ALL values in the right column are numbers
            if (!AllSettingValuesAreNumbers())
            {
                MessageBox.Show(
                    "All values in the 'Configuration Setting Value' column must be numbers (decimal or whole number). " +
                    "Please correct the highlighted value(s) and try again.",
                    "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return; // STOP: do not hit the database
            }

            // 3) Domain validation for DECIMAL(10,2)
            foreach (var item in items)
            {
                if (item == null)
                {
                    MessageBox.Show("A row is empty. Please correct and try again.",
                        "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!IsDecimal10_2(item.configValue))
                {
                    MessageBox.Show(
                        $"Invalid value for '{item.configSetting}'. Please enter a number between 0 and 99,999,999.99.",
                        "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            // 4) Persist to DB
            try
            {
                _configTable.UpdateAll(_connection.Sql, items);
                MessageBox.Show("Configuration settings updated successfully.",
                    "Success", MessageBoxButton.OK, MessageBoxImage.Information);

                // Reload and refresh baseline so future reverts use the newest values
                var refreshed = _configTable.LoadAll(_connection.Sql);
                dataGridConfigEditor.ItemsSource = refreshed;
                _baselineRows = CloneRows(refreshed);
            }
            catch (System.Data.SqlClient.SqlException ex)
            {
                MessageBox.Show("A database error occurred while saving settings.\n\n" + ex.Message,
                    "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to save settings.\n\n" + ex.Message,
                    "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static System.Collections.Generic.List<ConfigurationEditor> CloneRows(System.Collections.Generic.IEnumerable<ConfigurationEditor> source)
        {
            var clone = new System.Collections.Generic.List<ConfigurationEditor>();
            foreach (var r in source)
            {
                clone.Add(new ConfigurationEditor
                {
                    configSetting = r.configSetting,
                    configValue = r.configValue
                });
            }
            return clone;
        }

        private void RestoreFromDatabase()
        {
            try
            {
                var rows = _configTable.LoadAll(_connection.Sql);
                dataGridConfigEditor.ItemsSource = rows;
                _baselineRows = CloneRows(rows);
                dataGridConfigEditor.Items.Refresh();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to reload configuration settings.\n\n" + ex.Message,
                    "Reload Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool AllSettingValuesAreNumbers()
        {
            // Make sure visual tree is ready for cells we might touch
            dataGridConfigEditor.UpdateLayout();

            // Index of your "Configuration Setting Value" column
            var colIndex = -1;
            for (int i = 0; i < dataGridConfigEditor.Columns.Count; i++)
            {
                if (dataGridConfigEditor.Columns[i].Equals(columnConfigSettingVal))
                {
                    colIndex = i;
                    break;
                }
            }
            if (colIndex < 0) return false; // column must exist

            // Iterate through *data* items, skipping placeholder/non-data rows
            for (int rowIndex = 0; rowIndex < dataGridConfigEditor.Items.Count; rowIndex++)
            {
                var item = dataGridConfigEditor.Items[rowIndex];

                // Skip non-data items (e.g., NewItemPlaceholder if CanUserAddRows is ever turned back on)
                if (item == System.Windows.Data.CollectionView.NewItemPlaceholder) continue;
                var bound = item as ConfigurationEditor;
                if (bound == null) continue; // not a data row; ignore

                // Prefer reading the committed value from the model.
                // If it's already a decimal, it's numeric by definition.
                // (If an edit wasn't committed, the commit lines above force it.)
                // Still, double-check via decimal parsing of the formatted text if you want to be strict.

                // Use the cell's displayed text only if you want to catch odd formatting:
                string rawText = null;
                var row = (DataGridRow)dataGridConfigEditor.ItemContainerGenerator.ContainerFromIndex(rowIndex);
                if (row != null)
                {
                    var cellContent = dataGridConfigEditor.Columns[colIndex].GetCellContent(row);
                    if (cellContent is TextBox tbx) rawText = tbx.Text;
                    else if (cellContent is TextBlock tb) rawText = tb.Text;
                }

                // Fall back to the bound decimal if we didn't get a visual element
                if (string.IsNullOrWhiteSpace(rawText))
                    rawText = bound.configValue.ToString(System.Globalization.CultureInfo.CurrentCulture);

                rawText = rawText?.Trim();
                if (string.IsNullOrEmpty(rawText)) return false;

                decimal parsed;
                if (!TryParseDecimalFlexible(rawText, out parsed))
                {
                    // Focus the bad cell to help the user fix it
                    dataGridConfigEditor.SelectedIndex = rowIndex;
                    dataGridConfigEditor.CurrentCell = new DataGridCellInfo(item, dataGridConfigEditor.Columns[colIndex]);
                    dataGridConfigEditor.ScrollIntoView(item, dataGridConfigEditor.Columns[colIndex]);
                    return false;
                }
            }

            return true;
        }

        private static bool TryParseDecimalFlexible(string s, out decimal value)
        {
            // Accept integers and decimals using current culture OR invariant culture (e.g., "." as decimal)
            // Also allow leading/trailing whitespace which is already trimmed by caller.
            if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.CurrentCulture, out value))
                return true;
            if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out value))
                return true;

            value = 0m;
            return false;
        }

        private static bool IsAllDigits(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++)
            {
                if (!char.IsDigit(s[i])) return false;
            }
            return true;
        }

        private static bool IsDecimal10_2(decimal value)
        {
            return value >= 0m && value <= 99999999.99m;
        }

        // EXACTLY the properties your XAML binds to:
        public class ConfigurationEditor
        {
            public string configSetting { get; set; }  // maps to config_description
            public decimal configValue { get; set; }  // maps to config_value
        }
    }
}
