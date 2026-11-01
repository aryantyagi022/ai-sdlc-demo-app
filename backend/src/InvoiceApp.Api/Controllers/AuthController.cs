using InvoiceApp.Api.Auth;
using InvoiceApp.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace InvoiceApp.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly DemoUsersOptions _options;
    private readonly SessionStore _sessions;
    private readonly ApprovalPolicy _policy;

    public AuthController(IOptions<DemoUsersOptions> options, SessionStore sessions, ApprovalPolicy policy)
    {
        _options = options.Value;
        _sessions = sessions;
        _policy = policy;
    }

    public record LoginRequest(string Username, string Password);

    /// <summary>
    /// <paramref name="CanApprove"/> comes from the same policy the service enforces with, so the
    /// UI's role gate cannot drift from the configured ladder when a role is added or removed.
    /// </summary>
    public record LoginResponse(string Token, string Username, string Role, bool CanApprove);

    [HttpPost("login")]
    public ActionResult<LoginResponse> Login([FromBody] LoginRequest request)
    {
        var user = _options.Users.FirstOrDefault(u =>
            u.Username == request.Username && u.Password == request.Password);

        if (user is null)
        {
            return Unauthorized(new { error = "Invalid username or password." });
        }

        var token = _sessions.Create(new UserSession(user.Username, user.Role));
        return Ok(new LoginResponse(token, user.Username, user.Role, _policy.CanApprove(user.Role)));
    }

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        var header = Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer "))
        {
            _sessions.Remove(header["Bearer ".Length..]);
        }

        return NoContent();
    }
}
