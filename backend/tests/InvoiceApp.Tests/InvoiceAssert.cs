using InvoiceApp.Domain;
using Xunit;

namespace InvoiceApp.Tests;

/// <summary>
/// Proves a refused attempt changed nothing: every decision field, the Version and the number of
/// saves are compared with what they were immediately before the attempt.
/// </summary>
public static class InvoiceAssert
{
    public record InvoiceSnapshot(
        InvoiceStatus Status,
        IReadOnlyList<ApprovalRecord> Approvals,
        string? FirstApprovedBy,
        string? DecidedBy,
        DateTime? DecidedAt,
        string? DecisionNote,
        int Version,
        int UpdateCountBaseline);

    public static InvoiceSnapshot Capture(FakeInvoiceRepository repository, string invoiceId)
    {
        var invoice = repository.GetById(invoiceId)!;
        return new InvoiceSnapshot(
            invoice.Status,
            invoice.Approvals.ToList(),
            invoice.FirstApprovedBy,
            invoice.DecidedBy,
            invoice.DecidedAt,
            invoice.DecisionNote,
            invoice.Version,
            repository.UpdateCount);
    }

    public static void AssertUnchanged(FakeInvoiceRepository repository, string invoiceId, InvoiceSnapshot before)
    {
        var after = repository.GetById(invoiceId)!;

        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.Approvals, after.Approvals);
        Assert.Equal(before.FirstApprovedBy, after.FirstApprovedBy);
        Assert.Equal(before.DecidedBy, after.DecidedBy);
        Assert.Equal(before.DecidedAt, after.DecidedAt);
        Assert.Equal(before.DecisionNote, after.DecisionNote);
        Assert.Equal(before.Version, after.Version);
        Assert.Equal(before.UpdateCountBaseline, repository.UpdateCount);
    }
}
