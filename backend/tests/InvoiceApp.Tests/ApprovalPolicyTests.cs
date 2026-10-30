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
}
