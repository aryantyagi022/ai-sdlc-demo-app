namespace InvoiceApp.Domain;

public enum DecisionAction
{
    Approve,
    Reject
}

/// <summary>One refused authority attempt. Timestamps are UTC.</summary>
public record RefusalLogEntry(
    string InvoiceId,
    string Username,
    string Role,
    DecisionAction Action,
    ApprovalRefusalReason Reason,
    string Message,
    DateTime OccurredAt);
