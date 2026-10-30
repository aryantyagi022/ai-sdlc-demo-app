namespace InvoiceApp.Domain;

public class Invoice
{
    public string Id { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? SubmittedBy { get; set; }
    public List<ApprovalRecord> Approvals { get; set; } = new();
    public string? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }

    /// <summary>Incremented on every saved change so a stale write can be detected and refused.</summary>
    public int Version { get; set; }

    /// <summary>Who gave the first signature, or null if nobody has approved yet.</summary>
    public string? FirstApprovedBy => Approvals.Count == 0 ? null : Approvals[0].Username;

    public Invoice Clone() => new()
    {
        Id = Id,
        CustomerName = CustomerName,
        Amount = Amount,
        Status = Status,
        CreatedAt = CreatedAt,
        SubmittedBy = SubmittedBy,
        Approvals = new List<ApprovalRecord>(Approvals),
        DecidedBy = DecidedBy,
        DecidedAt = DecidedAt,
        DecisionNote = DecisionNote,
        Version = Version
    };
}
