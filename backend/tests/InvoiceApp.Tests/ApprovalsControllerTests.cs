using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using InvoiceApp.Api.Auth;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Xunit;

namespace InvoiceApp.Tests;

/// <summary>
/// Drives the real HTTP pipeline, so sign-in, the role ladder and the status codes the UI switches
/// on are exercised the way a browser exercises them. Each test gets its own application, and so
/// its own freshly seeded invoices — several of these tests change invoice state.
/// </summary>
public class ApprovalsControllerTests : IDisposable
{
    private const string SmallPendingInvoice = "INV-1002";
    private const string MidRangeInvoice = "INV-1006";
    private const string LargeInvoice = "INV-1007";
    private const string AlreadyApprovedInvoice = "INV-1003";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly WebApplicationFactory<Program> _application = new();

    public void Dispose() => _application.Dispose();

    private record Refusal(string Error, string? Reason);

    private record ApprovalState(string Status, string[] Approvals, bool AwaitingSecondApproval);

    private async Task<HttpClient> SignedInAs(string username) => await SignedInAs(_application, username);

    private static async Task<HttpClient> SignedInAs(WebApplicationFactory<Program> application, string username)
    {
        var demoUser = application.Services
            .GetRequiredService<IOptions<DemoUsersOptions>>().Value.Users
            .Single(user => user.Username == username);

        var client = application.CreateClient();
        var signIn = await client.PostAsJsonAsync("/api/auth/login", new Dictionary<string, string>
        {
            ["username"] = demoUser.Username,
            ["password"] = demoUser.Password
        });
        signIn.EnsureSuccessStatusCode();

        var token = (await signIn.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<ApprovalState> ReadApprovalState(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        return new ApprovalState(
            body.GetProperty("status").GetString()!,
            body.GetProperty("approvals").EnumerateArray()
                .Select(approval => approval.GetProperty("username").GetString()!).ToArray(),
            body.GetProperty("awaitingSecondApproval").GetBoolean());
    }

    [Fact]
    public async Task Approve_RequiresSignIn()
    {
        var anonymous = _application.CreateClient();

        var response = await anonymous.PostAsync($"/api/approvals/{SmallPendingInvoice}/approve", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Approve_RefusesAnAdminWithAReasonCode()
    {
        var admin = await SignedInAs("demo.admin");

        var response = await admin.PostAsync($"/api/approvals/{SmallPendingInvoice}/approve", null);
        var refusal = await response.Content.ReadFromJsonAsync<Refusal>(Json);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("RoleCannotApprove", refusal!.Reason);
    }

    [Fact]
    public async Task Approve_RefusesAManagerAboveTheirLimitAndNamesTheLimit()
    {
        var manager = await SignedInAs("demo.manager");

        var response = await manager.PostAsync($"/api/approvals/{MidRangeInvoice}/approve", null);
        var refusal = await response.Content.ReadFromJsonAsync<Refusal>(Json);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("AboveApprovalLimit", refusal!.Reason);
        Assert.Contains("50,000.00", refusal.Error);
    }

    [Fact]
    public async Task Approve_ConflictsWhenTheInvoiceIsAlreadyDecided()
    {
        var director = await SignedInAs("demo.director");

        var response = await director.PostAsync($"/api/approvals/{AlreadyApprovedInvoice}/approve", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Approve_IsNotFoundWhenTheInvoiceDoesNotExist()
    {
        var director = await SignedInAs("demo.director");

        var response = await director.PostAsync("/api/approvals/INV-does-not-exist/approve", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Approve_AnIneligibleRoleCannotUseTheEndpointToDiscoverWhichInvoicesExist()
    {
        var admin = await SignedInAs("demo.admin");

        var missing = await admin.PostAsync("/api/approvals/INV-does-not-exist/approve", null);
        var decided = await admin.PostAsync($"/api/approvals/{AlreadyApprovedInvoice}/approve", null);

        Assert.Equal(HttpStatusCode.Forbidden, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, decided.StatusCode);
    }

    [Fact]
    public async Task Approve_LargeInvoiceTakesTwoDifferentPeopleAndTheSecondMustBeADirector()
    {
        var manager = await SignedInAs("demo.manager");
        var director = await SignedInAs("demo.director");

        var afterFirst = await ReadApprovalState(
            await manager.PostAsync($"/api/approvals/{LargeInvoice}/approve", null));
        Assert.Equal("PendingApproval", afterFirst.Status);
        Assert.True(afterFirst.AwaitingSecondApproval);

        var repeated = await manager.PostAsync($"/api/approvals/{LargeInvoice}/approve", null);
        var refusal = await repeated.Content.ReadFromJsonAsync<Refusal>(Json);
        Assert.Equal(HttpStatusCode.Forbidden, repeated.StatusCode);
        Assert.Equal("DuplicateApprover", refusal!.Reason);

        var afterSecond = await ReadApprovalState(
            await director.PostAsync($"/api/approvals/{LargeInvoice}/approve", null));
        Assert.Equal("Approved", afterSecond.Status);
        Assert.Equal(new[] { "demo.manager", "demo.director" }, afterSecond.Approvals);
    }

    [Fact]
    public async Task Reject_RequiresAReason()
    {
        var manager = await SignedInAs("demo.manager");

        var response = await manager.PostAsJsonAsync(
            $"/api/approvals/{SmallPendingInvoice}/reject", new { Reason = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed record StoredInvoice(
        string Status, string[] Approvals, string? FirstApprovedBy, string? DecidedBy,
        DateTime? DecidedAt, string? DecisionNote, int Version);

    private static StoredInvoice Stored(WebApplicationFactory<Program> application, string invoiceId)
    {
        var invoice = application.Services.GetRequiredService<InvoiceApp.Domain.IInvoiceRepository>()
            .GetById(invoiceId)!;
        return new StoredInvoice(
            invoice.Status.ToString(),
            invoice.Approvals.Select(approval => approval.Username).ToArray(),
            invoice.FirstApprovedBy, invoice.DecidedBy, invoice.DecidedAt, invoice.DecisionNote, invoice.Version);
    }

    private static void AssertSame(StoredInvoice before, StoredInvoice after)
    {
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.Approvals, after.Approvals);
        Assert.Equal(before.FirstApprovedBy, after.FirstApprovedBy);
        Assert.Equal(before.DecidedBy, after.DecidedBy);
        Assert.Equal(before.DecidedAt, after.DecidedAt);
        Assert.Equal(before.DecisionNote, after.DecisionNote);
        Assert.Equal(before.Version, after.Version);
    }

    private static StringContent JsonBody(string json) => new(json, System.Text.Encoding.UTF8, "application/json");

    [Theory]
    [InlineData(null)]
    [InlineData("{}")]
    public async Task Reject_AnAdminWithNoBodyOrNoReasonIsRefusedForRole(string? body)
    {
        var admin = await SignedInAs("demo.admin");
        var before = Stored(_application, SmallPendingInvoice);

        var response = await admin.PostAsync(
            $"/api/approvals/{SmallPendingInvoice}/reject", body is null ? null : JsonBody(body));
        var refusal = await response.Content.ReadFromJsonAsync<Refusal>(Json);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("RoleCannotApprove", refusal!.Reason);
        AssertSame(before, Stored(_application, SmallPendingInvoice));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{}")]
    public async Task Reject_AManagerWithNoBodyOrNoReasonOnAPendingInvoiceGets400AndNothingChanges(string? body)
    {
        var manager = await SignedInAs("demo.manager");
        var before = Stored(_application, SmallPendingInvoice);

        var response = await manager.PostAsync(
            $"/api/approvals/{SmallPendingInvoice}/reject", body is null ? null : JsonBody(body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertSame(before, Stored(_application, SmallPendingInvoice));
    }

    [Fact]
    public async Task Approve_RefusalForAManagerOverTheirLimitNamesTheAmountAndTheLimit()
    {
        var manager = await SignedInAs("demo.manager");
        var before = Stored(_application, MidRangeInvoice);

        var response = await manager.PostAsync($"/api/approvals/{MidRangeInvoice}/approve", null);
        var refusal = await response.Content.ReadFromJsonAsync<Refusal>(Json);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("200,000.00", refusal!.Error);
        Assert.Contains("50,000.00", refusal.Error);
        AssertSame(before, Stored(_application, MidRangeInvoice));
    }

    [Theory]
    [InlineData("approve", "INV-does-not-exist", HttpStatusCode.NotFound)]
    [InlineData("reject", "INV-does-not-exist", HttpStatusCode.NotFound)]
    [InlineData("approve", AlreadyApprovedInvoice, HttpStatusCode.Conflict)]
    [InlineData("reject", AlreadyApprovedInvoice, HttpStatusCode.Conflict)]
    public async Task Decide_ByAnAuthorisedDirectorOnAnUnknownOrDecidedInvoiceGets404Or409(
        string action, string invoiceId, HttpStatusCode expected)
    {
        var director = await SignedInAs("demo.director");
        var known = new[] { SmallPendingInvoice, MidRangeInvoice, LargeInvoice, AlreadyApprovedInvoice };
        var before = known.ToDictionary(id => id, id => Stored(_application, id));

        var response = await director.PostAsync(
            $"/api/approvals/{invoiceId}/{action}",
            action == "reject" ? JsonBody("""{"reason":"Not payable"}""") : null);

        Assert.Equal(expected, response.StatusCode);
        foreach (var id in known)
        {
            AssertSame(before[id], Stored(_application, id));
        }
    }

    private WebApplicationFactory<Program> ApplicationWithClerk(bool grantClerkAnApprovalLimit)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Users:4:Username"] = "demo.clerk",
            ["Users:4:Password"] = "ChangeMe123!",
            ["Users:4:Role"] = "Clerk"
        };

        if (grantClerkAnApprovalLimit)
        {
            settings["Approval:RoleLimits:Clerk"] = "1000";
        }

        // UseSetting rather than ConfigureAppConfiguration: Program.cs reads Approval:* while the
        // builder is created, before ConfigureAppConfiguration callbacks have been applied.
        return _application.WithWebHostBuilder(host =>
        {
            foreach (var (key, value) in settings)
            {
                host.UseSetting(key, value);
            }
        });
    }

    [Fact]
    public async Task Approve_ARoleAddedOnlyToConfigurationGainsItsConfiguredLimit()
    {
        using var application = ApplicationWithClerk(grantClerkAnApprovalLimit: true);
        var clerk = await SignedInAs(application, "demo.clerk");

        var within = await clerk.PostAsync($"/api/approvals/{SmallPendingInvoice}/approve", null);
        var above = await clerk.PostAsync("/api/approvals/INV-1001/approve", null);
        var refusal = await above.Content.ReadFromJsonAsync<Refusal>(Json);

        Assert.Equal(HttpStatusCode.OK, within.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, above.StatusCode);
        Assert.Equal("AboveApprovalLimit", refusal!.Reason);
    }

    [Fact]
    public async Task Approve_TheSameRoleWithoutConfigurationIsRefusedAsRoleCannotApprove()
    {
        using var application = ApplicationWithClerk(grantClerkAnApprovalLimit: false);
        var clerk = await SignedInAs(application, "demo.clerk");

        var response = await clerk.PostAsync($"/api/approvals/{SmallPendingInvoice}/approve", null);
        var refusal = await response.Content.ReadFromJsonAsync<Refusal>(Json);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("RoleCannotApprove", refusal!.Reason);
    }

    [Fact]
    public async Task Delete_StaysAdminOnlyEvenThoughApprovingDoesNot()
    {
        var manager = await SignedInAs("demo.manager");
        var admin = await SignedInAs("demo.admin");

        var refused = await manager.DeleteAsync($"/api/invoices/{SmallPendingInvoice}");
        var refusal = await refused.Content.ReadFromJsonAsync<Refusal>(Json);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("RoleNotPermitted", refusal!.Reason);

        var allowed = await admin.DeleteAsync($"/api/invoices/{SmallPendingInvoice}");
        Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
    }

    [Fact]
    public async Task Invoices_TellEachCallerWhetherTheyThemselvesCouldApprove()
    {
        var manager = await SignedInAs("demo.manager");

        var invoices = await manager.GetFromJsonAsync<JsonElement>("/api/invoices", Json);
        var midRange = invoices.EnumerateArray()
            .Single(invoice => invoice.GetProperty("id").GetString() == MidRangeInvoice);
        var eligibility = midRange.GetProperty("yourApproval");

        Assert.False(eligibility.GetProperty("canApprove").GetBoolean());
        Assert.Equal("AboveApprovalLimit", eligibility.GetProperty("reason").GetString());
        Assert.Contains("50,000.00", eligibility.GetProperty("explanation").GetString()!);
    }

    [Theory]
    [InlineData("demo.manager", true)]
    [InlineData("demo.director", true)]
    [InlineData("demo.admin", false)]
    public async Task SignIn_TellsTheUiWhetherTheRoleMayApproveAtAll(string username, bool expected)
    {
        var client = await SignedInAs(username);

        // SignedInAs already signed in; repeat the call so the response body can be read.
        var demoUser = _application.Services
            .GetRequiredService<IOptions<DemoUsersOptions>>().Value.Users
            .Single(user => user.Username == username);
        var response = await client.PostAsJsonAsync("/api/auth/login", new Dictionary<string, string>
        {
            ["username"] = demoUser.Username,
            ["password"] = demoUser.Password
        });

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal(expected, body.GetProperty("canApprove").GetBoolean());
    }

    [Fact]
    public async Task Approve_TwoDirectorsHoldingTheSameVersionGiveExactlyOneSuccessAndOneConflict()
    {
        const string invoiceId = "INV-1008";
        using var racing = _application.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<InvoiceApp.Domain.IInvoiceRepository>();
            services.AddSingleton<InvoiceApp.Domain.IInvoiceRepository>(
                new RendezvousRepository(new InvoiceApp.Api.InMemoryInvoiceRepository()));
        }));
        var manager = await SignedInAs(racing, "demo.manager");
        var firstDirector = await SignedInAs(racing, "demo.director");
        var secondDirector = await SignedInAs(racing, "demo.director2");
        (await manager.PostAsync($"/api/approvals/{invoiceId}/approve", null)).EnsureSuccessStatusCode();

        // Both Directors must have read the invoice at the same version before either can save.
        ((RendezvousRepository)racing.Services.GetRequiredService<InvoiceApp.Domain.IInvoiceRepository>())
            .HoldTheNextTwoReadsOf(invoiceId);

        var results = await Task.WhenAll(
            firstDirector.PostAsync($"/api/approvals/{invoiceId}/approve", null),
            secondDirector.PostAsync($"/api/approvals/{invoiceId}/approve", null));

        // Which Director wins is deliberately not asserted.
        Assert.Single(results, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, response => response.StatusCode == HttpStatusCode.Conflict);

        var stored = Stored(racing, invoiceId);
        Assert.Equal("Approved", stored.Status);
        Assert.Equal(2, stored.Approvals.Length);
        Assert.Equal("demo.manager", stored.Approvals[0]);
        Assert.Contains(stored.Approvals[1], new[] { "demo.director", "demo.director2" });
        Assert.Equal(2, stored.Version);
    }
}

/// <summary>
/// Makes two requests meet inside <see cref="GetById"/>: neither read returns until both have been
/// made, so both callers hold the same version and the save-time version check decides the winner.
/// A barrier, not a delay, so the outcome does not depend on timing.
/// </summary>
file class RendezvousRepository : InvoiceApp.Domain.IInvoiceRepository
{
    private readonly InvoiceApp.Domain.IInvoiceRepository _inner;
    private readonly Barrier _meeting = new(2);
    private string? _heldInvoiceId;
    private int _readsStillToHold;

    public RendezvousRepository(InvoiceApp.Domain.IInvoiceRepository inner)
    {
        _inner = inner;
    }

    public void HoldTheNextTwoReadsOf(string invoiceId)
    {
        _heldInvoiceId = invoiceId;
        Volatile.Write(ref _readsStillToHold, 2);
    }

    public InvoiceApp.Domain.Invoice? GetById(string id)
    {
        var invoice = _inner.GetById(id);

        if (id == _heldInvoiceId && Interlocked.Decrement(ref _readsStillToHold) >= 0)
        {
            // The timeout only keeps a broken test from hanging; it is never what orders the requests.
            if (!_meeting.SignalAndWait(TimeSpan.FromSeconds(30)))
            {
                throw new TimeoutException("The second competing read never arrived.");
            }
        }

        return invoice;
    }

    public IReadOnlyList<InvoiceApp.Domain.Invoice> GetAll() => _inner.GetAll();
    public IReadOnlyList<InvoiceApp.Domain.Invoice> GetByIds(IEnumerable<string> ids) => _inner.GetByIds(ids);
    public InvoiceApp.Domain.Invoice Add(InvoiceApp.Domain.Invoice invoice) => _inner.Add(invoice);
    public bool Update(InvoiceApp.Domain.Invoice invoice) => _inner.Update(invoice);
    public bool Remove(string id) => _inner.Remove(id);
}
