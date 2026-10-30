using InvoiceApp.Domain;
using Xunit;

namespace InvoiceApp.Tests;

public class ApprovalServiceTests
{
    private const string Manager = "Manager";
    private const string Director = "Director";

    private static readonly Approver AManager = new("demo.manager", Manager);
    private static readonly Approver ADirector = new("demo.director", Director);
    private static readonly Approver AnAdmin = new("demo.admin", "Admin");


    private static readonly Approver ASecondDirector = new("demo.director2", Director);
    private static readonly Approver ASecondManager = new("demo.manager2", Manager);

    private const decimal AboveDualThreshold = 750_000m;

    /// <summary>Everything a refusal must leave untouched, in one comparable value.</summary>
    private static string Snapshot(Invoice invoice) => string.Join("|",
        invoice.Status,
        invoice.Version,
        invoice.FirstApprovedBy,
        invoice.DecidedBy,
        invoice.DecidedAt?.ToString("O"),
        invoice.DecisionNote,
        string.Join(",", invoice.Approvals.Select(a => $"{a.Username}:{a.Role}:{a.ApprovedAt:O}")));

    private static ApprovalPolicy Policy() =>
        new(new Dictionary<string, decimal> { [Manager] = 50_000m, [Director] = 500_000m }, Director);

    private static Invoice InvoiceWith(
        string id,
        InvoiceStatus status,
        decimal amount = 100m,
        string submittedBy = "demo.clerk") =>
        new() { Id = id, CustomerName = "Test School", Amount = amount, Status = status, SubmittedBy = submittedBy };

    private static ApprovalService ServiceFor(params Invoice[] invoices) =>
        new(new FakeInvoiceRepository(invoices), Policy());

    private static (ApprovalService Service, FakeInvoiceRepository Repository) ServiceAndRepositoryFor(
        params Invoice[] invoices)
    {
        var repository = new FakeInvoiceRepository(invoices);
        return (new ApprovalService(repository, Policy()), repository);
    }

    [Fact]
    public void Submit_MovesDraftToPendingApprovalAndRecordsTheSubmitter()
    {
        var service = ServiceFor(InvoiceWith("INV-1", InvoiceStatus.Draft));

        var result = service.Submit("INV-1", "demo.clerk");

        Assert.Equal(InvoiceStatus.PendingApproval, result.Status);
        Assert.Equal("demo.clerk", result.SubmittedBy);
    }

    [Fact]
    public void Submit_RejectsInvoiceThatIsNotADraft()
    {
        var service = ServiceFor(InvoiceWith("INV-1", InvoiceStatus.Approved));

        Assert.Throws<InvalidOperationException>(() => service.Submit("INV-1", "demo.clerk"));
    }

    [Fact]
    public void Approve_SetsStatusApproverAndTimestamp()
    {
        var service = ServiceFor(InvoiceWith("INV-1", InvoiceStatus.PendingApproval));

        var result = service.Approve("INV-1", AManager);

        Assert.Equal(InvoiceStatus.Approved, result.Status);
        Assert.Equal("demo.manager", result.DecidedBy);
        Assert.NotNull(result.DecidedAt);
        Assert.Single(result.Approvals);
    }

    [Fact]
    public void Approve_PersistsTheDecisionSoALaterReadSeesIt()
    {
        var (service, repository) = ServiceAndRepositoryFor(InvoiceWith("INV-1", InvoiceStatus.PendingApproval));

        service.Approve("INV-1", AManager);

        var reloaded = repository.GetById("INV-1")!;
        Assert.Equal(InvoiceStatus.Approved, reloaded.Status);
        Assert.Single(reloaded.Approvals);
    }

    [Fact]
    public void Approve_RejectsInvoiceThatIsStillADraft()
    {
        var service = ServiceFor(InvoiceWith("INV-1", InvoiceStatus.Draft));

        Assert.Throws<InvalidOperationException>(() => service.Approve("INV-1", AManager));
    }

