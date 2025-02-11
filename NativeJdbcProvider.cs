using Wyn.Data.Provider.Custom;
using JDBC.NET.Data;
using JDBC.NET.Data.Utilities;
using System.Data;
using System.Reflection;
using System.Text.RegularExpressions;

namespace NativeJdbcProvider
{
	public class NativeJdbcProvider : INativeQueryDataProvider
    {
        public static string ProviderName => "Native JDBC";

        public static void Configure(IFeatureCollection features)
        {
            features.Metadata().DisplayName = ProviderName;
            features.Metadata().Description = "General-purposed native JDBC connector";

            var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (Path.Combine(assemblyDirectory!, "UserGuide.md") is var userGuidePath && File.Exists(userGuidePath))
            {
                features.UserGuide().UserGuideMarkdown = File.ReadAllText(userGuidePath);
            }

            if (Path.Combine(assemblyDirectory!, "jdbc_16x16.png") is var smallIconPath && File.Exists(smallIconPath))
            {
                features.Metadata().SmallIcon = GetDataURL(smallIconPath);
            }

            if (Path.Combine(assemblyDirectory!, "jdbc_180x130.png") is var largeIconPath && File.Exists(largeIconPath))
            {
                features.Metadata().LargeIcon = GetDataURL(largeIconPath);
            }

            features.Get<IParameterParseFeature>().NeedFillParameters = false;
        }

        public static INativeQueryDataProvider CreateInstance() => new NativeJdbcProvider();

        public async Task ExecuteAsync(INativeQuery nativeQuery, Action<IDataReader> consumer, params NativeParameter[] parameters)
        {
            if (nativeQuery is null)
            {
                throw new DataException("Native query can not be null.");
            }

            var rawCommandText = nativeQuery.QueryText ?? throw new DataException("Command text can not be null or empty.");

            var connectionStringBuilder = ValidateConnectionString(nativeQuery.ConnectionString);

            using var connection = new JdbcConnection(connectionStringBuilder);
            try
            {
                await connection.OpenAsync();
            }
            catch (Exception ex)
            {
                throw new DataException($"Failed to open connection with data provider <{ProviderName}>.", ex);
            }

            using var command = connection.CreateCommand();

            Task task = nativeQuery.RowLimitOption.RowLimitType switch
            {
                RowLimitType.AllRows => RunAsync(rawCommandText, uint.MaxValue, false),
                RowLimitType.SchemaOnly => RunAsync($"select * from ({rawCommandText}) tmp where 1=0", 0, true),
                RowLimitType.SingleRow => RunAsync($"select * from ({rawCommandText}) tmp limit 1", 1, true),
                RowLimitType.SpecifiedRowLimit => RunAsync($"select * from ({rawCommandText}) tmp limit {nativeQuery.RowLimitOption.GetSpecifiedRowLimit.GetValueOrDefault(1)}", (uint)nativeQuery.RowLimitOption.GetSpecifiedRowLimit.GetValueOrDefault(1), true),
                _ => throw new DataException("Unsupported RowLimitType.")
            };

            await task;

            async Task RunAsync(string commandText, uint rowLimit, bool retry)
            {
                if (retry)
                {
                    try
                    {
                        await RunAsync(commandText, rowLimit, false);
                    }
                    catch
                    {
                        await RunAsync(rawCommandText, rowLimit, false);
                    }
                }
                else
                {
                    command.Parameters.Clear();
                    command!.CommandText = commandText;
                    if (parameters.Length > 0)
                    {
                        List<NativeParameter> needFillInParameters = new();
                        HashSet<string> parameterized = new();
                        foreach (var parameter in parameters)
                        {
                            if (!TryCreateParameter(command, parameter.Name, parameter.ParameterValue))
                            {
                                needFillInParameters.Add(parameter);
                            }
                            else
                            {
                                parameterized.Add(parameter.Name);
                            }
                        }
                        if (needFillInParameters.Count > 0)
                        {
                            command.CommandText = FillParameterValueInfoQuery(command.CommandText, needFillInParameters, parameterized);
                        }
                    }
                    using var jdbcReader = await command.ExecuteReaderAsync() as JdbcDataReader;
                    using var limitReader = new RowLimitedDataReader(jdbcReader, rowLimit);
                    consumer(limitReader);
                }
            }
        }

