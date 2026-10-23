using System.Security.Claims;
using MasidBaha.Application.Admin.Audit;

namespace MasidBaha.WebAPI.Security;

// The signed-in staff member behind the current request, for audit trails and ban records
public class CurrentAdmin : ICurrentAdmin
{
    private readonly IHttpContextAccessor _accessor;

    public CurrentAdmin(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    private ClaimsPrincipal? User => _accessor.HttpContext?.User;

    public Guid? Id => Guid.TryParse(User?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public string Email => User?.FindFirstValue(ClaimTypes.Email) ?? User?.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
}