    [Fact]
    public void Approve_ThrowsWhenInvoiceDoesNotExist()
    {
        var service = ServiceFor();

        Assert.Throws<KeyNotFoundException>(() => service.Approve("INV-404", AManager));
    }

    [Theory]
    [InlineData(50_000.00)]
    [InlineData(49_999.99)]
    public void Approve_AllowsManagerUpToAndIncludingTheirLimit(decimal amount)
    {
        var service = ServiceFor(InvoiceWith("INV-1", InvoiceStatus.PendingApproval, amount));

        var result = service.Approve("INV-1", AManager);

        Assert.Equal(InvoiceStatus.Approved, result.Status);
    }

    [Fact]
    public void Approve_RefusesManagerJustAboveTheirLimit()
    {
        var service = ServiceFor(InvoiceWith("INV-1", InvoiceStatus.PendingApproval, 50_000.01m));

        var refusal = Assert.Throws<ApprovalRefusedException>(() => service.Approve("INV-1", AManager));

        Assert.Equal(ApprovalRefusalReason.AboveApprovalLimit, refusal.Reason);
    }

    [Theory]
    [InlineData(200_000.00)]
    [InlineData(500_000.00)]
    public void Approve_AllowsDirectorUpToAndIncludingTheirLimit(decimal amount)
    {
        var service = ServiceFor(InvoiceWith("INV-1", InvoiceStatus.PendingApproval, amount));

        var result = service.Approve("INV-1", ADirector);

        Assert.Equal(InvoiceStatus.Approved, result.Status);
    }

    [Fact]
    public void Approve_RefusesManagerInTheBandThatOnlyADirectorCanClear()
    {
        var service = ServiceFor(InvoiceWith("INV-1", InvoiceStatus.PendingApproval, 200_000m));

        var refusal = Assert.Throws<ApprovalRefusedException>(() => service.Approve("INV-1", AManager));

        Assert.Equal(ApprovalRefusalReason.AboveApprovalLimit, refusal.Reason);
    }

    [Fact]
    public void Approve_DoesNotApproveOnADirectorsSignatureAloneAboveTheDualApprovalThreshold()
    {
        var service = ServiceFor(InvoiceWith("INV-1", InvoiceStatus.PendingApproval, 500_000.01m));

        var result = service.Approve("INV-1", ADirector);

        Assert.Equal(InvoiceStatus.PendingApproval, result.Status);
        Assert.Single(result.Approvals);
    }

    [Fact]
    public void Approve_RefusesARoleWithNoApprovalAuthority()
    {
        var service = ServiceFor(InvoiceWith("INV-1", InvoiceStatus.PendingApproval));

        var refusal = Assert.Throws<ApprovalRefusedException>(() => service.Approve("INV-1", AnAdmin));

        Assert.Equal(ApprovalRefusalReason.RoleCannotApprove, refusal.Reason);
    }

    [Fact]
    public void Approve_RefusesTheUserWhoSubmittedTheInvoice()
    {
        var service = ServiceFor(
            InvoiceWith("INV-1", InvoiceStatus.PendingApproval, submittedBy: "demo.manager"));

        var refusal = Assert.Throws<ApprovalRefusedException>(() => service.Approve("INV-1", AManager));

        Assert.Equal(ApprovalRefusalReason.SelfApprovalNotAllowed, refusal.Reason);
    }

    [Fact]
    public void Reject_RecordsReasonAndApprover()
    {
        var service = ServiceFor(InvoiceWith("INV-1", InvoiceStatus.PendingApproval));

        var result = service.Reject("INV-1", AManager, "Duplicate invoice");

        Assert.Equal(InvoiceStatus.Rejected, result.Status);
        Assert.Equal("Duplicate invoice", result.DecisionNote);
    }

    [Fact]
    public void Reject_RequiresAReason()
    {
        var service = ServiceFor(InvoiceWith("INV-1", InvoiceStatus.PendingApproval));

        Assert.Throws<ArgumentException>(() => service.Reject("INV-1", AManager, "  "));
    }

