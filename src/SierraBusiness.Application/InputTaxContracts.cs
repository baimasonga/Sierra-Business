namespace SierraBusiness.Application;

public sealed record InputTaxCommand(Guid OperationId, Guid PaymentDocumentId, DateOnly Date, string Supplier, string SupplierTin, string InvoiceReference, bool Imported, decimal NetPurchase, decimal TaxPaid, decimal AllowableTax, int ExpenseAccount, int RecoveryAccount, string ReviewReason,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] bool Stock = false);
public sealed record InputTaxView(Guid DocumentId, InputTaxCommand Claim, string Number,int Sign = 1,DateOnly? EventDate = null);
public sealed record InputTaxSource(Guid Id,string Number,DateOnly Date,string Party,bool Stock,decimal Amount);
public sealed record NetTaxReturnReport(TaxReturnReport Output, IReadOnlyList<InputTaxView> Inputs, decimal LocalNet, decimal LocalTax, decimal ImportedNet, decimal ImportedTax, decimal AllowableInputTax)
{
    public decimal NetPayable => Output.OutputTax - AllowableInputTax;
}
