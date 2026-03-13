using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace PathWeb.Controllers;

public abstract class BaseController : Controller
{
    protected byte GetAuthLevel() => (byte)(HttpContext.Items["AuthLevel"] ?? (byte)0);
    protected string GetUserEmail() => User.FindFirst(ClaimTypes.Email)?.Value
                                       ?? User.Identity?.Name ?? "unknown";
}
