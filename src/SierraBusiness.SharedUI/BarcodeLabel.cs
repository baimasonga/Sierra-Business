using System.Text;
using ZXing;
namespace SierraBusiness.SharedUI;
public static class BarcodeLabel
{
    public static string Svg(string code)
    {
        if (string.IsNullOrEmpty(code) || code.Length > 80 || code.Any(x => x < 32 || x > 126)) throw new ArgumentException("Printable labels require 1–80 ASCII characters.");
        var matrix = new MultiFormatWriter().encode(code, BarcodeFormat.CODE_128, 0, 60, new Dictionary<EncodeHintType, object> { [EncodeHintType.MARGIN] = 20 });
        var text = new StringBuilder($"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 {matrix.Width} 60' width='{matrix.Width}' height='60' role='img' aria-label='Barcode' style='max-width:100%;background:white'>");
        for (var x = 0; x < matrix.Width; x++) if (matrix[x, 0]) text.Append($"<rect x='{x}' y='0' width='1' height='60' fill='black'/>");
        return text.Append("</svg>").ToString();
    }
}
