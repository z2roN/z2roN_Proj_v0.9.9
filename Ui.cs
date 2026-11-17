namespace ZSheet;

static class Ui
{
    // 스킨(색 모음). SetSkin 으로 바꾸고 Apply 로 열려 있는 화면에 다시 입힘
    public static Color Blue, BlueDark, BlueSoft, BlueLine, Danger, Green, Orange, Purple, Head, HeadText, HeadSub, Bg, BtnText, BtnBorder;
    public static int Skin;
    public static readonly string[] SkinNames = { "파랑", "흑백", "파스텔" };
    public static readonly Color[] SkinSwatch = { Color.FromArgb(43, 99, 217), Color.FromArgb(50, 50, 50), Color.FromArgb(160, 190, 245) };
    static Ui() { SetSkin(0); }

    public static void SetSkin(int s)
    {
        Skin = s < 0 || s > 2 ? 0 : s;
        switch (Skin)
        {
            case 1:   // 흑백 (바탕·막대·표만 흑백, 버튼 색은 파랑 스킨과 같게 유지)
                Blue = Color.FromArgb(43, 99, 217); BlueDark = Color.FromArgb(40, 40, 40); BlueSoft = Color.FromArgb(238, 238, 238); BlueLine = Color.FromArgb(214, 214, 214);
                Green = Color.FromArgb(46, 158, 91); Orange = Color.FromArgb(232, 137, 12); Purple = Color.FromArgb(123, 79, 209); Danger = Color.FromArgb(210, 62, 62);
                Head = Color.FromArgb(38, 38, 38); HeadText = Color.White; HeadSub = Color.FromArgb(205, 205, 205); Bg = Color.FromArgb(246, 246, 246); BtnText = Color.White; BtnBorder = Color.FromArgb(190, 190, 190); break;
            case 2:   // 파스텔
                Blue = Color.FromArgb(140, 178, 245); BlueDark = Color.FromArgb(60, 80, 140); BlueSoft = Color.FromArgb(238, 243, 255); BlueLine = Color.FromArgb(212, 224, 250);
                Green = Color.FromArgb(140, 212, 176); Orange = Color.FromArgb(250, 200, 130); Purple = Color.FromArgb(198, 176, 240); Danger = Color.FromArgb(200, 70, 90);
                Head = Color.FromArgb(184, 208, 250); HeadText = Color.FromArgb(35, 55, 110); HeadSub = Color.FromArgb(70, 90, 140); Bg = Color.FromArgb(248, 250, 255); BtnText = Color.FromArgb(35, 50, 95); BtnBorder = Color.FromArgb(196, 210, 245); break;
            default:  // 파랑
                Blue = Color.FromArgb(43, 99, 217); BlueDark = Color.FromArgb(27, 63, 156); BlueSoft = Color.FromArgb(238, 243, 253); BlueLine = Color.FromArgb(217, 229, 251);
                Green = Color.FromArgb(46, 158, 91); Orange = Color.FromArgb(232, 137, 12); Purple = Color.FromArgb(123, 79, 209); Danger = Color.FromArgb(210, 62, 62);
                Head = Blue; HeadText = Color.White; HeadSub = Color.FromArgb(210, 225, 255); Bg = Color.FromArgb(244, 247, 253); BtnText = Color.White; BtnBorder = Color.FromArgb(180, 200, 240); break;
        }
    }

    // 버튼 색 규칙 (같은 종류의 일은 같은 색)
    //  kind 1 파랑 = 만들기·올리기·저장   3 초록 = 조회·받기·확인   4 주황 = 설정·도구   5 보라 = 상품(바코드) 전용
    //  kind 2 빨강 글자 = 삭제·종료·초기화   0 흰색 = 이동·열기·새로고침
    public static Button Btn(string text, EventHandler click, int kind = 0, int width = 0)
    {
        var b = new RoundButton { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlatStyle = FlatStyle.Flat, Height = 34, Padding = new Padding(6, 0, 6, 0), Margin = new Padding(3), Cursor = Cursors.Hand, UseVisualStyleBackColor = false, FixedW = width };
        StyleBtn(b, kind);
        if (click != null) b.Click += click;
        return b;
    }

