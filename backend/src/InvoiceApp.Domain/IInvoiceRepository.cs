namespace InvoiceApp.Domain;

public interface IInvoiceRepository
{
    IReadOnlyList<Invoice> GetAll();
    Invoice? GetById(string id);
    IReadOnlyList<Invoice> GetByIds(IEnumerable<string> ids);
    Invoice Add(Invoice invoice);

    /// <summary>
    /// Saves the invoice only if it has not changed since it was read. Returns false when another
    /// writer got there first, so a stale decision is refused rather than silently overwriting.
    /// </summary>
    bool Update(Invoice invoice);

    bool Remove(string id);
}