    [Fact]
    public void Reject_RefusesARoleWithNoApprovalAuthority()
    {
        var service = ServiceFor(InvoiceWith("INV-1", InvoiceStatus.PendingApproval));

        var refusal = Assert.Throws<ApprovalRefusedException>(
            () => service.Reject("INV-1", AnAdmin, "Not my call"));

        Assert.Equal(ApprovalRefusalReason.RoleCannotApprove, refusal.Reason);
    }

    // ---- Rule 3 and 4: two approvers above the threshold, and they must be different people ----

    [Fact] // 4.1
    public void Approve_ManagerThenDirectorApprovesALargeInvoice()
    {
        var service = ServiceFor(InvoiceWith("INV-1", InvoiceStatus.PendingApproval, AboveDualThreshold));

        var afterFirst = service.Approve("INV-1", AManager);
        Assert.Equal(InvoiceStatus.PendingApproval, afterFirst.Status);
        Assert.Equal("demo.manager", afterFirst.FirstApprovedBy);

        var afterSecond = service.Approve("INV-1", ADirector);
        Assert.Equal(InvoiceStatus.Approved, afterSecond.Status);
        Assert.Equal(2, afterSecond.Approvals.Count);
    }

    [Fact] // 4.2
    public void Approve_TwoDifferentDirectorsApproveALargeInvoice()
    {
        var service = ServiceFor(InvoiceWith("INV-1", InvoiceStatus.PendingApproval, AboveDualThreshold));

        service.Approve("INV-1", ADirector);
        var result = service.Approve("INV-1", ASecondDirector);

        Assert.Equal(InvoiceStatus.Approved, result.Status);
        Assert.Equal(
            new[] { "demo.director", "demo.director2" },
            result.Approvals.Select(approval => approval.Username).ToArray());
    }

    [Fact] // 4.3
    public void Approve_RefusesADirectorApprovingTheSameInvoiceTwice()
    {
        var (service, repository) = ServiceAndRepositoryFor(
            InvoiceWith("INV-1", InvoiceStatus.PendingApproval, AboveDualThreshold));
        service.Approve("INV-1", ADirector);

        var refusal = Assert.Throws<ApprovalRefusedException>(() => service.Approve("INV-1", ADirector));

        Assert.Equal(ApprovalRefusalReason.DuplicateApprover, refusal.Reason);
        Assert.Single(repository.GetById("INV-1")!.Approvals);
    }

    [Fact] // 4.4
    public void Approve_RefusesAManagerApprovingTheSameInvoiceTwice()
    {
        var service = ServiceFor(InvoiceWith("INV-1", InvoiceStatus.PendingApproval, AboveDualThreshold));
        service.Approve("INV-1", AManager);

        var refusal = Assert.Throws<ApprovalRefusedException>(() => service.Approve("INV-1", AManager));

        Assert.Equal(ApprovalRefusalReason.DuplicateApprover, refusal.Reason);
    }

    [Fact] // 4.5 - a different person, but the wrong role for the second signature
    public void Approve_RefusesASecondManagerAsTheSecondApprover()
    {
        var service = ServiceFor(InvoiceWith("INV-1", InvoiceStatus.PendingApproval, AboveDualThreshold));
        service.Approve("INV-1", AManager);

        var refusal = Assert.Throws<ApprovalRefusedException>(() => service.Approve("INV-1", ASecondManager));

        Assert.Equal(ApprovalRefusalReason.SecondApproverMustBeDirector, refusal.Reason);
    }

    [Fact] // 4.6 - self-approval is reported ahead of duplication, being the more specific reason
    public void Approve_RefusesTheSubmitterEvenWhenTheyCouldOtherwiseSignSecond()
    {
        var service = ServiceFor(InvoiceWith(
            "INV-1", InvoiceStatus.PendingApproval, AboveDualThreshold, submittedBy: "demo.director"));
        service.Approve("INV-1", AManager);

        var refusal = Assert.Throws<ApprovalRefusedException>(() => service.Approve("INV-1", ADirector));

        Assert.Equal(ApprovalRefusalReason.SelfApprovalNotAllowed, refusal.Reason);
    }