    public static void StyleBtn(Button b, int kind)
    {
        b.Tag = kind; b.FlatAppearance.BorderSize = 1;
        if (kind == 1 || kind == 3 || kind == 4 || kind == 5)
        {
            Color fill = kind switch { 1 => Blue, 3 => Green, 4 => Orange, _ => Purple };
            b.BackColor = fill; b.ForeColor = BtnText; b.FlatAppearance.BorderColor = fill; b.FlatAppearance.MouseOverBackColor = ControlPaint.Dark(fill, 0.06f);
        }
        else { b.BackColor = Color.White; b.ForeColor = kind == 2 ? Danger : BlueDark; b.FlatAppearance.BorderColor = BtnBorder; b.FlatAppearance.MouseOverBackColor = BlueLine; }
        b.Invalidate();
    }

    /// 열려 있는 화면(컨트롤 트리)에 현재 스킨 색을 다시 입힘. Tag: "soft" 연한 바탕, "head" 제목 글자, "headbar" 맨 위 막대, "headtext"/"headsub" 막대 위 글자
    public static void Apply(Control c)
    {
        if (c is Button b && b.Tag is int k) StyleBtn(b, k);
        else if (c is DataGridView g) StyleGrid(g);
        else if (c.Tag is string t)
        {
            switch (t)
            {
                case "soft": c.BackColor = BlueSoft; break;
                case "head": c.ForeColor = BlueDark; break;
                case "headbar": c.BackColor = Head; break;
                case "headtext": c.ForeColor = HeadText; if (c is LinkLabel l1) l1.LinkColor = l1.ActiveLinkColor = l1.VisitedLinkColor = HeadText; break;
                case "headsub": c.ForeColor = HeadSub; break;
            }
        }
        foreach (Control ch in c.Controls) Apply(ch);
    }

    public static void StyleGrid(DataGridView g)
    {
        g.BackgroundColor = Color.White; g.BorderStyle = BorderStyle.None;
        g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal; g.GridColor = Color.FromArgb(230, 236, 247);
        g.EnableHeadersVisualStyles = false;
        g.ColumnHeadersDefaultCellStyle.BackColor = BlueSoft; g.ColumnHeadersDefaultCellStyle.ForeColor = BlueDark;
        g.ColumnHeadersDefaultCellStyle.Font = new Font("Malgun Gothic", 9.5f, FontStyle.Bold);
        g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing; g.ColumnHeadersHeight = 32;
        g.DefaultCellStyle.SelectionBackColor = BlueLine; g.DefaultCellStyle.SelectionForeColor = Color.Black;
        g.RowHeadersVisible = true; g.RowHeadersWidth = 28; g.RowTemplate.Height = 26;
        g.AllowUserToAddRows = false; g.AllowUserToDeleteRows = false; g.AllowUserToResizeRows = false;
        g.SelectionMode = DataGridViewSelectionMode.RowHeaderSelect;
    }

    /// 한 줄 입력창. 취소하면 null
    public static string Ask(IWin32Window owner, string title, string label, string def = "", bool pw = false)
    {
        using var f = new Form { Text = title, Width = 420, Height = 170, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false, Font = new Font("Malgun Gothic", 9.5f) };
        f.Controls.Add(new Label { Left = 14, Top = 14, Width = 380, Text = label });
        var t = new TextBox { Left = 14, Top = 40, Width = 376, Text = def, UseSystemPasswordChar = pw };
        f.Controls.Add(t);
        var ok = Btn("확인", null, 1); ok.Left = 224; ok.Top = 84; ok.DialogResult = DialogResult.OK;
        var cancel = Btn("취소", null); cancel.Left = 310; cancel.Top = 84; cancel.DialogResult = DialogResult.Cancel;
        f.Controls.Add(ok); f.Controls.Add(cancel); f.AcceptButton = ok; f.CancelButton = cancel;
        return f.ShowDialog(owner) == DialogResult.OK ? t.Text : null;
    }

