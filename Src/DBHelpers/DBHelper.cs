﻿#region License
//  Copyright 2010-2014 Natan Vivo - http://github.com/nvivo/dbhelpers
//
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.
#endregion

using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Reflection;
using System.Threading.Tasks;

namespace DBHelpers
{
    public class DBHelper
    {
        #region Constructors

        public DBHelper(DbProviderFactory providerFactory, string connectionString)
        {
            if (providerFactory == null)
                throw new ArgumentNullException("providerFactory", "You must provide a DbProviderFactory instance.");

            if (String.IsNullOrEmpty(connectionString))
                throw new ArgumentException("The connection string cannot be empty.", "connectionString");

            _factory = providerFactory;
            _connectionString = connectionString;
        }

        public DBHelper(string connectionStringName)
        {
            ConnectionStringSettings css = ConfigurationManager.ConnectionStrings[connectionStringName];

            if (css == null)
                throw new ArgumentException("The connection string you specified does not exist in your configuration file.");

            _factory = DbProviderFactories.GetFactory(css.ProviderName);
            _connectionString = css.ConnectionString;
        }

        #endregion

        #region Properties

        private DbProviderFactory _factory;
        private string _connectionString;

        public DbProviderFactory Factory
        {
            get
            {
                return _factory;
            }
        }

        public string ConnectionString
        {
            get
            {
                return _connectionString;
            }
        }

        #endregion

        #region Private Helpers

        protected static void FillFromReader(DbDataReader reader, int startRecord, int maxRecords, Action<DbDataReader> action)
        {
            if (startRecord < 0)
                throw new ArgumentOutOfRangeException("startRecord", "StartRecord must be zero or higher.");

            while (startRecord > 0)
            {
                if (!reader.Read())
                    return;

                startRecord--;
            }

            if (maxRecords > 0)
            {
                int i = 0;

                while (i < maxRecords && reader.Read())
                {
                    action(reader);
                    i++;
                }
            }
            else
            {
                while (reader.Read())
                    action(reader);
            }
        }

        protected static async Task FillFromReaderAsync(DbDataReader reader, int startRecord, int maxRecords, Action<DbDataReader> action)
        {
            if (startRecord < 0)
                throw new ArgumentOutOfRangeException("startRecord", "StartRecord must be zero or higher.");

            while (startRecord > 0)
            {
                if (!await reader.ReadAsync())
                    return;

                startRecord--;
            }

            if (maxRecords > 0)
            {
                int i = 0;

                while (i < maxRecords && await reader.ReadAsync())
                {
                    action(reader);
                    i++;
                }
            }
            else
            {
                while (await reader.ReadAsync())
                    action(reader);
            }
        }

        private string GetProviderParameterFormatString()
        {
            var builder = Factory.CreateCommandBuilder();
            var type = builder.GetType();
            var method = type.GetMethod("GetParameterPlaceholder", BindingFlags.NonPublic | BindingFlags.Instance);
            var index = 42;
            var parameterName = method.Invoke(builder, new object[] { index }).ToString();
            return parameterName.Replace(index.ToString(CultureInfo.InvariantCulture), "{0}");
        }

        #endregion

        #region Helper Methods and Extension Points

        public DbConnection CreateConnection()
        {
            DbConnection connection = Factory.CreateConnection();
            connection.ConnectionString = ConnectionString;

            return connection;
        }

        public DbCommand CreateCommand(string commandText, params object[] parameters)
        {
            var len = parameters.Length;

            var command = Factory.CreateCommand();
            command.CommandType = CommandType.Text;

            if (len > 0)
            {
                var formatValues = new string[len];

                for (var i = 0; i < len; i++)
                {
                    var parameter = parameters[i];
                    var rawValue = parameter as RawValue;

                    if (rawValue != null)
                    {
                        formatValues[i] = rawValue.Value;
                    }
                    else
                    {
                        var dbParameter = Factory.CreateParameter();
                        var name = CreateParameterName(i);

                        dbParameter.ParameterName = name;
                        dbParameter.Value = parameter ?? DBNull.Value;

                        formatValues[i] = name;
                        command.Parameters.Add(dbParameter);
                    }
                }

                command.CommandText = String.Format(commandText, formatValues);
            }
            else
            {
                command.CommandText = commandText;
            }

            return command;
        }

        private string _parameterFormat;

        protected virtual string CreateParameterName(int index)
        {
            if (_parameterFormat == null)
                _parameterFormat = GetProviderParameterFormatString();

            return String.Format(_parameterFormat, index);
        }

        protected virtual Converter<object, T> GetTypeConverter<T>()
        {
            return (object o) => (T)DBConvert.To<T>(o);
        }

        protected virtual Converter<DbDataReader, T> GetDataReaderConverter<T>()
            where T : new()
        {
            return new DataReaderConverter<T>().Convert;
        }

