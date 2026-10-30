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
    private readonly ApprovalPolicy _policy;

    public ApprovalsController(ApprovalService approvals, ApprovalPolicy policy)
    {
        _approvals = approvals;
        _policy = policy;
    }

    public record RejectRequest(string Reason);

    [HttpPost("{invoiceId}/approve")]
    public ActionResult<InvoiceView> Approve(string invoiceId) =>
        Handle(() => View(_approvals.Approve(invoiceId, CurrentApprover())));

    [HttpPost("{invoiceId}/reject")]
    public ActionResult<InvoiceView> Reject(string invoiceId, [FromBody] RejectRequest request) =>
        Handle(() => View(_approvals.Reject(invoiceId, CurrentApprover(), request.Reason)));

    private InvoiceView View(Invoice invoice) =>
        InvoiceView.From(invoice, _policy, _approvals.EligibilityFor(invoice, CurrentApprover()));

    private Approver CurrentApprover()
    {
        var session = HttpContext.GetSession();
        return new Approver(session.Username, session.Role);
    }
}
