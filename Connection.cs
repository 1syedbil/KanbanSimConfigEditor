using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Data;
using System.Data.SqlClient;

namespace KanbanSimConfigEditor
{
    internal sealed class Connection : IDisposable
    {
        private SqlConnection _sqlConnection;

        public bool IsConnected => _sqlConnection != null && _sqlConnection.State == ConnectionState.Open;

        public SqlConnection Sql => _sqlConnection;

        public string LastError { get; private set; }

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

        public void Dispose()
        {
            Close();
        }
    }
}
