using System.Security.Cryptography;
using System.Text;
using MasidBaha.Application.Common.Options;
using Microsoft.Extensions.Options;

namespace MasidBaha.WebAPI.Security;

// The server secret behind everything that must not be forgeable by a client:
// the anonymous session cookie and the keyed IP hashes.
public interface ISigningKey
{
    byte[] Bytes { get; }

    // A separate key per purpose, so one use can never be mistaken for another
    byte[] Derive(string purpose);
}

public class SigningKey : ISigningKey
{
    public byte[] Bytes { get; }

    public SigningKey(IOptions<SecurityOptions> options, IHostEnvironment environment, ILogger<SigningKey> logger)
    {
        var configured = options.Value.SessionSigningKey;

        if (!string.IsNullOrWhiteSpace(configured))
        {
            Bytes = Encoding.UTF8.GetBytes(configured);
        }
        else if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
        {
            // Throwaway key: sessions reset (and IP hashes change) whenever the API restarts
            Bytes = RandomNumberGenerator.GetBytes(32);
            logger.LogWarning("Security:SessionSigningKey is not set; using a temporary key. Sessions will reset on restart.");
        }
        else
        {
            throw new InvalidOperationException(
                "Security:SessionSigningKey is not set. Set it (32+ characters) via user-secrets or the Security__SessionSigningKey environment variable.");
        }
    }

    public byte[] Derive(string purpose) => HMACSHA256.HashData(Bytes, Encoding.UTF8.GetBytes(purpose));
}
