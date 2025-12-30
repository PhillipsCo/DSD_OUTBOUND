
using DSD_Outbound.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

public class Program
{
    public static async Task Main(string[] args)
    {

        //// ------------------------------------------------------------
        //// STEP 0: Determine configuration file path
        //// ------------------------------------------------------------
        //// We check for an environment variable named CONFIG_PATH.
        //// If it's set, we use that directory; otherwise, we default to the current directory.
        //var configDirectory = Environment.GetEnvironmentVariable("CONFIG_PATH")
        //                      ?? Directory.GetCurrentDirectory();

        //// Combine directory with file name for full path
        //var configFilePath = Path.Combine(configDirectory, "appsettings.json");

        // ------------------------------------------------------------
        // STEP 1: Load application configuration from appsettings.json
        // ------------------------------------------------------------
        // We use ConfigurationBuilder to read settings from appsettings.json.
        // This allows us to access values like connection strings, environment flags,
        // and Serilog configuration without hardcoding them in code.
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        // ------------------------------------------------------------
        // STEP 2: Configure Serilog using settings from configuration
        // ------------------------------------------------------------
        // Instead of hardcoding sinks and log paths, we use Serilog's
        // ReadFrom.Configuration() method to load all logging settings
        // from the "Serilog" section in appsettings.json.
        // This makes logging fully configurable without code changes.
        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .CreateLogger();

        try
        {
            // ------------------------------------------------------------
            // STEP 3: Log application startup message
            // ------------------------------------------------------------
            // This is the first log entry indicating the application is starting.
            Log.Information("Starting DSD Outbound application...");

            // ------------------------------------------------------------
            // STEP 4: Build the Host for Dependency Injection and Configuration
            // ------------------------------------------------------------
            // Host.CreateDefaultBuilder sets up:
            // - Dependency Injection (DI)
            // - Configuration (including environment variables)
            // - Logging (integrated with Serilog via UseSerilog())
            var host = Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((context, config) =>
                {
                    // Ensure appsettings.json is loaded into the host configuration.
                    // reloadOnChange: true means changes to the file will be picked up at runtime.
                    config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                })
                .UseSerilog() // Integrates Serilog with the Host logging pipeline
                .ConfigureServices((context, services) =>
                {
                    // ------------------------------------------------------------
                    // STEP 5: Register application services with DI container
                    // ------------------------------------------------------------
                    // IHttpClientFactory for making HTTP calls (named client "ApiClient")
                    services.AddHttpClient("ApiClient");

                    // Register IConfiguration so services can access app settings
                    services.AddSingleton<IConfiguration>(context.Configuration);

                    // Register custom services for application logic
                    services.AddTransient<ApiService>();
                    services.AddTransient<EmailService>();
                    services.AddTransient<FtpService>();
                    services.AddTransient<SqlService>();
                    services.AddTransient<ApiExecutorService>();
                    services.AddTransient<AppRunner>();
                })
                .Build(); // Build the Host object

            // ------------------------------------------------------------
            // STEP 6: Resolve AppRunner and execute main logic
            // ------------------------------------------------------------
            // We create a scope to resolve scoped services and run the application.
            using var scope = host.Services.CreateScope();
            var appRunner = scope.ServiceProvider.GetRequiredService<AppRunner>();

            // Run the main application logic asynchronously
            await appRunner.RunAsync(args);

            // ------------------------------------------------------------
            // STEP 7: Log successful completion
            // ------------------------------------------------------------
            Log.Information("DSD Outbound application completed successfully.");
        }
        catch (Exception ex)
        {
            // ------------------------------------------------------------
            // STEP 8: Log fatal errors
            // ------------------------------------------------------------
            // If any unhandled exception occurs, log it as Fatal.
            Log.Fatal(ex, "Application terminated unexpectedly");
        }
        finally
        {
            // ------------------------------------------------------------
            // STEP 9: Flush and close Serilog
            // ------------------------------------------------------------
            // Ensures all log entries are written before the application exits.
            Log.CloseAndFlush();
        }
    }
}


//using DSD_Outbound.Services;
//using Microsoft.Extensions.Configuration;
//using Microsoft.Extensions.DependencyInjection;
//using Microsoft.Extensions.Hosting;
//using Serilog;

//public class Program
//{
//    public static async Task Main(string[] args)
//    {
//        // Configure Serilog
//        Log.Logger = new LoggerConfiguration()
//           .Enrich.WithProperty("Application", "DSD_Outbound")
//           .WriteTo.Console()
//           .WriteTo.File("c:logs//outbound-log-.txt", rollingInterval: RollingInterval.Day)
//           .CreateLogger();

//        try
//        {
//            Log.Information("Starting DSD Outbound application...");

//            // Build Host with DI and Configuration
//            var host = Host.CreateDefaultBuilder(args)
//                .ConfigureAppConfiguration((context, config) =>
//                {
//                    // Load appsettings.json
//                    config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
//                })
//                .UseSerilog() // Integrate Serilog
//                .ConfigureServices((context, services) =>
//                {
//                    // Register IHttpClientFactory
//                    services.AddHttpClient("ApiClient"); // Named client for API calls

//                    // Register services
//                    services.AddSingleton<IConfiguration>(context.Configuration);
//                    services.AddTransient<ApiService>();
//                    services.AddTransient<EmailService>();
//                    services.AddTransient<FtpService>();
//                    services.AddTransient<SqlService>();
//                    services.AddTransient<ApiExecutorService>();
//                    services.AddTransient<AppRunner>();
//                })
//                .Build();

//            // Run the main application logic
//            using var scope = host.Services.CreateScope();
//            var appRunner = scope.ServiceProvider.GetRequiredService<AppRunner>();
//            await appRunner.RunAsync(args);

//            Log.Information("DSD Outbound application completed successfully.");
//        }
//        catch (Exception ex)
//        {
//            Log.Fatal(ex, "Application terminated unexpectedly");
//        }
//        finally
//        {
//            Log.CloseAndFlush();
//        }
//    }
//}

