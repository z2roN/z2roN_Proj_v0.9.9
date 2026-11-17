using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace ZSheet;

static class Program
{
    // 실행 옵션: (없음) 관리 창 열기 / --tray 트레이로 조용히 시작 / --nogui 창 없이 서버만 / --restart 이전 실행이 끝나길 기다렸다 시작
    // 콘솔(cmd) 창 없이 더블클릭만으로 실행되는 Windows 프로그램이며, 작업 관리자에서 [작업 끝내기]로 종료할 수 있습니다.
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Contains("--reset-admin")) { ResetAdmin(); return; }   // 관리자 비밀번호 초기화 (서버 실행 여부와 상관없이 가능)
        bool noGui = args.Contains("--nogui"), tray = args.Contains("--tray"), restart = args.Contains("--restart");
        var mutex = new Mutex(false, "Global\\z2roN_Proj_server");
        bool got;
        try { got = mutex.WaitOne(restart ? 20000 : 0); } catch (AbandonedMutexException) { got = true; }   // 작업 관리자로 끝낸 뒤에도 바로 다시 실행 가능
        if (!got)
        {
            if (!noGui) MessageBox.Show("z2roN_Proj 서버가 이미 실행 중입니다.\n작업 표시줄 오른쪽 아래(트레이)의 아이콘을 눌러 여세요.", "z2roN_Proj");
            return;
        }
        try { Run(noGui, tray); }
        finally { try { mutex.ReleaseMutex(); } catch { } }
    }

    static void ResetAdmin()
    {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        if (!Ui.IsWindowsAdmin())
        {
            try { Process.Start(new ProcessStartInfo(Environment.ProcessPath, "--reset-admin") { UseShellExecute = true, Verb = "runas" }); }
            catch { MessageBox.Show("Windows 관리자 승인이 필요합니다. 승인하지 않아 취소되었습니다.", "z2roN_Proj"); }
            return;
        }
        var cfg = Config.Load(); Ui.SetSkin(cfg.Skin); var db = new Db(cfg.FullDataDir); db.LoadTitle();
        Ui.ResetAdminDialog(null, db);
    }

    static void Run(bool noGui, bool tray)
    {
        var cfg = Config.Load();
        var db = new Db(cfg.FullDataDir); db.LoadTitle();
        db.AutoBackup();

        WebApplication app = null; string err = "";
        try
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.ConfigureKestrel(o =>
            {
                o.Limits.MaxRequestBodySize = 300L * 1024 * 1024;
                o.ListenAnyIP(cfg.Port, lo => { if (cfg.Https) lo.UseHttps(cfg.Pfx, cfg.PfxPassword); });
            });
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
            app = builder.Build();
            Api.Map(app, db);
            app.StartAsync().GetAwaiter().GetResult();
        }
        catch (Exception e) { err = e.Message; app = null; }

        _ = Task.Run(async () => { while (true) { await Task.Delay(TimeSpan.FromHours(6)); db.AutoBackup(); } });

        if (noGui)
        {
            if (app == null) return;
            app.WaitForShutdown();
            return;
        }

        string scheme = cfg.Https ? "https" : "http";
        var urls = new List<string> { scheme + "://localhost:" + cfg.Port };
        try { foreach (var a in Dns.GetHostEntry(Dns.GetHostName()).AddressList) if (a.AddressFamily == AddressFamily.InterNetwork) urls.Add(scheme + "://" + a + ":" + cfg.Port); } catch { }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new ServerForm(db, cfg, urls.ToArray(), err, tray));
        try { app?.StopAsync().GetAwaiter().GetResult(); } catch { }
    }
}
