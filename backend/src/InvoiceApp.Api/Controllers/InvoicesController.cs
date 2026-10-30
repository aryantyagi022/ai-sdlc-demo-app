using InvoiceApp.Api.Auth;
using InvoiceApp.Domain;
using Microsoft.AspNetCore.Mvc;

namespace InvoiceApp.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class InvoicesController : ApiControllerBase
{
    private readonly IInvoiceRepository _repository;
    private readonly ApprovalService _approvals;
    private readonly ApprovalPolicy _policy;

    public InvoicesController(IInvoiceRepository repository, ApprovalService approvals, ApprovalPolicy policy)
    {
        _repository = repository;
        _approvals = approvals;
        _policy = policy;
    }

    public record CreateInvoiceRequest(string CustomerName, decimal Amount);

    [HttpGet]
    public ActionResult<IReadOnlyList<InvoiceView>> GetAll([FromQuery] string? status)
    {
        var invoices = _repository.GetAll();

        if (!string.IsNullOrEmpty(status))
        {
            if (!Enum.TryParse<InvoiceStatus>(status, ignoreCase: true, out var parsed))
            {
                return BadRequest(new { error = $"Unknown status '{status}'." });
            }

            invoices = invoices.Where(i => i.Status == parsed).ToList();
        }

        return Ok(invoices.Select(invoice => InvoiceView.From(invoice, _policy)).ToList());
    }

    [HttpGet("{id}")]
    public ActionResult<InvoiceView> GetById(string id)
    {
        var invoice = _repository.GetById(id);
        return invoice is null ? NotFound() : Ok(InvoiceView.From(invoice, _policy));
    }

    [HttpPost]
    public ActionResult<Invoice> Create([FromBody] CreateInvoiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CustomerName))
        {
            return BadRequest(new { error = "Customer name is required." });
        }

        if (request.Amount <= 0)
        {
            return BadRequest(new { error = "Amount must be greater than zero." });
        }

        var invoice = _repository.Add(new Invoice
        {
            CustomerName = request.CustomerName.Trim(),
            Amount = request.Amount
        });

        return CreatedAtAction(nameof(GetById), new { id = invoice.Id }, invoice);
    }

    [HttpPost("{id}/submit")]
    public ActionResult<Invoice> Submit(string id) =>
        Handle(() => _approvals.Submit(id, HttpContext.GetSession().Username));

    [HttpDelete("{id}")]
    [RequireRole("Admin")]
    public IActionResult Delete(string id) =>
        _repository.Remove(id) ? NoContent() : NotFound();
}
