
using DSD_Outbound.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Polly;
using Serilog;

namespace DSD_Outbound.Services
{
    public class SqlService
    {
        // ------------------------------------------------------------
        // Private field for configuration
        // ------------------------------------------------------------
        // IConfiguration is injected via DI and provides access to
        // appsettings.json values such as connection strings and flags.
        private readonly IConfiguration _config;

        // ------------------------------------------------------------
        // Constructor: Dependency Injection of IConfiguration
        // ------------------------------------------------------------
        public SqlService(IConfiguration config)
        {
            _config = config;
        }

        // ------------------------------------------------------------
        // GetApiListAsync: Retrieves list of APIs for a given customer DB and group
        // ------------------------------------------------------------
        // Parameters:
        //   db    - Database name (InitialCatalog for the customer)
        //   group - API execution group (e.g., ALL, HFS)
        // Returns:
        //   List<TableApiName> containing API details for execution


        public async Task DeleteSingleTableAsync(string databaseName, string tableName)
        {
            var connectionString = _config.GetConnectionString("CustomerConnectionDB")
                                          .Replace("CustomerConnection", databaseName);

            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();

            // Validate table exists and is a base table
            var checkTableSql = @"
        SELECT COUNT(*) 
        FROM INFORMATION_SCHEMA.TABLES
        WHERE TABLE_TYPE = 'BASE TABLE' AND TABLE_NAME = @tableName";

            await using (var cmd = new SqlCommand(checkTableSql, conn))
            {
                cmd.Parameters.AddWithValue("@tableName", tableName);
                var exists = (int)await cmd.ExecuteScalarAsync();
                if (exists == 0)
                {
                    Log.Warning("Table {TableName} does not exist or is not a base table.", tableName);
                    return;
                }
            }

            var deleteSql = $"DELETE FROM [{tableName}]";
            await using var deleteCmd = new SqlCommand(deleteSql, conn);
            var rowsAffected = await deleteCmd.ExecuteNonQueryAsync();
            Log.Information("Deleted {RowsAffected} rows from table {TableName}", rowsAffected, tableName);
        }

        public async Task DeleteTablesWithPrefixAsync(string databaseName, string prefix)
        {
            var connectionString = _config.GetConnectionString("CustomerConnectionDB")
                                          .Replace("CustomerConnection", databaseName);

            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();

            // Get all table names with the prefix
            var getTablesSql = @"
        SELECT TABLE_NAME
        FROM INFORMATION_SCHEMA.TABLES
        WHERE TABLE_TYPE = 'BASE TABLE' AND TABLE_NAME LIKE @prefix + '%'";

            var tables = new List<string>();
            await using (var cmd = new SqlCommand(getTablesSql, conn))
            {
                cmd.Parameters.AddWithValue("@prefix", prefix);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    tables.Add(reader.GetString(0));
                }
            }

            // Delete data from each table
            foreach (var table in tables)
            {
                var deleteSql = $"DELETE FROM [{table}]";
                await using var deleteCmd = new SqlCommand(deleteSql, conn);
                await deleteCmd.ExecuteNonQueryAsync();
                Log.Information("Deleted all records from table {Table}", table);
            }
        }

        public async Task<List<TableApiName>> GetApiListAsync(string db, string group)
        {
            var tableApiNames = new List<TableApiName>();

            // Build connection string dynamically by replacing InitialCatalog
            string connectionString = _config.GetConnectionString("CustomerConnectionDB")
                                             .Replace("CustomerConnection", db);

            // Use 'await using' for proper disposal of SqlConnection
            await using var conn = new SqlConnection(connectionString);

            try
            {
                // Log attempt to connect
                Log.Information("Attempting to connect to {Database} for API List", db);
                await conn.OpenAsync();
                Log.Information("Connected to database {Database}", conn.Database);

                // Base SQL query to retrieve API list
                string sql = @"SELECT [TABLE_NAME], [ENDPOINT], [FILTER], [BATCHSIZE]
                               FROM dsd_api_list
                               WHERE Dir = 'Outbound' AND RUNGROUP = @group
                               ORDER BY API_NAME";

                // Special case: If group starts with 'HFS', override query logic
                if (group.StartsWith("HFS"))
                {
                    sql = @"SELECT [TABLE_NAME], [ENDPOINT], [FILTER], [BATCHSIZE]
                            FROM dsd_api_list
                            WHERE Dir = 'Outbound' AND RUNGROUP = 'ALL' AND [TABLE_NAME] = @group";
                }

                // Prepare SQL command with parameterized query to prevent SQL injection
                await using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@group", group);

                // Execute query and read results asynchronously
                await using var rdr = await cmd.ExecuteReaderAsync();
                while (await rdr.ReadAsync())
                {
                    var apiInfo = new TableApiName
                    {
                        tableName = rdr["TABLE_NAME"].ToString(),
                        APIname = rdr["ENDPOINT"].ToString(),
                        filter = rdr["FILTER"].ToString(),
                        batchSize = (int)rdr["BATCHSIZE"]
                    };
                    tableApiNames.Add(apiInfo);
                }

                return tableApiNames;
            }
            catch (Exception ex)
            {
                // Log error and rethrow for higher-level handling
                Log.Error(ex, "Failed to retrieve API list from database {Database}", db);
                throw;
            }
        }

