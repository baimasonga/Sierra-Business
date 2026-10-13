using System.Globalization;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure.Imports;

public sealed partial class ImportService
{
    public async Task<byte[]> ConvertExchange(ExchangeImportCommand input)
    {
        var actor = await Authorize(input.Type); var template = Get(input.Type);
        if (input.Content is null || input.Content.Length is < 1 or > 10 * 1024 * 1024) throw new BusinessException("Choose a non-empty JSON or XML file of at most 10 MB.");
        var headers = template.Fields.Select(f => f.Key).ToArray(); var rows = new List<IReadOnlyList<string>> { headers };
        try
        {
            if (Path.GetExtension(input.FileName).Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                using var json = JsonDocument.Parse(input.Content, new JsonDocumentOptions { MaxDepth = 16 }); var root = json.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("Version", out var version) || version.GetInt32() != 1 || !root.TryGetProperty("Type", out var type) || type.GetString() != input.Type.ToString() || !root.TryGetProperty("Rows", out var data) || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() is < 1 or > 10000) throw new BusinessException("Use Version 1, the selected Type, and 1–10,000 Rows of named fields.");
                foreach (var row in data.EnumerateArray())
                {
                    if (row.ValueKind != JsonValueKind.Object) throw new BusinessException("Each JSON row must be an object.");
                    var properties = row.EnumerateObject().ToArray();
                    if (properties.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != properties.Length || properties.Any(p => !headers.Contains(p.Name, StringComparer.Ordinal) || p.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null))) throw new BusinessException("JSON rows require unique known template keys and scalar values; unknown fields cannot be discarded.");
                    rows.Add(headers.Select(h => row.TryGetProperty(h, out var value) ? value.ValueKind == JsonValueKind.String ? value.GetString()! : value.ValueKind == JsonValueKind.Null ? "" : value.GetRawText() : "").ToArray());
                }
            }
            else if (Path.GetExtension(input.FileName).Equals(".xml", StringComparison.OrdinalIgnoreCase))
            {
                using var stream = new MemoryStream(input.Content); using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 10 * 1024 * 1024 }); var xml = XDocument.Load(reader);
                if (input.Type != ImportType.Vouchers || xml.Root?.Name.LocalName != "ENVELOPE") throw new BusinessException("The XML adapter accepts Tally ENVELOPE accounting vouchers only. Use reviewed JSON or Excel templates for masters and stock.");
                var vouchers = xml.Descendants("VOUCHER").ToArray(); if (vouchers.Length is < 1 or > 10000) throw new BusinessException("No supported accounting vouchers found.");
                await using var db = await factory.CreateDbContextAsync(); var accounts = await db.LedgerAccounts.AsNoTracking().ToListAsync();
                foreach (var voucher in vouchers)
                {
                    string V(string name) => voucher.Element(name)?.Value.Trim() ?? "";
                    var voucherType = V("VOUCHERTYPENAME"); if (voucherType.Length == 0) voucherType = voucher.Attribute("VCHTYPE")?.Value ?? "";
                    if (!Enum.TryParse<VoucherType>(voucherType, true, out var parsedType) || !Enum.IsDefined(parsedType) || voucher.Descendants().Any(e => e.Name.LocalName.Contains("INVENTORY", StringComparison.OrdinalIgnoreCase) || e.Name.LocalName.Contains("COSTCENTRE", StringComparison.OrdinalIgnoreCase) || e.Name.LocalName == "BILLALLOCATIONS.LIST") || V("ISOPTIONAL").Equals("Yes", StringComparison.OrdinalIgnoreCase) || V("ISCANCELLED").Equals("Yes", StringComparison.OrdinalIgnoreCase)) throw new BusinessException("Only plain journal/payment/receipt/contra vouchers are supported. Inventory, bills, cost-centre allocations, optional and cancelled vouchers require separate migration review.");
                    if (!DateOnly.TryParseExact(V("DATE"), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) throw new BusinessException("XML voucher DATE must use yyyyMMdd.");
                    var reference = V("VOUCHERNUMBER"); if (reference.Length == 0) throw new BusinessException("Each XML voucher needs a stable voucher number for duplicate prevention.");
                    var entries = voucher.Elements().Where(e => e.Name.LocalName is "ALLLEDGERENTRIES.LIST" or "LEDGERENTRIES.LIST").ToArray(); if (entries.Length is < 2 or > 200) throw new BusinessException("XML vouchers need 2–200 ledger entries.");
                    foreach (var entry in entries)
                    {
                        var name = entry.Element("LEDGERNAME")?.Value.Trim() ?? ""; var matching = accounts.Where(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray(); if (matching.Length != 1) throw new BusinessException($"Map ledger '{name}' to one uniquely named existing account before converting.");
                        if (!decimal.TryParse(entry.Element("AMOUNT")?.Value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) || amount == 0) throw new BusinessException("XML amounts must be plain signed numbers in functional currency.");
                        var flag = entry.Element("ISDEEMEDPOSITIVE")?.Value.Trim(); var debit = flag?.Equals("Yes", StringComparison.OrdinalIgnoreCase) ?? amount < 0;
                        if (flag is not null && !flag.Equals("Yes", StringComparison.OrdinalIgnoreCase) && !flag.Equals("No", StringComparison.OrdinalIgnoreCase) || debit != (amount < 0)) throw new BusinessException("XML debit/credit flag conflicts with the signed amount.");
                        var values = new Dictionary<string, string> { ["voucher_key"] = reference, ["date"] = date.ToString("yyyy-MM-dd"), ["voucher_type"] = parsedType.ToString(), ["narration"] = V("NARRATION").Length == 0 ? "Imported " + reference : V("NARRATION"), ["account_code"] = matching[0].Id.ToString(CultureInfo.InvariantCulture), ["debit"] = debit ? Math.Abs(amount).ToString(CultureInfo.InvariantCulture) : "0", ["credit"] = debit ? "0" : amount.ToString(CultureInfo.InvariantCulture) };
                        rows.Add(headers.Select(h => values.GetValueOrDefault(h, "")).ToArray()); if (rows.Count > 10001) throw new BusinessException("Split XML files into batches of at most 10,000 ledger rows.");
                    }
                }
            }
            else throw new BusinessException("Choose a .json or .xml exchange file.");
        }
        catch (Exception ex) when (ex is JsonException or XmlException or InvalidOperationException or FormatException or OverflowException) { throw new BusinessException("The exchange file is malformed or uses unsupported field types. Review it before conversion."); }
        return WorkbookWriter.Write([("Data", rows)]);
    }
}
