using SierraBusiness.Application;
namespace SierraBusiness.SharedUI;

/// <summary>Editable form model for document details.</summary>
public sealed class DocumentDetailsModel
{
    public LibraryDocumentType Type { get; set; } = LibraryDocumentType.Other;
    public string Title { get; set; } = "";
    public string Reference { get; set; } = "";
    public string Party { get; set; } = "";
    public DateOnly? DocumentDate { get; set; }
    public decimal? Amount { get; set; }
    public string Currency { get; set; } = "";
    public DateOnly? DueDate { get; set; }
    public string Tags { get; set; } = "";
    public string Notes { get; set; } = "";
    public bool Confidential { get; set; }
    public DocumentDetails ToDetails() => new(Type, Title, Reference, Party, DocumentDate, Amount, Currency, DueDate, Tags, Notes, Confidential);
    public static DocumentDetailsModel From(DocumentDetails d) => new() { Type = d.Type, Title = d.Title, Reference = d.Reference, Party = d.Party, DocumentDate = d.DocumentDate, Amount = d.Amount, Currency = d.Currency, DueDate = d.DueDate, Tags = d.Tags, Notes = d.Notes, Confidential = d.Confidential };
}