    [Fact] // 4.7 - the empty list is the only case that tells Any from All
    public void Approve_AllowsTheFirstApprovalWhenNobodyHasApprovedYet()
    {
        var service = ServiceFor(InvoiceWith("INV-1", InvoiceStatus.PendingApproval, AboveDualThreshold));

        var result = service.Approve("INV-1", ADirector);

        Assert.Single(result.Approvals);
    }

    [Fact] // 4.8 - usernames compare Ordinal, so case matters; asserted so the choice stays deliberate
    public void Approve_TreatsADifferentlyCasedUsernameAsADifferentPerson()
    {
        var service = ServiceFor(InvoiceWith("INV-1", InvoiceStatus.PendingApproval, AboveDualThreshold));
        service.Approve("INV-1", ADirector);

        var result = service.Approve("INV-1", new Approver("Demo.Director", Director));

        Assert.Equal(InvoiceStatus.Approved, result.Status);
    }

    [Fact] // 4.9 - a stale second signature loses the version check rather than becoming a third approval
    public void Approve_RefusesToSaveOverANewerVersionOfTheInvoice()
    {
        var repository = new FakeInvoiceRepository(
            InvoiceWith("INV-1", InvoiceStatus.PendingApproval, AboveDualThreshold));
        var service = new ApprovalService(new StaleReadRepository(repository), Policy());

        service.Approve("INV-1", AManager);

        Assert.Throws<InvalidOperationException>(() => service.Approve("INV-1", ADirector));
        Assert.Single(repository.GetById("INV-1")!.Approvals);
    }

    [Fact] // 4.10 - SCOPE: the restriction is per invoice, not a history of who has ever approved
    public void Approve_AllowsTheSameDirectorToApproveADifferentInvoice()
    {
        var service = ServiceFor(
            InvoiceWith("INV-1", InvoiceStatus.PendingApproval, 400_000m),
            InvoiceWith("INV-2", InvoiceStatus.PendingApproval, 400_000m));

        Assert.Equal(InvoiceStatus.Approved, service.Approve("INV-1", ADirector).Status);
        Assert.Equal(InvoiceStatus.Approved, service.Approve("INV-2", ADirector).Status);
    }

    [Fact] // 4.11 - SCOPE: no memory of which signature someone gave on another invoice
    public void Approve_AllowsASecondApproverOnOneInvoiceToBeFirstApproverOnAnother()
    {
        var service = ServiceFor(
            InvoiceWith("INV-1", InvoiceStatus.PendingApproval, AboveDualThreshold),
            InvoiceWith("INV-2", InvoiceStatus.PendingApproval, AboveDualThreshold));

        service.Approve("INV-1", AManager);
        Assert.Equal(InvoiceStatus.Approved, service.Approve("INV-1", ADirector).Status);

        var otherInvoice = service.Approve("INV-2", ADirector);

        Assert.Equal("demo.director", otherInvoice.FirstApprovedBy);
        Assert.Equal(InvoiceStatus.PendingApproval, otherInvoice.Status);
    }

    // ---- Rule 5: a refused attempt changes nothing ----

    public static TheoryData<string, decimal, string, Approver, ApprovalRefusalReason> RefusedApprovals() => new()
    {
        { "over the solo limit", 200_000m, "demo.clerk", AManager, ApprovalRefusalReason.AboveApprovalLimit },
        { "own submission", 1_000m, "demo.manager", AManager, ApprovalRefusalReason.SelfApprovalNotAllowed },
        { "role cannot approve", 1_000m, "demo.clerk", AnAdmin, ApprovalRefusalReason.RoleCannotApprove },
    };

    [Theory] // 5.1, 5.2, 5.5
    [MemberData(nameof(RefusedApprovals))]
    public void Approve_LeavesTheInvoiceUntouchedWhenRefused(
        string scenario, decimal amount, string submittedBy, Approver approver, ApprovalRefusalReason expected)
    {
        var (service, repository) = ServiceAndRepositoryFor(
            InvoiceWith("INV-1", InvoiceStatus.PendingApproval, amount, submittedBy));
        var before = Snapshot(repository.GetById("INV-1")!);

        var refusal = Assert.Throws<ApprovalRefusedException>(() => service.Approve("INV-1", approver));

        Assert.Equal(expected, refusal.Reason);
        Assert.Equal(before, Snapshot(repository.GetById("INV-1")!));
        Assert.Equal(0, repository.GetById("INV-1")!.Version);
    }

