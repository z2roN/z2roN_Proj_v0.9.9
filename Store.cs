using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace ZSheet;

public class RowRec { public long Id, Ver; public Dictionary<string, string> Data = new(); public string Updated = "", By = ""; }
public record SheetInfo(long Id, string Name, long Rows);
public record UserRec(int Id, string Name, string Role, string Created);
public record SyncResult(long Sid, int Add, int Update, int Same, int Delete, int NoKey, int DupInFile);

/// 서버 화면(관리자 창)이 DB 를 직접 다루는 함수 모음
public static class Store
{
    public const string Admin = "서버관리자";

    public static List<SheetInfo> Sheets(Db db)
    {
        using var c = db.Open(); var list = new List<SheetInfo>();
        using var cmd = Db.Cmd(c, "SELECT id,name,(SELECT COUNT(*) FROM rows WHERE sheet_id=sheets.id) FROM sheets ORDER BY name");
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new SheetInfo(r.GetInt64(0), r.GetString(1), r.GetInt64(2)));
        return list;
    }

    public static (long Total, List<(string Key, string Name)> Cols, List<RowRec> Rows) Query(Db db, long sid, string q, string sort, bool desc, int off, int lim)
    {
        using var c = db.Open();
        var cols = db.Columns(c, sid);
        string where = "sheet_id=@s"; var ps = new List<(string, object)> { ("@s", sid) };
        if (!string.IsNullOrEmpty(q)) { where += " AND data LIKE @q ESCAPE '\\'"; ps.Add(("@q", "%" + q.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%")); }
        string order = "id";
        if (!string.IsNullOrEmpty(sort) && Regex.IsMatch(sort, "^c[0-9]+$") && cols.Any(x => x.Key == sort))
        {
            string d = desc ? " DESC" : "", ex = "json_extract(data,'$." + sort + "')";
            order = "CAST(" + ex + " AS REAL)" + d + ", " + ex + " COLLATE NOCASE" + d + ", id";
        }
        long total = Db.Count(c, "SELECT COUNT(*) FROM rows WHERE " + where, ps.ToArray());
        var rows = new List<RowRec>();
        var ps2 = new List<(string, object)>(ps) { ("@lim", lim), ("@off", off) };
        using var cmd = Db.Cmd(c, "SELECT id,data,ver,updated,updated_by FROM rows WHERE " + where + " ORDER BY " + order + " LIMIT @lim OFFSET @off", ps2.ToArray());
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var rec = new RowRec { Id = r.GetInt64(0), Ver = r.GetInt64(2), Updated = r.IsDBNull(3) ? "" : r.GetString(3), By = r.IsDBNull(4) ? "" : r.GetString(4) };
            if (JsonNode.Parse(r.GetString(1)) is JsonObject o) foreach (var kv in o) rec.Data[kv.Key] = kv.Value?.ToString() ?? "";
            rows.Add(rec);
        }
        return (total, cols, rows);
    }

    public static bool UpdateRow(Db db, long sid, long rid, long ver, Dictionary<string, string> data, string user)
    {
        using var c = db.Open();
        var keys = db.Columns(c, sid).Select(x => x.Key).ToHashSet();
        var o = new JsonObject();
        foreach (var kv in data) if (keys.Contains(kv.Key)) o[kv.Key] = kv.Value ?? "";
        int n = Db.Run(c, "UPDATE rows SET data=@d, ver=ver+1, updated=@t, updated_by=@u WHERE id=@r AND sheet_id=@s AND ver=@v",
            ("@d", o.ToJsonString(Db.J)), ("@t", Db.Now()), ("@u", user), ("@r", rid), ("@s", sid), ("@v", ver));
        if (n > 0) db.Log(user, "행 수정", "시트 " + sid + " 행 " + rid);
        return n > 0;
    }

    public static long AddRow(Db db, long sid, string user)
    {
        using var c = db.Open();
        Db.Run(c, "INSERT INTO rows(sheet_id,data,ver,updated,updated_by) VALUES(@s,'{}',1,@t,@u)", ("@s", sid), ("@t", Db.Now()), ("@u", user));
        db.Log(user, "행 추가", "시트 " + sid);
        return Db.Count(c, "SELECT last_insert_rowid()");
    }

    public static void DeleteRows(Db db, long sid, IEnumerable<long> ids, string user)
    {
        using var c = db.Open(); using var tx = c.BeginTransaction(); int n = 0;
        foreach (var id in ids) { Db.Run(c, "DELETE FROM rows WHERE id=@r AND sheet_id=@s", ("@r", id), ("@s", sid)); n++; }
        tx.Commit(); db.Log(user, "행 삭제", "시트 " + sid + " " + n + "행");
    }

    public static long NewSheet(Db db, string name, IEnumerable<string> heads, string user)
    {
        using var c = db.Open(); using var tx = c.BeginTransaction();
        var hs = heads.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();
        if (hs.Count == 0) hs.Add("항목");
        long id = db.CreateSheet(c, name, hs); tx.Commit(); db.Log(user, "시트 만들기", name); return id;
    }
    public static void RenameSheet(Db db, long sid, string name, string user) { using var c = db.Open(); Db.Run(c, "UPDATE sheets SET name=@n WHERE id=@i", ("@n", name), ("@i", sid)); db.Log(user, "시트 이름 변경", name); }
    public static void DeleteSheet(Db db, long sid, string user)
    {
        using var c = db.Open(); var name = Db.One(c, "SELECT name FROM sheets WHERE id=@i", ("@i", sid))?.ToString() ?? "";
        Db.Run(c, "DELETE FROM sheets WHERE id=@i", ("@i", sid)); db.Log(user, "시트 삭제", name);
    }
    /// 전체 데이터 초기화: 모든 시트·열·행·지정(picks)·매입·변경기록을 삭제. 계정·설정은 유지.
    /// 삭제 전에 backup 폴더에 현재 DB 를 통째로 복사해 두고 그 경로를 돌려줌.
    public static string WipeAll(Db db, string user)
    {
        string backup;
        using (var c = db.Open())
        {
            var dir = Path.Combine(db.Dir, "backup"); Directory.CreateDirectory(dir);
            backup = Path.Combine(dir, "z2roN_Proj_초기화전_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".db");
            Db.Run(c, "VACUUM INTO '" + backup.Replace("'", "''") + "'");
            using var tx = c.BeginTransaction();
            foreach (var t in new[] { "pitems", "purchases", "picks", "rows", "cols", "sheets", "audit" })
                Db.Run(c, "DELETE FROM " + t);
            try { Db.Run(c, "DELETE FROM sqlite_sequence"); } catch { }
            tx.Commit();
        }
        using (var c = db.Open())
        {
            try { Db.Run(c, "PRAGMA wal_checkpoint(TRUNCATE)"); Db.Run(c, "VACUUM"); } catch { }
        }
        db.Log(user, "전체 데이터 초기화", "백업: " + Path.GetFileName(backup));
        return backup;
    }
    public static List<(string Key, string Name)> Columns(Db db, long sid) { using var c = db.Open(); return db.Columns(c, sid); }
    public static void AddColumn(Db db, long sid, string name, string user) { using var c = db.Open(); db.AddColumn(c, sid, name); db.Log(user, "열 추가", name); }
    public static void RenameColumn(Db db, long sid, string key, string name, string user) { using var c = db.Open(); Db.Run(c, "UPDATE cols SET name=@n WHERE sheet_id=@s AND key=@k", ("@n", name), ("@s", sid), ("@k", key)); db.Log(user, "열 이름 변경", name); }
    public static void DeleteColumn(Db db, long sid, string key, string user)
    {
        if (!Regex.IsMatch(key, "^c[0-9]+$")) return;
        using var c = db.Open(); using var tx = c.BeginTransaction();
        Db.Run(c, "DELETE FROM cols WHERE sheet_id=@s AND key=@k", ("@s", sid), ("@k", key));
        Db.Run(c, "UPDATE rows SET data=json_remove(data,'$." + key + "') WHERE sheet_id=@s", ("@s", sid));
        tx.Commit(); db.Log(user, "열 삭제", key);
    }

    /// mode: new | append | replace. 돌려주는 값 = (시트 번호, 올린 행 수)
    public static (long Sid, int Count) Import(Db db, Table t, string mode, long sid, string name, string user, bool allowNewCols = true)
    {
        using var c = db.Open(); using var tx = c.BeginTransaction();
        bool exists = sid > 0 && Db.Count(c, "SELECT COUNT(*) FROM sheets WHERE id=@i", ("@i", sid)) > 0;
        if (mode == "new" || !exists) sid = db.CreateSheet(c, name, t.Headers);
        else if (mode == "replace") Db.Run(c, "DELETE FROM rows WHERE sheet_id=@s", ("@s", sid));
        var keyOf = db.MapHeaders(c, sid, t.Headers, allowNewCols);
        using var ins = Db.Cmd(c, "INSERT INTO rows(sheet_id,data,ver,updated,updated_by) VALUES(@s,@d,1,@t,@u)", ("@s", sid), ("@d", ""), ("@t", Db.Now()), ("@u", user));
        int n = 0;
        foreach (var row in t.Rows)
        {
            var o = new JsonObject();
            for (int i = 0; i < keyOf.Length; i++) o[keyOf[i]] = row[i];
            ins.Parameters["@d"].Value = o.ToJsonString(Db.J);
            ins.ExecuteNonQuery(); n++;
        }
        tx.Commit();
        db.Log(user, "엑셀 올리기(" + mode + ")", name + " " + n + "행");
        return (sid, n);
    }

    public static string SafeName(string n)
    {
        foreach (var ch in Path.GetInvalidFileNameChars()) n = n.Replace(ch, '_');
        return string.IsNullOrWhiteSpace(n) ? "sheet" : n;
    }

    public static byte[] ExportXlsx(Db db, long sid, string name)
    {
        using var c = db.Open();
        var cols = db.Columns(c, sid);
        using var wb = new XLWorkbook();
        string sn = SafeName(name).Replace("[", "_").Replace("]", "_"); if (sn.Length > 28) sn = sn.Substring(0, 28);
        var ws = wb.Worksheets.Add(sn);
        for (int i = 0; i < cols.Count; i++) { var h = ws.Cell(1, i + 1); h.Value = cols[i].Name; h.Style.Font.Bold = true; h.Style.Fill.BackgroundColor = XLColor.FromHtml("#EEF3FD"); }
        int r = 2;
        using var cmd = Db.Cmd(c, "SELECT data FROM rows WHERE sheet_id=@s ORDER BY id", ("@s", sid));
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var o = JsonNode.Parse(rd.GetString(0)) as JsonObject;
            for (int i = 0; i < cols.Count; i++)
            {
                string v = o?[cols[i].Key]?.ToString() ?? "";
                if (v == "") continue;
                if (v.Length <= 15 && Regex.IsMatch(v, @"^-?(0|[1-9][0-9]*)(\.[0-9]+)?$") && double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dv)) ws.Cell(r, i + 1).Value = dv;
                else ws.Cell(r, i + 1).Value = v;
            }
            r++;
        }
        ws.SheetView.FreezeRows(1);
        if (r < 3000) ws.Columns().AdjustToContents(5, 60);
        using var ms = new MemoryStream(); wb.SaveAs(ms);
        return ms.ToArray();
    }

    // 사용자 / 기록
    public static List<UserRec> Users(Db db)
    {
        using var c = db.Open(); var list = new List<UserRec>();
        using var cmd = Db.Cmd(c, "SELECT id,name,role,created FROM users ORDER BY id"); using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new UserRec(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3)));
        return list;
    }
    public static void DeleteUser(Db db, int id, string user)
    {
        using var c = db.Open();
        Db.Run(c, "DELETE FROM sessions WHERE user_id=@i", ("@i", id)); Db.Run(c, "DELETE FROM users WHERE id=@i", ("@i", id));
        db.Log(user, "사용자 삭제", "사용자 " + id);
    }
    public static List<string[]> Audit(Db db, int n)
    {
        using var c = db.Open(); var list = new List<string[]>();
        using var cmd = Db.Cmd(c, "SELECT ts,user,action,detail FROM audit ORDER BY id DESC LIMIT @n", ("@n", n)); using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new[] { r.GetString(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? "" : r.GetString(3) });
        return list;
    }

    class DRow { public long Id; public JsonObject Data; public string K1, K2; }
    class FRow { public string[] Row; public string K1, K2; }

    /// 기준열(바코드) 동기화. 같은 바코드가 단위·상품명이 달라 여러 줄일 수 있으므로 행의 정체는 "바코드+단위+상품명"(해당 열이 있을 때)으로 봅니다.
    /// 파일에 있고 DB 에 없으면 아래에 이어서 추가, 둘 다 있으면 바뀐 값만 수정, DB 에만 있으면 삭제.
    /// (상품명만 고친 경우처럼 바코드+단위가 한쪽에 1개씩만 남으면 같은 행의 수정으로 봄.)
    /// deleteMissing=false 이면(추가·수정만) 파일에 없는 DB 행은 삭제하지 않고 그대로 둡니다.
    /// 기준값이 빈 DB 행은 그대로 두고, 파일의 빈 기준값 줄은 건너뜁니다. dry=true 이면 저장하지 않고 건수만 계산합니다.
    public static SyncResult Sync(Db db, Table t, long sid, bool dry, string user, bool allowNewCols = true, bool deleteMissing = true)
    {
        using var c = db.Open(); using var tx = c.BeginTransaction();
        string keyKey = db.KeyCol(c, sid);
        if (keyKey == null) throw new InvalidOperationException("이 시트에는 기준열(바코드)이 없습니다. [시트·열 설정]에서 기준열을 지정하세요.");
        var colList = db.Columns(c, sid);
        string keyName = colList.First(x => x.Key == keyKey).Name;
        string unitKey = Product.FindCol(colList, "단위", "unit"), nameKey = Product.FindCol(colList, "상품명", "품명", "productname", "name");
        var keyOf = db.MapHeaders(c, sid, t.Headers, allowNewCols);
        int kIdx = Array.IndexOf(keyOf, keyKey);
        if (kIdx < 0) throw new InvalidOperationException("올린 파일에 기준열 \"" + keyName + "\" 머리글이 없습니다.");
        int uIdx = unitKey == null ? -1 : Array.IndexOf(keyOf, unitKey), nIdx = nameKey == null ? -1 : Array.IndexOf(keyOf, nameKey);

        string K1(string code, string unit, string name) => (code.Trim() + (unitKey != null ? "|" + unit.Trim() : "") + (nameKey != null ? "|" + name.Trim() : "")).ToLowerInvariant();
        string K2(string code, string unit) => (code.Trim() + (unitKey != null ? "|" + unit.Trim() : "")).ToLowerInvariant();

        // 파일: K1 → 행 (같은 K1 이 여러 번이면 마지막 줄), 파일 순서 유지
        var order = new List<string>(); var fileRows = new Dictionary<string, FRow>();
        int noKey = 0, dup = 0;
        foreach (var row in t.Rows)
        {
            string code = (row[kIdx] ?? "").Trim();
            if (code == "") { noKey++; continue; }
            string unit = uIdx < 0 ? "" : row[uIdx] ?? "", name = nIdx < 0 ? "" : row[nIdx] ?? "";
            string k1 = K1(code, unit, name);
            if (fileRows.ContainsKey(k1)) dup++; else order.Add(k1);
            fileRows[k1] = new FRow { Row = row, K1 = k1, K2 = K2(code, unit) };
        }
        if (order.Count == 0) throw new InvalidOperationException("올린 파일에 기준값(바코드)이 있는 행이 없습니다. 아무것도 변경하지 않았습니다.");

        // DB 현재 행
        var db1 = new Dictionary<string, DRow>(); var extra = new List<long>();
        using (var cmd = Db.Cmd(c, "SELECT id,data FROM rows WHERE sheet_id=@s ORDER BY id", ("@s", sid)))
        using (var r = cmd.ExecuteReader())
            while (r.Read())
            {
                var o = JsonNode.Parse(r.GetString(1)) as JsonObject ?? new JsonObject();
                string code = (o[keyKey]?.ToString() ?? "").Trim();
                if (code == "") continue;                       // 기준값 없는 DB 행은 그대로 둠
                string unit = unitKey == null ? "" : o[unitKey]?.ToString() ?? "", name = nameKey == null ? "" : o[nameKey]?.ToString() ?? "";
                var d = new DRow { Id = r.GetInt64(0), Data = o, K1 = K1(code, unit, name), K2 = K2(code, unit) };
                if (db1.ContainsKey(d.K1)) extra.Add(d.Id); else db1[d.K1] = d;
            }

        // 1차: 정확히 같은 K1. 2차: 남은 것 중 바코드+단위가 양쪽에 1개씩이면 같은 행(이름 수정)으로 봄
        var pairs = new Dictionary<string, DRow>(); var used = new HashSet<long>();
        foreach (var k in order) if (db1.TryGetValue(k, out var d)) { pairs[k] = d; used.Add(d.Id); }
        var dbLeft = db1.Values.Where(d => !used.Contains(d.Id)).GroupBy(d => d.K2).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var g in order.Where(k => !pairs.ContainsKey(k)).GroupBy(k => fileRows[k].K2))
            if (g.Count() == 1 && dbLeft.TryGetValue(g.Key, out var lst) && lst.Count == 1) { pairs[g.First()] = lst[0]; used.Add(lst[0].Id); }

        int add = 0, upd = 0, same = 0, del = 0;
        using var ins = Db.Cmd(c, "INSERT INTO rows(sheet_id,data,ver,updated,updated_by) VALUES(@s,@d,1,@t,@u)", ("@s", sid), ("@d", ""), ("@t", Db.Now()), ("@u", user));
        using var up = Db.Cmd(c, "UPDATE rows SET data=@d, ver=ver+1, updated=@t, updated_by=@u WHERE id=@i", ("@d", ""), ("@t", Db.Now()), ("@u", user), ("@i", 0L));
        using var dl = Db.Cmd(c, "DELETE FROM rows WHERE id=@i", ("@i", 0L));
        foreach (var k in order)
        {
            var row = fileRows[k].Row;
            if (pairs.TryGetValue(k, out var ex))
            {
                bool changed = false;
                for (int i = 0; i < keyOf.Length; i++)
                {
                    string nv = row[i] ?? "", ov = ex.Data[keyOf[i]]?.ToString() ?? "";
                    if (nv != ov) { ex.Data[keyOf[i]] = nv; changed = true; }
                }
                if (changed) { up.Parameters["@d"].Value = ex.Data.ToJsonString(Db.J); up.Parameters["@i"].Value = ex.Id; up.ExecuteNonQuery(); upd++; }
                else same++;
            }
            else
            {
                var o = new JsonObject();
                for (int i = 0; i < keyOf.Length; i++) o[keyOf[i]] = row[i] ?? "";
                ins.Parameters["@d"].Value = o.ToJsonString(Db.J); ins.ExecuteNonQuery(); add++;
            }
        }
        if (deleteMissing)   // false(추가·수정만)이면 파일에 없는 DB 행은 그대로 둠
        {
            foreach (var d in db1.Values)
                if (!used.Contains(d.Id)) { dl.Parameters["@i"].Value = d.Id; dl.ExecuteNonQuery(); del++; }
            foreach (var id in extra) { dl.Parameters["@i"].Value = id; dl.ExecuteNonQuery(); del++; }
        }

        if (!dry) { tx.Commit(); db.Log(user, "엑셀 동기화", "시트 " + sid + " 추가 " + add + " 수정 " + upd + " 삭제 " + del); }
        return new SyncResult(sid, add, upd, same, del, noKey, dup);
    }

    public static string KeyColName(Db db, long sid)
    {
        using var c = db.Open(); var k = db.KeyCol(c, sid);
        return k == null ? "" : db.Columns(c, sid).First(x => x.Key == k).Name;
    }
    public static string KeyColKey(Db db, long sid) { using var c = db.Open(); return db.KeyCol(c, sid) ?? ""; }
    public static void SetKeyCol(Db db, long sid, string key, string user) { using var c = db.Open(); db.SetKeyCol(c, sid, key); db.Log(user, "기준열 지정", "시트 " + sid + " " + key); }
}
