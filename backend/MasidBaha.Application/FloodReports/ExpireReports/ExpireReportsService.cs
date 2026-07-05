using System.Data;
using MasidBaha.Application.Common.Data;
using Microsoft.Data.SqlClient;

namespace MasidBaha.Application.FloodReports.ExpireReports;

public interface IExpireReportsService
{
    Task<List<Guid>> ExpireStaleReportsAsync();
}

public class ExpireReportsService : IExpireReportsService
{
    private readonly ISqlConnectionFactory _connectionFactory;

    public ExpireReportsService(ISqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<List<Guid>> ExpireStaleReportsAsync()
    {
        var expiredIds = new List<Guid>();

        using var connection = _connectionFactory.CreateConnection();
        using var command = new SqlCommand("sp_ExpireStaleReports", connection)
        {
            CommandType = CommandType.StoredProcedure
        };

        await connection.OpenAsync();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            expiredIds.Add(reader.GetGuid(reader.GetOrdinal("Id")));

        return expiredIds;
    }
}