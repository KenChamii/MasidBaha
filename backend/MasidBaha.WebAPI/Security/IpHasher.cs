using System.Security.Cryptography;
using System.Text;

namespace MasidBaha.WebAPI.Security;

// Reports record who sent them only as a keyed hash of the IP address: enough for
// staff to block a source that keeps abusing the app, without the system holding
// anyone's actual address. Without the server key the hashes can't be reversed
// or even compared against a list of known addresses.
public interface IIpHasher
{
    string? Hash(HttpContext context);
}

public class IpHasher : IIpHasher
{
    private readonly byte[] _key;

    public IpHasher(ISigningKey signingKey)
    {
        _key = signingKey.Derive("ip-hash-v1");
    }

    public string? Hash(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;
        if (address is null) return null;

        // An IPv4 address and its IPv6-mapped form are the same machine
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();

        return Convert.ToHexStringLower(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(address.ToString())));
    }
}
