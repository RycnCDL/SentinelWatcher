using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SentinelWatcher.Api.Models;

namespace SentinelWatcher.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AuthController : ControllerBase
{
    [HttpGet("me")]
    public ActionResult<UserInfo> GetCurrentUser()
    {
        var user = new UserInfo
        {
            Name = User.FindFirst("name")?.Value
                   ?? User.FindFirst("preferred_username")?.Value
                   ?? "Unknown",
            Email = User.FindFirst("preferred_username")?.Value
                    ?? User.FindFirst("email")?.Value
                    ?? "",
            ObjectId = User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
                       ?? User.FindFirst("oid")?.Value
                       ?? "",
            TenantId = User.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value
                       ?? User.FindFirst("tid")?.Value
                       ?? ""
        };

        return Ok(user);
    }
}
