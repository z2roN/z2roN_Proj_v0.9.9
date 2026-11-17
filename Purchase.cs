using System.Globalization;
using System.Text.Json.Nodes;
using ClosedXML.Excel;
using Microsoft.Data.Sqlite;

namespace ZSheet;

public class PItem
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "";
    public double Qty { get; set; }          // 예정 수량(엑셀)
    public string Cost { get; set; } = "";   // 입고단가
    public double Scanned { get; set; }      // 스캔해서 확인한 수량
    public bool IsNew { get; set; }          // 상품 DB 에 없던 신규 상품
    public bool Registered { get; set; }     // 상품 시트에 등록 완료
    public bool Extra { get; set; }          // 엑셀 목록에 없는데 스캔된 것
    public string By { get; set; } = "";
    public string Ts { get; set; } = "";
}
public class PHead
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Created { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public string Status { get; set; } = "";
    public long SheetId { get; set; }
    public string SheetName { get; set; } = "";
}
public record PPlan(int New, int Existing, int Merged, int NoCode, List<PItem> Items);
public record ScanResult(PItem Item, int Candidates);

/// 매입(입고 검수): ① 엑셀(바코드·상품명·수량·입고단가[·단위])을 올려 매입 건을 만들고 신규 상품을 상품 시트에 등록
/// ② 폰·PC 에서 바코드를 스캔해 수량을 세어 예정 수량과 비교(재고조사 방식)
public static class Purchase
{
    static double Num(string s) { s = (s ?? "").Replace(",", "").Trim(); return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0; }
    static string Sx(SqliteDataReader r, int i) => r.IsDBNull(i) ? "" : Convert.ToString(r.GetValue(i)) ?? "";

    const string IC = "id,code,name,unit,qty,cost,scanned,is_new,registered,extra,last_by,last_ts";
    static PItem Rd(SqliteDataReader r) => new PItem
    {
        Id = r.GetInt64(0), Code = Sx(r, 1), Name = Sx(r, 2), Unit = Sx(r, 3), Qty = r.IsDBNull(4) ? 0 : r.GetDouble(4), Cost = Sx(r, 5),
        Scanned = r.IsDBNull(6) ? 0 : r.GetDouble(6), IsNew = !r.IsDBNull(7) && r.GetInt64(7) == 1, Registered = !r.IsDBNull(8) && r.GetInt64(8) == 1,
        Extra = !r.IsDBNull(9) && r.GetInt64(9) == 1, By = Sx(r, 10), Ts = Sx(r, 11)
    };
    static List<PItem> ReadItems(SqliteConnection c, string where, params (string, object)[] ps)
    {
        var l = new List<PItem>();
        using var cmd = Db.Cmd(c, "SELECT " + IC + " FROM pitems WHERE " + where + " ORDER BY id", ps);
        using var r = cmd.ExecuteReader();
        while (r.Read()) l.Add(Rd(r));
        return l;
    }