        private static JdbcConnectionStringBuilder ValidateConnectionString(string rawConnectionString)
        {
            var connectionStringBuilder = new JdbcConnectionStringBuilder(rawConnectionString ?? throw new DataException("Connection string can not be null or empty."));
            if (connectionStringBuilder.DriverPath is var relativeDriverPath && string.IsNullOrEmpty(relativeDriverPath))
            {
                throw new DataException("Driver path can not be null or empty.");
            }
            if (Path.IsPathRooted(relativeDriverPath))
            {
                throw new DataException("Driver path must be a local relative path for security reasons.");
            }
            var absoluteDriverPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, relativeDriverPath);
            if (!File.Exists(absoluteDriverPath))
            {
                throw new DataException($"JDBC Driver file not found at relative path <{relativeDriverPath}>.");
            }
            connectionStringBuilder.DriverPath = absoluteDriverPath;
            return connectionStringBuilder;
        }

        public async Task TestConnectionAsync(string connectionString)
        {
            var connectionStringBuilder = ValidateConnectionString(connectionString);
            using var connection = new JdbcConnection(connectionStringBuilder);
            await connection.OpenAsync();
        }

        private static string GetDataURL(string imgFilePath)
        {
            return "data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(imgFilePath));
        }
        private static bool TryCreateParameter(JdbcCommand command, string parameterName, object parameterValue)
        {
            var p = command.CreateParameter();
            p.ParameterName = parameterName;
            if (parameterValue is DBNull || parameterValue is null)
            {
                p.Value = string.Empty;
                command.Parameters.Add(p);
                return true;
            }
            if (parameterValue is Array)
            {
                return false;
            }
            p.Value = parameterValue;
            try
            {
                ParameterTypeUtility.Convert(p.DbType);
                command.Parameters.Add(p);
                return true;
            }
            catch (NotSupportedException)
            {
                switch (p.Value)
                {
                    case DateTime datetime:
                        p.DbType = DbType.String;
                        if (datetime.TimeOfDay == TimeSpan.Zero)
                        {
                            p.Value = datetime.ToString("yyyy-MM-dd");
                        }
                        else
                        {
                            p.Value = datetime.ToString("yyyy-MM-dd HH:mm:ss");
                        }
                        break;
                    case Guid guid:
                        p.DbType = DbType.String;
                        p.Value = guid.ToString();
                        break;
                    default:
                        return false;
                }
                command.Parameters.Add(p);
                return true;
            }
        }
        private static readonly Regex _parameterReferenceRegex1 = new("(?<head>\\W+|^)(?<pName>@\\w+)(?<tail>\\W|$)", RegexOptions.Compiled);
        private static readonly Regex _parameterReferenceRegex2 = new("{{[^{}]+}}", RegexOptions.Compiled);
        private static string FillParameterValueInfoQuery(string rawCommandText, List<NativeParameter> parameters, HashSet<string> parameterized)
        {
            var processed = _parameterReferenceRegex2.Replace(rawCommandText, match =>
            {
                var paramName = match.Value[2..^2];
                if (parameterized.Contains(paramName))
                {
                    return match.Value;
                }
                return GetParameterValue(paramName, parameters);
            });
            processed = _parameterReferenceRegex1.Replace(processed, match =>
            {
                var head = match.Groups["head"].Value;
                var tail = match.Groups["tail"].Value;
                var paramName = match.Groups["pName"].Value;
                if (parameterized.Contains(paramName))
                {
                    return match.Value;
                }
                return head + GetParameterValue(paramName, parameters) + tail;
            });
            return processed;
        }
        private static string GetParameterValue(string parameterName, List<NativeParameter> parameters)
        {
            var p = parameters.FirstOrDefault(p => string.Equals(p.Name, parameterName, StringComparison.OrdinalIgnoreCase));
            if (p == null)
            {
                throw new InvalidOperationException($"Can't find the definition of the parameter '{parameterName}'");
            }
            var rawParamValue = p.ParameterValue;
            if (rawParamValue is Array array)
            {
                var pValue = string.Join(",", array.Cast<object>().Where(o => o != null && o != DBNull.Value).Select(o =>
                {
                    switch (o)
                    {
                        case DateTime datetime:
                            if (datetime.TimeOfDay == TimeSpan.Zero)
                            {
                                return $"'{datetime.ToString("yyyy-MM-dd")}'";
                            }
                            else
                            {
                                return $"'{datetime.ToString("yyyy-MM-dd HH:mm:ss")}'";
                            }
                        case Guid guid:
                            return $"'{guid.ToString()}'";
                        default:
                            return o.ToString();
                    }
                }));
                return pValue;
            }
            return rawParamValue.ToString();
        }
    }
}
