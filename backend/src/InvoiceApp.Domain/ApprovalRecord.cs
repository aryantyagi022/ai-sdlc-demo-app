namespace InvoiceApp.Domain;

/// <summary>One approval signature recorded against an invoice.</summary>
public record ApprovalRecord(string Username, string Role, DateTime ApprovedAt);