    [Fact] // 5.3 - duplicate approver, after a real approval has been recorded
    public void Approve_LeavesTheInvoiceUntouchedWhenTheSameUserApprovesTwice()
    {
        var (service, repository) = ServiceAndRepositoryFor(
            InvoiceWith("INV-1", InvoiceStatus.PendingApproval, AboveDualThreshold));
        service.Approve("INV-1", ADirector);
        var before = Snapshot(repository.GetById("INV-1")!);

        Assert.Throws<ApprovalRefusedException>(() => service.Approve("INV-1", ADirector));

        Assert.Equal(before, Snapshot(repository.GetById("INV-1")!));
    }

    [Fact] // 5.4 - wrong role for the second signature
    public void Approve_LeavesTheInvoiceUntouchedWhenTheSecondApproverIsNotADirector()
    {
        var (service, repository) = ServiceAndRepositoryFor(
            InvoiceWith("INV-1", InvoiceStatus.PendingApproval, AboveDualThreshold));
        service.Approve("INV-1", AManager);
        var before = Snapshot(repository.GetById("INV-1")!);

        Assert.Throws<ApprovalRefusedException>(() => service.Approve("INV-1", ASecondManager));

        Assert.Equal(before, Snapshot(repository.GetById("INV-1")!));
    }

    [Theory] // 5.6 - lifecycle refusals are 409 and must not write either
    [InlineData(InvoiceStatus.Approved)]
    [InlineData(InvoiceStatus.Rejected)]
    [InlineData(InvoiceStatus.Draft)]
    public void Approve_LeavesTheInvoiceUntouchedWhenItIsNotPending(InvoiceStatus status)
    {
        var (service, repository) = ServiceAndRepositoryFor(InvoiceWith("INV-1", status));
        var before = Snapshot(repository.GetById("INV-1")!);

        Assert.Throws<InvalidOperationException>(() => service.Approve("INV-1", ADirector));

        Assert.Equal(before, Snapshot(repository.GetById("INV-1")!));
    }

    [Fact] // 5.8 - a blank rejection reason must not reject the invoice
    public void Reject_LeavesTheInvoiceUntouchedWhenTheReasonIsBlank()
    {
        var (service, repository) = ServiceAndRepositoryFor(InvoiceWith("INV-1", InvoiceStatus.PendingApproval));
        var before = Snapshot(repository.GetById("INV-1")!);

        Assert.Throws<ArgumentException>(() => service.Reject("INV-1", ADirector, "   "));

        Assert.Equal(before, Snapshot(repository.GetById("INV-1")!));
    }

    [Fact] // 5.9 - the message must name the limit, not just carry a code
    public void Approve_ExplainsTheLimitThatWasExceeded()
    {
        var service = ServiceFor(InvoiceWith("INV-1", InvoiceStatus.PendingApproval, 200_000m));

        var refusal = Assert.Throws<ApprovalRefusedException>(() => service.Approve("INV-1", AManager));

        Assert.Contains("50,000.00", refusal.Message);
        Assert.Contains("200,000.00", refusal.Message);
        Assert.Contains(Manager, refusal.Message);
    }

    [Fact] // 5.9 - the message must name the role the second signature requires
    public void Approve_ExplainsThatTheSecondApproverMustBeADirector()
    {
        var service = ServiceFor(InvoiceWith("INV-1", InvoiceStatus.PendingApproval, AboveDualThreshold));
        service.Approve("INV-1", AManager);

        var refusal = Assert.Throws<ApprovalRefusedException>(() => service.Approve("INV-1", ASecondManager));

        Assert.Contains(Director, refusal.Message);
        Assert.Contains("500,000.00", refusal.Message);
    }

