namespace SierraBusiness.Application;
public sealed record PeriodStatus(DateOnly ClosedThrough, IReadOnlyList<TaxRateView> TaxRates);
public sealed record TaxRateView(DateOnly From, decimal Rate, string Description);
public sealed record ClosePeriodCommand(Guid OperationId, DateOnly Through, string Reason);
public sealed record TaxRateCommand(Guid OperationId, DateOnly From, decimal Rate, string Description);
public interface IPeriodService { Task<PeriodStatus> Read(); Task Close(ClosePeriodCommand input); Task SetTax(TaxRateCommand input); }
