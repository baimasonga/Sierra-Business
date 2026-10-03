using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Api;

public static class BusinessEndpoints
{
    public static IEndpointRouteBuilder MapBusinessEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1").RequireAuthorization();
        group.MapGet("/pos/barcodes", async (IPosService service) => await Result(() => service.Barcodes()));
        group.MapGet("/pos/held", async (IPosService service) => await Result(() => service.HeldSales()));
        group.MapPost("/pos/barcodes", async (BarcodeCommand input, HttpContext context, IAntiforgery antiforgery, IPosService service) => { await antiforgery.ValidateRequestAsync(context); return await Result(() => service.AddBarcode(input)); });
        group.MapPost("/pos/held", async (HoldSaleCommand input, HttpContext context, IAntiforgery antiforgery, IPosService service) => { await antiforgery.ValidateRequestAsync(context); return await Result(() => service.Hold(input)); });
        group.MapPost("/pos/held/cancel", async (CancelHeldSaleCommand input, HttpContext context, IAntiforgery antiforgery, IPosService service) => { await antiforgery.ValidateRequestAsync(context); return await Result(() => service.Cancel(input)); });
        group.MapGet("/banking", async (IBankingService service) => await Result(() => service.Read()));
        group.MapGet("/banking/tills", async (IBankingService service) => await Result(() => service.Tills()));
        group.MapPost("/banking/statements", async (ImportStatementCommand input, HttpContext context, IAntiforgery antiforgery, IBankingService service) => { await antiforgery.ValidateRequestAsync(context); return await Result(() => service.Import(input)); });
        group.MapPost("/banking/statements/void", async (VoidStatementCommand input, HttpContext context, IAntiforgery antiforgery, IBankingService service) => { await antiforgery.ValidateRequestAsync(context); return await Result(() => service.VoidStatement(input)); });
        group.MapPost("/banking/cheques/register", async (RegisterChequeCommand input, HttpContext context, IAntiforgery antiforgery, IBankingService service) => { await antiforgery.ValidateRequestAsync(context); return await Result(() => service.RegisterCheque(input)); });
        group.MapPost("/banking/cheques/receive", async (ReceiveChequeCommand input, HttpContext context, IAntiforgery antiforgery, IBankingService service) => { await antiforgery.ValidateRequestAsync(context); return await Result(() => service.ReceiveCheque(input)); });
        group.MapPost("/banking/cheques/action", async (ChequeActionCommand input, HttpContext context, IAntiforgery antiforgery, IBankingService service) => { await antiforgery.ValidateRequestAsync(context); return await Result(() => service.ActOnCheque(input)); });
        group.MapPost("/banking/supplier-credits/use", async (UseCreditCommand input, HttpContext context, IAntiforgery antiforgery, IBankingService service) => { await antiforgery.ValidateRequestAsync(context); return await Result(() => service.UseSupplierCredit(input)); });
        group.MapPost("/banking/tills/open", async (OpenTillCommand input, HttpContext context, IAntiforgery antiforgery, IBankingService service) => { await antiforgery.ValidateRequestAsync(context); return await Result(() => service.OpenTill(input)); });
        group.MapPost("/banking/tills/close", async (CloseTillCommand input, HttpContext context, IAntiforgery antiforgery, IBankingService service) => { await antiforgery.ValidateRequestAsync(context); return await Result(() => service.CloseTill(input)); });
        group.MapGet("/commerce/prices", async (ICommerceService service) => await Result(() => service.Prices()));
        group.MapGet("/commerce/reservations", async (ICommerceService service) => await Result(() => service.Reservations()));
        group.MapGet("/commerce/credits", async (ICommerceService service) => await Result(() => service.Credits()));
        group.MapPost("/commerce/prices", async (PricePolicyCommand input, HttpContext context, IAntiforgery antiforgery, ICommerceService service) => { await antiforgery.ValidateRequestAsync(context); return await Result(() => service.SetPrice(input)); });
        group.MapPost("/commerce/reservations", async (ReservationCommand input, HttpContext context, IAntiforgery antiforgery, ICommerceService service) => { await antiforgery.ValidateRequestAsync(context); return await Result(() => service.Reserve(input)); });
        group.MapPost("/commerce/reservations/release", async (ReleaseReservationCommand input, HttpContext context, IAntiforgery antiforgery, ICommerceService service) => { await antiforgery.ValidateRequestAsync(context); return await Result(() => service.Release(input)); });
        group.MapPost("/commerce/credits/use", async (UseCreditCommand input, HttpContext context, IAntiforgery antiforgery, ICommerceService service) => { await antiforgery.ValidateRequestAsync(context); return await Result(() => service.UseCredit(input)); });
        group.MapGet("/workspace", async (ClaimsPrincipal user, IBusinessService service, CancellationToken ct) => await Result(async () => await service.Read(ActorFrom(user), ct)));
        group.MapGet("/finance", async (DateOnly from, DateOnly to, IFinanceService service) => await Result(() => service.Read(from, to)));
        group.MapGet("/finance/cash-flow", async (DateOnly from, DateOnly to, IFinanceService service) => await Result(() => service.CashFlow(from, to)));
        group.MapGet("/finance/reconciliation/history", async (IFinanceService service) => await Result(() => service.MatchHistory()));
        group.MapPost("/finance/reconciliation/cancel", async (CancelBankMatchCommand input, HttpContext context, IAntiforgery antiforgery, IFinanceService service) => { await antiforgery.ValidateRequestAsync(context); return await Result(() => service.CancelMatch(input)); });
        group.MapGet("/orders", async (IOrderService service) => await Result(() => service.Read()));
        group.MapGet("/inventory/management/products", async (IInventoryManagement service) => await Result(() => service.Products()));
        group.MapGet("/inventory/management/reorder", async (IInventoryManagement service) => await Result(() => service.Reorder()));
        group.MapGet("/inventory/management/sessions", async (IInventoryManagement service) => await Result(() => service.Counts()));
        group.MapGet("/inventory/management/barcodes", async (IInventoryManagement service) => await Result(() => service.Barcodes()));
        group.MapGet("/inventory/management/movements/{productId:guid}", async (Guid productId, IInventoryManagement service) => await Result(() => service.Trace(productId)));
        group.MapPost("/inventory/management/products", async (SaveProductSettings input, HttpContext context, IAntiforgery antiforgery, IInventoryManagement service) => { await antiforgery.ValidateRequestAsync(context); return await Result(() => service.SaveProduct(input)); });
        group.MapPost("/inventory/management/reorder", async (SaveReorderRule input, HttpContext context, IAntiforgery antiforgery, IInventoryManagement service) => { await antiforgery.ValidateRequestAsync(context); return await Result(() => service.SaveReorder(input)); });
        group.MapPost("/inventory/management/sessions", async (CountSessionCommand input, HttpContext context, IAntiforgery antiforgery, IInventoryManagement service) => { await antiforgery.ValidateRequestAsync(context); return await Result(() => service.SubmitCount(input)); });
        group.MapPost("/inventory/management/sessions/decision", async (CountDecision input, HttpContext context, IAntiforgery antiforgery, IInventoryManagement service) => { await antiforgery.ValidateRequestAsync(context); return await Result(() => service.DecideCount(input)); });
        group.MapPost("/inventory/management/barcodes", async (BarcodeChange input, HttpContext context, IAntiforgery antiforgery, IInventoryManagement service) => { await antiforgery.ValidateRequestAsync(context); return await Result(() => service.ChangeBarcode(input)); });
        group.MapGet("/inventory/management/serials", async (IInventoryManagement service) => await Result(() => service.Serials()));
        group.MapGet("/inventory/management/batches", async (IInventoryManagement service) => await Result(() => service.Batches()));
        group.MapPost("/inventory/management/serials/service", async (SerialServiceCommand input, HttpContext context, IAntiforgery antiforgery, IInventoryManagement service) => { await antiforgery.ValidateRequestAsync(context); return await Result(() => service.SetSerialService(input)); });
        group.MapPost("/inventory/management/batches", async (SaveBatchMetadata input, HttpContext context, IAntiforgery antiforgery, IInventoryManagement service) => { await antiforgery.ValidateRequestAsync(context); return await Result(() => service.SaveBatch(input)); });
        group.MapGet("/inventory/locations", async (IInventoryService service) => await Result(() => service.Locations()));
        group.MapGet("/payroll", async (IPayrollService service) => await Result(() => service.Read()));
        group.MapGet("/parties", async (IPartyService service) => await Result(() => service.Read()));
        group.MapGet("/returns", async (IReturnService service) => await Result(() => service.Read()));
        group.MapGet("/periods", async (IPeriodService service) => await Result(() => service.Read()));
        group.MapGet("/users", async (IUserAdministration service) => await Result(() => service.Read()));
        group.MapPost("/periods/close", async (ClosePeriodCommand input, HttpContext context, IAntiforgery antiforgery, IPeriodService service) => { await antiforgery.ValidateRequestAsync(context); return await Result(async () => { await service.Close(input); return true; }); });
        group.MapPost("/periods/tax", async (TaxRateCommand input, HttpContext context, IAntiforgery antiforgery, IPeriodService service) => { await antiforgery.ValidateRequestAsync(context); return await Result(async () => { await service.SetTax(input); return true; }); });
        group.MapPost("/users", async (CreateUserCommand input, HttpContext context, IAntiforgery antiforgery, IUserAdministration service) => { await antiforgery.ValidateRequestAsync(context); return await Result(() => service.Create(input)); });
        group.MapPost("/users/permissions", async (UpdateUserCommand input, HttpContext context, IAntiforgery antiforgery, IUserAdministration service) => { await antiforgery.ValidateRequestAsync(context); return await Result(async () => { await service.Update(input); return true; }); });
        group.MapPost("/returns", async (ReturnCommand input, HttpContext context, IAntiforgery antiforgery, IReturnService service) => { await antiforgery.ValidateRequestAsync(context); return await Result(() => service.Post(input)); });
        group.MapPost("/parties", async (PartyCommand input, HttpContext context, IAntiforgery antiforgery, IPartyService service) => { await antiforgery.ValidateRequestAsync(context); return await Result(() => service.Save(input)); });
        group.MapPost("/payroll/employees", async (EmployeeCommand input, HttpContext context, IAntiforgery antiforgery, IPayrollService service) => { await antiforgery.ValidateRequestAsync(context); return await Result(() => service.SaveEmployee(input)); });
        group.MapPost("/payroll/runs", async (PayrollCommand input, HttpContext context, IAntiforgery antiforgery, IPayrollService service) => { await antiforgery.ValidateRequestAsync(context); return await Result(() => service.Post(input)); });
        group.MapPost("/inventory/locations", async (LocationCommand input, HttpContext context, IAntiforgery antiforgery, IInventoryService service) => { await antiforgery.ValidateRequestAsync(context); return await Result(() => service.CreateLocation(input)); });
        group.MapPost("/inventory/transfers", async (TransferCommand input, HttpContext context, IAntiforgery antiforgery, IInventoryService service) => { await antiforgery.ValidateRequestAsync(context); return await Result(() => service.Transfer(input)); });
        group.MapPost("/inventory/counts", async (StockCountCommand input, HttpContext context, IAntiforgery antiforgery, IInventoryService service) => { await antiforgery.ValidateRequestAsync(context); return await Result(() => service.Count(input)); });
        group.MapPost("/inventory/batch-suggestions", async (BatchSuggestionCommand input, HttpContext context, IAntiforgery antiforgery, IInventoryService service) => { await antiforgery.ValidateRequestAsync(context); return await Result(() => service.SuggestBatches(input)); });
        group.MapPost("/orders", async (OrderCommand input, HttpContext context, IAntiforgery antiforgery, IOrderService service) => { await antiforgery.ValidateRequestAsync(context); return await Result(() => service.Create(input)); });
        group.MapPost("/orders/close", async (CloseOrderCommand input, HttpContext context, IAntiforgery antiforgery, IOrderService service) => { await antiforgery.ValidateRequestAsync(context); return await Result(() => service.Close(input)); });
        group.MapPost("/finance/ledgers", async (LedgerInput input, HttpContext context, IAntiforgery antiforgery, IFinanceService service) => { await antiforgery.ValidateRequestAsync(context); return await Result(() => service.CreateLedger(input)); });
        group.MapPost("/finance/vouchers", async (VoucherCommand input, HttpContext context, IAntiforgery antiforgery, IFinanceService service) => { await antiforgery.ValidateRequestAsync(context); return await Result(() => service.PostVoucher(input)); });
        group.MapPost("/finance/reconciliation", async (BankMatchCommand input, HttpContext context, IAntiforgery antiforgery, IFinanceService service) => { await antiforgery.ValidateRequestAsync(context); return await Result(() => service.Reconcile(input)); });
        group.MapPost("/finance/budgets", async (BudgetCommand input, HttpContext context, IAntiforgery antiforgery, IFinanceService service) => { await antiforgery.ValidateRequestAsync(context); return await Result(() => service.SetBudget(input)); });
        group.MapGet("/antiforgery", (HttpContext context, IAntiforgery antiforgery) => Results.Ok(antiforgery.GetAndStoreTokens(context).RequestToken));
        group.MapPost("/products", async (ProductInput input, HttpContext context, IAntiforgery antiforgery, IBusinessService service, CancellationToken ct) => { await antiforgery.ValidateRequestAsync(context); return await Result(async () => await service.AddProduct(ActorFrom(context.User), input, ct)); });
        group.MapPost("/documents", async (TradeCommand input, HttpContext context, IAntiforgery antiforgery, IBusinessService service, CancellationToken ct) => { await antiforgery.ValidateRequestAsync(context); return await Result(async () => await service.Post(ActorFrom(context.User), input, ct)); });
        group.MapPost("/reversals", async (ReverseCommand input, HttpContext context, IAntiforgery antiforgery, IBusinessService service, CancellationToken ct) => { await antiforgery.ValidateRequestAsync(context); return await Result(async () => await service.Reverse(ActorFrom(context.User), input, ct)); });
        return routes;
    }
    public static Actor ActorFrom(ClaimsPrincipal user) => new(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "", user.FindFirstValue("branch") ?? "", user.FindAll("permission").Select(x => x.Value).ToHashSet());
    private static async Task<IResult> Result<T>(Func<Task<T>> action)
    {
        try { return Results.Ok(await action()); }
        catch (BusinessException ex) { return Results.Problem(title: "Business validation failed", detail: ex.Message, statusCode: 422); }
        catch (UnauthorizedAccessException) { return Results.Problem(title: "Permission denied", statusCode: 403); }
    }
}

