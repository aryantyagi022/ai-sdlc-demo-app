namespace InvoiceApp.Domain;

/// <summary>
/// Moves an invoice through its lifecycle: Draft -> PendingApproval -> Approved or Rejected.
/// Approval authority is graded by role. Invoices above the highest single-approver limit need
/// two different people, and the second must hold the second-approver role.
/// </summary>
public class ApprovalService
{
    private readonly IInvoiceRepository _repository;
    private readonly ApprovalPolicy _policy;
    private readonly IRefusalLog _refusalLog;

    public ApprovalService(IInvoiceRepository repository, ApprovalPolicy policy, IRefusalLog refusalLog)
    {
        _repository = repository;
        _policy = policy;
        _refusalLog = refusalLog;
    }

    public Invoice Submit(string invoiceId, string submittedBy)
    {
        var invoice = Find(invoiceId);

        if (invoice.Status != InvoiceStatus.Draft)
        {
            throw new InvalidOperationException(
                $"Invoice {invoiceId} is {invoice.Status}; only drafts can be submitted.");
        }

        invoice.Status = InvoiceStatus.PendingApproval;
        invoice.SubmittedBy = submittedBy;
        return Save(invoice);
    }

    /// <summary>
    /// Records an approval signature. Every check runs before anything is changed, so a refused
    /// attempt leaves the invoice exactly as it was.
    /// </summary>
    public Invoice Approve(string invoiceId, Approver approver)
    {
        RefuseIfNotAuthorised(invoiceId, approver, DecisionAction.Approve);

        var invoice = Find(invoiceId);
        EnsurePending(invoice);
        RefuseIfNotPermitted(invoice, approver);

        invoice.Approvals.Add(new ApprovalRecord(approver.Username, approver.Role, DateTime.UtcNow));

        if (ApprovalsAreComplete(invoice))
        {
            invoice.Status = InvoiceStatus.Approved;
            invoice.DecidedBy = approver.Username;
            invoice.DecidedAt = DateTime.UtcNow;
        }

        return Save(invoice);
    }

    /// <summary>
    /// Rejects an invoice. Any approving role may reject any amount alone. Approvals already
    /// recorded are kept, so the trail reads "Manager approved, Director rejected".
    /// </summary>
    public Invoice Reject(string invoiceId, Approver approver, string reason)
    {
        RefuseIfNotAuthorised(invoiceId, approver, DecisionAction.Reject);

        var invoice = Find(invoiceId);
        EnsurePending(invoice);

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A reason is required to reject an invoice.", nameof(reason));
        }

        invoice.Status = InvoiceStatus.Rejected;
        invoice.DecidedBy = approver.Username;
        invoice.DecidedAt = DateTime.UtcNow;
        invoice.DecisionNote = reason;
        return Save(invoice);
    }

    /// <summary>
    /// Answers "could this person approve this invoice right now?" using the very checks that
    /// enforce it, so what the UI shows can never drift from what the service allows.
    /// </summary>
    public ApprovalEligibility EligibilityFor(Invoice invoice, Approver approver)
    {
        var roleRefusal = _policy.RefuseRole(approver);

        if (roleRefusal is not null)
        {
            return ApprovalEligibility.Refused(roleRefusal.Reason, roleRefusal.Message);
        }

        if (invoice.Status != InvoiceStatus.PendingApproval)
        {
            return ApprovalEligibility.NotOpenForDecision(NotPendingMessage(invoice));
        }

        var refusal = _policy.RefuseApproval(invoice, approver);

        return refusal is null
            ? ApprovalEligibility.Allowed
            : ApprovalEligibility.Refused(refusal.Reason, refusal.Message);
    }

    /// <summary>
    /// Step 1, before the invoice is looked up and before any argument is validated, so a role
    /// that cannot approve never learns whether an invoice exists.
    /// </summary>
    private void RefuseIfNotAuthorised(string invoiceId, Approver approver, DecisionAction action)
    {
        var refusal = _policy.RefuseRole(approver);

        if (refusal is not null)
        {
            Refuse(refusal, invoiceId, approver, action);
        }
    }

    private void RefuseIfNotPermitted(Invoice invoice, Approver approver)
    {
        var refusal = _policy.RefuseApproval(invoice, approver);

        if (refusal is not null)
        {
            Refuse(refusal, invoice.Id, approver, DecisionAction.Approve);
        }
    }

    /// <summary>Logs the refusal apart from the invoice, then throws. Nothing on the invoice is touched.</summary>
    private void Refuse(ApprovalRefusal refusal, string invoiceId, Approver approver, DecisionAction action)
    {
        _refusalLog.Record(new RefusalLogEntry(
            invoiceId, approver.Username, approver.Role, action, refusal.Reason, refusal.Message, DateTime.UtcNow));

        throw new ApprovalRefusedException(refusal.Reason, refusal.Message);
    }

    private bool ApprovalsAreComplete(Invoice invoice) =>
        invoice.Approvals.Count >= (_policy.RequiresTwoApprovers(invoice.Amount) ? 2 : 1);

    private Invoice Save(Invoice invoice)
    {
        if (!_repository.Update(invoice))
        {
            throw new InvalidOperationException(
                $"Invoice {invoice.Id} was changed by someone else; the decision was not saved.");
        }

        return invoice;
    }

    private Invoice Find(string invoiceId) =>
        _repository.GetById(invoiceId)
        ?? throw new KeyNotFoundException($"Invoice {invoiceId} was not found.");

    private static string NotPendingMessage(Invoice invoice) =>
        $"Invoice {invoice.Id} is {invoice.Status}; only pending invoices can be decided.";

    private static void EnsurePending(Invoice invoice)
    {
        if (invoice.Status != InvoiceStatus.PendingApproval)
        {
            throw new InvalidOperationException(NotPendingMessage(invoice));
        }
    }
}
