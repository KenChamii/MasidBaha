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
        _connectionString = configuration.GetConnectionString("MasidBahaDb")
            ?? throw new InvalidOperationException("MasidBahaDb connection string not found");
    }

    public SqlConnection CreateConnection() => new SqlConnection(_connectionString);
}