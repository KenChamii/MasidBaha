using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace MasidBaha.Application.Common.Data;

public interface ISqlConnectionFactory
{
    SqlConnection CreateConnection();
}

public class SqlConnectionFactory : ISqlConnectionFactory
{
    private readonly string _connectionString;

    public SqlConnectionFactory(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("MasidBahaDb");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "ConnectionStrings:MasidBahaDb is not set. Run scripts/init-dev-secrets.ps1 or see appsettings.Example.json.");

        _connectionString = connectionString;
    }

    public SqlConnection CreateConnection() => new SqlConnection(_connectionString);
}