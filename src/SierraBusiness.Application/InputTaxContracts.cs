namespace SierraBusiness.Application;

public sealed record InputTaxCommand(Guid OperationId, Guid PaymentDocumentId, DateOnly Date, string Supplier, string SupplierTin, string InvoiceReference, bool Imported, decimal NetPurchase, decimal TaxPaid, decimal AllowableTax, int ExpenseAccount, int RecoveryAccount, string ReviewReason,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] bool Stock = false,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] decimal? IssuedTax = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] int IssuedAccount = 0);
/// <summary>Proposed split of reviewed stock GST. Standard-cost goods credit the purchase price variance (5100); other goods credit inventory still on hand and, after later issues, a reviewed cost ledger.</summary>
public sealed record StockInputTaxLine(Guid ProductId, string Sku, bool Standard, decimal Share, decimal ReceivedQuantity, decimal OnHandQuantity, decimal SuggestedIssued);
public sealed record StockInputTaxPreview(Guid ReceiptId, decimal AllowableTax, bool LaterIssues, decimal StandardVariance, decimal SuggestedIssued, IReadOnlyList<StockInputTaxLine> Lines)
{
    public decimal SuggestedOnHand => AllowableTax-StandardVariance-SuggestedIssued;
}
public sealed record InputTaxView(Guid DocumentId, InputTaxCommand Claim, string Number,int Sign = 1,DateOnly? EventDate = null);
public sealed record InputTaxSource(Guid Id,string Number,DateOnly Date,string Party,bool Stock,decimal Amount);
public sealed record NetTaxReturnReport(TaxReturnReport Output, IReadOnlyList<InputTaxView> Inputs, decimal LocalNet, decimal LocalTax, decimal ImportedNet, decimal ImportedTax, decimal AllowableInputTax)
{
    public decimal NetPayable => Output.OutputTax - AllowableInputTax;
}
