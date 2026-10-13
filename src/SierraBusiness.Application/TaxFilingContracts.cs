namespace SierraBusiness.Application;
public sealed record TaxFilingPreview(NetTaxReturnReport Report,string CalculationFingerprint,CompanyProfile? Company = null);
public sealed record TaxFilingCommand(Guid OperationId,DateOnly From,DateOnly To,string ExpectedCalculation,string FilingReference,string Reason,int BankAccount,int CarryAccount,decimal CarryCreditUsed,bool SourcesReconciled,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] decimal? DeclaredNetTax = null,int RoundingAccount = 5100);
public sealed record TaxFilingView(Guid Id,string Number,DateOnly From,DateOnly To,DateOnly Recorded,string FilingReference,decimal NetTax,decimal CarryUsed,decimal Paid,decimal NewCredit,string By);
