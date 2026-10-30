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

    public ApprovalService(IInvoiceRepository repository, ApprovalPolicy policy)
    {
        _repository = repository;
        _policy = policy;
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
        var invoice = Find(invoiceId);
        EnsurePending(invoice);
        EnsureMayApprove(invoice, approver);

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
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A reason is required to reject an invoice.", nameof(reason));
        }

        var invoice = Find(invoiceId);
        EnsurePending(invoice);
        EnsureRoleMayDecide(approver);

        invoice.Status = InvoiceStatus.Rejected;
        invoice.DecidedBy = approver.Username;
        invoice.DecidedAt = DateTime.UtcNow;
        invoice.DecisionNote = reason;
        return Save(invoice);
    }

    /// <summary>All the checks that can refuse an approval, in the order their reasons are reported.</summary>
    private void EnsureMayApprove(Invoice invoice, Approver approver)
    {
        EnsureRoleMayDecide(approver);

        if (IsSameUser(invoice.SubmittedBy, approver.Username))
        {
            throw new ApprovalRefusedException(
                ApprovalRefusalReason.SelfApprovalNotAllowed,
                $"{approver.Username} submitted invoice {invoice.Id} and cannot approve it.");
        }

        if (HasAlreadyApproved(invoice, approver))
        {
            throw new ApprovalRefusedException(
                ApprovalRefusalReason.DuplicateApprover,
                $"{approver.Username} has already approved invoice {invoice.Id}; " +
                "a second approver is required.");
        }

        if (_policy.RequiresTwoApprovers(invoice.Amount))
        {
            EnsureMaySignLargeInvoice(invoice, approver);
            return;
        }

        var limit = _policy.LimitFor(approver.Role)!.Value;

        if (invoice.Amount > limit)
        {
            throw new ApprovalRefusedException(
                ApprovalRefusalReason.AboveApprovalLimit,
                $"{approver.Role} may approve up to {limit:N2}; invoice {invoice.Id} is " +
                $"{invoice.Amount:N2} and needs a higher approval level.");
        }
    }

    /// <summary>
    /// Above the threshold the first signature may come from any approving role - a role limit
    /// governs solo authority, not participation - but the second must be a Director.
    /// </summary>
    private void EnsureMaySignLargeInvoice(Invoice invoice, Approver approver)
    {
        if (invoice.Approvals.Count == 0)
        {
            return;
        }

        if (!_policy.IsSecondApproverRole(approver.Role))
        {
            throw new ApprovalRefusedException(
                ApprovalRefusalReason.SecondApproverMustBeDirector,
                $"Invoice {invoice.Id} is {invoice.Amount:N2}, above {_policy.DualApprovalThreshold:N2}, " +
                $"so the second approval must come from a {_policy.SecondApproverRole}.");
        }
    }

    private void EnsureRoleMayDecide(Approver approver)
    {
        if (!_policy.CanApprove(approver.Role))
        {
            throw new ApprovalRefusedException(
                ApprovalRefusalReason.RoleCannotApprove,
                $"Role {approver.Role} has no approval authority.");
        }
    }

    private bool ApprovalsAreComplete(Invoice invoice) =>
        invoice.Approvals.Count >= (_policy.RequiresTwoApprovers(invoice.Amount) ? 2 : 1);

    /// <summary>
    /// Scoped to this invoice only. Approvals on other invoices place no restriction on a user.
    /// </summary>
    private static bool HasAlreadyApproved(Invoice invoice, Approver approver) =>
        invoice.Approvals.Any(approval => IsSameUser(approval.Username, approver.Username));

    private static bool IsSameUser(string? left, string? right) =>
        left is not null && right is not null && string.Equals(left, right, StringComparison.Ordinal);

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

    private static void EnsurePending(Invoice invoice)
    {
        if (invoice.Status != InvoiceStatus.PendingApproval)
        {
            throw new InvalidOperationException(
                $"Invoice {invoice.Id} is {invoice.Status}; only pending invoices can be decided.");
        }
    }
}
