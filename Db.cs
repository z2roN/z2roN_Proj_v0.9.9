using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace ZSheet;

public class User { public int Id; public string Name, Role; }

/// SQLite 데이터 파일 1개(data\z2roN_Proj.db)에 시트·열·행·사용자·접속기록을 모두 저장합니다.
public class Db
{
    public const string DefaultName = "z2roN_Proj";
    /// 화면 맨 위에 보이는 프로그램 이름 (관리자가 서버 창·웹에서 바꿀 수 있음, 설정 app_title 에 저장)
    public static string AppName = DefaultName;
    public const string Version = "0.9.9";
    public const int MaxClients = 10;   // 클라이언트(관리자 제외) 계정 최대 수
    public static readonly JsonSerializerOptions J = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    readonly string path;
    public string Dir { get; }

    public Db(string dir)
    {
        Dir = dir; Directory.CreateDirectory(dir);
        path = Path.Combine(dir, "z2roN_Proj.db");
        var oldPath = Path.Combine(dir, "z2roN_Sheet.db");   // 예전 이름의 데이터 파일이 있으면 그대로 이어서 사용
        if (!File.Exists(path) && File.Exists(oldPath)) path = oldPath;
        using var c = Open();
        Run(c, @"
CREATE TABLE IF NOT EXISTS users(id INTEGER PRIMARY KEY, name TEXT UNIQUE, salt TEXT, hash TEXT, role TEXT, created TEXT);
CREATE TABLE IF NOT EXISTS sessions(token TEXT PRIMARY KEY, user_id INTEGER, expires TEXT);
CREATE TABLE IF NOT EXISTS sheets(id INTEGER PRIMARY KEY, name TEXT, created TEXT, nextcol INTEGER DEFAULT 1);
CREATE TABLE IF NOT EXISTS cols(id INTEGER PRIMARY KEY, sheet_id INTEGER REFERENCES sheets(id) ON DELETE CASCADE, key TEXT, name TEXT, ord INTEGER);
CREATE TABLE IF NOT EXISTS rows(id INTEGER PRIMARY KEY, sheet_id INTEGER REFERENCES sheets(id) ON DELETE CASCADE, data TEXT, ver INTEGER DEFAULT 1, updated TEXT, updated_by TEXT);
CREATE TABLE IF NOT EXISTS audit(id INTEGER PRIMARY KEY, ts TEXT, user TEXT, action TEXT, detail TEXT);
CREATE TABLE IF NOT EXISTS settings(name TEXT PRIMARY KEY, val TEXT);
CREATE TABLE IF NOT EXISTS picks(sheet_id INTEGER, code TEXT, unit TEXT, name TEXT, PRIMARY KEY(sheet_id, code));
CREATE TABLE IF NOT EXISTS purchases(id INTEGER PRIMARY KEY, name TEXT, created TEXT, created_by TEXT, status TEXT, sheet_id INTEGER);
CREATE TABLE IF NOT EXISTS pitems(id INTEGER PRIMARY KEY, purchase_id INTEGER, code TEXT, name TEXT, unit TEXT, qty REAL, cost TEXT, scanned REAL DEFAULT 0, is_new INTEGER DEFAULT 0, registered INTEGER DEFAULT 0, extra INTEGER DEFAULT 0, last_by TEXT, last_ts TEXT);
CREATE INDEX IF NOT EXISTS ix_pitems ON pitems(purchase_id, code);
CREATE INDEX IF NOT EXISTS ix_rows_sheet ON rows(sheet_id);
CREATE INDEX IF NOT EXISTS ix_cols_sheet ON cols(sheet_id);");
        // 기준열(동기화 때 같은 행을 찾는 열) 보관: 예전 DB 에는 열이 없으므로 추가
        bool has = false;
        using (var cmd = Cmd(c, "PRAGMA table_info(sheets)")) using (var r = cmd.ExecuteReader()) while (r.Read()) if (r.GetString(1) == "keycol") has = true;
        if (!has) Run(c, "ALTER TABLE sheets ADD COLUMN keycol TEXT");
        bool has2 = false;   // 단위 우선순위(JSON 목록)
        using (var cmd = Cmd(c, "PRAGMA table_info(sheets)")) using (var r = cmd.ExecuteReader()) while (r.Read()) if (r.GetString(1) == "unitorder") has2 = true;
        if (!has2) Run(c, "ALTER TABLE sheets ADD COLUMN unitorder TEXT");
    }

    public SqliteConnection Open()
    {
        var c = new SqliteConnection("Data Source=" + path + ";Pooling=True");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        cmd.ExecuteNonQuery();
        return c;
    }

    public static string Now() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

    public static SqliteCommand Cmd(SqliteConnection c, string sql, params (string, object)[] ps)
    {
        var cmd = c.CreateCommand(); cmd.CommandText = sql;
        foreach (var (k, v) in ps) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
        return cmd;
    }
    public static int Run(SqliteConnection c, string sql, params (string, object)[] ps) { using var cmd = Cmd(c, sql, ps); return cmd.ExecuteNonQuery(); }
    public static object One(SqliteConnection c, string sql, params (string, object)[] ps) { using var cmd = Cmd(c, sql, ps); return cmd.ExecuteScalar(); }
    public static long Count(SqliteConnection c, string sql, params (string, object)[] ps) { var o = One(c, sql, ps); return o == null || o is DBNull ? 0 : Convert.ToInt64(o); }

    // ---------------- 사용자 / 로그인 ----------------
    static string HashPw(string pw, byte[] salt) => Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(pw, salt, 100000, HashAlgorithmName.SHA256, 32));

