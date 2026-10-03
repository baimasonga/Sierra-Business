namespace SierraBusiness.Domain;

public enum TrackingMode { Quantity, Serial, Batch, BatchExpiry, Variant, Compatibility }
public enum PaymentMethod { Cash, Cheque, BankTransfer }
public enum DocumentKind { Receipt, Sale, Reversal, Journal, Payment, MoneyReceipt, Contra, StockTransfer, StockCount, Payroll, SalesReturn, PurchaseReturn, Delivery, CustomerCreditUse, ChequeReceipt, ChequeMovement, ChequeBounce, SupplierCreditUse, OpeningStock, LandedCost, SupplierInvoice, ServiceIssue, ServiceReturn, ServiceInvoice, ServiceClose }
public enum Account { Cash = 1000, Bank = 1010, ChequesInHand = 1020, Receivables = 1100, Inventory = 1200, Payables = 2000, TaxPayable = 2100, Sales = 4000, CostOfSales = 5000 }
public class BusinessException(string message) : Exception(message);

public sealed record Posting(Account Account, decimal Debit, decimal Credit);
public static class AccountingEngine
{
    public static IReadOnlyList<Posting> Sale(decimal net, decimal tax, decimal cost, PaymentMethod? payment)
    {
        var debit = payment switch { PaymentMethod.Cash => Account.Cash, PaymentMethod.BankTransfer => Account.Bank, PaymentMethod.Cheque => Account.ChequesInHand, null => Account.Receivables, _ => throw new BusinessException("Unsupported payment method.") };
        return Balanced([new(debit, net + tax, 0), new(Account.Sales, 0, net), new(Account.TaxPayable, 0, tax), new(Account.CostOfSales, cost, 0), new(Account.Inventory, 0, cost)]);
    }
    public static IReadOnlyList<Posting> Receipt(decimal cost) => Balanced([new(Account.Inventory, cost, 0), new(Account.Payables, 0, cost)]);
    public static IReadOnlyList<Posting> Reverse(IEnumerable<Posting> lines) => Balanced(lines.Select(x => new Posting(x.Account, x.Credit, x.Debit)).ToList());
    public static IReadOnlyList<Posting> Balanced(IReadOnlyList<Posting> lines)
    {
        if (lines.Any(x => x.Debit < 0 || x.Credit < 0 || (x.Debit > 0 && x.Credit > 0)) || lines.Sum(x => x.Debit) != lines.Sum(x => x.Credit))
            throw new BusinessException("Journal must contain non-negative, balanced debits and credits.");
        return lines.Where(x => x.Debit != 0 || x.Credit != 0).ToList();
    }
    public static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}

public static class Rules
{
    public static string Required(string? value, string field, int max = 160)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value) || value.Length > max) throw new BusinessException($"{field} is required and must be at most {max} characters.");
        return value;
    }
    public static void Positive(decimal value, string field)
    {
        if (value <= 0 || value > 1_000_000_000 || decimal.Round(value, 4) != value) throw new BusinessException($"{field} must be positive, at most one billion, with up to four decimal places.");
    }
    public static void Tracking(TrackingMode mode, decimal quantity, string? serial, string? batch, DateOnly? expiry, DateOnly date, bool sale)
    {
        if (mode == TrackingMode.Serial && (quantity != 1 || string.IsNullOrWhiteSpace(serial))) throw new BusinessException("Each serial-controlled line must have quantity one and a serial number.");
        if (mode is TrackingMode.Batch or TrackingMode.BatchExpiry && string.IsNullOrWhiteSpace(batch)) throw new BusinessException("A batch number is required.");
        if (mode == TrackingMode.BatchExpiry && expiry is null) throw new BusinessException("An expiry date is required.");
        if (sale && expiry < date) throw new BusinessException("Expired stock cannot be sold.");
    }
}


