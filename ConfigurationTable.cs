// FILE          : ConfigurationTable.cs
// PROJECT       : Advanced SQL Project Milestone 1
// PROGRAMMER    : Bilal Syed
// FIRST VERSION : 2025-11-01
// DESCRIPTION   : Handles ADO.NET data access for ConfigurationSettings. 
//                 Retrieves and updates config_description and config_value 
//                 fields from the database for the Configuration Editor tool.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Data;
using System.Data.SqlClient;

namespace KanbanSimConfigEditor
{
    // NAME    : ConfigurationTable 
    // PURPOSE : Provides utilities for reading and updating the ConfigurationSettings
    //           table in the KanbanDB database using ADO.NET.
    internal class ConfigurationTable
    {
        // METHOD      : LoadAll 
        // DESCRIPTION : Reads all configuration rows (description and value)
        //               from the ConfigurationSettings table in sorted order.
        // PARAMETERS  : sql -> Active SqlConnection object.
        // RETURNS     : List<MainWindow.ConfigurationEditor> containing descriptions and values.
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

        // METHOD      : UpdateAll 
        // DESCRIPTION : Updates config_value in the ConfigurationSettings table
        //               for each row based on config_description.
        // PARAMETERS  : sql -> Active SqlConnection; items -> list of rows to update.
        // RETURNS     : void. Throws exceptions if validation or update fails.
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

        // METHOD      : IsValidDecimal10_2 
        // DESCRIPTION : Validates that a decimal fits within the DECIMAL(10,2) range.
        // PARAMETERS  : value -> decimal number to check.
        // RETURNS     : bool -> true if within range; otherwise false.
        private static bool IsValidDecimal10_2(decimal value)
        {
            return value >= 0m && value <= 99999999.99m;
        }
    }
}