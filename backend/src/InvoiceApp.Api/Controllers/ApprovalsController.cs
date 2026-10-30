using InvoiceApp.Api.Auth;
using InvoiceApp.Domain;
using Microsoft.AspNetCore.Mvc;

namespace InvoiceApp.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ApprovalsController : ApiControllerBase
{
    // No [RequireRole] here on purpose: which roles may approve is owned by Approval:RoleLimits
    // and enforced by ApprovalService. Repeating the list here would be a second copy that
    // silently blocks any role added to configuration.

    private readonly ApprovalService _approvals;

    public ApprovalsController(ApprovalService approvals)
    {
        _approvals = approvals;
    }

    public record RejectRequest(string Reason);

    [HttpPost("{invoiceId}/approve")]
    public ActionResult<Invoice> Approve(string invoiceId) =>
        Handle(() => _approvals.Approve(invoiceId, CurrentApprover()));

    [HttpPost("{invoiceId}/reject")]
    public ActionResult<Invoice> Reject(string invoiceId, [FromBody] RejectRequest request) =>
        Handle(() => _approvals.Reject(invoiceId, CurrentApprover(), request.Reason));

    private Approver CurrentApprover()
    {
        var session = HttpContext.GetSession();
        return new Approver(session.Username, session.Role);
    }
}
