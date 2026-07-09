using System.Data;
using MasidBaha.Application.Common.Data;
using Microsoft.Data.SqlClient;

namespace MasidBaha.Application.PushNotifications;

public interface IPushSubscriptionService
{
    Task SubscribeAsync(PushSubscriptionDto subscription);
    Task UnsubscribeAsync(string endpoint);
    Task<List<StoredPushSubscription>> GetAllAsync();
}

public class StoredPushSubscription
{
    public Guid Id { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string P256dh { get; set; } = string.Empty;
    public string Auth { get; set; } = string.Empty;
}

public class PushSubscriptionService : IPushSubscriptionService
{
    private readonly ISqlConnectionFactory _connectionFactory;

    public PushSubscriptionService(ISqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task SubscribeAsync(PushSubscriptionDto subscription)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var command = new SqlCommand("sp_UpsertPushSubscription", connection)
        {
            CommandType = CommandType.StoredProcedure
        };

        command.Parameters.AddWithValue("@SessionId", subscription.SessionId);
        command.Parameters.AddWithValue("@Endpoint", subscription.Endpoint);
        command.Parameters.AddWithValue("@P256dh", subscription.Keys.P256dh);
        command.Parameters.AddWithValue("@Auth", subscription.Keys.Auth);

        await connection.OpenAsync();
        await command.ExecuteNonQueryAsync();
    }

    public async Task UnsubscribeAsync(string endpoint)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var command = new SqlCommand("sp_DeletePushSubscription", connection)
        {
            CommandType = CommandType.StoredProcedure
        };

        command.Parameters.AddWithValue("@Endpoint", endpoint);

        await connection.OpenAsync();
        await command.ExecuteNonQueryAsync();
    }

    public async Task<List<StoredPushSubscription>> GetAllAsync()
    {
        var results = new List<StoredPushSubscription>();

        using var connection = _connectionFactory.CreateConnection();
        using var command = new SqlCommand("sp_GetAllPushSubscriptions", connection)
        {
            CommandType = CommandType.StoredProcedure
        };

        await connection.OpenAsync();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new StoredPushSubscription
            {
                Id = reader.GetGuid(reader.GetOrdinal("Id")),
                SessionId = reader.GetString(reader.GetOrdinal("SessionId")),
                Endpoint = reader.GetString(reader.GetOrdinal("Endpoint")),
                P256dh = reader.GetString(reader.GetOrdinal("P256dh")),
                Auth = reader.GetString(reader.GetOrdinal("Auth"))
            });
        }

        return results;
    }
}
