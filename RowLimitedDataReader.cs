using JDBC.NET.Data;
using System.Data;
using System.Data.Common;

namespace NativeJdbcProvider
{
	internal class RowLimitedDataReader : IDataReader
	{
		private int _readRows = 0;
		private readonly JdbcDataReader _reader;
		private readonly uint _rowLimit;
		private readonly Dictionary<int, (Type Type, Func<object, object> Convert)> _typeMapping = new();
		public RowLimitedDataReader(JdbcDataReader reader, uint rowLimit)
		{
			_reader = reader;
			_rowLimit = rowLimit;
			DetectUnsupportedDataType();
		}

		public void Dispose() { }

		public bool Read()
		{
			if (_readRows >= _rowLimit)
			{
				return false;
			}
			if (_reader.Read())
			{
				_readRows++;
				return true;
			}
			return false;
		}

		public int FieldCount => _reader.FieldCount;

		public object this[int i] => _reader[i];

		public object this[string name] => _reader[name];

		public bool IsDBNull(int i) => _reader.IsDBNull(i);

		public int Depth => _reader.Depth;

		public bool IsClosed { get; private set; }

		public int RecordsAffected => _reader.RecordsAffected;

		public void Close() => IsClosed = true;

		public DataTable? GetSchemaTable() => _reader.GetSchemaTable();

		public bool NextResult() => _reader.NextResult();

		public int GetInt32(int i) => _reader.GetInt32(i);

		public string GetString(int i) => _reader.GetString(i);

		public byte[] GetBytes(int i) => (byte[])_reader.GetValue(i);

		public string GetName(int i) => _reader.GetName(i);

		public Type GetFieldType(int i) => _typeMapping.TryGetValue(i, out var value) ? value.Type : _reader.GetFieldType(i);

		public int GetOrdinal(string name) => _reader.GetOrdinal(name);

		public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferoffset, int length) => _reader.GetBytes(i, fieldOffset, buffer!, bufferoffset, length);

		public long GetChars(int i, long fieldoffset, char[]? buffer, int bufferoffset, int length) => _reader.GetChars(i, fieldoffset, buffer!, bufferoffset, length);

		public char GetChar(int i) => _reader.GetChar(i);

		public Guid GetGuid(int i) => _reader.GetGuid(i);

		public bool GetBoolean(int i) => _reader.GetBoolean(i);

		public byte GetByte(int i) => _reader.GetByte(i);

		public short GetInt16(int i) => _reader.GetInt16(i);

		public long GetInt64(int i) => _reader.GetInt64(i);

		public float GetFloat(int i) => _reader.GetFloat(i);

		public double GetDouble(int i) => _reader.GetDouble(i);

		public decimal GetDecimal(int i) => _reader.GetDecimal(i);

		public DateTime GetDateTime(int i) => _reader.GetDateTime(i);

		public IDataReader GetData(int i) => _reader.GetData(i);

		public string GetDataTypeName(int i) => _reader.GetDataTypeName(i);

		public int GetValues(object[] values)
		{
			var result = _reader.GetValues(values);
			for (var i = 0; i < values.Length; i++)
			{
				if (_typeMapping.TryGetValue(i, out var value))
				{
					values[i] = value.Convert(values[i]);
				}
			}
			return result;
		}

		public object GetValue(int i)
		{
			if (_typeMapping.TryGetValue(i, out var value))
			{
				return value.Convert(_reader.GetValue(i));
			}
			return _reader.GetValue(i);
		}

		private void DetectUnsupportedDataType()
		{
			var table = _reader.GetSchemaTable();
			var index = 0;
			foreach (DataRow row in table.Rows)
			{
				var dataType = (Type)row[SchemaTableColumn.DataType];
				var dataTypeClassName = (string)row["DataTypeClassName"];
				var dataTypeName = (string)row["DataTypeName"];
				if (dataType == typeof(object))
				{
					switch (dataTypeClassName?.ToLower())
					{
						case "java.util.uuid":
							_typeMapping[index] = (typeof(Guid), ConvertJavaUUIDToGuid);
							break;
						default:
							break;
					}
				}
				++index;
			}
		}
		private object ConvertJavaUUIDToGuid(object value)
		{
			if (value is DBNull || value is null)
			{
				return DBNull.Value;
			}
			return Guid.Parse(value.ToString());
		}
	}
}
