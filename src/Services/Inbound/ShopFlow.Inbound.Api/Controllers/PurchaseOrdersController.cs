using Microsoft.AspNetCore.Mvc;
using ShopFlow.Inbound.Api.Contracts;
using ShopFlow.Inbound.Application.Ports;
using ShopFlow.Inbound.Application.Services;
using ShopFlow.Inbound.Domain;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Inbound.Api.Controllers;

/// <summary>
/// Operator-facing HTTP surface for the PO + receiving flow per
/// Sprint-2-redux plan U8. Controllers stay thin: validate input, drive
/// the Domain aggregate or call the orchestration service, map
/// <see cref="Result"/> to HTTP status via ProblemDetails on failure.
/// </summary>
[ApiController]
[Route("api/inbound/purchase-orders")]
public sealed class PurchaseOrdersController : ControllerBase
{
    private readonly IPurchaseOrderRepository _poRepo;
    private readonly IUnitOfWork _uow;
    private readonly ConfirmReceivingLineService _confirmService;
    private readonly TimeProvider _clock;

    public PurchaseOrdersController(
        IPurchaseOrderRepository poRepo,
        IUnitOfWork uow,
        ConfirmReceivingLineService confirmService,
        TimeProvider clock
    )
    {
        _poRepo = poRepo;
        _uow = uow;
        _confirmService = confirmService;
        _clock = clock;
    }

    [HttpPost]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreatePoRequest request,
        CancellationToken ct
    )
    {
        if (request is null)
        {
            return ProblemFromError("request body is required.", "po.request_required", 400);
        }

        var poResult = PurchaseOrder.Create(
            request.SupplierRef,
            request.ExpectedDeliveryAt,
            request.Lines?.Select(l => (l.Sku, l.ExpectedQty)) ?? Array.Empty<(string, int)>()
        );
        if (!poResult.IsSuccess)
        {
            return ProblemFromResult(poResult.Error!, poResult.ErrorCode!);
        }

        await _poRepo.AddAsync(poResult.Value!, ct);
        await _uow.SaveChangesAsync(ct);

        return CreatedAtAction(
            nameof(GetByIdAsync),
            new { id = poResult.Value!.Id },
            Map(poResult.Value!)
        );
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var po = await _poRepo.FindByIdAsync(id, ct);
        if (po is null)
        {
            return ProblemFromError($"purchase order {id} not found.", "po.not_found", 404);
        }
        return Ok(Map(po));
    }

    [HttpGet]
    public async Task<IActionResult> ListOpenAsync(CancellationToken ct)
    {
        var list = await _poRepo.ListOpenAsync(ct);
        return Ok(list.Select(Map).ToArray());
    }

    [HttpPatch("{id:guid}/open")]
    public async Task<IActionResult> OpenAsync(Guid id, CancellationToken ct)
    {
        var po = await _poRepo.FindByIdAsync(id, ct);
        if (po is null)
        {
            return ProblemFromError($"purchase order {id} not found.", "po.not_found", 404);
        }
        var result = po.Open(_clock.GetUtcNow().UtcDateTime);
        if (!result.IsSuccess)
        {
            return ProblemFromResult(result.Error!, result.ErrorCode!);
        }
        await _uow.SaveChangesAsync(ct);
        return Ok(Map(po));
    }

    [HttpPatch("{id:guid}/cancel")]
    public async Task<IActionResult> CancelAsync(
        Guid id,
        [FromBody] CancelPoRequest request,
        CancellationToken ct
    )
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Reason))
        {
            return ProblemFromError(
                "cancellation reason is required.",
                "po.cancel_reason_required",
                400
            );
        }
        var po = await _poRepo.FindByIdAsync(id, ct);
        if (po is null)
        {
            return ProblemFromError($"purchase order {id} not found.", "po.not_found", 404);
        }
        var result = po.Cancel(request.Reason, _clock.GetUtcNow().UtcDateTime);
        if (!result.IsSuccess)
        {
            return ProblemFromResult(result.Error!, result.ErrorCode!);
        }
        await _uow.SaveChangesAsync(ct);
        return Ok(Map(po));
    }

    [HttpPost("{id:guid}/receive")]
    public async Task<IActionResult> ReceiveLineAsync(
        Guid id,
        [FromBody] ConfirmReceivingLineRequest request,
        CancellationToken ct
    )
    {
        if (request is null)
        {
            return ProblemFromError("request body is required.", "receiving.request_required", 400);
        }
        var result = await _confirmService.ConfirmAsync(
            purchaseOrderId: id,
            receivingId: request.ReceivingId,
            purchaseOrderLineId: request.PurchaseOrderLineId,
            actualQty: request.ActualQty,
            suggestedBinId: request.SuggestedBinId,
            actualBinId: request.ActualBinId,
            operatorId: null,
            ct: ct
        );

        if (!result.IsSuccess)
        {
            var status = result.ErrorCode switch
            {
                "receiving.po_not_found" => 404,
                "receiving.line_not_found" => 404,
                "receiving.session_not_found" => 404,
                _ => 400,
            };
            return ProblemFromError(result.Error!, result.ErrorCode!, status);
        }

        var outcome = result.Value!;
        return Ok(
            new ConfirmReceivingLineResponse(
                ReceivingId: outcome.ReceivingId,
                ReceivingLineId: outcome.ReceivingLineId,
                Idempotent: outcome.Idempotent,
                TicketCreated: outcome.TicketCreated
            )
        );
    }

    private IActionResult ProblemFromError(string detail, string code, int status) =>
        Problem(
            statusCode: status,
            title: detail,
            type: $"https://shopflow.example/errors/{code}"
        );

    private IActionResult ProblemFromResult(string detail, string code)
    {
        var status = code.StartsWith("po.not_found") || code.EndsWith("not_found") ? 404 : 400;
        return ProblemFromError(detail, code, status);
    }

    private static PoResponse Map(PurchaseOrder po) =>
        new(
            Id: po.Id,
            SupplierRef: po.SupplierRef,
            ExpectedDeliveryAt: po.ExpectedDeliveryAt,
            Status: po.Status.ToString(),
            OpenedAt: po.OpenedAt,
            ClosedAt: po.ClosedAt,
            CancelledAt: po.CancelledAt,
            Lines: po
                .Lines.Select(l => new PoLineResponse(l.Id, l.Sku, l.ExpectedQty, l.ReceivedQty))
                .ToArray()
        );
}