        protected virtual void OnExecuteCommand(DbCommand command)
        { }

        #endregion

        #region ExecuteNonQuery

        public int ExecuteNonQuery(DbCommand command, DbConnection connection)
        {
            command.Connection = connection;
            return command.ExecuteNonQuery();
        }

        public int ExecuteNonQuery(DbCommand command)
        {
            int affectedRows;

            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                affectedRows = ExecuteNonQuery(command, connection);

                connection.Close();
            }

            return affectedRows;
        }

        public int ExecuteNonQuery(string commandText)
        {
            var command = CreateCommand(commandText);
            return ExecuteNonQuery(command);
        }

        #endregion

        #region ExecuteNonQueryAsync

        public Task<int> ExecuteNonQueryAsync(DbCommand command, DbConnection connection)
        {
            command.Connection = connection;
            return command.ExecuteNonQueryAsync();
        }

        public async Task<int> ExecuteNonQueryAsync(DbCommand command)
        {
            int affectedRows;

            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                affectedRows = await ExecuteNonQueryAsync(command, connection);

                connection.Close();
            }

            return affectedRows;
        }

        public Task<int> ExecuteNonQueryAsync(string commandText)
        {
            var command = CreateCommand(commandText);
            return ExecuteNonQueryAsync(command);
        }

        #endregion

        #region ExecuteScalar<T>

        public T ExecuteScalar<T>(DbCommand command, Converter<object, T> converter, DbConnection connection)
        {
            command.Connection = connection;
            var value = command.ExecuteScalar();
            return converter(value);
        }

        public T ExecuteScalar<T>(DbCommand command, Converter<object, T> converter)
        {
            T o;

            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                o = ExecuteScalar<T>(command, converter, connection);

                connection.Close();
            }

            return o;
        }

        public T ExecuteScalar<T>(DbCommand command, DbConnection connection)
        {
            return ExecuteScalar<T>(command, GetTypeConverter<T>(), connection);
        }

        public T ExecuteScalar<T>(DbCommand command)
        {
            return ExecuteScalar<T>(command, GetTypeConverter<T>());
        }

        public T ExecuteScalar<T>(string commandText)
        {
            var command = CreateCommand(commandText);
            return ExecuteScalar<T>(command);
        }

        public T ExecuteScalar<T>(string commandText, Converter<object, T> converter)
        {
            var command = CreateCommand(commandText);
            return ExecuteScalar<T>(command, converter);
        }

        #endregion

        #region ExecuteScalarAsync<T>

        public async Task<T> ExecuteScalarAsync<T>(DbCommand command, Converter<object, T> converter, DbConnection connection)
        {
            command.Connection = connection;
            var value = await command.ExecuteScalarAsync();
            return converter(value);
        }

        public async Task<T> ExecuteScalarAsync<T>(DbCommand command, Converter<object, T> converter)
        {
            T o;

            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                o = await ExecuteScalarAsync<T>(command, converter, connection);

                connection.Close();
            }

            return o;
        }

        public Task<T> ExecuteScalarAsync<T>(DbCommand command, DbConnection connection)
        {
            return ExecuteScalarAsync<T>(command, GetTypeConverter<T>(), connection);
        }

        public Task<T> ExecuteScalarAsync<T>(DbCommand command)
        {
            return ExecuteScalarAsync<T>(command, GetTypeConverter<T>());
        }

        public Task<T> ExecuteScalarAsync<T>(string commandText)
        {
            var command = CreateCommand(commandText);
            return ExecuteScalarAsync<T>(command);
        }

        public Task<T> ExecuteScalarAsync<T>(string commandText, Converter<object, T> converter)
        {
            var command = CreateCommand(commandText);
            return ExecuteScalarAsync<T>(command, converter);
        }

        #endregion

        #region ExecuteReader

        public DbDataReader ExecuteReader(DbCommand command, DbConnection connection)
        {
            command.Connection = connection;

            OnExecuteCommand(command);

            return command.ExecuteReader();
        }

        public DbDataReader ExecuteReader(DbCommand command)
        {
            DbConnection connection = CreateConnection();
            command.Connection = connection;

            OnExecuteCommand(command);
            connection.Open();

            return command.ExecuteReader(CommandBehavior.CloseConnection);
        }

        public DbDataReader ExecuteReader(string commandText)
        {
            var command = CreateCommand(commandText);
            return ExecuteReader(command);
        }

        #endregion

        #region ExecuteReaderAsync

        public Task<DbDataReader> ExecuteReaderAsync(DbCommand command, DbConnection connection)
        {
            command.Connection = connection;

            OnExecuteCommand(command);

            return command.ExecuteReaderAsync();
        }

        public Task<DbDataReader> ExecuteReaderAsync(DbCommand command)
        {
            DbConnection connection = CreateConnection();
            command.Connection = connection;

            OnExecuteCommand(command);
            connection.Open();

            return command.ExecuteReaderAsync(CommandBehavior.CloseConnection);
        }

        public Task<DbDataReader> ExecuteReaderAsync(string commandText)
        {
            var command = CreateCommand(commandText);
            return ExecuteReaderAsync(command);
        }

        #endregion

        #region ExecuteDataTable

        public DataTable ExecuteDataTable(DbCommand command, int startRecord, int maxRecords, DbConnection connection)
        {
            command.Connection = connection;

            DbDataAdapter adapter = Factory.CreateDataAdapter();
            adapter.SelectCommand = command;

            OnExecuteCommand(command);

            DataTable dt = new DataTable();

            if (startRecord >= 0 || maxRecords >= 0)
                adapter.Fill(startRecord, maxRecords, dt);
            else
                adapter.Fill(dt);

            return dt;
        }

        public DataTable ExecuteDataTable(DbCommand command, int startRecord, int maxRecords)
        {
            DataTable dt;

            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                dt = ExecuteDataTable(command, startRecord, maxRecords, connection);

                connection.Close();
            }

            return dt;
        }

        public DataTable ExecuteDataTable(DbCommand command, DbConnection connection)
        {
            return ExecuteDataTable(command, 0, 0, connection);
        }

        public DataTable ExecuteDataTable(DbCommand command)
        {
            return ExecuteDataTable(command, 0, 0);
        }

        public DataTable ExecuteDataTable(string commandText, int startRecord, int maxRecords)
        {
            var command = CreateCommand(commandText);
            return ExecuteDataTable(command, startRecord, maxRecords);
        }

        public DataTable ExecuteDataTable(string commandText)
        {
            var command = CreateCommand(commandText);
            return ExecuteDataTable(command);
        }

        #endregion

        #region ExecuteDataTableAsync

        public async Task<DataTable> ExecuteDataTableAsync(DbCommand command, int startRecord, int maxRecords, DbConnection connection)
        {
            command.Connection = connection;

            DbDataAdapter adapter = Factory.CreateDataAdapter();
            adapter.SelectCommand = command;

            OnExecuteCommand(command);

            DataTable dt = new DataTable();

            if (startRecord >= 0 || maxRecords >= 0)
                await Task.Run(() => adapter.Fill(startRecord, maxRecords, dt));
            else
                await Task.Run(() => adapter.Fill(dt));

            return dt;
        }

        public async Task<DataTable> ExecuteDataTableAsync(DbCommand command, int startRecord, int maxRecords)
        {
            DataTable dt;

            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                dt = await ExecuteDataTableAsync(command, startRecord, maxRecords, connection);

                connection.Close();
            }

            return dt;
        }

        public Task<DataTable> ExecuteDataTableAsync(DbCommand command, DbConnection connection)
        {
            return ExecuteDataTableAsync(command, 0, 0, connection);
        }

        public Task<DataTable> ExecuteDataTableAsync(DbCommand command)
        {
            return ExecuteDataTableAsync(command, 0, 0);
        }

        public Task<DataTable> ExecuteDataTableAsync(string commandText, int startRecord, int maxRecords)
        {
            var command = CreateCommand(commandText);
            return ExecuteDataTableAsync(command, startRecord, maxRecords);
        }

        public Task<DataTable> ExecuteDataTableAsync(string commandText)
        {
            var command = CreateCommand(commandText);
            return ExecuteDataTableAsync(command);
        }

        #endregion

        #region ExecuteDataSet

        public DataSet ExecuteDataSet(DbCommand command, DbConnection connection)
        {
            command.Connection = connection;

            DbDataAdapter adapter = Factory.CreateDataAdapter();
            adapter.SelectCommand = command;

            OnExecuteCommand(command);

            DataSet ds = new DataSet();
            adapter.Fill(ds);

            return ds;
        }

        public DataSet ExecuteDataSet(DbCommand command)
        {
            DataSet ds;

            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                ds = ExecuteDataSet(command, connection);

                connection.Close();
            }

            return ds;
        }

        public DataSet ExecuteDataSet(string commandText)
        {
            var command = CreateCommand(commandText);
            return ExecuteDataSet(command);
        }

        #endregion

        #region ExecuteDataSetAsync

        public async Task<DataSet> ExecuteDataSetAsync(DbCommand command, DbConnection connection)
        {
            command.Connection = connection;

            DbDataAdapter adapter = Factory.CreateDataAdapter();
            adapter.SelectCommand = command;

            OnExecuteCommand(command);

            DataSet ds = new DataSet();
            await Task.Run(() => adapter.Fill(ds));

            return ds;
        }

        public async Task<DataSet> ExecuteDataSetAsync(DbCommand command)
        {
            DataSet ds;

            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                ds = await ExecuteDataSetAsync(command, connection);

                connection.Close();
            }

            return ds;
        }

        public Task<DataSet> ExecuteDataSetAsync(string commandText)
        {
            var command = CreateCommand(commandText);
            return ExecuteDataSetAsync(command);
        }

        #endregion

        #region ExecuteArray<T>

        public T[] ExecuteArray<T>(DbCommand command, Converter<object, T> converter, int startRecord, int maxRecords, DbConnection connection)
        {
            List<T> list = new List<T>();

            using (DbDataReader reader = ExecuteReader(command, connection))
            {
                FillFromReader(reader, startRecord, maxRecords, r =>
                {
                    list.Add(
                    converter(r.GetValue(0))
                    );
                });

                reader.Close();
            }

            return list.ToArray();
        }

        public T[] ExecuteArray<T>(DbCommand command, Converter<object, T> converter, int startRecord, int maxRecords)
        {
            T[] arr;

            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                arr = ExecuteArray<T>(command, converter, startRecord, maxRecords, connection);

                connection.Close();
            }

            return arr;
        }

        public T[] ExecuteArray<T>(DbCommand command, Converter<object, T> converter, DbConnection connection)
        {
            return ExecuteArray<T>(command, converter, 0, 0, connection);
        }

        public T[] ExecuteArray<T>(DbCommand command, Converter<object, T> converter)
        {
            return ExecuteArray<T>(command, converter, 0, 0);
        }

        public T[] ExecuteArray<T>(DbCommand command, int startRecord, int maxRecords, DbConnection connection)
        {
            return ExecuteArray<T>(command, GetTypeConverter<T>(), startRecord, maxRecords, connection);
        }

        public T[] ExecuteArray<T>(DbCommand command, int startRecord, int maxRecords)
        {
            return ExecuteArray<T>(command, GetTypeConverter<T>(), startRecord, maxRecords);
        }

        public T[] ExecuteArray<T>(DbCommand command, DbConnection connection)
        {
            return ExecuteArray<T>(command, GetTypeConverter<T>(), connection);
        }

        public T[] ExecuteArray<T>(DbCommand command)
        {
            return ExecuteArray<T>(command, GetTypeConverter<T>());
        }

        public T[] ExecuteArray<T>(string commandText, int startRecord, int maxRecords)
        {
            var command = CreateCommand(commandText);
            return ExecuteArray<T>(command, startRecord, maxRecords);
        }

        public T[] ExecuteArray<T>(string commandText)
        {
            var command = CreateCommand(commandText);
            return ExecuteArray<T>(command);
        }

        public T[] ExecuteArray<T>(string commandText, Converter<object, T> converter, int startRecord, int maxRecords)
        {
            var command = CreateCommand(commandText);
            return ExecuteArray<T>(command, converter, startRecord, maxRecords);
        }

        public T[] ExecuteArray<T>(string commandText, Converter<object, T> converter)
        {
            var command = CreateCommand(commandText);
            return ExecuteArray<T>(command, converter);
        }

        #endregion

        #region ExecuteArrayAsync<T>

        public async Task<T[]> ExecuteArrayAsync<T>(DbCommand command, Converter<object, T> converter, int startRecord, int maxRecords, DbConnection connection)
        {
            List<T> list = new List<T>();

            using (DbDataReader reader = await ExecuteReaderAsync(command, connection))
            {
                await FillFromReaderAsync(reader, startRecord, maxRecords, r =>
                {
                    list.Add(converter(r.GetValue(0)));
                });

                reader.Close();
            }

            return list.ToArray();
        }

        public async Task<T[]> ExecuteArrayAsync<T>(DbCommand command, Converter<object, T> converter, int startRecord, int maxRecords)
        {
            T[] arr;

            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                arr = await ExecuteArrayAsync<T>(command, converter, startRecord, maxRecords, connection);

                connection.Close();
            }

            return arr;
        }

        public Task<T[]> ExecuteArrayAsync<T>(DbCommand command, Converter<object, T> converter, DbConnection connection)
        {
            return ExecuteArrayAsync<T>(command, converter, 0, 0, connection);
        }

        public Task<T[]> ExecuteArrayAsync<T>(DbCommand command, Converter<object, T> converter)
        {
            return ExecuteArrayAsync<T>(command, converter, 0, 0);
        }

        public Task<T[]> ExecuteArrayAsync<T>(DbCommand command, int startRecord, int maxRecords, DbConnection connection)
        {
            return ExecuteArrayAsync<T>(command, GetTypeConverter<T>(), startRecord, maxRecords, connection);
        }

        public Task<T[]> ExecuteArrayAsync<T>(DbCommand command, int startRecord, int maxRecords)
        {
            return ExecuteArrayAsync<T>(command, GetTypeConverter<T>(), startRecord, maxRecords);
        }

        public Task<T[]> ExecuteArrayAsync<T>(DbCommand command, DbConnection connection)
        {
            return ExecuteArrayAsync<T>(command, GetTypeConverter<T>(), connection);
        }

        public Task<T[]> ExecuteArrayAsync<T>(DbCommand command)
        {
            return ExecuteArrayAsync<T>(command, GetTypeConverter<T>());
        }

        public Task<T[]> ExecuteArrayAsync<T>(string commandText, int startRecord, int maxRecords)
        {
            var command = CreateCommand(commandText);
            return ExecuteArrayAsync<T>(command, startRecord, maxRecords);
        }

        public Task<T[]> ExecuteArrayAsync<T>(string commandText)
        {
            var command = CreateCommand(commandText);
            return ExecuteArrayAsync<T>(command);
        }

        public Task<T[]> ExecuteArrayAsync<T>(string commandText, Converter<object, T> converter, int startRecord, int maxRecords)
        {
            var command = CreateCommand(commandText);
            return ExecuteArrayAsync<T>(command, converter, startRecord, maxRecords);
        }

        public Task<T[]> ExecuteArrayAsync<T>(string commandText, Converter<object, T> converter)
        {
            var command = CreateCommand(commandText);
            return ExecuteArrayAsync<T>(command, converter);
        }

        #endregion

        #region ExecuteDictionary<TKey, TValue>

        public Dictionary<TKey, TValue> ExecuteDictionary<TKey, TValue>(DbCommand command, Converter<object, TKey> keyConverter, Converter<object, TValue> valueConverter, int startRecord, int maxRecords, DbConnection connection)
        {
            Dictionary<TKey, TValue> dict = new Dictionary<TKey, TValue>();

            using (DbDataReader reader = ExecuteReader(command, connection))
            {
                FillFromReader(reader, startRecord, maxRecords, r =>
                {
                    dict.Add(
                    keyConverter(r.GetValue(0)),
                    valueConverter(r.GetValue(1))
                    );
                });

                reader.Close();
            }

            return dict;
        }

        public Dictionary<TKey, TValue> ExecuteDictionary<TKey, TValue>(DbCommand command, Converter<object, TKey> keyConverter, Converter<object, TValue> valueConverter, int startRecord, int maxRecords)
        {
            Dictionary<TKey, TValue> dict;

            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                dict = ExecuteDictionary<TKey, TValue>(command, keyConverter, valueConverter, startRecord, maxRecords, connection);

                connection.Close();
            }

            return dict;
        }

        public Dictionary<TKey, TValue> ExecuteDictionary<TKey, TValue>(DbCommand command, Converter<object, TKey> keyConverter, Converter<object, TValue> valueConverter, DbConnection connection)
        {
            return ExecuteDictionary<TKey, TValue>(command, keyConverter, valueConverter, 0, 0, connection);
        }

        public Dictionary<TKey, TValue> ExecuteDictionary<TKey, TValue>(DbCommand command, Converter<object, TKey> keyConverter, Converter<object, TValue> valueConverter)
        {
            return ExecuteDictionary<TKey, TValue>(command, keyConverter, valueConverter, 0, 0);
        }

        public Dictionary<TKey, TValue> ExecuteDictionary<TKey, TValue>(DbCommand command, int startRecord, int maxRecords, DbConnection connection)
        {
            return ExecuteDictionary<TKey, TValue>(command, GetTypeConverter<TKey>(), GetTypeConverter<TValue>(), startRecord, maxRecords, connection);
        }

        public Dictionary<TKey, TValue> ExecuteDictionary<TKey, TValue>(DbCommand command, int startRecord, int maxRecords)
        {
            return ExecuteDictionary<TKey, TValue>(command, GetTypeConverter<TKey>(), GetTypeConverter<TValue>(), startRecord, maxRecords);
        }

        public Dictionary<TKey, TValue> ExecuteDictionary<TKey, TValue>(DbCommand command, DbConnection connection)
        {
            return ExecuteDictionary<TKey, TValue>(command, GetTypeConverter<TKey>(), GetTypeConverter<TValue>(), connection);
        }

        public Dictionary<TKey, TValue> ExecuteDictionary<TKey, TValue>(DbCommand command)
        {
            return ExecuteDictionary<TKey, TValue>(command, GetTypeConverter<TKey>(), GetTypeConverter<TValue>());
        }

        public Dictionary<TKey, TValue> ExecuteDictionary<TKey, TValue>(string commandText, Converter<object, TKey> keyConverter, Converter<object, TValue> valueConverter, int startRecord, int maxRecords)
        {
            var command = CreateCommand(commandText);
            return ExecuteDictionary<TKey, TValue>(command, keyConverter, valueConverter, startRecord, maxRecords);
        }

        public Dictionary<TKey, TValue> ExecuteDictionary<TKey, TValue>(string commandText, Converter<object, TKey> keyConverter, Converter<object, TValue> valueConverter)
        {
            var command = CreateCommand(commandText);
            return ExecuteDictionary<TKey, TValue>(command, keyConverter, valueConverter);
        }

        public Dictionary<TKey, TValue> ExecuteDictionary<TKey, TValue>(string commandText, int startRecord, int maxRecords)
        {
            var command = CreateCommand(commandText);
            return ExecuteDictionary<TKey, TValue>(command, startRecord, maxRecords);
        }

        public Dictionary<TKey, TValue> ExecuteDictionary<TKey, TValue>(string commandText)
        {
            var command = CreateCommand(commandText);
            return ExecuteDictionary<TKey, TValue>(command);
        }

        #endregion

        #region ExecuteDictionaryAsync<TKey, TValue>

        public async Task<Dictionary<TKey, TValue>> ExecuteDictionaryAsync<TKey, TValue>(DbCommand command, Converter<object, TKey> keyConverter, Converter<object, TValue> valueConverter, int startRecord, int maxRecords, DbConnection connection)
        {
            Dictionary<TKey, TValue> dict = new Dictionary<TKey, TValue>();

            using (DbDataReader reader = await ExecuteReaderAsync(command, connection))
            {
                await FillFromReaderAsync(reader, startRecord, maxRecords, r =>
                {
                    dict.Add(
                        keyConverter(r.GetValue(0)),
                        valueConverter(r.GetValue(1))
                    );
                });

                reader.Close();
            }

            return dict;
        }

        public async Task<Dictionary<TKey, TValue>> ExecuteDictionaryAsync<TKey, TValue>(DbCommand command, Converter<object, TKey> keyConverter, Converter<object, TValue> valueConverter, int startRecord, int maxRecords)
        {
            Dictionary<TKey, TValue> dict;

            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                dict = await ExecuteDictionaryAsync<TKey, TValue>(command, keyConverter, valueConverter, startRecord, maxRecords, connection);

                connection.Close();
            }

            return dict;
        }

        public Task<Dictionary<TKey, TValue>> ExecuteDictionaryAsync<TKey, TValue>(DbCommand command, Converter<object, TKey> keyConverter, Converter<object, TValue> valueConverter, DbConnection connection)
        {
            return ExecuteDictionaryAsync<TKey, TValue>(command, keyConverter, valueConverter, 0, 0, connection);
        }

        public Task<Dictionary<TKey, TValue>> ExecuteDictionaryAsync<TKey, TValue>(DbCommand command, Converter<object, TKey> keyConverter, Converter<object, TValue> valueConverter)
        {
            return ExecuteDictionaryAsync<TKey, TValue>(command, keyConverter, valueConverter, 0, 0);
        }

        public Task<Dictionary<TKey, TValue>> ExecuteDictionaryAsync<TKey, TValue>(DbCommand command, int startRecord, int maxRecords, DbConnection connection)
        {
            return ExecuteDictionaryAsync<TKey, TValue>(command, GetTypeConverter<TKey>(), GetTypeConverter<TValue>(), startRecord, maxRecords, connection);
        }

        public Task<Dictionary<TKey, TValue>> ExecuteDictionaryAsync<TKey, TValue>(DbCommand command, int startRecord, int maxRecords)
        {
            return ExecuteDictionaryAsync<TKey, TValue>(command, GetTypeConverter<TKey>(), GetTypeConverter<TValue>(), startRecord, maxRecords);
        }

        public Task<Dictionary<TKey, TValue>> ExecuteDictionaryAsync<TKey, TValue>(DbCommand command, DbConnection connection)
        {
            return ExecuteDictionaryAsync<TKey, TValue>(command, GetTypeConverter<TKey>(), GetTypeConverter<TValue>(), connection);
        }

        public Task<Dictionary<TKey, TValue>> ExecuteDictionaryAsync<TKey, TValue>(DbCommand command)
        {
            return ExecuteDictionaryAsync<TKey, TValue>(command, GetTypeConverter<TKey>(), GetTypeConverter<TValue>());
        }

        public Task<Dictionary<TKey, TValue>> ExecuteDictionaryAsync<TKey, TValue>(string commandText, Converter<object, TKey> keyConverter, Converter<object, TValue> valueConverter, int startRecord, int maxRecords)
        {
            var command = CreateCommand(commandText);
            return ExecuteDictionaryAsync<TKey, TValue>(command, keyConverter, valueConverter, startRecord, maxRecords);
        }

        public Task<Dictionary<TKey, TValue>> ExecuteDictionaryAsync<TKey, TValue>(string commandText, Converter<object, TKey> keyConverter, Converter<object, TValue> valueConverter)
        {
            var command = CreateCommand(commandText);
            return ExecuteDictionaryAsync<TKey, TValue>(command, keyConverter, valueConverter);
        }

        public Task<Dictionary<TKey, TValue>> ExecuteDictionaryAsync<TKey, TValue>(string commandText, int startRecord, int maxRecords)
        {
            var command = CreateCommand(commandText);
            return ExecuteDictionaryAsync<TKey, TValue>(command, startRecord, maxRecords);
        }

        public Task<Dictionary<TKey, TValue>> ExecuteDictionaryAsync<TKey, TValue>(string commandText)
        {
            var command = CreateCommand(commandText);
            return ExecuteDictionaryAsync<TKey, TValue>(command);
        }

        #endregion

        #region ExecuteObject<T>

        public T ExecuteObject<T>(DbCommand command, Converter<DbDataReader, T> converter, DbConnection connection)
        {
            T o;

            using (DbDataReader reader = ExecuteReader(command, connection))
            {
                if (reader.Read())
                    o = converter(reader);
                else
                    o = default(T);

                reader.Close();
            }

            return o;
        }

        public T ExecuteObject<T>(DbCommand command, Converter<DbDataReader, T> converter)
        {
            T o;

            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                o = ExecuteObject<T>(command, converter, connection);

                connection.Close();
            }

            return o;
        }

        public T ExecuteObject<T>(DbCommand command, DbConnection connection)
            where T : new()
        {
            var converter = GetDataReaderConverter<T>();
            return ExecuteObject<T>(command, converter, connection);
        }

        public T ExecuteObject<T>(DbCommand command)
            where T : new()
        {
            var converter = GetDataReaderConverter<T>();
            return ExecuteObject<T>(command, converter);
        }

        public T ExecuteObject<T>(string commandText, Converter<DbDataReader, T> converter)
        {
            var command = CreateCommand(commandText);
            return ExecuteObject<T>(command, converter);
        }

        public T ExecuteObject<T>(string commandText)
            where T : new()
        {
            var command = CreateCommand(commandText);
            return ExecuteObject<T>(command);
        }

        #endregion

        #region ExecuteObjectAsync<T>

        public async Task<T> ExecuteObjectAsync<T>(DbCommand command, Converter<DbDataReader, T> converter, DbConnection connection)
        {
            T o;

            using (DbDataReader reader = await ExecuteReaderAsync(command, connection))
            {
                if (await reader.ReadAsync())
                    o = converter(reader);
                else
                    o = default(T);

                reader.Close();
            }

            return o;
        }

        public async Task<T> ExecuteObjectAsync<T>(DbCommand command, Converter<DbDataReader, T> converter)
        {
            T o;

            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                o = await ExecuteObjectAsync<T>(command, converter, connection);

                connection.Close();
            }

            return o;
        }

        public Task<T> ExecuteObjectAsync<T>(DbCommand command, DbConnection connection)
            where T : new()
        {
            var converter = GetDataReaderConverter<T>();
            return ExecuteObjectAsync<T>(command, converter, connection);
        }

        public Task<T> ExecuteObjectAsync<T>(DbCommand command)
            where T : new()
        {
            var converter = GetDataReaderConverter<T>();
            return ExecuteObjectAsync<T>(command, converter);
        }

        public Task<T> ExecuteObjectAsync<T>(string commandText, Converter<DbDataReader, T> converter)
        {
            var command = CreateCommand(commandText);
            return ExecuteObjectAsync<T>(command, converter);
        }

        public Task<T> ExecuteObjectAsync<T>(string commandText)
            where T : new()
        {
            var command = CreateCommand(commandText);
            return ExecuteObjectAsync<T>(command);
        }

        #endregion

        #region ExecuteList<T>

        public List<T> ExecuteList<T>(DbCommand command, Converter<DbDataReader, T> converter, int startRecord, int maxRecords, DbConnection connection)
        {
            var list = new List<T>();

            using (DbDataReader reader = ExecuteReader(command, connection))
            {
                FillFromReader(reader, startRecord, maxRecords, r =>
                {
                    list.Add(converter(reader));
                });

                reader.Close();
            }

            return list;
        }

        public List<T> ExecuteList<T>(DbCommand command, Converter<DbDataReader, T> converter, int startRecord, int maxRecords)
        {
            List<T> list;

            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                list = ExecuteList<T>(command, converter, startRecord, maxRecords, connection);

                connection.Close();
            }

            return list;
        }

        public List<T> ExecuteList<T>(DbCommand command, Converter<DbDataReader, T> converter, DbConnection connection)
        {
            return ExecuteList<T>(command, converter, 0, 0, connection);
        }

        public List<T> ExecuteList<T>(DbCommand command, Converter<DbDataReader, T> converter)
        {
            return ExecuteList<T>(command, converter, 0, 0);
        }

        public List<T> ExecuteList<T>(DbCommand command, int startRecord, int maxRecords, DbConnection connection)
            where T : new()
        {
            var converter = GetDataReaderConverter<T>();
            return ExecuteList<T>(command, converter, startRecord, maxRecords, connection);
        }

        public List<T> ExecuteList<T>(DbCommand command, int startRecord, int maxRecords)
            where T : new()
        {
            var converter = GetDataReaderConverter<T>();
            return ExecuteList<T>(command, converter, startRecord, maxRecords);
        }

        public List<T> ExecuteList<T>(DbCommand command, DbConnection connection)
            where T : new()
        {
            var converter = GetDataReaderConverter<T>();
            return ExecuteList<T>(command, converter, connection);
        }

        public List<T> ExecuteList<T>(DbCommand command)
            where T : new()
        {
            var converter = GetDataReaderConverter<T>();
            return ExecuteList<T>(command, converter);
        }

        public List<T> ExecuteList<T>(string commandText, Converter<DbDataReader, T> converter, int startRecord, int maxRecords)
        {
            var command = CreateCommand(commandText);
            return ExecuteList<T>(command, converter, startRecord, maxRecords);
        }

        public List<T> ExecuteList<T>(string commandText, Converter<DbDataReader, T> converter)
        {
            var command = CreateCommand(commandText);
            return ExecuteList<T>(command, converter);
        }

        public List<T> ExecuteList<T>(string commandText, int startRecord, int maxRecords)
            where T : new()
        {
            var command = CreateCommand(commandText);
            return ExecuteList<T>(command, startRecord, maxRecords);
        }

        public List<T> ExecuteList<T>(string commandText)
            where T : new()
        {
            var command = CreateCommand(commandText);
            return ExecuteList<T>(command);
        }

        #endregion

        #region ExecuteListAsync<T>

        public async Task<List<T>> ExecuteListAsync<T>(DbCommand command, Converter<DbDataReader, T> converter, int startRecord, int maxRecords, DbConnection connection)
        {
            var list = new List<T>();

            using (DbDataReader reader = await ExecuteReaderAsync(command, connection))
            {
                await FillFromReaderAsync(reader, startRecord, maxRecords, r =>
                {
                    list.Add(converter(reader));
                });

                reader.Close();
            }

            return list;
        }

        public async Task<List<T>> ExecuteListAsync<T>(DbCommand command, Converter<DbDataReader, T> converter, int startRecord, int maxRecords)
        {
            List<T> list;

            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                list = await ExecuteListAsync<T>(command, converter, startRecord, maxRecords, connection);

                connection.Close();
            }

            return list;
        }

        public Task<List<T>> ExecuteListAsync<T>(DbCommand command, Converter<DbDataReader, T> converter, DbConnection connection)
        {
            return ExecuteListAsync<T>(command, converter, 0, 0, connection);
        }

        public Task<List<T>> ExecuteListAsync<T>(DbCommand command, Converter<DbDataReader, T> converter)
        {
            return ExecuteListAsync<T>(command, converter, 0, 0);
        }

        public Task<List<T>> ExecuteListAsync<T>(DbCommand command, int startRecord, int maxRecords, DbConnection connection)
            where T : new()
        {
            var converter = GetDataReaderConverter<T>();
            return ExecuteListAsync<T>(command, converter, startRecord, maxRecords, connection);
        }

        public Task<List<T>> ExecuteListAsync<T>(DbCommand command, int startRecord, int maxRecords)
            where T : new()
        {
            var converter = GetDataReaderConverter<T>();
            return ExecuteListAsync<T>(command, converter, startRecord, maxRecords);
        }

        public Task<List<T>> ExecuteListAsync<T>(DbCommand command, DbConnection connection)
            where T : new()
        {
            var converter = GetDataReaderConverter<T>();
            return ExecuteListAsync<T>(command, converter, connection);
        }

        public Task<List<T>> ExecuteListAsync<T>(DbCommand command)
            where T : new()
        {
            var converter = GetDataReaderConverter<T>();
            return ExecuteListAsync<T>(command, converter);
        }

        public Task<List<T>> ExecuteListAsync<T>(string commandText, Converter<DbDataReader, T> converter, int startRecord, int maxRecords)
        {
            var command = CreateCommand(commandText);
            return ExecuteListAsync<T>(command, converter, startRecord, maxRecords);
        }

        public Task<List<T>> ExecuteListAsync<T>(string commandText, Converter<DbDataReader, T> converter)
        {
            var command = CreateCommand(commandText);
            return ExecuteListAsync<T>(command, converter);
        }

        public Task<List<T>> ExecuteListAsync<T>(string commandText, int startRecord, int maxRecords)
            where T : new()
        {
            var command = CreateCommand(commandText);
            return ExecuteListAsync<T>(command, startRecord, maxRecords);
        }

        public Task<List<T>> ExecuteListAsync<T>(string commandText)
            where T : new()
        {
            var command = CreateCommand(commandText);
            return ExecuteListAsync<T>(command);
        }

        #endregion
    }
}
