namespace InvoiceApp.Domain;

/// <summary>The person making an approval decision, with the role they hold.</summary>
public record Approver(string Username, string Role);
