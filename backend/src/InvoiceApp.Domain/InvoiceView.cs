namespace InvoiceApp.Domain;

/// <summary>
/// An invoice plus the approval facts that depend on policy, so callers never re-implement
/// the ladder to decide what to show.
/// </summary>
public record InvoiceView(
    string Id,
    string CustomerName,
    decimal Amount,
    InvoiceStatus Status,
    DateTime CreatedAt,
    string? SubmittedBy,
    IReadOnlyList<ApprovalRecord> Approvals,
    string? FirstApprovedBy,
    string? DecidedBy,
    DateTime? DecidedAt,
    string? DecisionNote,
    bool RequiresTwoApprovers,
    bool AwaitingSecondApproval,
    int ApprovalsRequired,
    int Version,
    ApprovalEligibility? YourApproval)
{
    public static InvoiceView From(
        Invoice invoice, ApprovalPolicy policy, ApprovalEligibility? yourApproval = null)
    {
        var requiresTwo = policy.RequiresTwoApprovers(invoice.Amount);

        return new InvoiceView(
            invoice.Id,
            invoice.CustomerName,
            invoice.Amount,
            invoice.Status,
            invoice.CreatedAt,
            invoice.SubmittedBy,
            invoice.Approvals,
            invoice.FirstApprovedBy,
            invoice.DecidedBy,
            invoice.DecidedAt,
            invoice.DecisionNote,
            requiresTwo,
            requiresTwo && invoice.Status == InvoiceStatus.PendingApproval && invoice.Approvals.Count == 1,
            requiresTwo ? 2 : 1,
            invoice.Version,
            yourApproval);
    }
}