        // ------------------------------------------------------------
        // GetAccessInfoAsync: Retrieves customer-specific access credentials
        // ------------------------------------------------------------
        // Parameters:
        //   customerCode - Unique identifier for the customer
        // Returns:
        //   AccessInfo object populated with API, FTP, and DB credentials
        public async Task<AccessInfo> GetAccessInfoAsync(string customerCode)
        {
            var accessInfo = new AccessInfo();

            // Base connection string for CustomerConnectionDB
            string connectionString = _config.GetConnectionString("CustomerConnectionDB");

            // PROD flag from appsettings.json (e.g., "Y" or "N")
            string prod = _config["prod"];

            // ------------------------------------------------------------
            // Define Polly retry policy for transient errors
            // ------------------------------------------------------------
            // Retries 3 times with exponential backoff (2s, 4s, 6s)
            var retryPolicy = Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(2 * attempt),
                    (exception, timespan, attempt, context) =>
                    {
                        Log.Warning(exception, "Retry {Attempt} after {Delay}s due to error connecting to CustomerConnectionDB", attempt, timespan.TotalSeconds);
                    });

            // Execute DB logic with retry policy
            await retryPolicy.ExecuteAsync(async () =>
            {
                await using var conn = new SqlConnection(connectionString);
                try
                {
                    Log.Information("Attempting to connect to CustomerConnectionDB for customer {Customer}", customerCode);
                    await conn.OpenAsync();
                    Log.Information("Connected to database {Database}", conn.Database);

                    // SQL query to fetch customer info based on customer code and PROD flag
                    string sql = "SELECT * FROM DSD_CustomerInfo WHERE customer = @customer AND PROD = @prod";

                    await using var cmd = new SqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@customer", customerCode);
                    cmd.Parameters.AddWithValue("@prod", prod);

                    // Execute query and read results
                    await using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        // Populate AccessInfo object with values from DB
                        accessInfo.Url = reader["Url"].ToString();
                        accessInfo.Grant_Type = reader["Grant_Type"].ToString();
                        accessInfo.Client_ID = reader["Client_ID"].ToString();
                        accessInfo.Scope = reader["Scope"].ToString();
                        accessInfo.Client_Secret = reader["Client_Secret"].ToString();
                        accessInfo.RootUrl = reader["RootUrl"].ToString();
                        accessInfo.ftpHost = reader["ftpHost"].ToString();
                        accessInfo.ftpUser = reader["ftpUser"].ToString();
                        accessInfo.ftpPass = reader["ftpPass"].ToString();
                        accessInfo.ftpRemoteFilePath = reader["ftpRemoteFilePath"].ToString();
                        accessInfo.ftpLocalFilePath = reader["ftpLocalFilePath"].ToString();
                        accessInfo.DataSource = reader["DataSource"].ToString();
                        accessInfo.InitialCatalog = reader["InitialCatalog"].ToString();
                        accessInfo.UserID = reader["UserID"].ToString();
                        accessInfo.Password = reader["Password"].ToString();
                        accessInfo.DayOffset = reader["DayOffset"].ToString();
                        accessInfo.email_tenantId = reader["email_tenantId"].ToString();
                        accessInfo.email_clientId = reader["email_clientid"].ToString();
                        accessInfo.email_secret = reader["email_secret"].ToString();
                        accessInfo.email_sender = reader["email_sender"].ToString();
                        accessInfo.email_recipient = reader["email_recipient"].ToString();

                        Log.Information("AccessInfo successfully retrieved for customer {Customer}", customerCode);
                    }
                    else
                    {
                        // No record found for customer
                        Log.Error("No customer info found for {Customer}", customerCode);
                        throw new Exception($"No customer info found for {customerCode}");
                    }
                }
                catch (Exception ex)
                {
                    // Log error and let Polly handle retry
                    Log.Error(ex, "Failed to retrieve AccessInfo for customer {Customer}", customerCode);
                    throw;
                }
            });

            return accessInfo;
        }
    }
}

//using DSD_Outbound.Models;
//using Microsoft.Data.SqlClient;
//using Microsoft.Extensions.Configuration;
//using Polly;
//using Serilog;

//namespace DSD_Outbound.Services
//{
//    public class SqlService
//    {
//        private readonly IConfiguration _config;

//        public SqlService(IConfiguration config)
//        {
//            _config = config;
//        }

//        public async Task<List<TableApiName>> GetApiListAsync(string db, string group)
//        {
//            var tableApiNames = new List<TableApiName>();
//            string connectionString = _config.GetConnectionString("CustomerConnectionDB")
//                                             .Replace("CustomerConnection", db);

//            await using var conn = new SqlConnection(connectionString);
//            try
//            {
//                Log.Information("Attempting to connect to {Database} for API List", db);
//                await conn.OpenAsync();
//                Log.Information("Connected to database {Database}", conn.Database);

