using System.Globalization;
using Microsoft.AspNetCore.Http;

namespace ZSheet;

/// 매입(입고 검수) API. 권한: 조회=viewer, 올리기·스캔·수정=user, 삭제=admin
public static partial class Api
{
    static void MapPurchases(WebApplication app, Db db)
    {
        // 프로그램 이름(화면 맨 위 제목) 바꾸기: 관리자만. 서버 창·웹·폰 화면 모두에 적용
        app.MapPut("/api/title", (HttpContext ctx, CancellationToken _ct) => G(ctx, db, "admin", async u =>
        {
            var b = await Body(ctx); db.SetTitle(S(b, "title")); db.Log(u.Name, "프로그램 이름 변경", Db.AppName);
            return Js(new { name = Db.AppName });
        }));
        app.MapGet("/api/purchases", (HttpContext ctx, CancellationToken _ct) => GS(ctx, db, "viewer", u => Js(Purchase.List(db))));

        app.MapPost("/api/purchases", (HttpContext ctx, CancellationToken _ct) => G(ctx, db, "user", async u =>
        {
            if (!ctx.Request.HasFormContentType) return Err(400, "파일을 올려주세요.");
            var form = await ctx.Request.ReadFormAsync();
            var file = form.Files.FirstOrDefault();
            if (file == null) return Err(400, "파일이 없습니다.");
            long sid = long.TryParse(form["sheetId"].ToString(), out var s0) ? s0 : 0;
            bool dry = form["dry"].ToString() == "1", reg = form["register"].ToString() == "1";
            string name = form["name"].ToString().Trim();
            if (name == "") return Err(400, "매입 이름을 입력하세요.");
            name = System.Text.RegularExpressions.Regex.Replace(name, @"_\d{8}$", "") + "_" + DateTime.Now.ToString("yyyyMMdd");   // 이름 뒤에 작업 날짜를 항상 붙임
            bool ok; using (var c = db.Open()) ok = SheetExists(c, sid);
            if (!ok) return Err(400, "상품 시트를 고르세요.");
            using var ms = new MemoryStream(); await file.CopyToAsync(ms); ms.Position = 0;
            var t = Importer.Read(ms, file.FileName);
            if (t.Headers.Length == 0) return Err(400, "읽을 데이터가 없습니다. 첫 줄은 머리글이어야 합니다.");
            if (dry)
            {
                var p = Purchase.Plan(db, sid, t);
                return Js(new { lines = p.Items.Count, newCount = p.New, existing = p.Existing, merged = p.Merged, noCode = p.NoCode });
            }
            var r = Purchase.Create(db, sid, name, t, reg, u.Name);
            return Js(new { id = r.Id, lines = r.Plan.Items.Count, newCount = r.Plan.New, registered = r.Registered });
        }));

        app.MapGet("/api/purchases/{id:long}", (long id, HttpContext ctx) => GS(ctx, db, "viewer", u =>
        {
            var h = Purchase.Head(db, id);
            if (h == null) return Err(404, "매입 건이 없습니다.");
            return Js(new { head = h, items = Purchase.Items(db, id) });
        }));

        app.MapPost("/api/purchases/{id:long}/scan", (long id, HttpContext ctx) => G(ctx, db, "user", async u =>
        {
            var b = await Body(ctx);
            double d = double.TryParse(b["delta"]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var dd) ? dd : 1;
            if (d == 0) d = 1;
            if (d > 100000) d = 100000; if (d < -100000) d = -100000;
            var r = Purchase.Scan(db, id, S(b, "code"), d, u.Name);
            return Js(new { item = r.Item, candidates = r.Candidates });
        }));

        app.MapPut("/api/purchase-items/{id:long}", (long id, HttpContext ctx) => G(ctx, db, "user", async u =>
        {
            var b = await Body(ctx);
            if (!double.TryParse(b["scanned"]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return Err(400, "수량이 올바르지 않습니다.");
            return Js(new { item = Purchase.SetScanned(db, id, v, u.Name) });
        }));
        app.MapDelete("/api/purchase-items/{id:long}", (long id, HttpContext ctx) => GS(ctx, db, "user", u => { Purchase.DeleteExtra(db, id, u.Name); return Js(new { ok = true }); }));

        app.MapPost("/api/purchases/{id:long}/register", (long id, HttpContext ctx) => G(ctx, db, "user", async u =>
        {
            var b = await Body(ctx);
            bool upc = (b["updateCost"]?.ToString() ?? "").ToLowerInvariant() == "true";
            var r = Purchase.Register(db, id, upc, u.Name);
            return Js(new { added = r.Added, updated = r.Updated });
        }));
        app.MapPost("/api/purchases/{id:long}/status", (long id, HttpContext ctx) => G(ctx, db, "user", async u =>
        {
            var b = await Body(ctx); Purchase.SetStatus(db, id, S(b, "status"), u.Name);
            return Js(new { ok = true });
        }));
        app.MapDelete("/api/purchases/{id:long}", (long id, HttpContext ctx) => GS(ctx, db, "admin", u => { Purchase.Delete(db, id, u.Name); return Js(new { ok = true }); }));
        app.MapGet("/api/purchases/{id:long}/export", (long id, HttpContext ctx) => GS(ctx, db, "viewer", u =>
        {
            var bytes = Purchase.Export(db, id, out var fn);
            return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fn);
        }));
    }
}
