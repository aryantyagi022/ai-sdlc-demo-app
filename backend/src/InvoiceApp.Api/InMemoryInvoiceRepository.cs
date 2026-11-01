using InvoiceApp.Domain;

namespace InvoiceApp.Api;

/// <summary>
/// In-memory store so the demo app runs with no database. Reads hand out copies and writes
/// check the version, so two approvers acting at once cannot overwrite each other.
/// </summary>
public class InMemoryInvoiceRepository : IInvoiceRepository
{
    private readonly object _lock = new();
    private int _nextNumber = 1009;

    private readonly List<Invoice> _invoices = new()
    {
        new Invoice { Id = "INV-1001", CustomerName = "Lakeside School District", Amount = 4250.00m, Status = InvoiceStatus.PendingApproval, SubmittedBy = "demo.admin" },
        new Invoice { Id = "INV-1002", CustomerName = "Hilltop Academy", Amount = 980.50m, Status = InvoiceStatus.PendingApproval, SubmittedBy = "demo.admin" },
        new Invoice { Id = "INV-1003", CustomerName = "Riverside Charter", Amount = 12300.00m, Status = InvoiceStatus.Approved, SubmittedBy = "demo.admin", DecidedBy = "demo.manager", DecidedAt = DateTime.UtcNow.AddDays(-2), Approvals = { new ApprovalRecord("demo.manager", "Manager", DateTime.UtcNow.AddDays(-2)) } },
        new Invoice { Id = "INV-1004", CustomerName = "Oakwood Independent", Amount = 615.75m, Status = InvoiceStatus.PendingApproval, SubmittedBy = "demo.admin" },
        new Invoice { Id = "INV-1005", CustomerName = "Maple Grove Prep", Amount = 310.00m, Status = InvoiceStatus.Draft },
        new Invoice { Id = "INV-1006", CustomerName = "Northgate Unified", Amount = 200000.00m, Status = InvoiceStatus.PendingApproval, SubmittedBy = "demo.admin" },
        new Invoice { Id = "INV-1007", CustomerName = "Summit Valley Trust", Amount = 750000.00m, Status = InvoiceStatus.PendingApproval, SubmittedBy = "demo.admin" },
        new Invoice { Id = "INV-1008", CustomerName = "Cedar Point Collegiate", Amount = 600000.00m, Status = InvoiceStatus.PendingApproval, SubmittedBy = "demo.admin" },
        new Invoice { Id = "INV-1009", CustomerName = "Harbour City Schools", Amount = 900000.00m, Status = InvoiceStatus.PendingApproval, SubmittedBy = "demo.admin", Approvals = { new ApprovalRecord("demo.manager", "Manager", DateTime.UtcNow.AddHours(-3)) } },
    };

    public IReadOnlyList<Invoice> GetAll()
    {
        lock (_lock) return _invoices.Select(invoice => invoice.Clone()).ToList();
    }

    public Invoice? GetById(string id)
    {
        lock (_lock) return _invoices.FirstOrDefault(invoice => invoice.Id == id)?.Clone();
    }

    public IReadOnlyList<Invoice> GetByIds(IEnumerable<string> ids)
    {
        var wanted = ids.ToHashSet();
        lock (_lock) return _invoices.Where(invoice => wanted.Contains(invoice.Id)).Select(invoice => invoice.Clone()).ToList();
    }

    public Invoice Add(Invoice invoice)
    {
        lock (_lock)
        {
            if (string.IsNullOrEmpty(invoice.Id))
            {
                invoice.Id = $"INV-{++_nextNumber}";
            }

            _invoices.Add(invoice.Clone());
            return invoice;
        }
    }

    public bool Update(Invoice invoice)
    {
        lock (_lock)
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
    }

    public bool Remove(string id)
    {
        lock (_lock) return _invoices.RemoveAll(invoice => invoice.Id == id) > 0;
    }
}
