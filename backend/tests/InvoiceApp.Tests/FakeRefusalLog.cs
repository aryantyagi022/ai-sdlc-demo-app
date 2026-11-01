using InvoiceApp.Domain;

namespace InvoiceApp.Tests;

public class FakeRefusalLog : IRefusalLog
{
    private readonly List<RefusalLogEntry> _entries = new();

    public IReadOnlyList<RefusalLogEntry> Entries => _entries;

    public void Record(RefusalLogEntry entry) => _entries.Add(entry);
}