    public static Icon MakeIcon()
    {
        try
        {
            using var st = typeof(Ui).Assembly.GetManifestResourceStream("z2roN_Proj.ico");
            if (st != null) return new Icon(st);
        }
        catch { }
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
            using var br = new SolidBrush(Blue); g.FillEllipse(br, 1, 1, 30, 30);
            using var f = new Font("Segoe UI", 18, FontStyle.Bold, GraphicsUnit.Pixel);
            g.DrawString("z", f, Brushes.White, 8, 4);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    public static string GenPassword()
    {
        const string a = "abcdefghjkmnpqrstuvwxyz23456789";
        var b = System.Security.Cryptography.RandomNumberGenerator.GetBytes(8);
        return new string(b.Select(x => a[x % a.Length]).ToArray());
    }

    /// 자체 서명 HTTPS 인증서(.pfx) 만들기 (브라우저에 경고가 나옴)
    public static void MakeSelfSigned(string host, string pw, string path)
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var req = new System.Security.Cryptography.X509Certificates.CertificateRequest("CN=" + host, rsa, System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        var san = new System.Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder(); san.AddDnsName(host); san.AddDnsName("localhost");
        req.CertificateExtensions.Add(san.Build());
        using var cert = req.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(5));
        File.WriteAllBytes(path, cert.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pfx, pw));
    }

    static Form Dlg(string title, int w, int h) => new Form { Text = title, Width = w, Height = h, StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, TopMost = true, Font = new Font("Malgun Gothic", 9.5f) };

    // ---- 로그인·계정 창 공통 모양: 위에 스킨 색 머리띠(아이콘·이름·버전·안내), 아래에 입력 칸과 둥근 버튼. 스킨(파랑·흑백·파스텔)을 따름 ----
    static Form AuthDlg(string title, string purpose, int bodyH, out Panel body, Color? headColor = null)
    {
        var f = new Form { Text = title, ClientSize = new Size(420, 96 + bodyH), StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, TopMost = true, Font = new Font("Malgun Gothic", 9.5f), BackColor = Bg };
        try { f.Icon = MakeIcon(); } catch { }
        var head = new Panel { Dock = DockStyle.Top, Height = 96, BackColor = headColor ?? Head };
        var pic = new PictureBox { Left = 20, Top = 18, Width = 58, Height = 58, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent };
        try { pic.Image = new Icon(MakeIcon(), new Size(48, 48)).ToBitmap(); } catch { }
        head.Controls.Add(pic);
        head.Controls.Add(new Label { Left = 90, Top = 18, AutoSize = true, Text = Db.AppName, ForeColor = HeadText, BackColor = Color.Transparent, Font = new Font("Malgun Gothic", 15f, FontStyle.Bold) });
        head.Controls.Add(new Label { Left = 92, Top = 52, AutoSize = true, MaximumSize = new Size(310, 0), Text = "v" + Db.Version + "  ·  " + purpose, ForeColor = HeadSub, BackColor = Color.Transparent });
        body = new Panel { Dock = DockStyle.Fill, BackColor = Bg };
        f.Controls.Add(body); f.Controls.Add(head);
        return f;
    }
    static TextBox AuthField(Panel b, string label, ref int y, bool pw = false, string text = "")
    {
        b.Controls.Add(new Label { Left = 20, Top = y, AutoSize = true, Text = label, ForeColor = BlueDark, BackColor = Color.Transparent, Font = new Font("Malgun Gothic", 9f, FontStyle.Bold) });
        var t = new TextBox { Left = 20, Top = y + 21, Width = 380, Font = new Font("Malgun Gothic", 10.5f), UseSystemPasswordChar = pw, Text = text };
        b.Controls.Add(t); y += 64; return t;
    }
    static void AuthNote(Panel b, string text, ref int y, Color? c = null)
    {
        var l = new Label { Left = 20, Top = y, Width = 380, AutoSize = false, Text = text, ForeColor = c ?? Color.FromArgb(90, 100, 120), BackColor = Color.Transparent }; l.Height = 40; b.Controls.Add(l); y += 46;
    }
    static void AuthButtons(Panel b, int y, Button ok, Button cancel)
    {
        ok.Top = y; cancel.Top = y; cancel.Left = 400 - cancel.Width; ok.Left = cancel.Left - 10 - ok.Width; cancel.DialogResult = DialogResult.Cancel;
        b.Controls.Add(ok); b.Controls.Add(cancel);
    }

    /// 서버 관리 창 열기 전 관리자 확인. 맞으면 true
    public static bool LoginDialog(IWin32Window owner, Db db)
    {
        using var f = AuthDlg("관리자 확인 — " + Db.AppName, "관리자 로그인", 212, out var body);
        int y = 18; var n = AuthField(body, "관리자 아이디", ref y); var p = AuthField(body, "비밀번호", ref y, true);
        var ok = Btn("로그인", null, 1, 110); var cc = Btn("취소", null, 0, 90); AuthButtons(body, y + 2, ok, cc);
        bool good = false;
        var forgot = new LinkLabel { Left = 20, Top = y + 10, AutoSize = true, Text = "비밀번호를 잊으셨나요?", LinkColor = Blue, ActiveLinkColor = BlueDark, BackColor = Color.Transparent };
        forgot.LinkClicked += (_, _) => ResetFlow(f, db);
        body.Controls.Add(forgot);
        ok.Click += (_, _) =>
        {
            var u = db.CheckLogin(n.Text.Trim(), p.Text);
            if (u != null && u.Role == "admin") { good = true; db.Log(u.Name, "서버 관리 화면 열기", ""); f.DialogResult = DialogResult.OK; }
            else { MessageBox.Show(f, "관리자 아이디 또는 비밀번호가 맞지 않습니다.", Db.AppName); p.Clear(); p.Focus(); }
        };
        f.AcceptButton = ok; f.CancelButton = cc;
        f.ShowDialog(owner);
        return good;
    }

    /// 전체 데이터 초기화 확인: 관리자 아이디·비밀번호가 맞고 확인 글자("초기화")를 입력해야 통과. 통과하면 관리자 이름을 돌려줌(아니면 null)
    public static string WipeDialog(IWin32Window owner, Db db, int sheets, long rows)
    {
        using var f = AuthDlg("전체 데이터 초기화 — 관리자 확인", "전체 데이터 초기화", 322, out var body, Danger);
        int y = 14;
        AuthNote(body, "시트 " + sheets.ToString("N0") + "개 / 행 " + rows.ToString("N0") + "건이 모두 삭제됩니다.\n(계정과 설정은 유지, 삭제 직전의 DB 는 backup 폴더에 자동 복사)", ref y, Danger);
        var n = AuthField(body, "관리자 아이디", ref y, false, "admin"); var p = AuthField(body, "비밀번호", ref y, true); var w = AuthField(body, "확인을 위해 [초기화] 라고 입력하세요", ref y);
        var ok = Btn("전체 삭제", null, 2, 120); var cc = Btn("취소", null, 0, 90); AuthButtons(body, y + 2, ok, cc);
        string who = null;
        ok.Click += (_, _) =>
        {
            var u = db.CheckLogin(n.Text.Trim(), p.Text);
            if (u == null || u.Role != "admin") { MessageBox.Show(f, "관리자 아이디 또는 비밀번호가 맞지 않습니다.", Db.AppName); p.Clear(); p.Focus(); return; }
            if (w.Text.Trim() != "초기화") { MessageBox.Show(f, "확인 글자 [초기화] 를 정확히 입력하세요.", Db.AppName); w.Focus(); return; }
            who = u.Name; f.DialogResult = DialogResult.OK;
        };
        f.CancelButton = cc;
        f.ShowDialog(owner);
        return who;
    }

    // ---- 바코드 자리수 검사 (서버 창, 이 PC 에서만 설정) ----
    /// 스캔한 바코드 자리수가 지정과 다르면 경고음을 내고 설명 글자를 돌려줌 (맞거나 검사를 안 쓰면 "")
    public static string DigitWarn(Config cfg, string code)
    {
        if (!cfg.DigitOn) return "";
        var l = cfg.DigitList();
        if (l.Count == 0 || l.Contains(code.Length)) return "";
        Alarm();
        return code.Length + "자리 — 지정 자리수(" + string.Join(", ", l) + ")와 다릅니다";
    }
    /// 경고음 3번 (소리 크기는 PC 볼륨을 따름)
    public static void Alarm()
    {
        Task.Run(() =>
        {
            try { for (int i = 0; i < 3; i++) Console.Beep(i % 2 == 0 ? 1000 : 650, 220); }
            catch { try { System.Media.SystemSounds.Hand.Play(); } catch { } }
        });
    }

    // ---- 관리자 비밀번호 초기화 (서버 PC 에서만, Windows 관리자 권한 확인 후) ----
    public static bool IsWindowsAdmin()
    {
        try { return new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator); }
        catch { return false; }
    }

    /// 이 PC 의 Windows 관리자(UAC 승인)만 비밀번호를 새로 정할 수 있음. 웹·외부에서는 불가능.
    public static void ResetFlow(IWin32Window owner, Db db)
    {
        if (IsWindowsAdmin()) { ResetAdminDialog(owner, db); return; }
        if (MessageBox.Show(owner, "관리자 비밀번호를 새로 정합니다.\n\n이 PC 의 Windows 관리자 확인(승인 창)이 필요합니다.\n[예]를 누른 뒤 승인 창에서 [예]를 누르면 초기화 창이 열립니다.\n\n계속할까요?", Db.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath, "--reset-admin") { UseShellExecute = true, Verb = "runas" }); }
        catch { MessageBox.Show(owner, "Windows 관리자 승인이 없어 취소되었습니다.", Db.AppName); }
    }

    public static bool ResetAdminDialog(IWin32Window owner, Db db)
    {
        var admins = Store.Users(db).Where(x => x.Role == "admin").ToList();
        if (admins.Count == 0) { MessageBox.Show(owner, "관리자 계정이 없습니다. 서버를 다시 실행해 처음 시작 화면에서 만드세요.", Db.AppName); return false; }
        using var f = AuthDlg("관리자 비밀번호 초기화 — " + Db.AppName, "관리자 비밀번호 초기화", 322, out var body);
        int y = 14;
        AuthNote(body, "이 PC 에서 Windows 관리자 확인을 마쳤습니다. 관리자 계정을 고르고 새 비밀번호를 정하세요.", ref y);
        body.Controls.Add(new Label { Left = 20, Top = y, AutoSize = true, Text = "관리자 아이디", ForeColor = BlueDark, BackColor = Color.Transparent, Font = new Font("Malgun Gothic", 9f, FontStyle.Bold) });
        var cb = new ComboBox { Left = 20, Top = y + 21, Width = 380, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Malgun Gothic", 10.5f) }; foreach (var a in admins) cb.Items.Add(a.Name); cb.SelectedIndex = 0; body.Controls.Add(cb); y += 64;
        var p = AuthField(body, "새 비밀번호 (6자 이상)", ref y, true); var p2 = AuthField(body, "새 비밀번호 확인", ref y, true);
        var ok = Btn("비밀번호 바꾸기", null, 1, 150); var cc = Btn("취소", null, 0, 90); AuthButtons(body, y + 2, ok, cc);
        bool good = false;
        ok.Click += (_, _) =>
        {
            if (p.Text.Length < 6) { MessageBox.Show(f, "6자 이상 비밀번호를 입력하세요."); return; }
            if (p.Text != p2.Text) { MessageBox.Show(f, "비밀번호 확인이 다릅니다."); return; }
            var u = admins[cb.SelectedIndex];
            db.SetPassword(u.Id, p.Text); db.Log("서버 PC", "관리자 비밀번호 초기화", u.Name);
            good = true; MessageBox.Show(f, "\"" + u.Name + "\" 의 비밀번호를 바꿨습니다. 로그인되어 있던 기기는 로그아웃됩니다."); f.DialogResult = DialogResult.OK;
        };
        f.AcceptButton = ok; f.CancelButton = cc;
        f.ShowDialog(owner);
        return good;
    }

    /// 처음 실행: 관리자 아이디·암호 만들기
    public static bool SetupAdminDialog(IWin32Window owner, Db db)
    {
        using var f = AuthDlg("처음 시작: 관리자 계정 만들기", "처음 시작 — 관리자 계정 만들기", 322, out var body);
        int y = 14;
        AuthNote(body, "서버 관리 화면과 클라이언트 관리자 화면에서 쓸 관리자 계정을 만듭니다.", ref y);
        var n = AuthField(body, "관리자 아이디", ref y); var p = AuthField(body, "비밀번호 (6자 이상)", ref y, true); var p2 = AuthField(body, "비밀번호 확인", ref y, true);
        var ok = Btn("만들기", null, 1, 110); var cc = Btn("취소", null, 0, 90); AuthButtons(body, y + 2, ok, cc);
        bool good = false;
        ok.Click += (_, _) =>
        {
            if (n.Text.Trim() == "" || p.Text.Length < 6) { MessageBox.Show(f, "아이디와 6자 이상 비밀번호를 입력하세요."); return; }
            if (p.Text != p2.Text) { MessageBox.Show(f, "비밀번호 확인이 다릅니다."); return; }
            try { db.AddUser(n.Text.Trim(), p.Text, "admin"); db.Log(n.Text.Trim(), "관리자 생성", "서버 화면"); good = true; f.DialogResult = DialogResult.OK; }
            catch (Exception ex) { MessageBox.Show(f, ex.Message); }
        };
        f.AcceptButton = ok; f.CancelButton = cc;
        f.ShowDialog(owner);
        return good;
    }
}

