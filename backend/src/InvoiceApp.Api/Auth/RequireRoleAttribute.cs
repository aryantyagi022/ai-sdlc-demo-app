using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace InvoiceApp.Api.Auth;

/// <summary>
/// Allows the action only for the listed roles. Refusals use the same shape as the ones the
/// domain raises, so a caller can read <c>reason</c> without knowing which layer refused.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireRoleAttribute : ActionFilterAttribute
{
    private readonly string[] _roles;

    public RequireRoleAttribute(params string[] roles)
    {
        _roles = roles;
    }

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var session = context.HttpContext.Items[HttpContextExtensions.SessionKey] as UserSession;

        if (session is not null && _roles.Contains(session.Role, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        var role = session?.Role ?? "anonymous";

        context.Result = new ObjectResult(new
        {
            error = $"Role {role} is not allowed to perform this action.",
            reason = "RoleNotPermitted"
        })
        {
            StatusCode = StatusCodes.Status403Forbidden
        };
    }
}
