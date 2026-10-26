using DotNetEnv;
using PlanCoreMAUI.Services;

namespace PlanCoreMAUI;

// Compatibility shim for the ASP.NET Core MVC web app: provides the same static
// service-provider access that was previously in MauiProgram.cs, so the shared
// service classes (e.g. SubscriptionService, PayMongoService) compile and run unchanged.
// Kept in the PlanCoreMAUI namespace so all existing callers work without changes.
public static class MauiProgram
{
    private static IServiceProvider? _services;

    public static IServiceProvider? GetServiceProvider() => _services;

    public static void SetServiceProvider(IServiceProvider services) => _services = services;

    // Initializes the .env / encryption key / connection string resolution
    // used by the rest of the app at startup (Program.cs).
    public static (string ConnectionString, bool Loaded) LoadEnvironment()
    {
        Env.Load();

        // Ensure encryption key exists
        var encryptionKey = Env.GetString("ENCRYPTION_KEY");
        if (string.IsNullOrEmpty(encryptionKey))
        {
            using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
            var keyBytes = new byte[32];
            rng.GetBytes(keyBytes);
            encryptionKey = Convert.ToBase64String(keyBytes);
            try
            {
                var envPath = Path.Combine(AppContext.BaseDirectory, ".env");
                if (!File.Exists(envPath))
                    File.AppendAllText(envPath, $"\n# AES-256 encryption key (generated automatically - do not share)\nENCRYPTION_KEY={encryptionKey}\n");
            }
            catch
            {
                Environment.SetEnvironmentVariable("ENCRYPTION_KEY", encryptionKey);
            }
        }

        // Prefer an environment variable (e.g. set via IIS web.config on the host)
        // so production can point at its own database without changing .env locally.
        var connStr = Environment.GetEnvironmentVariable("CONNECTION_STRING");
        if (string.IsNullOrEmpty(connStr))
            connStr = Env.GetString("CONNECTION_STRING");
        if (!string.IsNullOrEmpty(connStr) && connStr.StartsWith("ENC:"))
        {
            try
            {
                var encService = new EncryptionService();
                connStr = encService.Decrypt(connStr[4..]);
            }
            catch { }
        }

        if (string.IsNullOrEmpty(connStr))
        {
            var host     = Env.GetString("DB_HOST",               "localhost");
            var instance = Env.GetString("DB_INSTANCE",           "SQLEXPRESS");
            var dbName   = Env.GetString("DB_NAME",               "PlanCoreProject");
            var trusted  = Env.GetString("DB_TRUSTED_CONNECTION", "True");
            var encrypt  = Env.GetString("DB_ENCRYPT",            "True");
            var trustCert= Env.GetString("DB_TRUST_CERT",         "True");
            var timeout  = Env.GetString("DB_TIMEOUT",            "30");
            connStr = $"Data Source={host}\\{instance};Initial Catalog={dbName};Integrated Security={trusted};Encrypt={encrypt};Trust Server Certificate={trustCert};Connection Timeout={timeout};";
        }

        return (connStr, true);
    }
}
