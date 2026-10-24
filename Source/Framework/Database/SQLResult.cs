// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Framework.Database
{
    // Results own their rows, never a database reader/connection. Callers may read
    // just one row, retain a result in a callback, or stop an iteration early.
    public sealed class SQLResult : IDisposable
    {
        readonly List<object[]> _rows = new();
        int _row;
        int _fieldCount;

        public SQLResult() { }

        public SQLResult(MySqlDataReader reader)
        {
            using (reader)
            {
                _fieldCount = reader.FieldCount;
                while (reader.Read())
                {
                    var values = new object[_fieldCount];
                    reader.GetValues(values);
                    _rows.Add(values);
                }
            }
        }

        public T Read<T>(int column) => SQLFields.ConvertValue<T>(_rows[_row][column]);

        public T[] ReadValues<T>(int startIndex, int numColumns)
        {
            T[] values = new T[numColumns];
            for (int column = 0; column < numColumns; ++column)
                values[column] = Read<T>(startIndex + column);
            return values;
        }

        public bool IsNull(int column) => _rows[_row][column] is null or DBNull;
        public int GetFieldCount() => _fieldCount;
        public bool IsEmpty() => _row >= _rows.Count;
        public SQLFields GetFields() => new(_rows[_row]);

        public bool NextRow()
        {
            if (IsEmpty()) return false;
            if (++_row < _rows.Count) return true;
            Close();
            return false;
        }

        public void Close() { _rows.Clear(); _row = 0; }
        public void Dispose() => Close();
    }
    public class SQLFields
    {
        object[] _currentRow;

        public SQLFields(object[] row) { _currentRow = row; }

        public T Read<T>(int column)
        {
            return ConvertValue<T>(_currentRow[column]);
        }

        internal static T ConvertValue<T>(object value)
        {
            if (value is null or DBNull) return default;
            if (value is T typed) return typed;
            Type target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            Type numericTarget = target.IsEnum ? Enum.GetUnderlyingType(target) : target;
            // SQL/hotfix schemas sometimes store a signed field in an unsigned column.
            // Preserve its bits with numeric casts, without reinterpreting boxed memory.
            if (value is sbyte or byte or short or ushort or int or uint or long or ulong)
            {
                ulong bits = value is ulong unsigned ? unsigned : unchecked((ulong)Convert.ToInt64(value, CultureInfo.InvariantCulture));
                object converted = Type.GetTypeCode(numericTarget) switch
                {
                    TypeCode.SByte => unchecked((sbyte)bits), TypeCode.Byte => unchecked((byte)bits),
                    TypeCode.Int16 => unchecked((short)bits), TypeCode.UInt16 => unchecked((ushort)bits),
                    TypeCode.Int32 => unchecked((int)bits), TypeCode.UInt32 => unchecked((uint)bits),
                    TypeCode.Int64 => unchecked((long)bits), TypeCode.UInt64 => bits,
                    _ => null
                };
                if (converted != null) return target.IsEnum ? (T)Enum.ToObject(target, converted) : (T)converted;
            }
            if (target.IsEnum)
                return (T)Enum.ToObject(target, Convert.ChangeType(value, numericTarget, CultureInfo.InvariantCulture));
            return (T)Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
        }

        public T[] ReadValues<T>(int startIndex, int numColumns)
        {
            T[] values = new T[numColumns];
            for (var c = 0; c < numColumns; ++c)
                values[c] = Read<T>(startIndex + c);

            return values;
        }

        public bool IsNull(int column)
        {
            return _currentRow[column] == DBNull.Value;
        }
    }
}
