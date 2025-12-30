
using DSD_Outbound.Models;
using DSD_Outbound.Services;
using Serilog;

/// <summary>
/// AppRunner is the main orchestrator for executing the outbound API process.
/// It coordinates database lookups, API execution, and data persistence.
/// </summary>
public class AppRunner
{
    // ------------------------------------------------------------
    // Private fields for services used by AppRunner
    // ------------------------------------------------------------
    // _sqlService: Handles all database operations such as:
    //   - Fetching AccessInfo (credentials and DB details for the customer)
    //   - Retrieving the list of APIs to execute
    //   - Deleting records from tables before inserting new data
    //
    // _apiExecutorService: Responsible for:
    //   - Calling external APIs
    //   - Handling OAuth token management
    //   - Inserting API response data into SQL tables
    private readonly SqlService _sqlService;
    private readonly ApiExecutorService _apiExecutorService;

    // ------------------------------------------------------------
    // Constructor: Dependency Injection of required services
    // ------------------------------------------------------------
    // These services are registered in Program.cs and injected by the DI container.
    // This promotes testability and loose coupling.
    public AppRunner(SqlService sqlService, ApiExecutorService apiExecutorService)
    {
        _sqlService = sqlService;
        _apiExecutorService = apiExecutorService;
    }

    // ------------------------------------------------------------
    // RunAsync: Main entry point for executing the application logic
    // ------------------------------------------------------------
    // This method performs the following steps:
    // 1. Reads input arguments (customer code, group, sendCIS flag)
    // 2. Retrieves configuration and API details from the database
    // 3. Deletes old records based on group logic
    // 4. Executes APIs and stores results in SQL
    // 5. Handles errors gracefully
    public async Task RunAsync(string[] args)
    {
        try
        {
            // ------------------------------------------------------------
            // STEP 1: Determine customer code and other parameters
            // ------------------------------------------------------------
            // If arguments are provided, use them; otherwise, fall back to defaults.
            // args[0] = Customer Code (default: DEMO)
            // args[1] = Group (default: ALL)
            // args[2] = Send CIS flag (default: N)
            var customerCode = args.Length > 0 ? args[0] : "DEMO";
            var group = args.Length > 1 ? args[1] : "ALL";
            var sendCIS = args.Length > 2 ? args[2] : "N";

            // Log the starting point for this customer
            Log.Information("Starting AppRunner for customer {CustomerCode}", customerCode);

            // ------------------------------------------------------------
            // STEP 2: Retrieve AccessInfo from the database
            // ------------------------------------------------------------
            // AccessInfo contains credentials, OAuth details, and database name.
            // This is critical for connecting to the correct customer database.
            var accessInfo = await _sqlService.GetAccessInfoAsync(customerCode);
            Log.Information("AccessInfo retrieved for customer {CustomerCode}", customerCode);

            // ------------------------------------------------------------
            // STEP 3: Get list of APIs to execute for this customer
            // ------------------------------------------------------------
            // The API list is filtered by InitialCatalog (from AccessInfo) and group.
            // Each API entry contains:
            //   - API name (endpoint)
            //   - Target SQL table name
            //   - Filter criteria for dynamic queries
            var apiList = await _sqlService.GetApiListAsync(accessInfo.InitialCatalog, group);
            Log.Information("Retrieved {Count} APIs for execution", apiList.Count);

            // If no APIs are found, log a warning and exit gracefully.
            if (apiList.Count == 0)
            {
                Log.Warning("No APIs found for customer {CustomerCode}", customerCode);
                return;
            }

            // ------------------------------------------------------------
            // STEP 4: Delete old records before inserting new data
            // ------------------------------------------------------------
            // Logic:
            // - If group starts with "HFS", delete only the table matching the group name.
            // - Otherwise, delete all tables with prefix "HFS" (excluding views).
            if (group.StartsWith("HFS", StringComparison.OrdinalIgnoreCase))
            {
                Log.Information("Group starts with HFS. Deleting records only from table {Group} in database {Database}", group, accessInfo.InitialCatalog);
                await _sqlService.DeleteSingleTableAsync(accessInfo.InitialCatalog, group);
            }
            else
            {
                Log.Information("Deleting all records from tables with prefix 'HFS' in database {Database}", accessInfo.InitialCatalog);
                await _sqlService.DeleteTablesWithPrefixAsync(accessInfo.InitialCatalog, "HFS");
            }

            // ------------------------------------------------------------
            // STEP 5: Execute APIs and insert results into SQL
            // ------------------------------------------------------------
            // ApiExecutorService handles:
            // - OAuth token acquisition and refresh
            // - API calls with retry policies
            // - JSON parsing and dynamic SQL inserts
            await _apiExecutorService.ExecuteApisAndInsertAsync(apiList, accessInfo);
            Log.Information("API execution completed for customer {CustomerCode}", customerCode);
        }
        catch (Exception ex)
        {
            // ------------------------------------------------------------
            // STEP 6: Error handling
            // ------------------------------------------------------------
            // Log the exception details and rethrow to allow higher-level handling.
            Log.Error(ex, "Error occurred while running AppRunner");
            throw;
        }
    }
}