    [Fact] // 5.10 - "changes nothing" must not leak into the one path that keeps history
    public void Reject_KeepsApprovalsAlreadyRecorded()
    {
        var service = ServiceFor(InvoiceWith("INV-1", InvoiceStatus.PendingApproval, 900_000m));
        service.Approve("INV-1", ADirector);

        var result = service.Reject("INV-1", AManager, "Budget withdrawn");

        Assert.Equal(InvoiceStatus.Rejected, result.Status);
        Assert.Single(result.Approvals);
        Assert.Equal("demo.director", result.Approvals[0].Username);
    }

    // ---- Authority is checked before anything else, and comes only from configuration ----

    [Fact]
    public void Approve_RefusesAnIneligibleRoleWithoutRevealingWhetherTheInvoiceExists()
    {
        var service = ServiceFor();

        var refusal = Assert.Throws<ApprovalRefusedException>(() => service.Approve("INV-404", AnAdmin));

        Assert.Equal(ApprovalRefusalReason.RoleCannotApprove, refusal.Reason);
    }

    [Fact]
    public void Reject_RefusesAnIneligibleRoleWithoutRevealingWhetherTheInvoiceExists()
    {
        var service = ServiceFor();

        var refusal = Assert.Throws<ApprovalRefusedException>(
            () => service.Reject("INV-404", AnAdmin, "No reason to care"));

        Assert.Equal(ApprovalRefusalReason.RoleCannotApprove, refusal.Reason);
    }

    [Fact]
    public void Reject_ChecksAuthorityBeforeTheReasonIsValidated()
    {
        var service = ServiceFor(InvoiceWith("INV-1", InvoiceStatus.PendingApproval));

        var refusal = Assert.Throws<ApprovalRefusedException>(() => service.Reject("INV-1", AnAdmin, "  "));

        Assert.Equal(ApprovalRefusalReason.RoleCannotApprove, refusal.Reason);
    }

    [Fact]
    public void Approve_StillReportsAMissingInvoiceToSomeoneWhoCouldHaveApprovedIt()
    {
        var service = ServiceFor();

        Assert.Throws<KeyNotFoundException>(() => service.Approve("INV-404", ADirector));
    }

    [Fact] // adding a role to configuration is the only edit needed to grant approval authority
    public void Approve_AllowsARoleThatOnlyExistsInConfiguration()
    {
        var ladder = new Dictionary<string, decimal>
        {
            [Manager] = 50_000m,
            ["VicePresident"] = 250_000m,
            [Director] = 500_000m
        };
        var service = new ApprovalService(
            new FakeInvoiceRepository(InvoiceWith("INV-1", InvoiceStatus.PendingApproval, 200_000m)),
            new ApprovalPolicy(ladder, Director));

        var result = service.Approve("INV-1", new Approver("demo.vp", "VicePresident"));

        Assert.Equal(InvoiceStatus.Approved, result.Status);
    }
}

/// <summary>
/// Hands the service a snapshot taken before any approval, so a save races against a newer
/// version the way two approvers acting at the same time would.
/// </summary>
file class StaleReadRepository : IInvoiceRepository
{
    private readonly IInvoiceRepository _inner;
    private readonly Dictionary<string, Invoice> _firstReads = new();

    public StaleReadRepository(IInvoiceRepository inner)
    {
        _inner = inner;
    }

    public Invoice? GetById(string id)
    {
        if (!_firstReads.TryGetValue(id, out var snapshot))
        {
            snapshot = _inner.GetById(id);
            if (snapshot is null)
            {
                return null;
            }

            _firstReads[id] = snapshot;
        }

        return snapshot.Clone();
    }

    public IReadOnlyList<Invoice> GetAll() => _inner.GetAll();
    public IReadOnlyList<Invoice> GetByIds(IEnumerable<string> ids) => _inner.GetByIds(ids);
    public Invoice Add(Invoice invoice) => _inner.Add(invoice);
    public bool Update(Invoice invoice) => _inner.Update(invoice);
    public bool Remove(string id) => _inner.Remove(id);
}