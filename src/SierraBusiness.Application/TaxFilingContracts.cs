namespace SierraBusiness.Application;
public sealed record TaxFilingPreview(NetTaxReturnReport Report,string CalculationFingerprint,CompanyProfile? Company = null);
public sealed record TaxFilingCommand(Guid OperationId,DateOnly From,DateOnly To,string ExpectedCalculation,string FilingReference,string Reason,int BankAccount,int CarryAccount,decimal CarryCreditUsed,bool SourcesReconciled,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] decimal? DeclaredNetTax = null,int RoundingAccount = 5100);
public sealed record TaxFilingView(Guid Id,string Number,DateOnly From,DateOnly To,DateOnly Recorded,string FilingReference,decimal NetTax,decimal CarryUsed,decimal Paid,decimal NewCredit,string By);
/// <summary>An accountant-reviewed correction to a recorded monthly return, booked today. Positive <paramref name="OutputTaxChange"/> is
/// GST under-declared; positive <paramref name="InputTaxChange"/> is input GST newly allowable. Their difference is the extra tax due (or credit),
/// offset against <paramref name="OffsetAccount"/> and settled from cash/bank or brought-forward credit, or added to the carry-forward credit.</summary>
public sealed record TaxCorrectionCommand(Guid OperationId,Guid FilingId,string CorrectionReference,string Reason,decimal OutputTaxChange,decimal InputTaxChange,int OffsetAccount,int BankAccount,int CarryAccount,decimal CarryCreditUsed,decimal Penalty = 0,int PenaltyAccount = 0);
public sealed record TaxCorrectionView(Guid Id,string Number,Guid FilingId,DateOnly From,DateOnly To,DateOnly Recorded,string CorrectionReference,string Reason,decimal OutputTaxChange,decimal InputTaxChange,decimal NetChange,decimal CarryUsed,decimal Paid,decimal NewCredit,decimal Penalty,string By);
