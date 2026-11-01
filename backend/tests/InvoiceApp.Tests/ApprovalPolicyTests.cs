using InvoiceApp.Domain;
using Xunit;

namespace InvoiceApp.Tests;

public class ApprovalPolicyTests
{
    private static Dictionary<string, decimal> Ladder() =>
        new() { ["Manager"] = 50_000m, ["Director"] = 500_000m };

    [Fact]
    public void Constructor_RejectsASecondApproverRoleThatHasNoLimit()
    {
        var error = Assert.Throws<ArgumentException>(
            () => new ApprovalPolicy(Ladder(), "VicePresident"));

        Assert.Contains("VicePresident", error.Message);
        Assert.Contains("Manager", error.Message);
    }

    [Fact]
    public void Constructor_RejectsABlankSecondApproverRole()
    {
        Assert.Throws<ArgumentException>(() => new ApprovalPolicy(Ladder(), "  "));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_RejectsALimitThatIsNotPositive(decimal limit)
    {
        var ladder = Ladder();
        ladder["Manager"] = limit;

        var error = Assert.Throws<ArgumentException>(() => new ApprovalPolicy(ladder, "Director"));

        Assert.Contains("Manager", error.Message);
    }

    [Fact]
    public void Constructor_RejectsAnEmptyLadder()
    {
        Assert.Throws<ArgumentException>(
            () => new ApprovalPolicy(new Dictionary<string, decimal>(), "Director"));
    }

    [Fact]
    public void DualApprovalThreshold_IsTheHighestConfiguredLimit()
    {
        var policy = new ApprovalPolicy(Ladder(), "Director");

        Assert.Equal(500_000m, policy.DualApprovalThreshold);
        Assert.True(policy.RequiresTwoApprovers(500_000.01m));
        Assert.False(policy.RequiresTwoApprovers(500_000m));
    }

    [Fact]
    public void CanApprove_IgnoresTheCaseOfTheRoleName()
    {
        var policy = new ApprovalPolicy(Ladder(), "Director");

        Assert.True(policy.CanApprove("manager"));
        Assert.True(policy.IsSecondApproverRole("DIRECTOR"));
    }

    [Fact]
    public void AddingARoleToConfigurationIsEnoughToGrantApprovalAuthority()
    {
        var ladder = Ladder();
        ladder["VicePresident"] = 250_000m;

        var policy = new ApprovalPolicy(ladder, "Director");

        Assert.True(policy.CanApprove("VicePresident"));
        Assert.Equal(250_000m, policy.LimitFor("VicePresident"));
    }

    // ---- RefuseApproval / RefuseRole (spec FR-001..FR-005, FR-009) ----

    private static Invoice Pending(decimal amount, string submittedBy = "demo.clerk", params ApprovalRecord[] approvals)
    {
        var invoice = new Invoice
        {
            Id = "INV-1",
            Amount = amount,
            Status = InvoiceStatus.PendingApproval,
            SubmittedBy = submittedBy
        };
        invoice.Approvals.AddRange(approvals);
        return invoice;
    }

    private static ApprovalRecord Signed(string username, string role) => new(username, role, DateTime.UtcNow);

    private static readonly Approver AManager = new("demo.manager", "Manager");
    private static readonly Approver ADirector = new("demo.director", "Director");

    [Theory]
    [InlineData("Manager", 50_000.00)]
    [InlineData("manager", 50_000.00)]
    [InlineData("MANAGER", 50_000.00)]
    [InlineData("Director", 50_000.01)]
    [InlineData("DIRECTOR", 500_000.00)]
    public void RefuseApproval_AllowsAmountsUpToAndIncludingTheRoleLimit(string role, double amount)
    {
        var policy = new ApprovalPolicy(Ladder(), "Director");

        var refusal = policy.RefuseApproval(Pending((decimal)amount), new Approver("demo.user", role));

        Assert.Null(refusal);
    }

    [Fact]
    public void RefuseApproval_RefusesAManagerOneCentAboveTheLimitAndNamesAmountAndLimit()
    {
        var policy = new ApprovalPolicy(Ladder(), "Director");

        var refusal = policy.RefuseApproval(Pending(50_000.01m), AManager);

        Assert.NotNull(refusal);
        Assert.Equal(ApprovalRefusalReason.AboveApprovalLimit, refusal.Reason);
        Assert.Contains("50,000.01", refusal.Message);
        Assert.Contains("50,000.00", refusal.Message);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Intern")]
    public void RefuseRole_RefusesAdminAndUnknownRolesAndNamesTheRole(string role)
    {
        var policy = new ApprovalPolicy(Ladder(), "Director");

        var refusal = policy.RefuseRole(new Approver("demo.user", role));

        Assert.NotNull(refusal);
        Assert.Equal(ApprovalRefusalReason.RoleCannotApprove, refusal.Reason);
        Assert.Contains(role, refusal.Message);
    }

    [Theory]
    [InlineData("Manager")]
    [InlineData("director")]
    public void RefuseRole_AllowsConfiguredRolesWhateverTheirCase(string role)
    {
        var policy = new ApprovalPolicy(Ladder(), "Director");

        Assert.Null(policy.RefuseRole(new Approver("demo.user", role)));
    }

    [Fact]
    public void RefuseApproval_HasNoTwoManagerPathBetweenTheManagerLimitAndTheDirectorLimit()
    {
        var policy = new ApprovalPolicy(Ladder(), "Director");
        var alreadySignedByAnotherManager = Pending(
            200_000m, "demo.clerk", Signed("demo.manager2", "Manager"));

        var refusal = policy.RefuseApproval(alreadySignedByAnotherManager, AManager);

        Assert.NotNull(refusal);
        Assert.Equal(ApprovalRefusalReason.AboveApprovalLimit, refusal.Reason);
    }

    [Fact]
    public void RefuseApproval_RefusesTheSubmitterAndNamesTheUserAndInvoice()
    {
        var policy = new ApprovalPolicy(Ladder(), "Director");

        var refusal = policy.RefuseApproval(Pending(1_000m, submittedBy: "demo.manager"), AManager);

        Assert.NotNull(refusal);
        Assert.Equal(ApprovalRefusalReason.SelfApprovalNotAllowed, refusal.Reason);
        Assert.Contains("demo.manager", refusal.Message);
        Assert.Contains("INV-1", refusal.Message);
    }

    [Fact]
    public void RefuseApproval_ReportsSelfApprovalNotDuplicateWhenTheSubmitterAlsoAlreadySigned()
    {
        var policy = new ApprovalPolicy(Ladder(), "Director");
        var invoice = Pending(1_000m, "demo.manager", Signed("demo.manager", "Manager"));

        var refusal = policy.RefuseApproval(invoice, AManager);

        Assert.Equal(ApprovalRefusalReason.SelfApprovalNotAllowed, refusal?.Reason);
    }

    [Fact]
    public void RefuseApproval_RefusesTheSameUserTwiceEvenWhenTheAmountIsAlsoAboveTheirLimit()
    {
        var policy = new ApprovalPolicy(Ladder(), "Director");
        var invoice = Pending(200_000m, "demo.clerk", Signed("demo.manager", "Manager"));

        var refusal = policy.RefuseApproval(invoice, AManager);

        Assert.NotNull(refusal);
        Assert.Equal(ApprovalRefusalReason.DuplicateApprover, refusal.Reason);
        Assert.Contains("demo.manager", refusal.Message);
        Assert.Contains("INV-1", refusal.Message);
        Assert.Contains("a second approver is required", refusal.Message);
    }

    [Fact]
    public void Constructor_ExplainsEachMisconfigurationInItsOwnWords()
    {
        var empty = Assert.Throws<ArgumentException>(
            () => new ApprovalPolicy(new Dictionary<string, decimal>(), "Director"));
        var blankRole = Assert.Throws<ArgumentException>(
            () => new ApprovalPolicy(new Dictionary<string, decimal> { [" "] = 1m }, "Director"));
        var blankSecond = Assert.Throws<ArgumentException>(() => new ApprovalPolicy(Ladder(), ""));
        var zeroLimit = Assert.Throws<ArgumentException>(
            () => new ApprovalPolicy(new Dictionary<string, decimal> { ["Manager"] = 0m }, "Manager"));

        Assert.Contains("At least one role limit", empty.Message);
        Assert.Contains("role name cannot be blank", blankRole.Message);
        Assert.Contains("second-approver role must be configured", blankSecond.Message);
        Assert.Contains("greater than zero", zeroLimit.Message);
        Assert.Equal("roleLimits", empty.ParamName);
        Assert.Equal("roleLimits", blankRole.ParamName);
        Assert.Equal("roleLimits", zeroLimit.ParamName);
        Assert.Equal("secondApproverRole", blankSecond.ParamName);
    }

    [Theory]
    [InlineData("Alice", "alice")]
    [InlineData("alice", "alice ")]
    [InlineData("Alice", "alice ")]
    public void RefuseApproval_ComparesUsernamesOrdinallyWithoutTrimming(string signed, string approving)
    {
        var policy = new ApprovalPolicy(Ladder(), "Director");
        var invoice = Pending(1_000m, "demo.clerk", Signed(signed, "Manager"));

        Assert.Null(policy.RefuseApproval(invoice, new Approver(approving, "Manager")));
    }

    [Fact]
    public void RefuseApproval_TreatsTheIdenticalUsernameAsTheSamePerson()
    {
        var policy = new ApprovalPolicy(Ladder(), "Director");
        var invoice = Pending(1_000m, "demo.clerk", Signed("alice", "Manager"));

        var refusal = policy.RefuseApproval(invoice, new Approver("alice", "Manager"));

        Assert.Equal(ApprovalRefusalReason.DuplicateApprover, refusal?.Reason);
    }

    // ---- Phase B: above the single-approver threshold (spec FR-006..FR-008, FR-015) ----

    private const decimal JustAboveThreshold = 500_000.01m;

    private static readonly Approver ASecondDirector = new("demo.director2", "Director");
    private static readonly Approver ASecondManager = new("demo.manager2", "Manager");

    [Fact]
    public void RefuseApproval_AtTheThresholdOneDirectorSignatureIsEnough()
    {
        var policy = new ApprovalPolicy(Ladder(), "Director");

        Assert.False(policy.RequiresTwoApprovers(500_000.00m));
        Assert.Null(policy.RefuseApproval(Pending(500_000.00m), ADirector));
    }

    [Theory]
    [InlineData("Manager")]
    [InlineData("Director")]
    public void RefuseApproval_AboveTheThresholdAllowsTheFirstSignatureFromAManagerOrADirector(string role)
    {
        var policy = new ApprovalPolicy(Ladder(), "Director");

        Assert.True(policy.RequiresTwoApprovers(JustAboveThreshold));
        Assert.Null(policy.RefuseApproval(Pending(JustAboveThreshold), new Approver("demo.first", role)));
    }

    [Fact]
    public void RefuseApproval_AboveTheThresholdRefusesAManagerAsSecondSignatureAndNamesAmountAndRole()
    {
        var policy = new ApprovalPolicy(Ladder(), "Director");
        var invoice = Pending(JustAboveThreshold, "demo.clerk", Signed("demo.director", "Director"));

        var refusal = policy.RefuseApproval(invoice, ASecondManager);

        Assert.NotNull(refusal);
        Assert.Equal(ApprovalRefusalReason.SecondApproverMustBeDirector, refusal.Reason);
        Assert.Contains("500,000.01", refusal.Message);
        Assert.Contains("Director", refusal.Message);
    }

    [Theory]
    [InlineData("Manager")]
    [InlineData("Director")]
    public void RefuseApproval_AboveTheThresholdAllowsADifferentDirectorToSignSecondWhoeverSignedFirst(string firstRole)
    {
        var policy = new ApprovalPolicy(Ladder(), "Director");
        var invoice = Pending(JustAboveThreshold, "demo.clerk", Signed("demo.first", firstRole));

        Assert.Null(policy.RefuseApproval(invoice, ADirector));
    }

    [Fact]
    public void RefuseApproval_OnALargeInvoiceAnAdminIsRefusedForRoleEvenWithSignaturesPresent()
    {
        var policy = new ApprovalPolicy(Ladder(), "Director");
        var invoice = Pending(JustAboveThreshold, "demo.admin", Signed("demo.manager", "Manager"));

        var refusal = policy.RefuseApproval(invoice, new Approver("demo.admin", "Admin"));

        Assert.Equal(ApprovalRefusalReason.RoleCannotApprove, refusal?.Reason);
    }

    [Fact]
    public void RefuseApproval_OnALargeInvoiceTheSubmittingDirectorWhoAlreadySignedIsRefusedAsSelfApproval()
    {
        var policy = new ApprovalPolicy(Ladder(), "Director");
        var invoice = Pending(JustAboveThreshold, "demo.director", Signed("demo.director", "Director"));

        var refusal = policy.RefuseApproval(invoice, ADirector);

        Assert.Equal(ApprovalRefusalReason.SelfApprovalNotAllowed, refusal?.Reason);
    }

    [Fact]
    public void RefuseApproval_OnALargeInvoiceAManagerWhoSignedFirstIsRefusedAsDuplicateNotAsWrongSecondRole()
    {
        var policy = new ApprovalPolicy(Ladder(), "Director");
        var invoice = Pending(JustAboveThreshold, "demo.clerk", Signed("demo.manager", "Manager"));

        var refusal = policy.RefuseApproval(invoice, AManager);

        Assert.Equal(ApprovalRefusalReason.DuplicateApprover, refusal?.Reason);
    }

    [Fact]
    public void RefuseApproval_OnALargeInvoiceADirectorWhoSignedFirstCannotSignSecondToo()
    {
        var policy = new ApprovalPolicy(Ladder(), "Director");
        var invoice = Pending(JustAboveThreshold, "demo.clerk", Signed("demo.director", "Director"));

        Assert.Equal(ApprovalRefusalReason.DuplicateApprover, policy.RefuseApproval(invoice, ADirector)?.Reason);
        Assert.Null(policy.RefuseApproval(invoice, ASecondDirector));
    }
}