    // ---- 엑셀 읽어서 계획 만들기 (저장 안 함) ----
    public static PPlan Plan(Db db, long sid, Table t)
    {
        using var c = db.Open();
        var (ck, uk, nk) = Product.Cols(db, c, sid);
        if (string.IsNullOrEmpty(ck)) throw new InvalidOperationException("상품 시트에 기준열(바코드)이 없습니다. 시트·열 설정에서 바코드 열을 기준열로 지정하세요.");
        int Ix(params string[] names)
        {
            for (int i = 0; i < t.Headers.Length; i++)
            {
                var hd = (t.Headers[i] ?? "").Trim().Replace(" ", "").ToLowerInvariant();
                if (names.Contains(hd)) return i;
            }
            return -1;
        }
        int ic = Ix("바코드", "barcode", "code", "코드"), inm = Ix("상품명", "품명", "name", "productname"), iq = Ix("수량", "qty", "quantity", "입고수량"),
            iu = Ix("단위", "unit"), ip = Ix("입고단가", "단가", "매입가", "입고가", "cost");
        if (ic < 0) throw new InvalidOperationException("엑셀 첫 줄에 '바코드' 열이 있어야 합니다. (바코드, 상품명, 수량, 입고단가 — 단위는 선택)");
        string Cell(string[] r, int i) => i >= 0 && i < r.Length ? (r[i] ?? "").Trim() : "";

        var map = new Dictionary<string, PItem>(StringComparer.OrdinalIgnoreCase);
        var list = new List<PItem>(); int noCode = 0, merged = 0;
        foreach (var r in t.Rows)
        {
            string code = Cell(r, ic);
            if (code == "") { noCode++; continue; }
            string unit = Cell(r, iu), key = code + "|" + unit;
            double q = Num(Cell(r, iq));
            if (map.TryGetValue(key, out var ex)) { ex.Qty += q; merged++; if (ex.Name == "") ex.Name = Cell(r, inm); if (ex.Cost == "") ex.Cost = Cell(r, ip); continue; }
            var it = new PItem { Code = code, Name = Cell(r, inm), Unit = unit, Cost = Cell(r, ip), Qty = q };
            map[key] = it; list.Add(it);
        }

        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var cu = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = Db.Cmd(c, "SELECT data FROM rows WHERE sheet_id=@s", ("@s", sid)))
        using (var rd = cmd.ExecuteReader())
            while (rd.Read())
            {
                if (rd.IsDBNull(0)) continue;
                JsonObject o; try { o = JsonNode.Parse(rd.GetString(0)) as JsonObject; } catch { continue; }
                if (o == null) continue;
                string cd = (o[ck]?.ToString() ?? "").Trim(); if (cd == "") continue;
                codes.Add(cd);
                if (uk != null) cu.Add(cd + "|" + (o[uk]?.ToString() ?? "").Trim());
            }
        foreach (var it in list) it.IsNew = (uk != null && it.Unit != "") ? !cu.Contains(it.Code + "|" + it.Unit) : !codes.Contains(it.Code);
        int nn = list.Count(x => x.IsNew);
        return new PPlan(nn, list.Count - nn, merged, noCode, list);
    }

    /// 매입 건 만들기 (register=true 면 신규 상품을 상품 시트에 바로 등록)
    public static (long Id, PPlan Plan, int Registered) Create(Db db, long sid, string name, Table t, bool register, string user)
    {
        var plan = Plan(db, sid, t);
        if (plan.Items.Count == 0) throw new InvalidOperationException("등록할 줄이 없습니다. 바코드가 있는 줄이 필요합니다.");
        long pid;
        using (var c = db.Open())
        {
            using var tx = c.BeginTransaction();
            Db.Run(c, "INSERT INTO purchases(name,created,created_by,status,sheet_id) VALUES(@n,@t,@u,'open',@s)", ("@n", name), ("@t", Db.Now()), ("@u", user), ("@s", sid));
            pid = Db.Count(c, "SELECT last_insert_rowid()");
            foreach (var it in plan.Items)
                Db.Run(c, "INSERT INTO pitems(purchase_id,code,name,unit,qty,cost,scanned,is_new,registered,extra) VALUES(@p,@c,@n,@u,@q,@k,0,@w,0,0)",
                    ("@p", pid), ("@c", it.Code), ("@n", it.Name), ("@u", it.Unit), ("@q", it.Qty), ("@k", it.Cost), ("@w", it.IsNew ? 1 : 0));
            tx.Commit();
        }
        db.Log(user, "매입 만들기", name + " " + plan.Items.Count + "줄");
        int reg = register ? Register(db, pid, false, user).Added : 0;
        return (pid, plan, reg);
    }

    /// 신규 상품을 상품 시트에 등록. updateCost=true 면 기존 상품의 입고단가도 엑셀 값으로 고침
    public static (int Added, int Updated) Register(Db db, long pid, bool updateCost, string user)
    {
        using var c = db.Open(); using var tx = c.BeginTransaction();
        long sid = Db.Count(c, "SELECT sheet_id FROM purchases WHERE id=@i", ("@i", pid));
        if (Db.Count(c, "SELECT COUNT(*) FROM sheets WHERE id=@i", ("@i", sid)) == 0) throw new InvalidOperationException("상품 시트가 없습니다.");
        var (ck, uk, nk) = Product.Cols(db, c, sid);
        if (string.IsNullOrEmpty(ck)) throw new InvalidOperationException("상품 시트에 기준열(바코드)이 없습니다.");
        string pk = Product.FindCol(db.Columns(c, sid), "입고단가", "매입가", "입고가", "cost");
        var items = ReadItems(c, "purchase_id=@p AND extra=0", ("@p", pid));
        int add = 0, upd = 0;
        foreach (var it in items.Where(x => x.IsNew && !x.Registered))
        {
            var o = new JsonObject(); o[ck] = it.Code;
            if (nk != null) o[nk] = it.Name;
            if (uk != null) o[uk] = it.Unit;
            if (pk != null) o[pk] = it.Cost;
            Db.Run(c, "INSERT INTO rows(sheet_id,data,ver,updated,updated_by) VALUES(@s,@d,1,@t,@u)", ("@s", sid), ("@d", o.ToJsonString(Db.J)), ("@t", Db.Now()), ("@u", user));
            Db.Run(c, "UPDATE pitems SET registered=1 WHERE id=@i", ("@i", it.Id));
            add++;
        }
        if (updateCost && pk != null)
            foreach (var it in items.Where(x => !x.IsNew && x.Cost != ""))
            {
                var found = new List<(long Id, JsonObject O)>();
                using (var cmd = Db.Cmd(c, "SELECT id,data FROM rows WHERE sheet_id=@s AND json_extract(data,'$." + ck + "')=@c", ("@s", sid), ("@c", it.Code)))
                using (var rd = cmd.ExecuteReader())
                    while (rd.Read()) { try { if (JsonNode.Parse(rd.GetString(1)) is JsonObject jo) found.Add((rd.GetInt64(0), jo)); } catch { } }
                foreach (var (rid, jo) in found)
                {
                    if (uk != null && it.Unit != "" && !string.Equals((jo[uk]?.ToString() ?? "").Trim(), it.Unit, StringComparison.OrdinalIgnoreCase)) continue;
                    if ((jo[pk]?.ToString() ?? "").Trim() == it.Cost) continue;
                    jo[pk] = it.Cost;
                    Db.Run(c, "UPDATE rows SET data=@d, ver=ver+1, updated=@t, updated_by=@u WHERE id=@r", ("@d", jo.ToJsonString(Db.J)), ("@t", Db.Now()), ("@u", user), ("@r", rid));
                    upd++;
                }
            }
        tx.Commit();
        db.Log(user, "매입 신규상품 등록", "매입 " + pid + ": 신규 " + add + (updateCost ? ", 단가수정 " + upd : ""));
        return (add, upd);
    }

    // ---- 조회 ----
    public static List<object> List(Db db)
    {
        using var c = db.Open(); var l = new List<object>();
        using var cmd = Db.Cmd(c, @"SELECT p.id,p.name,p.created,p.created_by,p.status,p.sheet_id,
 (SELECT COUNT(*) FROM pitems WHERE purchase_id=p.id AND extra=0),
 (SELECT IFNULL(SUM(qty),0) FROM pitems WHERE purchase_id=p.id),
 (SELECT IFNULL(SUM(scanned),0) FROM pitems WHERE purchase_id=p.id),
 (SELECT COUNT(*) FROM pitems WHERE purchase_id=p.id AND is_new=1 AND registered=0),
 (SELECT COUNT(*) FROM pitems WHERE purchase_id=p.id AND extra=0 AND scanned>=qty)
 FROM purchases p ORDER BY p.id DESC");
        using var r = cmd.ExecuteReader();
        while (r.Read())
            l.Add(new { id = r.GetInt64(0), name = Sx(r, 1), created = Sx(r, 2), createdBy = Sx(r, 3), status = Sx(r, 4), sheetId = r.GetInt64(5), lines = r.GetInt64(6),
                        qty = r.GetDouble(7), scanned = r.GetDouble(8), pendingNew = r.GetInt64(9), doneLines = r.GetInt64(10) });
        return l;
    }

    public static PHead Head(Db db, long pid)
    {
        using var c = db.Open();
        using var cmd = Db.Cmd(c, "SELECT p.id,p.name,p.created,p.created_by,p.status,p.sheet_id,IFNULL((SELECT name FROM sheets WHERE id=p.sheet_id),'') FROM purchases p WHERE p.id=@i", ("@i", pid));
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new PHead { Id = r.GetInt64(0), Name = Sx(r, 1), Created = Sx(r, 2), CreatedBy = Sx(r, 3), Status = Sx(r, 4), SheetId = r.GetInt64(5), SheetName = Sx(r, 6) };
    }
    public static List<PItem> Items(Db db, long pid) { using var c = db.Open(); return ReadItems(c, "purchase_id=@p", ("@p", pid)); }

    // ---- 스캔 ----
    /// 바코드 1회 스캔 = 확인 수량 +delta. 같은 바코드 줄이 여럿이면 단위 우선순위가 높은 줄에 반영. 목록에 없으면 '목록 외' 줄을 만든다.
    public static ScanResult Scan(Db db, long pid, string code, double delta, string user)
    {
        code = (code ?? "").Trim();
        if (code == "") throw new InvalidOperationException("바코드가 비었습니다.");
        using var c = db.Open();
        if (Db.One(c, "SELECT status FROM purchases WHERE id=@i", ("@i", pid)) is not string st) throw new InvalidOperationException("매입 건이 없습니다.");
        if (st != "open") throw new InvalidOperationException("완료된 매입입니다. [다시 열기] 후 스캔할 수 있습니다.");
        long sid = Db.Count(c, "SELECT sheet_id FROM purchases WHERE id=@i", ("@i", pid));
        var cands = ReadItems(c, "purchase_id=@p AND code=@c COLLATE NOCASE", ("@p", pid), ("@c", code));
        if (cands.Count == 0)
        {
            string nm = "(상품 DB 에 없는 바코드)", un = "";
            try
            {
                var lr = Product.Lookup(db, sid, code);
                if (lr.Status == "ok")
                {
                    var (_, uk2, nk2) = Product.Cols(db, sid);
                    nm = "";
                    if (nk2 != null && lr.Row.Data.TryGetValue(nk2, out var v1)) nm = v1;
                    if (uk2 != null && lr.Row.Data.TryGetValue(uk2, out var v2)) un = v2;
                }
                else if (lr.Status == "conflict") nm = "(중복 바코드 — 관리자 지정 필요)";
            }
            catch { }
            Db.Run(c, "INSERT INTO pitems(purchase_id,code,name,unit,qty,cost,scanned,is_new,registered,extra) VALUES(@p,@c,@n,@u,0,'',0,0,0,1)", ("@p", pid), ("@c", code), ("@n", nm), ("@u", un));
            cands = ReadItems(c, "purchase_id=@p AND code=@c COLLATE NOCASE", ("@p", pid), ("@c", code));
        }
        var pick = cands[0];
        if (cands.Count > 1)
        {
            var order = Product.UnitOrder(db, sid);
            int Rank(PItem x) { int i = order.FindIndex(o => string.Equals(o, x.Unit, StringComparison.OrdinalIgnoreCase)); return i < 0 ? int.MaxValue : i; }
            pick = cands.OrderBy(Rank).ThenBy(x => x.Id).First();
        }
        Db.Run(c, "UPDATE pitems SET scanned=MAX(0,scanned+@d), last_by=@u, last_ts=@t WHERE id=@i", ("@d", delta), ("@u", user), ("@t", Db.Now()), ("@i", pick.Id));
        return new ScanResult(ReadItems(c, "id=@i", ("@i", pick.Id))[0], cands.Count);
    }

    static void CheckOpen(SqliteConnection c, long itemId)
    {
        var st = Db.One(c, "SELECT p.status FROM purchases p JOIN pitems i ON i.purchase_id=p.id WHERE i.id=@i", ("@i", itemId)) as string;
        if (st == null) throw new InvalidOperationException("항목이 없습니다.");
        if (st != "open") throw new InvalidOperationException("완료된 매입입니다. [다시 열기] 후 고칠 수 있습니다.");
    }
    public static PItem SetScanned(Db db, long itemId, double value, string user)
    {
        using var c = db.Open(); CheckOpen(c, itemId);
        if (value < 0) value = 0;
        Db.Run(c, "UPDATE pitems SET scanned=@v, last_by=@u, last_ts=@t WHERE id=@i", ("@v", value), ("@u", user), ("@t", Db.Now()), ("@i", itemId));
        return ReadItems(c, "id=@i", ("@i", itemId))[0];
    }
    public static void DeleteExtra(Db db, long itemId, string user)
    {
        using var c = db.Open(); CheckOpen(c, itemId);
        int n = Db.Run(c, "DELETE FROM pitems WHERE id=@i AND extra=1", ("@i", itemId));
        if (n == 0) throw new InvalidOperationException("엑셀 목록에 있던 줄은 지울 수 없습니다. (확인 수량을 0으로 고치세요)");
    }
    public static void SetStatus(Db db, long pid, string status, string user)
    {
        if (status != "open" && status != "done") throw new InvalidOperationException("상태 값이 올바르지 않습니다.");
        using var c = db.Open();
        Db.Run(c, "UPDATE purchases SET status=@s WHERE id=@i", ("@s", status), ("@i", pid));
        db.Log(user, status == "done" ? "매입 완료" : "매입 다시 열기", "매입 " + pid);
    }
    public static void Delete(Db db, long pid, string user)
    {
        using var c = db.Open(); using var tx = c.BeginTransaction();
        Db.Run(c, "DELETE FROM pitems WHERE purchase_id=@i", ("@i", pid));
        Db.Run(c, "DELETE FROM purchases WHERE id=@i", ("@i", pid));
        tx.Commit(); db.Log(user, "매입 삭제", "매입 " + pid);
    }

    // ---- 결과 엑셀 ----
    public static byte[] Export(Db db, long pid, out string fileName)
    {
        var h = Head(db, pid) ?? throw new InvalidOperationException("매입 건이 없습니다.");
        var items = Items(db, pid);
        fileName = Store.SafeName(h.Name) + "_매입결과.xlsx";
        using var wb = new XLWorkbook(); var ws = wb.Worksheets.Add("매입결과");
        string[] hd = { "바코드", "상품명", "단위", "예정수량", "확인수량", "차이", "입고단가", "결과", "신규" };
        for (int i = 0; i < hd.Length; i++) { var x = ws.Cell(1, i + 1); x.Value = hd[i]; x.Style.Font.Bold = true; x.Style.Fill.BackgroundColor = XLColor.FromHtml("#EEF3FD"); }
        int r = 2;
        foreach (var it in items)
        {
            double diff = it.Scanned - it.Qty;
            string res = it.Extra ? "목록 외" : it.Scanned == 0 && it.Qty > 0 ? "미확인" : diff < 0 ? "부족" : diff > 0 ? "초과" : "일치";
            ws.Cell(r, 1).SetValue(it.Code); ws.Cell(r, 2).SetValue(it.Name); ws.Cell(r, 3).SetValue(it.Unit);
            ws.Cell(r, 4).SetValue(it.Qty); ws.Cell(r, 5).SetValue(it.Scanned); ws.Cell(r, 6).SetValue(diff);
            ws.Cell(r, 7).SetValue(it.Cost); ws.Cell(r, 8).SetValue(res); ws.Cell(r, 9).SetValue(it.IsNew ? (it.Registered ? "신규(등록됨)" : "신규(미등록)") : "");
            r++;
        }
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream(); wb.SaveAs(ms); return ms.ToArray();
    }
}
