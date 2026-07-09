using Microsoft.Data.SqlClient;

namespace MasidBaha.Application.Common.Data;

// SqlDataReader.GetDateTime() always comes back as DateTimeKind.Unspecified,
// even though every timestamp column here is stored as UTC. When that gets
// serialized to JSON it has no "Z" at the end, so the browser reads it as
// local time and the times end up wrong by whatever the reader's timezone
// offset is. Use these helpers instead of calling GetDateTime directly.
public static class SqlDataReaderExtensions
{
    public static DateTime GetUtcDateTime(this SqlDataReader reader, int ordinal) =>
        DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc);

    public static DateTime? GetUtcDateTimeOrNull(this SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetUtcDateTime(ordinal);
}
