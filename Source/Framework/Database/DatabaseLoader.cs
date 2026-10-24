// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Configuration;
using MySqlConnector;
using System;
using System.Collections.Generic;

namespace Framework.Database
{
    public class DatabaseLoader
    {
        public void AddDatabase<T>(MySqlBase<T> database, string baseDBName)
        {
            _open.Add(() =>
            {
                MySqlConnectionInfo connectionObject = new()
                {
                    Host = ConfigMgr.GetDefaultValue(baseDBName + "DatabaseInfo.Host", ""),
                    PortOrSocket = ConfigMgr.GetDefaultValue(baseDBName + "DatabaseInfo.Port", ""),
                    Username = ConfigMgr.GetDefaultValue(baseDBName + "DatabaseInfo.Username", ""),
                    Password = ConfigMgr.GetDefaultValue(baseDBName + "DatabaseInfo.Password", ""),
                    Database = ConfigMgr.GetDefaultValue(baseDBName + "DatabaseInfo.Database", ""),
                    UseSSL = ConfigMgr.GetDefaultValue(baseDBName + "DatabaseInfo.SSL", false),
                    Poolsize = ConfigMgr.GetDefaultValue(baseDBName + "DatabaseInfo.MaxPoolSize", 16),
                    ConnectionTimeout = ConfigMgr.GetDefaultValue(baseDBName + "DatabaseInfo.ConnectionTimeout", 15u),
                    CommandTimeout = ConfigMgr.GetDefaultValue(baseDBName + "DatabaseInfo.CommandTimeout", 60u)
                };

                var error = database.Initialize(connectionObject);
                if (error != MySqlErrorCode.None)
                {
                    Log.outError(LogFilter.ServerLoading, $"\nDatabase {connectionObject.Database} NOT opened. There were errors opening the MySQL connections. Check your SQLErrors for specific errors.");
                    return false;
                }

                return true;
            });

            _prepare.Add(() =>
            {
                database.LoadPreparedStatements();
                return true;
            });
        }

        public bool Load()
        {
            if (!Process(_open))
                return false;

            if (!Process(_prepare))
                return false;

            return true;
        }

        // Processes the elements of the given stack until a predicate returned false.
        bool Process(List<Func<bool>> list)
        {
            while (!list.Empty())
            {
                if (!list[0].Invoke())
                    return false;

                list.RemoveAt(0);
            }
            return true;
        }

        List<Func<bool>> _open = new();
        List<Func<bool>> _prepare = new();
    }
}
