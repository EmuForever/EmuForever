// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Configuration;
using Framework.Threading;
using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Transactions;

namespace Framework.Database
{
    public record class MySqlConnectionInfo
    {
        public string ConnectionString            
        {
            get
            {
                return new MySqlConnectionStringBuilder
                {
                    Server = Host, Port = string.IsNullOrEmpty(PortOrSocket) ? 3306u : uint.Parse(PortOrSocket),
                    UserID = Username, Password = Password, Database = Database,
                    SslMode = UseSSL ? MySqlSslMode.Required : MySqlSslMode.Disabled,
                    AllowUserVariables = true, Pooling = true, MinimumPoolSize = 0,
                    MaximumPoolSize = (uint)Math.Clamp(Poolsize, 1, 128),
                    ConnectionIdleTimeout = 60, ConnectionTimeout = ConnectionTimeout,
                    DefaultCommandTimeout = CommandTimeout
                }.ConnectionString;
            }
        }

        public string Host;
        public string PortOrSocket;
        public bool UseSSL;
        public string Username;
        public string Password;
        public string Database;
        public int Poolsize = 16;
        public uint ConnectionTimeout = 15;
        public uint CommandTimeout = 60;
    }

    public struct DBVersion
    {
        public int Major { get; }
        public int Minor { get; }
        public int Build { get; }
        public bool IsMariaDB { get; }

        public DBVersion(int major, int minor, int build, bool isMariaDB)
        {
            Major = major;
            Minor = minor;
            Build = build;
            IsMariaDB = isMariaDB;
        }

        public static DBVersion Parse(string versionString)
        {
            int start = 0;
            int index = versionString.IndexOf('.', start);

            string val = versionString.Substring(start, index - start).Trim();
            int major = Convert.ToInt32(val, System.Globalization.NumberFormatInfo.InvariantInfo);

            start = index + 1;
            index = versionString.IndexOf('.', start);

            val = versionString.Substring(start, index - start).Trim();
            int minor = Convert.ToInt32(val, System.Globalization.NumberFormatInfo.InvariantInfo);

            start = index + 1;
            int i = start;
            while (i < versionString.Length && Char.IsDigit(versionString, i))
                i++;
            val = versionString.Substring(start, i - start).Trim();
            int build = Convert.ToInt32(val, System.Globalization.NumberFormatInfo.InvariantInfo);

            return new DBVersion(major, minor, build, versionString.Contains("Maria"));
        }

        public bool IsAtLeast(int majorNum, int minorNum, int buildNum)
        {
            if (Major > majorNum) return true;
            if (Major == majorNum && Minor > minorNum) return true;
            if (Major == majorNum && Minor == minorNum && Build >= buildNum) return true;
            return false;
        }
    }

    public abstract class MySqlBase<T> : IDataBase
    {
        static Dictionary<T, string> _preparedQueries = new();

        ProducerConsumerQueue<ISqlOperation> _queue = new();
        MySqlConnectionInfo _connectionInfo;
        DatabaseWorker<T> _worker;
        DBVersion version;

        public MySqlErrorCode Initialize(MySqlConnectionInfo connectionInfo)
        {
            _connectionInfo = connectionInfo;
            _worker = new DatabaseWorker<T>(_queue, this);            

            try
            {
                using var connection = new MySqlConnection(_connectionInfo.ConnectionString);
                connection.Open();

                version = DBVersion.Parse(connection.ServerVersion);
                Log.outInfo(LogFilter.SqlDriver, $"Connected to DB: {_connectionInfo.Database} Server: {(version.IsMariaDB ? "MariaDB" : "MySQL")} Ver: {connection.ServerVersion}");
                return MySqlErrorCode.None;
            }
            catch (MySqlException ex)
            {
                return HandleMySQLException(ex);
            }
        }

        public bool DirectExecute(string sql, params object[] args)
        {
            return DirectExecute(new PreparedStatement(string.Format(sql, args)));
        }

        public bool DirectExecute(PreparedStatement stmt) => DirectExecute(stmt, null);

        public bool DirectExecute(PreparedStatement stmt, string serializingLock) => ExecuteStatement(stmt,serializingLock,false,"account","account_id_allocator");
        public bool DirectExecuteWithAccountId(PreparedStatement stmt, string accounts = "account", string sequence = "account_id_allocator", string lockName = "game-account-create") =>
            DirectExecuteWithAccountId(stmt,accounts,sequence,lockName,7);
        public bool DirectExecuteWithAccountId(PreparedStatement stmt, string accounts, string sequence, string lockName, int parameterIndex) =>
            ExecuteStatement(stmt,lockName,true,accounts,sequence,parameterIndex);
        bool ExecuteStatement(PreparedStatement stmt,string serializingLock,bool allocateAccount,string accounts,string sequence,int parameterIndex = 7)
        {            
            try
            {

                using var connection = new MySqlConnection(_connectionInfo.ConnectionString); 
                connection.Open();
                using var guard = serializingLock == null ? null : MySqlNamedLock.Acquire(connection, serializingLock);
                if (allocateAccount)
                {
                    AccountIdAllocator.Ensure(connection,accounts,sequence);
                    stmt.SetUInt32(parameterIndex,AccountIdAllocator.Reserve(connection,accounts,sequence));
                }

                using MySqlCommand cmd = connection.CreateCommand();

                cmd.CommandText = stmt.CommandText;
                foreach (var parameter in stmt.Parameters)
                    cmd.Parameters.AddWithValue("@" + parameter.Key, parameter.Value);

                int affected = cmd.ExecuteNonQuery();
                return serializingLock == null || affected > 0;

            }
            catch (MySqlException ex)
            {
                HandleMySQLException(ex, stmt.CommandText, stmt.Parameters);
                return false;
            }
        }

