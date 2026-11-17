using System.Globalization;
using System.Text;
using ClosedXML.Excel;

namespace ZSheet;

public class Table { public string[] Headers = Array.Empty<string>(); public List<string[]> Rows = new(); }

/// 엑셀(xlsx/xls)·CSV 읽기: 첫 줄은 머리글, 나머지는 행. 첫 시트만 읽음.
public static class Importer
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static Table Read(Stream s, string fileName)
    {
        var ms = new MemoryStream(); s.CopyTo(ms); var b = ms.ToArray();
        List<string[]> raw;
        if (b.Length > 4 && b[0] == 0x50 && b[1] == 0x4B) raw = ReadXlsx(new MemoryStream(b));
        else if (b.Length > 4 && b[0] == 0xD0 && b[1] == 0xCF && b[2] == 0x11 && b[3] == 0xE0) raw = ReadXls(new MemoryStream(b));
        else raw = ParseCsv(Decode(b));
        return Normalize(raw);
    }

    static Table Normalize(List<string[]> raw)
    {
        var t = new Table();
        raw = raw.Where(r => r.Any(x => !string.IsNullOrWhiteSpace(x))).ToList();
        if (raw.Count == 0) return t;
        int w = 0;
        foreach (var r in raw) for (int i = r.Length - 1; i >= 0; i--) if (!string.IsNullOrWhiteSpace(r[i])) { w = Math.Max(w, i + 1); break; }
        if (w == 0) return t;
        var heads = new string[w];
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < w; i++)
        {
            string h = i < raw[0].Length ? (raw[0][i] ?? "").Trim() : "";
            if (h == "") h = "열" + (i + 1);
            string h2 = h; int k = 2;
            while (!used.Add(h2)) h2 = h + "_" + k++;
            heads[i] = h2;
        }
        t.Headers = heads;
        for (int r = 1; r < raw.Count; r++)
        {
            var row = new string[w];
            for (int i = 0; i < w; i++) row[i] = i < raw[r].Length ? (raw[r][i] ?? "") : "";
            t.Rows.Add(row);
        }
        return t;
    }

    static string Num(double d) => d.ToString("0.###############", Inv);
    static string Dt(DateTime d) => d.TimeOfDay == TimeSpan.Zero ? d.ToString("yyyy-MM-dd") : d.ToString("yyyy-MM-dd HH:mm");

    static string CellText(IXLCell cell)
    {
        try
        {
            if (cell.IsEmpty()) return "";
            switch (cell.DataType)
            {
                case XLDataType.Number: return Num(cell.GetDouble());
                case XLDataType.DateTime: return Dt(cell.GetDateTime());
                case XLDataType.Boolean: return cell.GetBoolean() ? "TRUE" : "FALSE";
                default: return cell.GetString().Trim();
            }
        }
        catch { try { return cell.GetString(); } catch { return ""; } }
    }

    static List<string[]> ReadXlsx(Stream s)
    {
        var rows = new List<string[]>();
        using var wb = new XLWorkbook(s);
        var ws = wb.Worksheets.First();
        var used = ws.RangeUsed();
        if (used == null) return rows;
        int r1 = used.FirstRow().RowNumber(), r2 = used.LastRow().RowNumber(), c1 = used.FirstColumn().ColumnNumber(), c2 = used.LastColumn().ColumnNumber();
        for (int r = r1; r <= r2; r++)
        {
            var arr = new string[c2 - c1 + 1];
            for (int c = c1; c <= c2; c++) arr[c - c1] = CellText(ws.Cell(r, c));
            rows.Add(arr);
        }
        return rows;
    }

    static List<string[]> ReadXls(Stream s)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var rows = new List<string[]>();
        using var rd = ExcelDataReader.ExcelReaderFactory.CreateReader(s);
        while (rd.Read())
        {
            var arr = new string[rd.FieldCount];
            for (int i = 0; i < arr.Length; i++)
            {
                var v = rd.GetValue(i);
                arr[i] = v == null ? "" : v is double d ? Num(d) : v is DateTime dt ? Dt(dt) : v is bool bo ? (bo ? "TRUE" : "FALSE") : v.ToString().Trim();
            }
            rows.Add(arr);
        }
        return rows;
    }

    static string Decode(byte[] b)
    {
        int off = b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF ? 3 : 0;
        try { return new UTF8Encoding(false, true).GetString(b, off, b.Length - off); }
        catch
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(949).GetString(b, off, b.Length - off);
        }
    }

    static List<string[]> ParseCsv(string text)
    {
        string first = text.Split('\n')[0];
        char d = ','; int best = first.Count(ch => ch == ',');
        foreach (var cand in new[] { '\t', ';' }) { int n = first.Count(ch => ch == cand); if (n > best) { best = n; d = cand; } }
        var rows = new List<string[]>(); var row = new List<string>(); var sb = new StringBuilder(); bool q = false;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (q) { if (ch == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { sb.Append('"'); i++; } else q = false; } else sb.Append(ch); }
            else if (ch == '"') q = true;
            else if (ch == d) { row.Add(sb.ToString()); sb.Clear(); }
            else if (ch == '\n' || ch == '\r')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(sb.ToString()); sb.Clear(); rows.Add(row.ToArray()); row.Clear();
            }
            else sb.Append(ch);
        }
        if (sb.Length > 0 || row.Count > 0) { row.Add(sb.ToString()); rows.Add(row.ToArray()); }
        return rows;
    }
}