/// 모서리가 둥근 버튼 (색은 StyleBtn 이 입힘). FixedW > 0 이면 그 폭으로 고정해 줄을 맞춤
class RoundButton : Button
{
    public int FixedW, MinW = 92, Radius = 9;
    bool hover, down;
    public RoundButton() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }
    public override Size GetPreferredSize(Size proposedSize)
    {
        var sz = TextRenderer.MeasureText(Text, Font);
        int w = FixedW > 0 ? FixedW : Math.Max(MinW, sz.Width + 26);
        return new Size(w, Math.Max(34, Height));
    }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var pb = new SolidBrush(Parent?.BackColor ?? SystemColors.Control)) g.FillRectangle(pb, ClientRectangle);
        var r = new Rectangle(0, 0, Width - 1, Height - 1); int d = Radius * 2;
        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90); path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90); path.CloseFigure();
        Color fill = !Enabled ? Color.FromArgb(235, 238, 243) : down ? ControlPaint.Dark(FlatAppearance.MouseOverBackColor.IsEmpty ? BackColor : FlatAppearance.MouseOverBackColor, 0.05f) : hover && !FlatAppearance.MouseOverBackColor.IsEmpty ? FlatAppearance.MouseOverBackColor : BackColor;
        using (var br = new SolidBrush(fill)) g.FillPath(br, path);
        using (var pen = new Pen(Enabled ? FlatAppearance.BorderColor : Color.FromArgb(210, 214, 222))) g.DrawPath(pen, path);
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, Enabled ? ForeColor : Color.Gray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }
}