//                string sql = @"SELECT [TABLE_NAME], [API_NAME], [FILTER], [BATCHSIZE]
//                               FROM dsd_api_list
//                               WHERE Dir = 'Outbound' AND RUNGROUP = @group
//                               ORDER BY API_NAME";

//                if (group.StartsWith("HFS"))
//                {
//                    sql = @"SELECT [TABLE_NAME], [API_NAME], [FILTER], [BATCHSIZE]
//                            FROM dsd_api_list
//                            WHERE Dir = 'Outbound' AND RUNGROUP = 'ALL' AND [TABLE_NAME] = @group";
//                }

//                await using var cmd = new SqlCommand(sql, conn);
//                cmd.Parameters.AddWithValue("@group", group);

//                await using var rdr = await cmd.ExecuteReaderAsync();
//                while (await rdr.ReadAsync())
//                {
//                    var apiInfo = new TableApiName
//                    {
//                        tableName = rdr["TABLE_NAME"].ToString(),
//                        APIname = rdr["API_NAME"].ToString(),
//                        filter = rdr["FILTER"].ToString(),
//                        batchSize = (int)rdr["BATCHSIZE"]
//                    };
//                    tableApiNames.Add(apiInfo);
//                }

//                return tableApiNames;
//            }
//            catch (Exception ex)
//            {
//                Log.Error(ex, "Failed to retrieve API list from database {Database}", db);
//                throw;
//            }
//        }

//        public async Task<AccessInfo> GetAccessInfoAsync(string customerCode)
//        {
//            var accessInfo = new AccessInfo();
//            string connectionString = _config.GetConnectionString("CustomerConnectionDB");
//            string prod = _config["prod"];

//            // Define Polly retry policy
//            var retryPolicy = Policy
//                .Handle<Exception>()
//                .WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(2 * attempt),
//                    (exception, timespan, attempt, context) =>
//                    {
//                        Log.Warning(exception, "Retry {Attempt} after {Delay}s due to error connecting to CustomerConnectionDB", attempt, timespan.TotalSeconds);
//                    });

//            await retryPolicy.ExecuteAsync(async () =>
//            {
//                await using var conn = new SqlConnection(connectionString);
//                try
//                {
//                    Log.Information("Attempting to connect to CustomerConnectionDB for customer {Customer}", customerCode);
//                    await conn.OpenAsync();
//                    Log.Information("Connected to database {Database}", conn.Database);

//                    string sql = "SELECT * FROM DSD_CustomerInfo WHERE customer = @customer AND PROD = @prod";

//                    await using var cmd = new SqlCommand(sql, conn);
//                    cmd.Parameters.AddWithValue("@customer", customerCode);
//                    cmd.Parameters.AddWithValue("@prod", prod);

//                    await using var reader = await cmd.ExecuteReaderAsync();
//                    if (await reader.ReadAsync())
//                    {
//                        accessInfo.Url = reader["Url"].ToString();
//                        accessInfo.Grant_Type = reader["Grant_Type"].ToString();
//                        accessInfo.Client_ID = reader["Client_ID"].ToString();
//                        accessInfo.Scope = reader["Scope"].ToString();
//                        accessInfo.Client_Secret = reader["Client_Secret"].ToString();
//                        accessInfo.RootUrl = reader["RootUrl"].ToString();
//                        accessInfo.ftpHost = reader["ftpHost"].ToString();
//                        accessInfo.ftpUser = reader["ftpUser"].ToString();
//                        accessInfo.ftpPass = reader["ftpPass"].ToString();
//                        accessInfo.ftpRemoteFilePath = reader["ftpRemoteFilePath"].ToString();
//                        accessInfo.ftpLocalFilePath = reader["ftpLocalFilePath"].ToString();
//                        accessInfo.DataSource = reader["DataSource"].ToString();
//                        accessInfo.InitialCatalog = reader["InitialCatalog"].ToString();
//                        accessInfo.UserID = reader["UserID"].ToString();
//                        accessInfo.Password = reader["Password"].ToString();
//                        accessInfo.DayOffset = reader["DayOffset"].ToString();
//                        accessInfo.email_tenantId = reader["email_tenantId"].ToString();
//                        accessInfo.email_clientId = reader["email_clientid"].ToString();
//                        accessInfo.email_secret = reader["email_secret"].ToString();
//                        accessInfo.email_sender = reader["email_sender"].ToString();
//                        accessInfo.email_recipient = reader["email_recipient"].ToString();

//                        Log.Information("AccessInfo successfully retrieved for customer {Customer}", customerCode);
//                    }
//                    else
//                    {
//                        Log.Error("No customer info found for {Customer}", customerCode);
//                        throw new Exception($"No customer info found for {customerCode}");
//                    }
//                }
//                catch (Exception ex)
//                {
//                    Log.Error(ex, "Failed to retrieve AccessInfo for customer {Customer}", customerCode);
//                    throw; // Let Polly handle retry
//                }
//            });

//            return accessInfo;
//        }
//    }
//}
