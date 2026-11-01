namespace InvoiceApp.Domain;

/// <summary>
/// Who may approve what. Role limits are supplied from configuration; the two-approver
/// threshold is derived from them rather than configured separately, so the ladder can
/// never disagree with itself.
/// </summary>
public class ApprovalPolicy
{
    private readonly IReadOnlyDictionary<string, decimal> _roleLimits;

    public ApprovalPolicy(IReadOnlyDictionary<string, decimal> roleLimits, string secondApproverRole)
    {
        if (roleLimits.Count == 0)
        {
            throw new ArgumentException("At least one role limit must be configured.", nameof(roleLimits));
        }

        foreach (var (role, limit) in roleLimits)
        {
            if (string.IsNullOrWhiteSpace(role))
            {
                throw new ArgumentException("A role name cannot be blank.", nameof(roleLimits));
            }

            if (limit <= 0)
            {
                throw new ArgumentException(
                    $"The limit for {role} is {limit}; approval limits must be greater than zero.",
                    nameof(roleLimits));
            }
        }

        _roleLimits = new Dictionary<string, decimal>(roleLimits, StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(secondApproverRole))
        {
            throw new ArgumentException(
                "The second-approver role must be configured.", nameof(secondApproverRole));
        }

        if (!_roleLimits.ContainsKey(secondApproverRole))
        {
            throw new ArgumentException(
                $"The second-approver role {secondApproverRole} has no configured limit, so no invoice " +
                $"above the dual-approval threshold could ever be approved. Known roles: " +
                $"{string.Join(", ", _roleLimits.Keys)}.",
                nameof(secondApproverRole));
        }

        SecondApproverRole = secondApproverRole;
    }

    /// <summary>The role required for the second signature on a large invoice.</summary>
    public string SecondApproverRole { get; }

    /// <summary>Above this amount one signature is never enough. The highest single-approver limit.</summary>
    public decimal DualApprovalThreshold => _roleLimits.Values.Max();

    public bool CanApprove(string role) => _roleLimits.ContainsKey(role);

    /// <summary>The most this role may approve on its own, or null if the role cannot approve at all.</summary>
    public decimal? LimitFor(string role) =>
        _roleLimits.TryGetValue(role, out var limit) ? limit : null;

    public bool IsSecondApproverRole(string role) =>
        string.Equals(role, SecondApproverRole, StringComparison.OrdinalIgnoreCase);

    public bool RequiresTwoApprovers(decimal amount) => amount > DualApprovalThreshold;

    /// <summary>Step 1 of the order of evaluation: does this role carry approval authority at all?</summary>
    public ApprovalRefusal? RefuseRole(Approver approver) =>
        CanApprove(approver.Role)
            ? null
            : new ApprovalRefusal(
                ApprovalRefusalReason.RoleCannotApprove,
                $"Role {approver.Role} has no approval authority.");

    /// <summary>
    /// The checks that can refuse an approval of a pending invoice, in the order their reasons are
    /// reported: self-approval, duplicate approver, then the amount against the role's authority.
    /// Pure: reads only this invoice and this approver.
    /// </summary>
    public ApprovalRefusal? RefuseApproval(Invoice invoice, Approver approver)
    {
        var roleRefusal = RefuseRole(approver);

        if (roleRefusal is not null)
        {
            return roleRefusal;
        }

        if (IsSameUser(invoice.SubmittedBy, approver.Username))
        {
            return new ApprovalRefusal(
                ApprovalRefusalReason.SelfApprovalNotAllowed,
                $"{approver.Username} submitted invoice {invoice.Id} and cannot approve it.");
        }

        if (invoice.Approvals.Any(approval => IsSameUser(approval.Username, approver.Username)))
        {
            return new ApprovalRefusal(
                ApprovalRefusalReason.DuplicateApprover,
                $"{approver.Username} has already approved invoice {invoice.Id}; " +
                "a second approver is required.");
        }

        if (RequiresTwoApprovers(invoice.Amount))
        {
            return RefuseSignatureOnLargeInvoice(invoice, approver);
        }

        var limit = LimitFor(approver.Role)!.Value;

        return invoice.Amount > limit
            ? new ApprovalRefusal(
                ApprovalRefusalReason.AboveApprovalLimit,
                $"{approver.Role} may approve up to {limit:N2}; invoice {invoice.Id} is " +
                $"{invoice.Amount:N2} and needs a higher approval level.")
            : null;
    }

    /// <summary>
    /// Above the threshold the first signature may come from any approving role - a role limit
    /// governs solo authority, not participation - but the second must hold the second-approver role.
    /// </summary>
    private ApprovalRefusal? RefuseSignatureOnLargeInvoice(Invoice invoice, Approver approver) =>
        invoice.Approvals.Count == 0 || IsSecondApproverRole(approver.Role)
            ? null
            : new ApprovalRefusal(
                ApprovalRefusalReason.SecondApproverMustBeDirector,
                $"Invoice {invoice.Id} is {invoice.Amount:N2}, above {DualApprovalThreshold:N2}, " +
                $"so the second approval must come from a {SecondApproverRole}.");

    /// <summary>Usernames compare exactly: ordinal, case-sensitive, no trimming.</summary>
    private static bool IsSameUser(string? left, string? right) =>
        left is not null && right is not null && string.Equals(left, right, StringComparison.Ordinal);
}