    public bool NeedSetup() { using var c = Open(); return Count(c, "SELECT COUNT(*) FROM users") == 0; }

    public int AddUser(string name, string pw, string role)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        using var c = Open();
        Run(c, "INSERT INTO users(name,salt,hash,role,created) VALUES(@n,@s,@h,@r,@t)", ("@n", name), ("@s", Convert.ToBase64String(salt)), ("@h", HashPw(pw, salt)), ("@r", role), ("@t", Now()));
        return (int)Count(c, "SELECT id FROM users WHERE name=@n", ("@n", name));
    }

    public void SetPassword(int id, string pw)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        using var c = Open();
        Run(c, "UPDATE users SET salt=@s, hash=@h WHERE id=@i", ("@s", Convert.ToBase64String(salt)), ("@h", HashPw(pw, salt)), ("@i", id));
        Run(c, "DELETE FROM sessions WHERE user_id=@i", ("@i", id));
    }

    public User CheckLogin(string name, string pw)
    {
        using var c = Open();
        using var cmd = Cmd(c, "SELECT id,salt,hash,role FROM users WHERE name=@n", ("@n", name));
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        var salt = Convert.FromBase64String(r.GetString(1));
        var want = Convert.FromBase64String(r.GetString(2));
        var got = Convert.FromBase64String(HashPw(pw, salt));
        if (!CryptographicOperations.FixedTimeEquals(want, got)) return null;
        return new User { Id = r.GetInt32(0), Name = name, Role = r.GetString(3) };
    }

    public string NewSession(int userId)
    {
        var tok = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        using var c = Open();
        Run(c, "DELETE FROM sessions WHERE expires<@n", ("@n", Now()));
        Run(c, "INSERT INTO sessions(token,user_id,expires) VALUES(@t,@u,@e)", ("@t", tok), ("@u", userId), ("@e", DateTime.Now.AddDays(30).ToString("yyyy-MM-dd HH:mm:ss")));
        return tok;
    }

    public void EndSession(string tok) { using var c = Open(); Run(c, "DELETE FROM sessions WHERE token=@t", ("@t", tok)); }

    public static string TokenOf(HttpContext ctx)
    {
        var h = ctx.Request.Headers.Authorization.ToString();
        return h.StartsWith("Bearer ") ? h.Substring(7).Trim() : "";
    }

    public User Auth(HttpContext ctx)
    {
        var tok = TokenOf(ctx);
        if (tok == "") return null;
        using var c = Open();
        using var cmd = Cmd(c, "SELECT u.id,u.name,u.role FROM sessions s JOIN users u ON u.id=s.user_id WHERE s.token=@t AND s.expires>@n", ("@t", tok), ("@n", Now()));
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new User { Id = r.GetInt32(0), Name = r.GetString(1), Role = r.GetString(2) };
    }

    public void Log(string user, string action, string detail)
    {
        try { using var c = Open(); Run(c, "INSERT INTO audit(ts,user,action,detail) VALUES(@t,@u,@a,@d)", ("@t", Now()), ("@u", user), ("@a", action), ("@d", detail)); } catch { }
    }

    // ---------------- 시트 / 열 ----------------
    public string AddColumn(SqliteConnection c, long sheetId, string name)
    {
        long n = Count(c, "SELECT nextcol FROM sheets WHERE id=@s", ("@s", sheetId));
        if (n < 1) n = 1;
        string key = "c" + n;
        long ord = Count(c, "SELECT COALESCE(MAX(ord),0)+1 FROM cols WHERE sheet_id=@s", ("@s", sheetId));
        Run(c, "INSERT INTO cols(sheet_id,key,name,ord) VALUES(@s,@k,@n,@o)", ("@s", sheetId), ("@k", key), ("@n", name), ("@o", ord));
        Run(c, "UPDATE sheets SET nextcol=@n WHERE id=@s", ("@n", n + 1), ("@s", sheetId));
        return key;
    }

    public long CreateSheet(SqliteConnection c, string name, IEnumerable<string> headers)
    {
        Run(c, "INSERT INTO sheets(name,created,nextcol) VALUES(@n,@t,1)", ("@n", name), ("@t", Now()));
        long id = Count(c, "SELECT last_insert_rowid()");
        foreach (var h in headers) AddColumn(c, id, h);
        var bc = Columns(c, id).FirstOrDefault(x => IsBarcodeName(x.Name));
        if (bc.Key != null) SetKeyCol(c, id, bc.Key);
        return id;
    }

    public static bool IsBarcodeName(string n) { n = (n ?? "").Trim().ToLowerInvariant().Replace(" ", ""); return n == "바코드" || n == "barcode" || n == "바코드번호"; }

    /// 시트의 기준열 키(없으면 이름이 '바코드'인 열, 그것도 없으면 null)
    public string KeyCol(SqliteConnection c, long sheetId)
    {
        var cols = Columns(c, sheetId);
        var k = One(c, "SELECT keycol FROM sheets WHERE id=@i", ("@i", sheetId)) as string;
        if (!string.IsNullOrEmpty(k) && cols.Any(x => x.Key == k)) return k;
        return cols.FirstOrDefault(x => IsBarcodeName(x.Name)).Key;
    }
    public void SetKeyCol(SqliteConnection c, long sheetId, string key) => Run(c, "UPDATE sheets SET keycol=@k WHERE id=@i", ("@k", key), ("@i", sheetId));

    /// 프로그램 이름 바꾸기 (비우면 기본 이름). 서버 창·웹 화면 모두 이 이름을 씁니다.
    public static string CleanTitle(string t) { t = (t ?? "").Replace("\r", " ").Replace("\n", " ").Trim(); if (t.Length > 30) t = t.Substring(0, 30); return t == "" ? DefaultName : t; }
    public void SetTitle(string t) { t = CleanTitle(t); SetSetting("app_title", t); AppName = t; }
    public void LoadTitle() { AppName = CleanTitle(GetSetting("app_title", DefaultName)); }
    public string GetSetting(string name, string def = "") { using var c = Open(); return One(c, "SELECT val FROM settings WHERE name=@n", ("@n", name)) as string ?? def; }
    public void SetSetting(string name, string val) { using var c = Open(); Run(c, "INSERT OR REPLACE INTO settings(name,val) VALUES(@n,@v)", ("@n", name), ("@v", val)); }
    /// 클라이언트(사용자 권한)도 시트·열을 만들고 바꿀 수 있는지 (기본 false = 관리자만)
    public bool ClientStructure() => GetSetting("client_structure", "0") == "1";

    public int ClientCount() { using var c = Open(); return (int)Count(c, "SELECT COUNT(*) FROM users WHERE role<>'admin'"); }

    public List<(string Key, string Name)> Columns(SqliteConnection c, long sheetId)
    {
        var list = new List<(string, string)>();
        using var cmd = Cmd(c, "SELECT key,name FROM cols WHERE sheet_id=@s ORDER BY ord,id", ("@s", sheetId));
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add((r.GetString(0), r.GetString(1)));
        return list;
    }

    /// 파일의 머리글을 시트의 열에 연결. 없는 머리글은 새 열로 추가.
    public string[] MapHeaders(SqliteConnection c, long sheetId, string[] headers, bool allowAdd = true)
    {
        var cols = Columns(c, sheetId);
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, n) in cols) if (!map.ContainsKey(n)) map[n] = k;
        var keys = new string[headers.Length];
        for (int i = 0; i < headers.Length; i++)
        {
            if (!map.TryGetValue(headers[i], out var k)) { if (!allowAdd) throw new InvalidOperationException("서버에 없는 열이 있습니다: \"" + headers[i] + "\" (열 추가는 관리자만 할 수 있습니다)"); k = AddColumn(c, sheetId, headers[i]); map[headers[i]] = k; }
            keys[i] = k;
        }
        return keys;
    }

    // ---------------- 자동 백업 ----------------
    public void AutoBackup()
    {
        try
        {
            var dir = Path.Combine(Dir, "backup"); Directory.CreateDirectory(dir);
            var f = Path.Combine(dir, "z2roN_Proj_" + DateTime.Now.ToString("yyyyMMdd") + ".db");
            if (!File.Exists(f)) { using var c = Open(); Run(c, "VACUUM INTO '" + f.Replace("'", "''") + "'"); }
            foreach (var old in Directory.GetFiles(dir, "z2roN_Proj_*.db").OrderByDescending(x => x).Skip(14)) File.Delete(old);
        }
        catch { }
    }
}
