using System.Text.Json.Serialization;
using InvoiceApp.Api;
using InvoiceApp.Api.Auth;
using InvoiceApp.Domain;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.Configure<DemoUsersOptions>(builder.Configuration);
builder.Services.AddSingleton<SessionStore>();
builder.Services.AddSingleton<IInvoiceRepository, InMemoryInvoiceRepository>();
builder.Services.AddSingleton<IRefusalLog, InMemoryRefusalLog>();

var roleLimits = builder.Configuration
    .GetSection("Approval:RoleLimits")
    .Get<Dictionary<string, decimal>>() ?? new Dictionary<string, decimal>();
var secondApproverRole = builder.Configuration["Approval:SecondApproverRole"] ?? "Director";

builder.Services.AddSingleton(new ApprovalPolicy(roleLimits, secondApproverRole));
builder.Services.AddScoped<ApprovalService>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseMiddleware<SessionAuthMiddleware>();
app.MapControllers();

app.Run();

public partial class Program { }
