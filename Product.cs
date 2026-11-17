using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace ZSheet;

public class Cand { public long Id; public string Code = "", Unit = "", Name = ""; public JsonObject Data = new(); }
public record LookupResult(string Status, RowRec Row, string Message, int Count);   // Status: ok | none | conflict
public record DupInfo(string Code, string State, List<Cand> Cands, Cand Resolved);

/// 상품 시트: 같은 바코드가 단위(개/갑/박스…)·상품명이 달라 여러 줄일 때, 스캔 결과를 한 줄로 정하는 규칙
///  1) 관리자가 그 바코드에 직접 지정한 행(단위+상품명)  2) 시트의 단위 우선순위(맨 위 단위만)  3) 그래도 여럿이면 '충돌' → 관리자가 지정해야 함
public static class Product
{
    static string N(string s) => (s ?? "").Trim();
    static bool Eq(string a, string b) => string.Equals(N(a), N(b), StringComparison.OrdinalIgnoreCase);

    public static string FindCol(List<(string Key, string Name)> cols, params string[] names)
    {
        var set = names.Select(x => x.ToLowerInvariant()).ToHashSet();
        return cols.FirstOrDefault(x => set.Contains((x.Name ?? "").Trim().Replace(" ", "").ToLowerInvariant())).Key;
    }

    /// 바코드·단위·상품명 열의 키 (없으면 null)
    public static (string Code, string Unit, string Name) Cols(Db db, SqliteConnection c, long sid)
    {
        var cols = db.Columns(c, sid);
        return (db.KeyCol(c, sid), FindCol(cols, "단위", "unit"), FindCol(cols, "상품명", "품명", "productname", "name"));
    }
    public static (string Code, string Unit, string Name) Cols(Db db, long sid) { using var c = db.Open(); return Cols(db, c, sid); }

    // ---- 단위 우선순위 ----
    static List<string> UnitOrder(SqliteConnection c, long sid)
    {
        var t = Db.One(c, "SELECT unitorder FROM sheets WHERE id=@i", ("@i", sid)) as string;
        if (string.IsNullOrEmpty(t)) return new List<string>();
        try { return JsonSerializer.Deserialize<List<string>>(t) ?? new List<string>(); } catch { return new List<string>(); }
    }
    public static List<string> UnitOrder(Db db, long sid) { using var c = db.Open(); return UnitOrder(c, sid); }
    public static void SetUnitOrder(Db db, long sid, List<string> order, string user)
    {
        using var c = db.Open();
        Db.Run(c, "UPDATE sheets SET unitorder=@o WHERE id=@i", ("@o", JsonSerializer.Serialize(order, Db.J)), ("@i", sid));
        db.Log(user, "단위 우선순위", "시트 " + sid + ": " + string.Join(" > ", order));
    }
    /// 시트 데이터에 실제로 있는 단위 목록
    public static List<string> Units(Db db, long sid)
    {
        using var c = db.Open(); var (_, uk, _) = Cols(db, c, sid);
        var list = new List<string>(); if (uk == null) return list;
        using var cmd = Db.Cmd(c, "SELECT DISTINCT json_extract(data,'$." + uk + "') FROM rows WHERE sheet_id=@s", ("@s", sid));
        using var r = cmd.ExecuteReader();
        while (r.Read()) { var v = r.IsDBNull(0) ? "" : r.GetValue(0)?.ToString() ?? ""; if (N(v) != "") list.Add(N(v)); }
        return list.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
    }

    // ---- 관리자 지정(바코드별) ----
    static Dictionary<string, (string Unit, string Name)> LoadPicks(SqliteConnection c, long sid)
    {
        var d = new Dictionary<string, (string, string)>();
        using var cmd = Db.Cmd(c, "SELECT code,unit,name FROM picks WHERE sheet_id=@s", ("@s", sid));
        using var r = cmd.ExecuteReader();
        while (r.Read()) d[r.GetString(0)] = (r.IsDBNull(1) ? "" : r.GetString(1), r.IsDBNull(2) ? "" : r.GetString(2));
        return d;
    }
    public static void SetPick(Db db, long sid, string code, string unit, string name, string user)
    {
        using var c = db.Open();
        Db.Run(c, "INSERT OR REPLACE INTO picks(sheet_id,code,unit,name) VALUES(@s,@c,@u,@n)", ("@s", sid), ("@c", N(code).ToLowerInvariant()), ("@u", N(unit)), ("@n", N(name)));
        db.Log(user, "바코드 지정", code + " → " + unit + " / " + name);
    }
    public static void ClearPick(Db db, long sid, string code, string user)
    {
        using var c = db.Open();
        Db.Run(c, "DELETE FROM picks WHERE sheet_id=@s AND code=@c", ("@s", sid), ("@c", N(code).ToLowerInvariant()));
        db.Log(user, "바코드 지정 해제", code);
    }