        public void Execute(string sql, params object[] args)
        {
            Execute(new PreparedStatement(string.Format(sql, args)));
        }

        public void Execute(PreparedStatement stmt)
        {
            PreparedStatementTask task = new(stmt);
            _queue.Push(task);
        }

        public void ExecuteOrAppend(SQLTransaction trans, PreparedStatement stmt)
        {
            if (trans == null)
                Execute(stmt);
            else
                trans.Append(stmt);
        }

        public SQLResult Query(string sql, params object[] args)
        {
            return Query(new PreparedStatement(string.Format(sql, args)));
        }

        public SQLResult Query(PreparedStatement stmt)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionInfo.ConnectionString); 
                connection.Open();

                using MySqlCommand cmd = connection.CreateCommand();

                cmd.CommandText = stmt.CommandText;
                foreach (var parameter in stmt.Parameters)
                    cmd.Parameters.AddWithValue("@" + parameter.Key, parameter.Value);

                using var reader = cmd.ExecuteReader();
                return new SQLResult(reader);
            }
            catch (MySqlException ex)
            {
                HandleMySQLException(ex, stmt.CommandText, stmt.Parameters);
                return new SQLResult();
            }
        }

        public QueryCallback AsyncQuery(PreparedStatement stmt)
        {
            PreparedStatementTask task = new(stmt, true);
            // Store future result before enqueueing - task might get already processed and deleted before returning from this method
            Task<SQLResult> result = task.GetFuture();
            _queue.Push(task);
            return new QueryCallback(result);
        }

        public SQLQueryHolderCallback<R> DelayQueryHolder<R>(SQLQueryHolder<R> holder)
        {
            SQLQueryHolderTask<R> task = new(holder);
            // Store future result before enqueueing - task might get already processed and deleted before returning from this method
            Task<SQLQueryHolder<R>> result = task.GetFuture();
            _queue.Push(task);
            return new(result);
        }

        public void LoadPreparedStatements()
        {
            PreparedStatements();
        }

        public void PrepareStatement(T statement, string sql)
        {
            StringBuilder sb = new();
            int index = 0;
            for (var i = 0; i < sql.Length; i++)
            {
                if (sql[i].Equals('?'))
                    sb.Append("@" + index++);
                else
                    sb.Append(sql[i]);
            }

            _preparedQueries[statement] = sb.ToString();
        }

        public static PreparedStatement GetPreparedStatement(T statement)
        {
            return new PreparedStatement(_preparedQueries[statement]);
        }

        public static void EscapeString(ref string str)
        {
            str = MySqlHelper.EscapeString(str);
        }

        public void CommitTransaction(SQLTransaction transaction)
        {
            _queue.Push(new TransactionTask(transaction));
        }

        public TransactionCallback AsyncCommitTransaction(SQLTransaction transaction)
        {
            TransactionWithResultTask task = new(transaction);
            Task<bool> result = task.GetFuture();
            _queue.Push(task);
            return new TransactionCallback(result);
        }

        public MySqlErrorCode DirectCommitTransaction(SQLTransaction transaction)
        {
            using var connection = new MySqlConnection(_connectionInfo.ConnectionString);

            string query = "";

            try
            {
                // Open/Begin failures must be returned to the worker, not escape
                // its thread and terminate the world process.
                connection.Open();
                using var trans = connection.BeginTransaction();

                foreach (var cmd in transaction.commands)
                {
                    cmd.Transaction = trans;
                    cmd.Connection = connection;
                    query = cmd.CommandText;
                    cmd.ExecuteNonQuery();
                }

                trans.Commit();

                return MySqlErrorCode.None;
            }
            catch (MySqlException ex) //error occurred
            {
                return HandleMySQLException(ex, query);
            }
        }

        MySqlErrorCode HandleMySQLException(MySqlException ex, string query = "", Dictionary<int, object> parameters = null)
        {
            MySqlErrorCode code = (MySqlErrorCode)ex.Number;
            if (ex.InnerException is MySqlException)
                code = (MySqlErrorCode)((MySqlException)ex.InnerException).Number;

            StringBuilder stringBuilder = new($"SqlException: MySqlErrorCode: {code} Message: {ex.Message} SqlQuery: {query} ");
            if (parameters != null)
            {
                stringBuilder.Append("Parameters: ");
                foreach (var pair in parameters)
                    stringBuilder.Append($"{pair.Key} : {pair.Value}");
            }

            Log.outError(LogFilter.Sql, stringBuilder.ToString());

            switch (code)
            {
                case MySqlErrorCode.BadFieldError:
                case MySqlErrorCode.NoSuchTable:
                    Log.outError(LogFilter.Sql, "Your database structure is not up to date. Please make sure you've executed all queries in the sql/updates folders.");
                    break;
                case MySqlErrorCode.ParseError:
                    Log.outError(LogFilter.Sql, "Error while parsing SQL. Core fix required.");
                    break;
            }

            return code;
        }

        public string GetDatabaseName()
        {
            return _connectionInfo.Database;
        }

        // Independent pooled connections for services whose work spans an asynchronous request.
        public MySqlConnection CreateConnection() => new(_connectionInfo.ConnectionString);

        public abstract void PreparedStatements();
    }
}
