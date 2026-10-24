using MySqlConnector;
using System;
using System.Threading.Tasks;

namespace Framework.Database
{
    // Connection-scoped serialization shared by WorldServer and BattleNot. A
    // pooled connection must never be returned while still owning an advisory lock.
    public sealed class MySqlNamedLock : IDisposable, IAsyncDisposable
    {
        readonly MySqlConnection _connection;
        readonly string _name;
        bool _released;
        MySqlNamedLock(MySqlConnection connection, string name) { _connection = connection; _name = name; }
        static MySqlCommand Command(MySqlConnection connection, string function, string name)
        {
            var command = connection.CreateCommand();
            command.CommandText = "SELECT " + function + "(CONCAT(DATABASE(), ':', @lockName)" + (function == "GET_LOCK" ? ", 5)" : ")");
            command.Parameters.AddWithValue("@lockName", name); return command;
        }
        public static MySqlNamedLock Acquire(MySqlConnection connection, string name)
        {
            using var command = Command(connection, "GET_LOCK", name);
            if (Convert.ToInt32(command.ExecuteScalar()) != 1) throw new InvalidOperationException("Account creation is busy; retry shortly.");
            return new(connection, name);
        }
        public static async Task<MySqlNamedLock> AcquireAsync(MySqlConnection connection, string name)
        {
            await using var command = Command(connection, "GET_LOCK", name);
            if (Convert.ToInt32(await command.ExecuteScalarAsync()) != 1) throw new InvalidOperationException("Account creation is busy; retry shortly.");
            return new(connection, name);
        }
        public void Dispose()
        {
            if (_released) return;
            _released = true;
            using var command = Command(_connection, "RELEASE_LOCK", _name); command.ExecuteScalar();
        }
        public async ValueTask DisposeAsync()
        {
            if (_released) return;
            _released = true;
            await using var command = Command(_connection, "RELEASE_LOCK", _name); await command.ExecuteScalarAsync();
        }
    }
}
