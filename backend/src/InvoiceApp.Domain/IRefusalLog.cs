namespace InvoiceApp.Domain;

/// <summary>
/// Where refused authority attempts are recorded, separately from the invoice. Writing here
/// never changes an invoice.
/// </summary>
public interface IRefusalLog
{
    void Record(RefusalLogEntry entry);
}
