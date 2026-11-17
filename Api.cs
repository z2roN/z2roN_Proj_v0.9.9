using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.Data.Sqlite;

namespace ZSheet;

/// 웹 API. 권한: viewer(조회·받기) < user(추가·수정·삭제·올리기) < admin(시트 삭제·사용자·백업)
public static partial class Api
{
    static readonly ConcurrentDictionary<string, (int Fails, DateTime Until)> fails = new();

    static IResult Err(int code, string msg) => Results.Json(new { error = msg }, statusCode: code);

    // 응답을 먼저 문자열로 완성한 뒤 한 번에 보냄 (직렬화 도중 오류로 본문이 비어 가는 문제 방지: 오류는 500 + 메시지로 전달됨)
    static readonly JsonSerializerOptions WebJ = new(JsonSerializerDefaults.Web) { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    static IResult Js(object o)
    {
        var text = JsonSerializer.Serialize(o, WebJ);
        return Results.Text(text, "application/json; charset=utf-8");
    }

    static async Task<IResult> G(HttpContext ctx, Db db, string minRole, Func<User, Task<IResult>> f)
    {
        var u = db.Auth(ctx);
        if (u == null) return Err(401, "로그인이 필요합니다.");
        int need = minRole == "admin" ? 3 : minRole == "user" ? 2 : 1;
        int have = u.Role == "admin" ? 3 : u.Role == "user" ? 2 : 1;
        if (have < need) return Err(403, "권한이 없습니다.");
        try { return await f(u); }
        catch (InvalidOperationException e) { return Err(400, e.Message); }
        catch (Exception e) { return Err(500, e.Message); }
    }
    static Task<IResult> GS(HttpContext ctx, Db db, string minRole, Func<User, IResult> f) => G(ctx, db, minRole, u => Task.FromResult(f(u)));

    static async Task<JsonObject> Body(HttpContext ctx)
    {
        try { return await ctx.Request.ReadFromJsonAsync<JsonObject>() ?? new JsonObject(); } catch { return new JsonObject(); }
    }
    /// 시트·열을 만들거나 바꿀 수 있는 사람: 관리자, 그리고 (관리자가 허용했을 때) 사용자 권한
    static bool CanStruct(Db db, User u) => u.Role == "admin" || (u.Role == "user" && db.ClientStructure());
    const string NoStruct = "시트·열은 관리자만 만들거나 바꿀 수 있습니다.";

    /// 프록시(Tailscale Funnel, Cloudflare 등)를 거치면 접속이 모두 127.0.0.1 로 보이므로, 전달 헤더가 있으면 '서버 PC 에서 직접'이 아닌 것으로 본다
    static bool ViaProxy(HttpContext ctx) =>
        ctx.Request.Headers.ContainsKey("X-Forwarded-For") || ctx.Request.Headers.ContainsKey("Forwarded") || ctx.Request.Headers.ContainsKey("X-Real-IP") ||
        ctx.Request.Headers.ContainsKey("CF-Connecting-IP") || ctx.Request.Headers.Keys.Any(k => k.StartsWith("Tailscale-", StringComparison.OrdinalIgnoreCase));
    static bool IsLocal(HttpContext ctx) => System.Net.IPAddress.IsLoopback(ctx.Connection.RemoteIpAddress ?? System.Net.IPAddress.None) && !ViaProxy(ctx);
    static string ClientIp(HttpContext ctx)
    {
        var xff = ctx.Request.Headers["X-Forwarded-For"].ToString().Split(',')[0].Trim();
        return xff != "" ? xff : ctx.Connection.RemoteIpAddress?.ToString() ?? "";
    }

    static string S(JsonNode n, string k) => n?[k]?.ToString()?.Trim() ?? "";

    static string CleanRow(Db db, SqliteConnection c, long sheetId, JsonNode data)
    {
        var keys = db.Columns(c, sheetId).Select(x => x.Key).ToHashSet();
        var o = new JsonObject();
        if (data is JsonObject src)
            foreach (var kv in src)
                if (keys.Contains(kv.Key)) o[kv.Key] = kv.Value?.ToString() ?? "";
        return o.ToJsonString(Db.J);
    }

    static bool SheetExists(SqliteConnection c, long id) => Db.Count(c, "SELECT COUNT(*) FROM sheets WHERE id=@i", ("@i", id)) > 0;

    static string SafeName(string n)
    {
        foreach (var ch in Path.GetInvalidFileNameChars()) n = n.Replace(ch, '_');
        return string.IsNullOrWhiteSpace(n) ? "sheet" : n;
    }

    public static void Map(WebApplication app, Db db)
    {
        // 처리 중 예외는 errors.log(프로그램 폴더)에 기록하고 500 + 메시지로 응답
        app.Use(async (ctx, next) =>
        {
            try { await next(); }
            catch (Exception e)
            {
                try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "errors.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + ctx.Request.Method + " " + ctx.Request.Path + "\n" + e + "\n\n"); } catch { }
                if (!ctx.Response.HasStarted)
                {
                    ctx.Response.StatusCode = 500; ctx.Response.ContentType = "application/json; charset=utf-8";
                    await ctx.Response.WriteAsync("{\"error\":" + JsonSerializer.Serialize(e.Message) + "}");
                }
            }
        });
        // 화면
        app.MapGet("/", (HttpContext ctx) =>
        {
            using var s = typeof(Db).Assembly.GetManifestResourceStream("index.html");
            using var rd = new StreamReader(s);
            ctx.Response.Headers.CacheControl = "no-store";
            return Results.Content(rd.ReadToEnd(), "text/html; charset=utf-8");
        });
        MapPurchases(app, db);
        app.MapGet("/api/info", () => Js(new { name = Db.AppName, version = Db.Version, needSetup = db.NeedSetup(), maxClients = Db.MaxClients, clientStructure = db.ClientStructure() }));

        // 처음 관리자 만들기 / 로그인
        app.MapPost("/api/setup", async (HttpContext ctx) =>
        {
            if (!db.NeedSetup()) return Err(400, "이미 설정되었습니다.");
            if (!IsLocal(ctx)) return Err(403, "처음 관리자 계정은 서버 PC 에서 만들어야 합니다.");
            var b = await Body(ctx); string name = S(b, "name"), pw = b["pw"]?.ToString() ?? "";
            if (name == "" || pw.Length < 6) return Err(400, "아이디와 6자 이상 비밀번호를 입력하세요.");
            int id = db.AddUser(name, pw, "admin"); db.Log(name, "관리자 생성", "");
            return Js(new { token = db.NewSession(id), user = new { id, name, role = "admin" } });
        });
        app.MapPost("/api/login", async (HttpContext ctx) =>
        {
            var b = await Body(ctx); string name = S(b, "name"), pw = b["pw"]?.ToString() ?? "";
            string key = (ViaProxy(ctx) ? "proxy" : ctx.Connection.RemoteIpAddress?.ToString() ?? "") + "|" + name.ToLowerInvariant();
            if (fails.TryGetValue(key, out var f) && f.Until > DateTime.Now) return Err(429, "로그인 실패가 많아 5분간 잠겼습니다.");
            var u = db.CheckLogin(name, pw);
            if (u == null)
            {
                var cur = fails.TryGetValue(key, out var p) ? p.Fails + 1 : 1;
                fails[key] = (cur, cur >= 5 ? DateTime.Now.AddMinutes(5) : DateTime.MinValue);
                db.Log(name, "로그인 실패", ClientIp(ctx));
                return Err(401, "아이디 또는 비밀번호가 맞지 않습니다.");
            }
            fails.TryRemove(key, out _);
            db.Log(u.Name, "로그인", ClientIp(ctx));
            return Js(new { token = db.NewSession(u.Id), user = new { id = u.Id, name = u.Name, role = u.Role } });
        });
        app.MapPost("/api/logout", (HttpContext ctx) => { db.EndSession(Db.TokenOf(ctx)); return Js(new { ok = true }); });
        app.MapGet("/api/me", (HttpContext ctx, CancellationToken _ct) => GS(ctx, db, "viewer", u => Js(new { id = u.Id, name = u.Name, role = u.Role })));
        app.MapPost("/api/me/password", (HttpContext ctx, CancellationToken _ct) => G(ctx, db, "viewer", async u =>
        {
            var b = await Body(ctx); string old = b["old"]?.ToString() ?? "", nw = b["new"]?.ToString() ?? "";
            if (nw.Length < 6) return Err(400, "새 비밀번호는 6자 이상이어야 합니다.");
            if (db.CheckLogin(u.Name, old) == null) return Err(400, "현재 비밀번호가 맞지 않습니다.");
            db.SetPassword(u.Id, nw); db.Log(u.Name, "비밀번호 변경", "");
            return Js(new { token = db.NewSession(u.Id) });
        }));

        // 시트 목록 / 만들기 / 이름 변경 / 삭제
        app.MapGet("/api/sheets", (HttpContext ctx, CancellationToken _ct) => GS(ctx, db, "viewer", u =>
        {
            using var c = db.Open();
            var list = new List<object>();
            using var cmd = Db.Cmd(c, "SELECT id,name,created,(SELECT COUNT(*) FROM rows WHERE sheet_id=sheets.id) FROM sheets ORDER BY name");
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(new { id = r.GetInt64(0), name = r.GetString(1), created = r.GetString(2), rows = r.GetInt64(3) });
            return Js(list);
        }));
        app.MapPost("/api/sheets", (HttpContext ctx, CancellationToken _ct) => G(ctx, db, "user", async u =>
        {
            if (!CanStruct(db, u)) return Err(403, NoStruct);
            var b = await Body(ctx); string name = S(b, "name");
            if (name == "") return Err(400, "시트 이름을 입력하세요.");
            var heads = new List<string>();
            if (b["columns"] is JsonArray arr) foreach (var x in arr) { var h = x?.ToString()?.Trim(); if (!string.IsNullOrEmpty(h)) heads.Add(h); }
            if (heads.Count == 0) heads.Add("항목");
            using var c = db.Open(); using var tx = c.BeginTransaction();
            long id = db.CreateSheet(c, name, heads); tx.Commit();
            db.Log(u.Name, "시트 만들기", name);
            return Js(new { id });
        }));
        app.MapPut("/api/sheets/{id:long}", (long id, HttpContext ctx) => G(ctx, db, "user", async u =>
        {
            var b = await Body(ctx); string name = S(b, "name"), keyCol = S(b, "keyCol");
            if (!CanStruct(db, u)) return Err(403, NoStruct);
            if (keyCol != "" && u.Role != "admin") return Err(403, "기준열은 관리자만 지정할 수 있습니다.");
            using var c = db.Open();
            if (keyCol != "")
            {
                if (!db.Columns(c, id).Any(x => x.Key == keyCol)) return Err(400, "없는 열입니다.");
                db.SetKeyCol(c, id, keyCol); db.Log(u.Name, "기준열 지정", "시트 " + id + " " + keyCol);
            }
            if (name != "") { Db.Run(c, "UPDATE sheets SET name=@n WHERE id=@i", ("@n", name), ("@i", id)); db.Log(u.Name, "시트 이름 변경", name); }
            return Js(new { ok = true });
        }));
        app.MapDelete("/api/sheets/{id:long}", (long id, HttpContext ctx) => GS(ctx, db, "admin", u =>
        {
            using var c = db.Open();
            var name = Db.One(c, "SELECT name FROM sheets WHERE id=@i", ("@i", id))?.ToString() ?? "";
            Db.Run(c, "DELETE FROM sheets WHERE id=@i", ("@i", id));
            db.Log(u.Name, "시트 삭제", name);
            return Js(new { ok = true });
        }));

        // 열
        app.MapPost("/api/sheets/{id:long}/columns", (long id, HttpContext ctx) => G(ctx, db, "user", async u =>
        {
            if (!CanStruct(db, u)) return Err(403, NoStruct);
            var b = await Body(ctx); string name = S(b, "name");
            if (name == "") return Err(400, "열 이름을 입력하세요.");
            using var c = db.Open();
            if (!SheetExists(c, id)) return Err(404, "시트가 없습니다.");
            if (db.Columns(c, id).Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))) return Err(400, "같은 이름의 열이 있습니다.");
            var key = db.AddColumn(c, id, name); db.Log(u.Name, "열 추가", name);
            return Js(new { key });
        }));
        app.MapPut("/api/sheets/{id:long}/columns/{key}", (long id, string key, HttpContext ctx) => G(ctx, db, "user", async u =>
        {
            if (!CanStruct(db, u)) return Err(403, NoStruct);
            var b = await Body(ctx); string name = S(b, "name");
            if (name == "") return Err(400, "열 이름을 입력하세요.");
            using var c = db.Open();
            if (db.Columns(c, id).Any(x => x.Key != key && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))) return Err(400, "같은 이름의 열이 있습니다.");
            Db.Run(c, "UPDATE cols SET name=@n WHERE sheet_id=@s AND key=@k", ("@n", name), ("@s", id), ("@k", key));
            db.Log(u.Name, "열 이름 변경", name);
            return Js(new { ok = true });
        }));
        app.MapDelete("/api/sheets/{id:long}/columns/{key}", (long id, string key, HttpContext ctx) => GS(ctx, db, "admin", u =>
        {
            if (!Regex.IsMatch(key, "^c[0-9]+$")) return Err(400, "잘못된 열입니다.");
            using var c = db.Open(); using var tx = c.BeginTransaction();
            Db.Run(c, "DELETE FROM cols WHERE sheet_id=@s AND key=@k", ("@s", id), ("@k", key));
            Db.Run(c, "UPDATE rows SET data=json_remove(data,'$." + key + "') WHERE sheet_id=@s", ("@s", id));
            tx.Commit(); db.Log(u.Name, "열 삭제", key);
            return Js(new { ok = true });
        }));

        // 행 조회
        app.MapGet("/api/sheets/{id:long}/rows", (long id, HttpContext ctx) => GS(ctx, db, "viewer", u =>
        {
            using var c = db.Open();
            if (!SheetExists(c, id)) return Err(404, "시트가 없습니다.");
            var cols = db.Columns(c, id);
            string q = ctx.Request.Query["q"].ToString().Trim();
            string sort = ctx.Request.Query["sort"].ToString();
            bool desc = ctx.Request.Query["desc"].ToString() == "1";
            int offset = int.TryParse(ctx.Request.Query["offset"], out var o1) && o1 > 0 ? o1 : 0;
            int limit = int.TryParse(ctx.Request.Query["limit"], out var l1) ? Math.Clamp(l1, 1, 500) : 50;
            string where = "sheet_id=@s"; var ps = new List<(string, object)> { ("@s", id) };
            if (q != "") { where += " AND data LIKE @q ESCAPE '\\'"; ps.Add(("@q", "%" + q.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%")); }
            string order = "id";
            if (Regex.IsMatch(sort, "^c[0-9]+$") && cols.Any(x => x.Key == sort))
            {
                string d = desc ? " DESC" : "", ex = "json_extract(data,'$." + sort + "')";
                order = "CAST(" + ex + " AS REAL)" + d + ", " + ex + " COLLATE NOCASE" + d + ", id";
            }
            long total = Db.Count(c, "SELECT COUNT(*) FROM rows WHERE " + where, ps.ToArray());
            var rows = new List<object>();
            var ps2 = new List<(string, object)>(ps) { ("@lim", limit), ("@off", offset) };
            using var cmd = Db.Cmd(c, "SELECT id,data,ver,updated,updated_by FROM rows WHERE " + where + " ORDER BY " + order + " LIMIT @lim OFFSET @off", ps2.ToArray());
            using var r = cmd.ExecuteReader();
            while (r.Read()) rows.Add(new { id = r.GetInt64(0), data = JsonNode.Parse(r.GetString(1)), ver = r.GetInt64(2), updated = r.IsDBNull(3) ? "" : r.GetString(3), by = r.IsDBNull(4) ? "" : r.GetString(4) });
            return Js(new { total, keyCol = db.KeyCol(c, id) ?? "", cols = cols.Select(x => new { key = x.Key, name = x.Name }), rows });
        }));

        // 행 추가 / 수정 / 삭제
        app.MapPost("/api/sheets/{id:long}/rows", (long id, HttpContext ctx) => G(ctx, db, "user", async u =>
        {
            var b = await Body(ctx);
            using var c = db.Open();
            if (!SheetExists(c, id)) return Err(404, "시트가 없습니다.");
            Db.Run(c, "INSERT INTO rows(sheet_id,data,ver,updated,updated_by) VALUES(@s,@d,1,@t,@u)", ("@s", id), ("@d", CleanRow(db, c, id, b["data"])), ("@t", Db.Now()), ("@u", u.Name));
            db.Log(u.Name, "행 추가", "시트 " + id);
            return Js(new { id = Db.Count(c, "SELECT last_insert_rowid()") });
        }));
        app.MapPut("/api/sheets/{id:long}/rows/{rid:long}", (long id, long rid, HttpContext ctx) => G(ctx, db, "user", async u =>
        {
            var b = await Body(ctx);
            long ver = long.TryParse(b["ver"]?.ToString(), out var v0) ? v0 : -1;
            using var c = db.Open();
            int n = Db.Run(c, "UPDATE rows SET data=@d, ver=ver+1, updated=@t, updated_by=@u WHERE id=@r AND sheet_id=@s AND ver=@v",
                ("@d", CleanRow(db, c, id, b["data"])), ("@t", Db.Now()), ("@u", u.Name), ("@r", rid), ("@s", id), ("@v", ver));
            if (n == 0)
            {
                if (Db.Count(c, "SELECT COUNT(*) FROM rows WHERE id=@r AND sheet_id=@s", ("@r", rid), ("@s", id)) == 0) return Err(404, "이미 삭제된 행입니다.");
                return Err(409, "다른 사람이 먼저 수정했습니다. 새로 고침한 뒤 다시 수정하세요.");
            }
            db.Log(u.Name, "행 수정", "시트 " + id + " 행 " + rid);
            return Js(new { ok = true });
        }));
        app.MapDelete("/api/sheets/{id:long}/rows/{rid:long}", (long id, long rid, HttpContext ctx) => GS(ctx, db, "user", u =>
        {
            using var c = db.Open();
            Db.Run(c, "DELETE FROM rows WHERE id=@r AND sheet_id=@s", ("@r", rid), ("@s", id));
            db.Log(u.Name, "행 삭제", "시트 " + id + " 행 " + rid);
            return Js(new { ok = true });
        }));

        // 엑셀/CSV 올리기, 엑셀 받기
        app.MapPost("/api/import", (HttpContext ctx, CancellationToken _ct) => G(ctx, db, "user", async u =>
        {
            if (!ctx.Request.HasFormContentType) return Err(400, "파일을 올려주세요.");
            var form = await ctx.Request.ReadFormAsync();
            var file = form.Files.FirstOrDefault();
            if (file == null) return Err(400, "파일이 없습니다.");
            string mode = form["mode"].ToString();            // new | append | replace | sync
            bool dry = form["dry"].ToString() == "1";
            long sid = long.TryParse(form["sheetId"].ToString(), out var s0) ? s0 : 0;
            string name = form["name"].ToString().Trim();
            if (mode == "replace" && u.Role != "admin") return Err(403, "덮어쓰기는 관리자만 할 수 있습니다.");
            bool allowNew = CanStruct(db, u);
            if ((mode == "new" || sid == 0) && !allowNew) return Err(403, NoStruct);
            using var ms = new MemoryStream(); await file.CopyToAsync(ms); ms.Position = 0;
            var t = Importer.Read(ms, file.FileName);
            if (t.Headers.Length == 0) return Err(400, "읽을 데이터가 없습니다. 첫 줄은 머리글이어야 합니다.");
            if (mode == "sync" || mode == "merge")
            {
                if (sid == 0) return Err(400, "올릴 시트를 고르세요.");
                try
                {
                    var sr = Store.Sync(db, t, sid, dry, u.Name, allowNew, deleteMissing: mode == "sync");
                    return Js(new { dry, sheetId = sid, add = sr.Add, update = sr.Update, same = sr.Same, delete = sr.Delete, noKey = sr.NoKey, dup = sr.DupInFile });
                }
                catch (InvalidOperationException ex) { return Err(400, ex.Message); }
            }
            using var c = db.Open(); using var tx = c.BeginTransaction();
            if (mode == "new" || sid == 0 || !SheetExists(c, sid))
            {
                if (name == "") name = Path.GetFileNameWithoutExtension(file.FileName);
                sid = db.CreateSheet(c, name, t.Headers);
            }
            else if (mode == "replace") Db.Run(c, "DELETE FROM rows WHERE sheet_id=@s", ("@s", sid));
            var keyOf = db.MapHeaders(c, sid, t.Headers, allowNew);
            using var ins = Db.Cmd(c, "INSERT INTO rows(sheet_id,data,ver,updated,updated_by) VALUES(@s,@d,1,@t,@u)", ("@s", sid), ("@d", ""), ("@t", Db.Now()), ("@u", u.Name));
            int n = 0;
            foreach (var row in t.Rows)
            {
                var o = new JsonObject();
                for (int i = 0; i < keyOf.Length; i++) o[keyOf[i]] = row[i];
                ins.Parameters["@d"].Value = o.ToJsonString(Db.J);
                ins.ExecuteNonQuery(); n++;
            }
            tx.Commit();
            db.Log(u.Name, "엑셀 올리기(" + mode + ")", file.FileName + " " + n + "행");
            return Js(new { sheetId = sid, imported = n });
        }));
        // 바코드 스캔 조회: 같은 바코드가 여러 줄이어도 관리자가 정한 한 줄만 돌려줌 (클라이언트는 고를 수 없음)
        app.MapGet("/api/sheets/{id:long}/lookup", (long id, HttpContext ctx) => GS(ctx, db, "viewer", u =>
        {
            var r = Product.Lookup(db, id, ctx.Request.Query["code"].ToString());
            if (r.Status == "none") return Err(404, r.Message);
            if (r.Status == "conflict") return Err(409, r.Message);
            using var c = db.Open();
            return Js(new { id = r.Row.Id, count = r.Count, cols = db.Columns(c, id).Select(x => new { key = x.Key, name = x.Name }), data = r.Row.Data });
        }));
        app.MapGet("/api/sheets/{id:long}/export", (long id, HttpContext ctx) => GS(ctx, db, "viewer", u =>
        {
            using var c = db.Open();
            var name = Db.One(c, "SELECT name FROM sheets WHERE id=@i", ("@i", id))?.ToString();
            if (name == null) return Err(404, "시트가 없습니다.");
            var cols = db.Columns(c, id);
            using var wb = new XLWorkbook();
            string sn = SafeName(name).Replace("[", "_").Replace("]", "_"); if (sn.Length > 28) sn = sn.Substring(0, 28);
            var ws = wb.Worksheets.Add(sn);
            for (int i = 0; i < cols.Count; i++) { var h = ws.Cell(1, i + 1); h.Value = cols[i].Name; h.Style.Font.Bold = true; h.Style.Fill.BackgroundColor = XLColor.FromHtml("#EEF3FD"); }
            int r = 2;
            using var cmd = Db.Cmd(c, "SELECT data FROM rows WHERE sheet_id=@s ORDER BY id", ("@s", id));
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
            db.Log(u.Name, "엑셀 받기", name);
            return Results.File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", SafeName(name) + ".xlsx");
        }));

        // 관리자: 사용자 / 접속기록 / 백업
        app.MapGet("/api/users", (HttpContext ctx, CancellationToken _ct) => GS(ctx, db, "admin", u =>
        {
            using var c = db.Open(); var list = new List<object>();
            using var cmd = Db.Cmd(c, "SELECT id,name,role,created FROM users ORDER BY id"); using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(new { id = r.GetInt32(0), name = r.GetString(1), role = r.GetString(2), created = r.GetString(3) });
            return Js(list);
        }));
        app.MapPost("/api/users", (HttpContext ctx, CancellationToken _ct) => G(ctx, db, "admin", async u =>
        {
            var b = await Body(ctx); string name = S(b, "name"), pw = b["pw"]?.ToString() ?? "", role = S(b, "role");
            if (name == "" || pw.Length < 6) return Err(400, "아이디와 6자 이상 비밀번호를 입력하세요.");
            if (role != "admin" && role != "user" && role != "viewer") role = "user";
            if (role != "admin" && db.ClientCount() >= Db.MaxClients) return Err(400, "클라이언트 계정은 최대 " + Db.MaxClients + "개까지 만들 수 있습니다.");
            try { db.AddUser(name, pw, role); } catch (SqliteException) { return Err(400, "이미 있는 아이디입니다."); }
            db.Log(u.Name, "사용자 추가", name + " (" + role + ")");
            return Js(new { ok = true });
        }));
        app.MapPost("/api/users/{uid:int}/password", (int uid, HttpContext ctx) => G(ctx, db, "admin", async u =>
        {
            var b = await Body(ctx); string pw = b["pw"]?.ToString() ?? "";
            if (pw.Length < 6) return Err(400, "6자 이상 비밀번호를 입력하세요.");
            db.SetPassword(uid, pw); db.Log(u.Name, "비밀번호 재설정", "사용자 " + uid);
            return Js(new { ok = true });
        }));
        app.MapDelete("/api/users/{uid:int}", (int uid, HttpContext ctx) => GS(ctx, db, "admin", u =>
        {
            if (uid == u.Id) return Err(400, "자기 자신은 삭제할 수 없습니다.");
            using var c = db.Open();
            Db.Run(c, "DELETE FROM sessions WHERE user_id=@i", ("@i", uid));
            Db.Run(c, "DELETE FROM users WHERE id=@i", ("@i", uid));
            db.Log(u.Name, "사용자 삭제", "사용자 " + uid);
            return Js(new { ok = true });
        }));
        app.MapGet("/api/audit", (HttpContext ctx, CancellationToken _ct) => GS(ctx, db, "admin", u =>
        {
            using var c = db.Open(); var list = new List<object>();
            using var cmd = Db.Cmd(c, "SELECT ts,user,action,detail FROM audit ORDER BY id DESC LIMIT 300"); using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(new { ts = r.GetString(0), user = r.GetString(1), action = r.GetString(2), detail = r.IsDBNull(3) ? "" : r.GetString(3) });
            return Js(list);
        }));
        app.MapGet("/api/backup", (HttpContext ctx, CancellationToken _ct) => GS(ctx, db, "admin", u =>
        {
            var tmp = Path.Combine(Path.GetTempPath(), "z2roN_" + Guid.NewGuid().ToString("N") + ".db");
            using (var c = db.Open()) Db.Run(c, "VACUUM INTO '" + tmp.Replace("'", "''") + "'");
            var bytes = File.ReadAllBytes(tmp); File.Delete(tmp);
            db.Log(u.Name, "백업 받기", "");
            return Results.File(bytes, "application/octet-stream", "z2roN_Proj_backup_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".db");
        }));
    }
}
