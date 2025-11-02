using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Data;
using System.Data.SqlClient;

namespace KanbanSimConfigEditor
{
    internal class ConfigurationTable
    {
        /// <summary>
        /// Returns (config_description, config_value) rows for the grid.
        /// Maps to MainWindow.ConfigurationEditor: configSetting, configValue.
        /// </summary>
        public List<MainWindow.ConfigurationEditor> LoadAll(SqlConnection sql)
        {
            if (sql == null || sql.State != ConnectionState.Open)
                throw new InvalidOperationException("Database connection is not open.");

            var list = new List<MainWindow.ConfigurationEditor>();
            const string query = @"
                SELECT config_description, config_value
                FROM dbo.ConfigurationSettings
                ORDER BY config_id;";

            using (var cmd = new SqlCommand(query, sql))
            using (var rdr = cmd.ExecuteReader())
            {
                while (rdr.Read())
                {
                    var desc = rdr.GetString(0);
                    var val = rdr.GetDecimal(1);

                    list.Add(new MainWindow.ConfigurationEditor
                    {
                        configSetting = desc,
                        configValue = val
                    });
                }
            }
            return list;
        }

        /// <summary>
        /// Persists changes by matching on config_description.
        /// Validates values against DECIMAL(10,2).
        /// </summary>
        public void UpdateAll(SqlConnection sql, IEnumerable<MainWindow.ConfigurationEditor> items)
        {
            if (sql == null || sql.State != ConnectionState.Open)
                throw new InvalidOperationException("Database connection is not open.");
            if (items == null)
                throw new ArgumentNullException(nameof(items));

            const string update = @"
                UPDATE dbo.ConfigurationSettings
                SET config_value = @val
                WHERE config_description = @desc;";

            using (var tx = sql.BeginTransaction())
            {
                try
                {
                    foreach (var item in items)
                    {
                        if (item == null)
                            throw new ArgumentNullException("A configuration row was null.");

                        // Validate DECIMAL(10,2) domain (adjust if negatives should be allowed)
                        if (!IsValidDecimal10_2(item.configValue))
                            throw new ArgumentOutOfRangeException(
                                $"Value '{item.configValue}' for '{item.configSetting}' is outside DECIMAL(10,2) range (0..99,999,999.99).");

                        using (var cmd = new SqlCommand(update, sql, tx))
                        {
                            var rounded = Math.Round(item.configValue, 2, MidpointRounding.AwayFromZero);

                            var pVal = cmd.Parameters.Add(new SqlParameter("@val", SqlDbType.Decimal));
                            pVal.Precision = 10; pVal.Scale = 2; pVal.Value = rounded;

                            cmd.Parameters.Add("@desc", SqlDbType.VarChar, 50).Value = item.configSetting;

                            var rows = cmd.ExecuteNonQuery();
                            if (rows != 1)
                                throw new InvalidOperationException($"Update failed for '{item.configSetting}'.");
                        }
                    }

                    tx.Commit();
                }
                catch
                {
                    try { tx.Rollback(); } catch { /* ignore */ }
                    throw;
                }
            }
        }

        private static bool IsValidDecimal10_2(decimal value)
        {
            return value >= 0m && value <= 99999999.99m;
        }
    }
}
