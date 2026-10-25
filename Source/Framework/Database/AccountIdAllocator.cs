using MySqlConnector;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Framework.Database
{
    // Legacy SI IDs poisoned account AUTO_INCREMENT counters at UINT_MAX. Keep a durable
    // positive-range high-water mark so deleting the newest account cannot cause
    // an old identity to be reused. Callers hold the shared lock for their account table.
    public static class AccountIdAllocator
    {
        static string Identifier(string value)
        {
            if (value.Length == 0 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_')) throw new ArgumentException("Invalid allocator table identifier.");
            return "`" + value + "`";
        }
        public static void Ensure(MySqlConnection connection, string accounts = "account", string sequence = "account_id_allocator")
        {
            using var create = new MySqlCommand($"CREATE TABLE IF NOT EXISTS {Identifier(sequence)} (id TINYINT UNSIGNED NOT NULL PRIMARY KEY, next_id BIGINT UNSIGNED NOT NULL) ENGINE=InnoDB",connection);
            create.ExecuteNonQuery();
            using var seed = new MySqlCommand($"INSERT IGNORE INTO {Identifier(sequence)} (id,next_id) SELECT 1,COALESCE(MAX(id),0)+1 FROM {Identifier(accounts)} WHERE id<=2147483647",connection);
            seed.ExecuteNonQuery();
        }
        public static Task EnsureAsync(MySqlConnection connection) => EnsureAsync(connection, "account", "account_id_allocator");
        public static async Task EnsureAsync(MySqlConnection connection, string accounts, string sequence)
        {
            await using var create = new MySqlCommand($"CREATE TABLE IF NOT EXISTS {Identifier(sequence)} (id TINYINT UNSIGNED NOT NULL PRIMARY KEY, next_id BIGINT UNSIGNED NOT NULL) ENGINE=InnoDB",connection);
            await create.ExecuteNonQueryAsync();
            await using var seed = new MySqlCommand($"INSERT IGNORE INTO {Identifier(sequence)} (id,next_id) SELECT 1,COALESCE(MAX(id),0)+1 FROM {Identifier(accounts)} WHERE id<=2147483647",connection);
            await seed.ExecuteNonQueryAsync();
        }
        static string NextSql(string accounts, string sequence) => $"SELECT GREATEST(next_id,COALESCE((SELECT MAX(id)+1 FROM {Identifier(accounts)} WHERE id<=2147483647),1)) FROM {Identifier(sequence)} WHERE id=1";
        static uint Valid(object result)
        {
            ulong id = Convert.ToUInt64(result);
            if (id == 0 || id > int.MaxValue) throw new InvalidOperationException("The positive game-account ID range is exhausted.");
            return (uint)id;
        }
        public static uint Reserve(MySqlConnection connection, string accounts = "account", string sequence = "account_id_allocator")
        {
            using var read = new MySqlCommand(NextSql(accounts,sequence),connection);
            uint id = Valid(read.ExecuteScalar());
            using var advance = new MySqlCommand($"UPDATE {Identifier(sequence)} SET next_id=@next WHERE id=1",connection);
            advance.Parameters.AddWithValue("@next",(ulong)id+1); advance.ExecuteNonQuery(); return id;
        }
        public static Task<uint> ReserveAsync(MySqlConnection connection, MySqlTransaction transaction) => ReserveAsync(connection, transaction, "account", "account_id_allocator");
        public static async Task<uint> ReserveAsync(MySqlConnection connection, MySqlTransaction transaction, string accounts, string sequence)
        {
            await using var read = new MySqlCommand(NextSql(accounts,sequence),connection,transaction);
            uint id = Valid(await read.ExecuteScalarAsync());
            await using var advance = new MySqlCommand($"UPDATE {Identifier(sequence)} SET next_id=@next WHERE id=1",connection,transaction);
            advance.Parameters.AddWithValue("@next",(ulong)id+1); await advance.ExecuteNonQueryAsync(); return id;
        }
    }
}
