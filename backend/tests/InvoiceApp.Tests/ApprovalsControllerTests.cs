using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using InvoiceApp.Api.Auth;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
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

    private async Task<HttpClient> SignedInAs(string username)
    {
        var demoUser = _application.Services
            .GetRequiredService<IOptions<DemoUsersOptions>>().Value.Users
            .Single(user => user.Username == username);

        var client = _application.CreateClient();
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
}
