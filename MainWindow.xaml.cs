// FILE          : MainWindow.xaml.cs
// PROJECT       : Advanced SQL Project Milestone 1
// PROGRAMMER    : Bilal Syed
// FIRST VERSION : 2025-11-01
// DESCRIPTION   : Main window for the Configuration Editor UI. Handles DB connection toggling,
//                 populates/updates ConfigurationSettings via ADO.NET, and validates input.

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
    public partial class MainWindow : Window
    {
        private readonly Connection _connection = new Connection();
        private readonly ConfigurationTable _configTable = new ConfigurationTable();
        private List<ConfigurationEditor> _baselineRows;

        public MainWindow()
        {
            InitializeComponent();

            dataGridConfigEditor.AutoGenerateColumns = false;

            dataGridConfigEditor.CanUserAddRows = false;

            btnSubmitSettings.Click += btnSubmitSettings_Click;
        }

        // METHOD      : btnConnectToDB_Click 
        // DESCRIPTION : Validates the connection string, opens the DB connection via Connection,
        //               toggles UI visibility, and loads ConfigurationSettings into the grid.
        // PARAMETERS  : sender -> event source; e -> click event arguments.
        // RETURNS     : void. Shows message boxes on validation or connection errors.
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

            lblEnterString.Visibility = Visibility.Collapsed;
            txtConnectionString.Visibility = Visibility.Collapsed;
            btnConnectToDB.Visibility = Visibility.Collapsed;

            dataGridConfigEditor.Visibility = Visibility.Visible;
            btnSubmitSettings.Visibility = Visibility.Visible;

            try
            {
                var rows = _configTable.LoadAll(_connection.Sql);
                dataGridConfigEditor.ItemsSource = rows;
                _baselineRows = CloneRows(rows);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load configuration settings.\n\n" + ex.Message,
                    "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // METHOD      : btnSubmitSettings_Click 
        // DESCRIPTION : Commits in-progress edits, reverts illegal description edits,
        //               validates numeric values, then persists changes to the database.
        // PARAMETERS  : sender -> event source; e -> click event arguments.
        // RETURNS     : void. Displays messages on validation or persistence errors.
        private void btnSubmitSettings_Click(object sender, RoutedEventArgs e)
        {
            dataGridConfigEditor.CommitEdit(DataGridEditingUnit.Cell, true);
            dataGridConfigEditor.CommitEdit(DataGridEditingUnit.Row, true);

            var items = dataGridConfigEditor.ItemsSource as System.Collections.Generic.IEnumerable<ConfigurationEditor>;
            if (items == null)
            {
                MessageBox.Show("Nothing to submit.", "No Data",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

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
                if (obj == CollectionView.NewItemPlaceholder) continue;
                var row = obj as ConfigurationEditor;
                if (row == null) continue;

                var baseline = _baselineRows[dataRowIndex];

                if (!string.Equals(row.configSetting, baseline.configSetting, StringComparison.Ordinal))
                {
                    row.configSetting = baseline.configSetting;
                    anyDescEdited = true;
                }

                dataRowIndex++;
            }

            if (anyDescEdited)
            {
                dataGridConfigEditor.Items.Refresh();
                MessageBox.Show("Configuration Setting names cannot be changed. The edited cell(s) were reverted.",
                    "Edit Not Allowed", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!AllSettingValuesAreNumbers())
            {
                MessageBox.Show(
                    "All values in the 'Configuration Setting Value' column must be numbers (decimal or whole number). " +
                    "Please correct the highlighted value(s) and try again.",
                    "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

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

            try
            {
                _configTable.UpdateAll(_connection.Sql, items);
                MessageBox.Show("Configuration settings updated successfully.",
                    "Success", MessageBoxButton.OK, MessageBoxImage.Information);

                var refreshed = _configTable.LoadAll(_connection.Sql);
                dataGridConfigEditor.ItemsSource = refreshed;
                _baselineRows = CloneRows(refreshed);
            }
            catch (SqlException ex)
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

        // METHOD      : CloneRows
        // DESCRIPTION : Creates a shallow copy list of ConfigurationEditor rows for baseline
        //               comparison and restoration without mutating the live ItemsSource.
        // PARAMETERS  : source -> enumerable of ConfigurationEditor to clone.
        // RETURNS     : List<ConfigurationEditor> copy preserving values only.
        private static List<ConfigurationEditor> CloneRows(IEnumerable<ConfigurationEditor> source)
        {
            var clone = new List<ConfigurationEditor>();
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

        // METHOD      : RestoreFromDatabase 
        // DESCRIPTION : Reloads ConfigurationSettings from DB, resets ItemsSource and baseline,
        //               and refreshes the grid to discard local, invalid edits.
        // PARAMETERS  : none.
        // RETURNS     : void. Shows an error message if reload fails.
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

        // METHOD      : AllSettingValuesAreNumbers 
        // DESCRIPTION : Verifies every visible cell in the value column parses as a decimal
        //               (integer or fractional, current or invariant culture).
        // PARAMETERS  : none.
        // RETURNS     : bool -> true if all cells are numeric; otherwise false.
        private bool AllSettingValuesAreNumbers()
        {
            dataGridConfigEditor.UpdateLayout();

            var colIndex = -1;
            for (int i = 0; i < dataGridConfigEditor.Columns.Count; i++)
            {
                if (dataGridConfigEditor.Columns[i].Equals(columnConfigSettingVal))
                {
                    colIndex = i;
                    break;
                }
            }
            if (colIndex < 0) return false;

            for (int rowIndex = 0; rowIndex < dataGridConfigEditor.Items.Count; rowIndex++)
            {
                var item = dataGridConfigEditor.Items[rowIndex];

                if (item == CollectionView.NewItemPlaceholder) continue;
                var bound = item as ConfigurationEditor;
                if (bound == null) continue;

                string rawText = null;
                var row = (DataGridRow)dataGridConfigEditor.ItemContainerGenerator.ContainerFromIndex(rowIndex);
                if (row != null)
                {
                    var cellContent = dataGridConfigEditor.Columns[colIndex].GetCellContent(row);
                    if (cellContent is TextBox tbx) rawText = tbx.Text;
                    else if (cellContent is TextBlock tb) rawText = tb.Text;
                }

                if (string.IsNullOrWhiteSpace(rawText))
                    rawText = bound.configValue.ToString(CultureInfo.CurrentCulture);

                rawText = rawText?.Trim();
                if (string.IsNullOrEmpty(rawText)) return false;

                decimal parsed;
                if (!TryParseDecimalFlexible(rawText, out parsed))
                {
                    dataGridConfigEditor.SelectedIndex = rowIndex;
                    dataGridConfigEditor.CurrentCell = new DataGridCellInfo(item, dataGridConfigEditor.Columns[colIndex]);
                    dataGridConfigEditor.ScrollIntoView(item, dataGridConfigEditor.Columns[colIndex]);
                    return false;
                }
            }

            return true;
        }

        // METHOD      : TryParseDecimalFlexible 
        // DESCRIPTION : Attempts to parse a numeric string as decimal using current culture,
        //               then invariant culture (accepts integers and decimals).
        // PARAMETERS  : s -> input text; value -> parsed decimal out param.
        // RETURNS     : bool -> true if parse succeeded; otherwise false.
        private static bool TryParseDecimalFlexible(string s, out decimal value)
        {
            if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.CurrentCulture, out value))
                return true;
            if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out value))
                return true;

            value = 0m;
            return false;
        }

        // METHOD      : IsAllDigits 
        // DESCRIPTION : Checks whether a string contains only digit characters.
        // PARAMETERS  : s -> input string to test.
        // RETURNS     : bool -> true if all characters are digits; false otherwise.
        private static bool IsAllDigits(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++)
            {
                if (!char.IsDigit(s[i])) return false;
            }
            return true;
        }

        // METHOD      : IsDecimal10_2 
        // DESCRIPTION : Validates that a decimal fits the DECIMAL(10,2) non-negative range
        //               (0.00 to 99,999,999.99). Adjust if negatives are later allowed.
        // PARAMETERS  : value -> decimal to validate.
        // RETURNS     : bool -> true if within range; otherwise false.
        private static bool IsDecimal10_2(decimal value)
        {
            return value >= 0m && value <= 99999999.99m;
        }

        // NAME    : ConfigurationEditor
        // PURPOSE : DataGrid row model bound to existing columns:
        //           configSetting (description) and configValue (numeric value).
        public class ConfigurationEditor
        {
            public string configSetting { get; set; }
            public decimal configValue { get; set; }
        }
    }
}
