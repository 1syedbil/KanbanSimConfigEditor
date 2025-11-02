// FILE          : Connection.cs
// PROJECT       : Advanced SQL Project Milestone 1
// PROGRAMMER    : Bilal Syed
// FIRST VERSION : 2025-11-01
// DESCRIPTION   : Manages SQL Server database connectivity for the Configuration Editor.
//                 Handles opening, closing, and disposing of SqlConnection instances
//                 using ADO.NET with simple error handling.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Data;
using System.Data.SqlClient;

namespace KanbanSimConfigEditor
{
    // NAME    : Connection
    // PURPOSE : Provides methods to establish, close, and manage an active SQL Server
    //           connection using ADO.NET. Simplifies error handling for connection logic.
    internal sealed class Connection : IDisposable
    {
        private SqlConnection _sqlConnection;

        public bool IsConnected => _sqlConnection != null && _sqlConnection.State == ConnectionState.Open;

        public SqlConnection Sql => _sqlConnection;

        public string LastError { get; private set; }

        // METHOD      : TryOpen 
        // DESCRIPTION : Attempts to open a SQL connection using the given connection string.
        //               Captures error messages on failure and returns true on success.
        // PARAMETERS  : connectionString -> full connection string to the SQL Server.
        // RETURNS     : bool -> true if successfully connected; false otherwise.
        public bool TryOpen(string connectionString)
        {
            LastError = null;

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                LastError = "Connection string is empty.";
                return false;
            }

            Close();

            try
            {
                var conn = new SqlConnection(connectionString);
                conn.Open();
                _sqlConnection = conn;
                return true;
            }
            catch (SqlException ex)
            {
                LastError = ex.Message;
            }
            catch (InvalidOperationException ex)
            {
                LastError = ex.Message;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }

            return false;
        }

        // METHOD      : Close 
        // DESCRIPTION : Safely closes and disposes the current SqlConnection if open.
        // PARAMETERS  : none.
        // RETURNS     : void. Cleans up resources tied to the active connection.
        public void Close()
        {
            if (_sqlConnection != null)
            {
                try
                {
                    if (_sqlConnection.State != ConnectionState.Closed)
                        _sqlConnection.Close();
                }
                finally
                {
                    _sqlConnection.Dispose();
                    _sqlConnection = null;
                }
            }
        }

        // METHOD      : Dispose 
        // DESCRIPTION : Implements IDisposable to ensure connections are closed
        //               and disposed properly when the object is no longer needed.
        // PARAMETERS  : none.
        // RETURNS     : void.
        public void Dispose()
        {
            Close();
        }
    }
}