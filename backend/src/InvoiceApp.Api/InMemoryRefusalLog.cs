using InvoiceApp.Domain;

namespace InvoiceApp.Api;

/// <summary>In-memory refusal log, kept apart from the invoices it describes.</summary>
public class InMemoryRefusalLog : IRefusalLog
{
    private readonly object _lock = new();
    private readonly List<RefusalLogEntry> _entries = new();

    public void Record(RefusalLogEntry entry)
    {
        lock (_lock) _entries.Add(entry);
    }

    public IReadOnlyList<RefusalLogEntry> Entries()
    {
        lock (_lock) return _entries.ToList();
    }
}
