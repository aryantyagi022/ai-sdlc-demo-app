namespace InvoiceApp.Domain;

/// <summary>
/// Whether a given person may approve a given invoice right now, and in plain words why not.
/// The UI uses this to disable a button with a reason instead of re-deciding the rules itself.
/// </summary>
public record ApprovalEligibility(bool CanApprove, ApprovalRefusalReason? Reason, string? Explanation)
{
    public static readonly ApprovalEligibility Allowed = new(true, null, null);

    public static ApprovalEligibility Refused(ApprovalRefusalReason reason, string explanation) =>
        new(false, reason, explanation);

    /// <summary>Not an authority problem: the invoice is not in a state anyone can decide.</summary>
    public static ApprovalEligibility NotOpenForDecision(string explanation) =>
        new(false, null, explanation);
}
