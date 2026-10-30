using InvoiceApp.Domain;

namespace InvoiceApp.Tests;

/// <summary>
/// Test double with the same copy-on-read and version-check semantics as the real repository,
/// so a test can tell a saved change apart from one that was never written.
/// </summary>
public class FakeInvoiceRepository : IInvoiceRepository
{
    private readonly List<Invoice> _invoices = new();

    public FakeInvoiceRepository(params Invoice[] invoices)
    {
        _invoices.AddRange(invoices.Select(invoice => invoice.Clone()));
    }

    public IReadOnlyList<Invoice> GetAll() =>
        _invoices.Select(invoice => invoice.Clone()).ToList();

    public Invoice? GetById(string id) =>
        _invoices.FirstOrDefault(invoice => invoice.Id == id)?.Clone();

    public IReadOnlyList<Invoice> GetByIds(IEnumerable<string> ids)
    {
        var wanted = ids.ToHashSet();
        return _invoices.Where(invoice => wanted.Contains(invoice.Id)).Select(invoice => invoice.Clone()).ToList();
    }

    public Invoice Add(Invoice invoice)
    {
        _invoices.Add(invoice.Clone());
        return invoice;
    }

    public bool Update(Invoice invoice)
    {
        var index = _invoices.FindIndex(stored => stored.Id == invoice.Id);

        if (index < 0 || _invoices[index].Version != invoice.Version)
        {
            return false;
        }

        invoice.Version++;
        _invoices[index] = invoice.Clone();
        return true;
    }

    public bool Remove(string id) => _invoices.RemoveAll(invoice => invoice.Id == id) > 0;
}
