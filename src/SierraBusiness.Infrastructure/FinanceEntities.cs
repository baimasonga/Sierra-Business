using SierraBusiness.Application;
namespace SierraBusiness.Infrastructure;

public sealed class LedgerAccount
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public LedgerGroup Group { get; set; }
    public bool CashOrBank { get; set; }
    public bool Control { get; set; }
    public static LedgerAccount[] Standard => [
        new() { Id=1000, Name="Cash", Group=LedgerGroup.Asset, CashOrBank=true },
        new() { Id=1010, Name="Bank", Group=LedgerGroup.Asset, CashOrBank=true },
        new() { Id=1020, Name="Cheques in hand", Group=LedgerGroup.Asset, Control=true },
        new() { Id=1030, Name="Cheques deposited awaiting clearance", Group=LedgerGroup.Asset, Control=true },
        new() { Id=1300, Name="Supplier refunds and credits", Group=LedgerGroup.Asset, Control=true },
        new() { Id=1100, Name="Trade receivables", Group=LedgerGroup.Asset, Control=true },
        new() { Id=1250, Name="Delivered goods awaiting invoice", Group=LedgerGroup.Asset, Control=true },
        new() { Id=1260, Name="Service jobs in progress", Group=LedgerGroup.Asset, Control=true },
        new() { Id=2400, Name="Customer credits", Group=LedgerGroup.Liability, Control=true },
        new() { Id=1200, Name="Inventory", Group=LedgerGroup.Asset, Control=true },
        new() { Id=2000, Name="Trade payables", Group=LedgerGroup.Liability, Control=true },
        new() { Id=2050, Name="Goods received not invoiced", Group=LedgerGroup.Liability, Control=true },
        new() { Id=2100, Name="Tax payable", Group=LedgerGroup.Liability },
        new() { Id=2200, Name="Payroll payable", Group=LedgerGroup.Liability },
        new() { Id=2300, Name="Payroll deductions payable", Group=LedgerGroup.Liability },
        new() { Id=2310, Name="PAYE payable", Group=LedgerGroup.Liability, Control=true },
        new() { Id=2320, Name="NASSIT payable", Group=LedgerGroup.Liability, Control=true },
        new() { Id=3000, Name="Owner capital", Group=LedgerGroup.Equity },
        new() { Id=3100, Name="Retained earnings", Group=LedgerGroup.Equity },
        new() { Id=3200, Name="Opening balance equity", Group=LedgerGroup.Equity, Control=true },
        new() { Id=4000, Name="Sales", Group=LedgerGroup.Income, Control=true },
        new() { Id=4100, Name="Other income", Group=LedgerGroup.Income },
        new() { Id=4200, Name="Exchange gains", Group=LedgerGroup.Income, Control=true },
        new() { Id=4300, Name="Service income", Group=LedgerGroup.Income, Control=true },
        new() { Id=5000, Name="Cost of sales", Group=LedgerGroup.Expense, Control=true },
        new() { Id=5100, Name="Operating expenses", Group=LedgerGroup.Expense },
        new() { Id=5200, Name="Wages and salaries", Group=LedgerGroup.Expense },
        new() { Id=5210, Name="Employer NASSIT contributions", Group=LedgerGroup.Expense, Control=true },
        new() { Id=5300, Name="Exchange losses", Group=LedgerGroup.Expense, Control=true },
        new() { Id=5400, Name="Warranty and service costs", Group=LedgerGroup.Expense, Control=true } ];
}
public sealed class BillAllocation
{
    public Guid Id { get; set; }
    public Guid VoucherId { get; set; }
    public Guid InvoiceId { get; set; }
    public decimal Amount { get; set; }
}
public sealed class BankMatchCancellation
{
    public Guid Id { get; set; }
    public Guid MatchId { get; set; }
    public string Reason { get; set; } = "";
}
public sealed class BankMatch
{
    public Guid Id { get; set; }
    public Guid JournalLineId { get; set; }
    public string Branch { get; set; } = "";
    public Guid? StatementRowId { get; set; }
    public DateOnly ClearedDate { get; set; }
    public string StatementReference { get; set; } = "";
}
public sealed class LedgerBudget
{
    public Guid Id { get; set; }
    public string Branch { get; set; } = "";
    public int AccountCode { get; set; }
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public decimal Amount { get; set; }
    public string CostCentre { get; set; } = "";
}