    // ---- 후보 읽기 / 결정 ----
    static List<Cand> Load(SqliteConnection c, long sid, string ck, string uk, string nk, string onlyCode)
    {
        var list = new List<Cand>();
        string sql = "SELECT id,data FROM rows WHERE sheet_id=@s";
        var ps = new List<(string, object)> { ("@s", sid) };
        if (onlyCode != null) { sql += " AND json_extract(data,'$." + ck + "') = @c COLLATE NOCASE"; ps.Add(("@c", onlyCode)); }
        using var cmd = Db.Cmd(c, sql + " ORDER BY id", ps.ToArray());
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var o = JsonNode.Parse(r.GetString(1)) as JsonObject ?? new JsonObject();
            list.Add(new Cand { Id = r.GetInt64(0), Data = o, Code = N(o[ck]?.ToString()), Unit = uk == null ? "" : N(o[uk]?.ToString()), Name = nk == null ? "" : N(o[nk]?.ToString()) });
        }
        return list;
    }

    // State: single | pick | unit | conflict
    static (string State, Cand Resolved) Resolve(List<Cand> cands, List<string> order, (string Unit, string Name)? pick)
    {
        if (cands.Count == 1) return ("single", cands[0]);
        if (pick != null)
        {
            var m = cands.Where(x => Eq(x.Unit, pick.Value.Unit) && Eq(x.Name, pick.Value.Name)).OrderBy(x => x.Id).FirstOrDefault();
            if (m != null) return ("pick", m);
        }
        var list = cands;
        if (order.Count > 0)
        {
            int Rank(Cand x) { int i = order.FindIndex(o => Eq(o, x.Unit)); return i < 0 ? int.MaxValue : i; }
            int best = list.Min(Rank);
            list = list.Where(x => Rank(x) == best).ToList();
        }
        int names = list.Select(x => N(x.Name).ToLowerInvariant()).Distinct().Count();
        int units = list.Select(x => N(x.Unit).ToLowerInvariant()).Distinct().Count();
        if (names == 1 && units == 1) return ("unit", list.OrderBy(x => x.Id).First());
        return ("conflict", null);
    }

    static RowRec ToRec(Cand x)
    {
        var rec = new RowRec { Id = x.Id };
        foreach (var kv in x.Data) rec.Data[kv.Key] = kv.Value?.ToString() ?? "";
        return rec;
    }

    /// 바코드 스캔 결과: 클라이언트는 이 결과만 쓸 수 있음 (후보를 고르지 못함)
    public static LookupResult Lookup(Db db, long sid, string code)
    {
        code = N(code);
        if (code == "") return new LookupResult("none", null, "바코드를 입력하세요.", 0);
        using var c = db.Open();
        var (ck, uk, nk) = Cols(db, c, sid);
        if (ck == null) return new LookupResult("none", null, "이 시트에는 바코드 열이 없습니다.", 0);
        var cands = Load(c, sid, ck, uk, nk, code);
        if (cands.Count == 0) return new LookupResult("none", null, "없는 바코드입니다.", 0);
        var picks = LoadPicks(c, sid);
        (string, string)? pk = picks.TryGetValue(code.ToLowerInvariant(), out var p) ? p : null;
        var r = Resolve(cands, UnitOrder(c, sid), pk);
        if (r.Resolved == null) return new LookupResult("conflict", null, "같은 바코드가 여러 개입니다. 관리자가 지정해야 사용할 수 있습니다.", cands.Count);
        return new LookupResult("ok", ToRec(r.Resolved), "", cands.Count);
    }

    /// 같은 바코드가 2줄 이상인 것들과 각 상태 (충돌이 먼저)
    public static List<DupInfo> Dups(Db db, long sid)
    {
        using var c = db.Open();
        var (ck, uk, nk) = Cols(db, c, sid);
        var res = new List<DupInfo>(); if (ck == null) return res;
        var all = Load(c, sid, ck, uk, nk, null);
        var order = UnitOrder(c, sid); var picks = LoadPicks(c, sid);
        foreach (var g in all.Where(x => x.Code != "").GroupBy(x => x.Code.ToLowerInvariant()))
        {
            if (g.Count() < 2) continue;
            var cands = g.OrderBy(x => x.Id).ToList();
            (string, string)? pk = picks.TryGetValue(g.Key, out var p) ? p : null;
            var r = Resolve(cands, order, pk);
            res.Add(new DupInfo(cands[0].Code, r.State, cands, r.Resolved));
        }
        return res.OrderBy(x => x.State == "conflict" ? 0 : 1).ThenBy(x => x.Code, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
