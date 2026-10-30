namespace InvoiceApp.Domain;

/// <summary>Why an approval attempt was refused. Returned to callers as a stable code.</summary>
public enum ApprovalRefusalReason
{
    /// <summary>The approver submitted this invoice themselves.</summary>
    SelfApprovalNotAllowed,

    /// <summary>The approver has already approved this invoice.</summary>
    DuplicateApprover,

    /// <summary>The amount exceeds the approver's role limit and no two-approver path applies.</summary>
    AboveApprovalLimit,

    /// <summary>The second signature on a large invoice must come from a Director.</summary>
    SecondApproverMustBeDirector,

    /// <summary>The approver's role carries no approval authority.</summary>
    RoleCannotApprove
}

/// <summary>
/// An approval attempt that was refused on authority grounds. Carries a stable reason code
/// alongside a message naming the concrete fact behind the refusal.
/// </summary>
public class ApprovalRefusedException : Exception
{
    public ApprovalRefusedException(ApprovalRefusalReason reason, string message)
        : base(message)
    {
        Reason = reason;
    }

    public ApprovalRefusalReason Reason { get; }
}
