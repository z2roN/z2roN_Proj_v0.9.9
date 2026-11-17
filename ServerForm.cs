using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Net;
using System.Net.Sockets;

namespace ZSheet;

class SheetItem { public long Id; public string Name; public long Rows; public override string ToString() => Name + " (" + Rows + ")"; }

/// 서버 PC 에서 보는 관리자 화면: 데이터 조회·수정·추가·삭제, 사용자, 기록, 서버 정보
class ServerForm : Form
{
    readonly Db db; readonly Config cfg; readonly string startError; readonly bool https; readonly int port;   // https, port = 지금 실행 중인 값
    string dataDir => cfg.FullDataDir; string cfgPath => Config.FilePath;
    readonly string[] urls;
    bool unlocked;
    readonly TabControl tabs = new() { Dock = DockStyle.Fill, Font = new Font("Malgun Gothic", 10f) };
    readonly NotifyIcon tray = new();
    Label lblName;   // 맨 위 프로그램 이름 표시 (이름을 바꾸면 갱신)
    readonly List<Button> swatches = new();   // 스킨 선택 색 네모
    public bool ReallyExit;
    bool startHidden, balloonShown;

    // 데이터 탭
    readonly ComboBox cbSheet = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    readonly TextBox txtQ = new() { Width = 200 };
    readonly TextBox txtScan = new() { Width = 200 };
    readonly DataGridView grid = new() { Dock = DockStyle.Fill };
    readonly Label lblPage = new() { AutoSize = true, Margin = new Padding(8, 9, 8, 0) };
    bool loading, loadingSheets;
    long sid, total; int off; const int Lim = 200; string sort = ""; bool desc;
    List<(string Key, string Name)> cols = new(); List<RowRec> rows = new();
    // 사용자 / 기록
    readonly DataGridView gUsers = new() { Dock = DockStyle.Fill, ReadOnly = true };
    readonly Label lblClients = new() { AutoSize = true, Margin = new Padding(14, 9, 0, 0), ForeColor = Ui.BlueDark };
    readonly DataGridView gAudit = new() { Dock = DockStyle.Fill, ReadOnly = true };

    public ServerForm(Db db, Config cfg, string[] urls, string startError, bool startHidden)
    {
        Ui.SetSkin(cfg.Skin);
        this.db = db; this.cfg = cfg; this.https = cfg.Https; this.port = cfg.Port; this.urls = urls; this.startError = startError; this.startHidden = startHidden;
        Text = Db.AppName + " 서버 v" + Db.Version; Width = 1180; Height = 760; MinimumSize = new Size(820, 520);
        StartPosition = FormStartPosition.CenterScreen; Font = new Font("Malgun Gothic", 9.5f); BackColor = Color.FromArgb(244, 247, 253);
        var ico = Ui.MakeIcon(); Icon = ico;
        Load += (_, _) => HookDrop(this);

        // 맨 위 막대: [아이콘]  이름 v버전 / ● 실행 중 접속주소들 ........ 스킨
        var head = new TableLayoutPanel { Dock = DockStyle.Top, Height = 76, ColumnCount = 3, RowCount = 1, Padding = new Padding(14, 8, 14, 8), Tag = "headbar", BackColor = Ui.Head };
        head.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60)); head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var pic = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 0, 10, 0) };
        try { pic.Image = new Icon(ico, new Size(48, 48)).ToBitmap(); } catch { pic.Image = ico.ToBitmap(); }
        head.Controls.Add(pic, 0, 0);
        var mid = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) };
        var line1 = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        lblName = new Label { Text = Db.AppName, Tag = "headtext", ForeColor = Ui.HeadText, Font = new Font("Malgun Gothic", 15f, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 0, 0, 0) };
        line1.Controls.Add(lblName);
        line1.Controls.Add(new Label { Text = "v" + Db.Version, Tag = "headsub", ForeColor = Ui.HeadSub, Font = new Font("Malgun Gothic", 10f), AutoSize = true, Margin = new Padding(8, 11, 0, 0) });
        var line2 = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 0) };
        if (startError != "")
            line2.Controls.Add(new Label { Text = "● 서버 시작 실패: " + startError, AutoSize = true, ForeColor = Color.White, BackColor = Ui.Danger, Margin = new Padding(0, 3, 0, 0), Padding = new Padding(4, 1, 4, 1) });
        else
        {
            line2.Controls.Add(new Label { Text = "● 실행 중", AutoSize = true, ForeColor = Color.FromArgb(60, 200, 110), Font = new Font("Malgun Gothic", 9.5f, FontStyle.Bold), Margin = new Padding(0, 3, 10, 0) });
            foreach (var u in urls)
            {
                var ll = new LinkLabel { Text = u, AutoSize = true, Tag = "headtext", LinkBehavior = LinkBehavior.HoverUnderline, Margin = new Padding(0, 3, 12, 0), Cursor = Cursors.Hand };
                ll.LinkColor = ll.ActiveLinkColor = ll.VisitedLinkColor = Ui.HeadText;
                string uu = u; ll.Click += (_, _) => { try { Process.Start(new ProcessStartInfo(uu) { UseShellExecute = true }); } catch { } };
                line2.Controls.Add(ll);
            }
        }
        mid.Controls.Add(line1); mid.Controls.Add(line2);
        head.Controls.Add(mid, 1, 0);
        // 스킨 선택 (색 네모를 누르면 바로 바뀜)
        var skinBox = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, Margin = new Padding(0) };
        skinBox.Controls.Add(new Label { Text = "스킨", Tag = "headsub", ForeColor = Ui.HeadSub, AutoSize = true, Margin = new Padding(0, 6, 6, 0) });
        var tip = new ToolTip();
        for (int i = 0; i < 3; i++)
        {
            int si = i;
            var sw = new Button { Width = 26, Height = 26, FlatStyle = FlatStyle.Flat, BackColor = Ui.SkinSwatch[i], Margin = new Padding(3), Cursor = Cursors.Hand, Tag = "swatch", UseVisualStyleBackColor = false };
            sw.FlatAppearance.BorderSize = 2; sw.FlatAppearance.MouseOverBackColor = Ui.SkinSwatch[i]; sw.FlatAppearance.MouseDownBackColor = Ui.SkinSwatch[i];
            tip.SetToolTip(sw, Ui.SkinNames[i] + " 스킨");
            sw.Click += (_, _) => { cfg.Skin = si; try { cfg.Save(); } catch { } ApplyTheme(); };
            swatches.Add(sw); skinBox.Controls.Add(sw);
        }
        head.Controls.Add(skinBox, 2, 0);

        var tpData = new TabPage("데이터") { BackColor = Color.White };
        var tpUsers = new TabPage("사용자") { BackColor = Color.White };
        var tpAudit = new TabPage("접속·작업 기록") { BackColor = Color.White };
        var tpServer = new TabPage("서버 정보") { BackColor = Color.White };
        var tpSet = new TabPage("서버 설정 (DDNS)") { BackColor = Color.White };
        var tpHelp = new TabPage("도움말") { BackColor = Color.White };
        BuildData(tpData); BuildUsers(tpUsers); BuildAudit(tpAudit); BuildServer(tpServer); BuildSettings(tpSet); BuildHelp(tpHelp);
        tabs.TabPages.AddRange(new[] { tpData, tpUsers, tpAudit, tpServer, tpSet, tpHelp });
        tabs.SelectedIndexChanged += (_, _) => { if (tabs.SelectedTab == tpUsers) LoadUsers(); else if (tabs.SelectedTab == tpAudit) LoadAudit(); };
        Controls.Add(tabs); Controls.Add(head);
        ApplyTheme();

        // 트레이
        tray.Icon = ico; tray.Text = Db.AppName + " 서버"; tray.Visible = true;
        var menu = new ContextMenuStrip();
        menu.Items.Add("관리 화면 열기", null, (_, _) => ShowMe());
        menu.Items.Add("브라우저로 열기", null, (_, _) => OpenBrowser());
        menu.Items.Add("-");
        menu.Items.Add("서버 종료", null, (_, _) => QuitServer());
        tray.ContextMenuStrip = menu; tray.DoubleClick += (_, _) => ShowMe();

        Load += (_, _) => { if (!Gate()) BeginInvoke(new Action(Hide)); };
    }

    /// 스킨을 바꾼 뒤 열려 있는 서버 창 전체에 새 색을 입힘
    void ApplyTheme()
    {
        Ui.SetSkin(cfg.Skin); BackColor = Ui.Bg; Ui.Apply(this);
        for (int i = 0; i < swatches.Count; i++) swatches[i].FlatAppearance.BorderColor = i == Ui.Skin ? Color.White : Ui.SkinSwatch[i];
        Refresh();
    }

    protected override void SetVisibleCore(bool value)
    {
        if (startHidden) { startHidden = false; if (!IsHandleCreated) CreateHandle(); value = false; }
        base.SetVisibleCore(value);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (cfg.CloseExits && e.CloseReason == CloseReason.UserClosing && !ReallyExit) { if (MessageBox.Show("서버를 종료할까요? 종료하면 클라이언트가 접속할 수 없습니다.", "종료", MessageBoxButtons.YesNo) != DialogResult.Yes) { e.Cancel = true; return; } ReallyExit = true; }
        if (!ReallyExit && !cfg.CloseExits && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true; unlocked = false; Hide();
            if (!balloonShown) { balloonShown = true; tray.ShowBalloonTip(3000, Db.AppName, "서버는 계속 실행 중입니다. 트레이 아이콘을 눌러 다시 열 수 있습니다.", ToolTipIcon.Info); }
            return;
        }
        tray.Visible = false;
        base.OnFormClosing(e);
    }

    /// 관리 창을 열 때 관리자 아이디·암호를 확인 (처음이면 관리자 계정을 먼저 만듦)
    bool Gate()
    {
        if (unlocked) return true;
        IWin32Window owner = Visible ? this : null;
        if (db.NeedSetup()) { if (!Ui.SetupAdminDialog(owner, db)) return false; }
        else if (!Ui.LoginDialog(owner, db)) return false;
        unlocked = true; RefreshSheetList(0); LoadRows(); return true;
    }

    void ShowMe() { if (!Gate()) return; Show(); WindowState = FormWindowState.Normal; Activate(); }
    void OpenBrowser() { try { Process.Start(new ProcessStartInfo("http" + (https ? "s" : "") + "://localhost:" + port) { UseShellExecute = true }); } catch { } }
    void QuitServer()
    {
        if (MessageBox.Show("서버를 종료하면 클라이언트(PC·모바일)가 접속할 수 없습니다.\n종료할까요?", "서버 종료", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        ReallyExit = true; tray.Visible = false; Application.Exit();
    }

    // ================= 데이터 탭 =================
    void BuildData(TabPage tp)
    {
        Ui.StyleGrid(grid);
        grid.CellEndEdit += CellEnd;
        grid.ColumnHeaderMouseClick += (_, e) =>
        {
            if (e.ColumnIndex < 1) return;
            var key = grid.Columns[e.ColumnIndex].Tag as string; if (key == null) return;
            if (sort == key) desc = !desc; else { sort = key; desc = false; }
            off = 0; LoadRows();
        };
        grid.DataError += (_, e) => { e.ThrowException = false; };

        // 3줄 정리: 글자(84px) + 입력칸(240px) 폭을 모두 같게 해서 줄마다 버튼 시작 위치가 맞음
        Label Lb(string t) => new Label { Text = t, Width = 84, Height = 32, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(3) };
        FlowLayoutPanel Line() => new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0), Padding = new Padding(0) };
        Button Gap(Button bt) { bt.Margin = new Padding(18, 3, 3, 3); return bt; }
        var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = 138, ColumnCount = 1, RowCount = 3, Padding = new Padding(8, 6, 8, 6), Tag = "soft", BackColor = Ui.BlueSoft };
        for (int i = 0; i < 3; i++) top.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        cbSheet.SelectedIndexChanged += (_, _) =>
        {
            if (loadingSheets) return;
            sid = (cbSheet.SelectedItem as SheetItem)?.Id ?? 0; off = 0; sort = ""; desc = false; txtQ.Text = ""; LoadRows();
        };
        cbSheet.Width = 240; txtQ.Width = 240; txtScan.Width = 240;
        foreach (Control ic in new Control[] { cbSheet, txtQ, txtScan }) ic.Margin = new Padding(3, 6, 3, 3);
        Button B(string t, EventHandler h, int k = 0) => Ui.Btn(t, h, k, 124);   // 데이터 탭 버튼은 모두 같은 크기
        var r1 = Line(); r1.Controls.Add(Lb("시트")); r1.Controls.Add(cbSheet);
        r1.Controls.Add(B("새 시트", (_, _) => NewSheet(), 1));
        r1.Controls.Add(B("엑셀 올리기", (_, _) => ImportFile(), 1));
        r1.Controls.Add(Gap(B("엑셀 받기", (_, _) => ExportFile(), 3)));
        r1.Controls.Add(Gap(B("시트·열 설정", (_, _) => SheetSettings(), 4)));
        txtQ.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; off = 0; LoadRows(); } };
        var r2 = Line(); r2.Controls.Add(Lb("검색")); r2.Controls.Add(txtQ);
        r2.Controls.Add(B("검색", (_, _) => { off = 0; LoadRows(); }, 3));
        r2.Controls.Add(Gap(B("+ 행 추가", (_, _) => AddRow(), 1)));
        r2.Controls.Add(B("선택 행 삭제", (_, _) => DeleteSelected(), 2));
        r2.Controls.Add(Gap(B("새로고침", (_, _) => { RefreshSheetList(sid); LoadRows(); })));
        txtScan.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; ScanLookup(); } };
        var r3 = Line(); r3.Controls.Add(Lb("바코드 조회")); r3.Controls.Add(txtScan);
        r3.Controls.Add(B("조회", (_, _) => ScanLookup(), 3));
        r3.Controls.Add(Gap(B("단위 우선순위", (_, _) => { if (sid == 0) return; ProductForms.UnitOrder(this, db, sid); }, 5)));
        r3.Controls.Add(B("중복 바코드 지정", (_, _) => { if (sid == 0) return; ProductForms.Dups(this, db, sid); }, 5));
        r3.Controls.Add(B("상품 시트 만들기", (_, _) => NewProductSheet(), 5));
        top.Controls.Add(r1, 0, 0); top.Controls.Add(r2, 0, 1); top.Controls.Add(r3, 0, 2);

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(8, 4, 8, 3), BackColor = Ui.BlueSoft, Tag = "soft" };
        bottom.Controls.Add(Ui.Btn("◀ 이전", (_, _) => { if (off > 0) { off = Math.Max(0, off - Lim); LoadRows(); } }));
        bottom.Controls.Add(Ui.Btn("다음 ▶", (_, _) => { if (off + Lim < total) { off += Lim; LoadRows(); } }));
        bottom.Controls.Add(lblPage);
        bottom.Controls.Add(new Label { Text = "셀을 두 번 누르거나 F2 로 고치면 바로 저장됩니다. 머리글을 누르면 정렬됩니다.", AutoSize = true, ForeColor = Color.Gray, Margin = new Padding(20, 9, 0, 0) });

        tp.Controls.Add(grid); tp.Controls.Add(bottom); tp.Controls.Add(top);   // 가운데(Fill) 먼저, 가장자리는 나중
    }

    void RefreshSheetList(long select)
    {
        loadingSheets = true;
        try
        {
            cbSheet.Items.Clear(); SheetItem pick = null;
            foreach (var s in Store.Sheets(db)) { var it = new SheetItem { Id = s.Id, Name = s.Name, Rows = s.Rows }; cbSheet.Items.Add(it); if (s.Id == select) pick = it; }
            if (pick == null && cbSheet.Items.Count > 0) pick = (SheetItem)cbSheet.Items[0];
            if (pick != null) cbSheet.SelectedItem = pick;
            sid = pick?.Id ?? 0;
        }
        finally { loadingSheets = false; }
    }

    void LoadRows()
    {
        loading = true;
        try
        {
            grid.Columns.Clear(); grid.Rows.Clear();
            if (sid == 0) { lblPage.Text = "시트가 없습니다. [새 시트] 또는 [엑셀 올리기]로 시작하세요."; return; }
            var r = Store.Query(db, sid, txtQ.Text.Trim(), sort, desc, off, Lim);
            cols = r.Cols; rows = r.Rows; total = r.Total;
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "#", ReadOnly = true, Width = 55, SortMode = DataGridViewColumnSortMode.Programmatic });
            foreach (var c in cols)
                grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = c.Name + (sort == c.Key ? (desc ? " ▼" : " ▲") : ""), Tag = c.Key, Width = 150, SortMode = DataGridViewColumnSortMode.Programmatic });
            for (int i = 0; i < rows.Count; i++)
            {
                var vals = new object[cols.Count + 1]; vals[0] = off + i + 1;
                for (int j = 0; j < cols.Count; j++) vals[j + 1] = rows[i].Data.TryGetValue(cols[j].Key, out var v) ? v : "";
                int ri = grid.Rows.Add(vals); grid.Rows[ri].Tag = rows[i];
            }
            long pages = Math.Max(1, (total + Lim - 1) / Lim);
            lblPage.Text = "총 " + total + "행 · " + (off / Lim + 1) + "/" + pages + " 쪽";
        }
        finally { loading = false; }
    }

    void CellEnd(object s, DataGridViewCellEventArgs e)
    {
        if (loading || e.RowIndex < 0 || e.ColumnIndex < 1) return;
        var row = grid.Rows[e.RowIndex]; var rec = row.Tag as RowRec; if (rec == null) return;
        var d = new Dictionary<string, string>();
        for (int j = 0; j < cols.Count; j++) d[cols[j].Key] = Convert.ToString(row.Cells[j + 1].Value) ?? "";
        bool same = cols.All(c => (rec.Data.TryGetValue(c.Key, out var o) ? o : "") == d[c.Key]);
        if (same) return;
        if (Store.UpdateRow(db, sid, rec.Id, rec.Ver, d, Store.Admin)) { rec.Ver++; rec.Data = d; }
        else { MessageBox.Show("저장하지 못했습니다. 다른 사용자가 먼저 수정했거나 이미 삭제된 행입니다.\n화면을 새로 불러옵니다.", "수정 충돌"); BeginInvoke(new Action(LoadRows)); }
    }

    void AddRow()
    {
        if (sid == 0) { MessageBox.Show("먼저 시트를 만드세요."); return; }
        Store.AddRow(db, sid, Store.Admin);
        sort = ""; desc = false; txtQ.Text = "";
        LoadRows(); if (total > off + Lim) { off = (int)((total - 1) / Lim * Lim); LoadRows(); }
        RefreshSheetList(sid); 
        if (grid.Rows.Count > 0 && grid.Columns.Count > 1)
        {
            var last = grid.Rows[grid.Rows.Count - 1]; grid.CurrentCell = last.Cells[1]; grid.BeginEdit(true);
        }
    }

    void DeleteSelected()
    {
        var idx = grid.SelectedCells.Cast<DataGridViewCell>().Select(c => c.RowIndex).Distinct().ToList();
        var ids = idx.Select(i => (grid.Rows[i].Tag as RowRec)?.Id ?? 0).Where(x => x > 0).ToList();
        if (ids.Count == 0) { MessageBox.Show("삭제할 행을 선택하세요. (왼쪽 회색 칸을 누르면 행 전체가 선택됩니다)"); return; }
        if (MessageBox.Show(ids.Count + "개 행을 삭제할까요? 되돌릴 수 없습니다.", "행 삭제", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        Store.DeleteRows(db, sid, ids, Store.Admin);
        RefreshSheetList(sid); LoadRows();
    }

    void ScanLookup()
    {
        if (sid == 0) { MessageBox.Show("시트를 먼저 고르세요."); return; }
        string code = txtScan.Text.Trim(), dw = Ui.DigitWarn(cfg, code);
        if (dw != "")
        {
            txtScan.BackColor = Color.MistyRose; _ = Task.Delay(2000).ContinueWith(_ => { try { BeginInvoke(new Action(() => txtScan.BackColor = SystemColors.Window)); } catch { } });
        }
        var r = Product.Lookup(db, sid, txtScan.Text);
        if (r.Status != "ok") { MessageBox.Show(r.Message + (r.Count > 1 ? "\n(후보 " + r.Count + "개 — [중복 바코드 지정]에서 정하세요)" : ""), "바코드 조회", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        var lines = new List<string>();
        foreach (var c in Store.Columns(db, sid)) { var v = r.Row.Data.TryGetValue(c.Key, out var x) ? x : ""; if (v != "") lines.Add(c.Name + ": " + v); }
        MessageBox.Show(string.Join("\n", lines) + (r.Count > 1 ? "\n\n(같은 바코드 " + r.Count + "개 중 이 행이 스캔 결과입니다)" : ""), "바코드 조회 결과");
        txtScan.SelectAll();
    }

    void NewProductSheet()
    {
        var name = Ui.Ask(this, "상품 시트 만들기", "시트 이름 (열: 바코드, 상품명, 단위, 입고단가, 판매가1~판매가10)", "상품"); if (string.IsNullOrWhiteSpace(name)) return;
        long id = Store.NewSheet(db, name.Trim(), ProductForms.ProductHeads, Store.Admin);
        off = 0; sort = ""; RefreshSheetList(id); LoadRows();
    }

    void NewSheet()
    {
        var name = Ui.Ask(this, "새 시트", "시트 이름"); if (string.IsNullOrWhiteSpace(name)) return;
        var cs = Ui.Ask(this, "새 시트", "열 이름을 쉼표로 구분해 입력 (예: 품목,수량,단가,비고)", "품목,수량,비고"); if (cs == null) return;
        long id = Store.NewSheet(db, name.Trim(), cs.Split(','), Store.Admin);
        off = 0; sort = ""; RefreshSheetList(id); LoadRows();
    }


    // ---- 파일 끌어놓기: 창 어디에 놓아도 [엑셀 올리기]가 해당 파일로 시작됨 ----
    static bool IsDataFile(string f) { var e = Path.GetExtension(f).ToLowerInvariant(); return e == ".xlsx" || e == ".xls" || e == ".csv" || e == ".txt"; }
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    static extern bool ChangeWindowMessageFilterEx(IntPtr hWnd, uint msg, uint action, IntPtr pChangeInfo);
    /// 관리자 권한으로 실행 중이면 윈도우(UIPI)가 일반 탐색기의 끌어놓기를 막음 → 이 창에 한해 끌어놓기 관련 메시지를 허용
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try { foreach (var m in new uint[] { 0x233, 0x4A, 0x49 }) ChangeWindowMessageFilterEx(Handle, m, 1, IntPtr.Zero); } catch { }
    }
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        try { foreach (Control c in Controls) AllowDropMsg(c); } catch { }
    }
    void AllowDropMsg(Control c)
    {
        if (c.IsHandleCreated) foreach (var m in new uint[] { 0x233, 0x4A, 0x49 }) ChangeWindowMessageFilterEx(c.Handle, m, 1, IntPtr.Zero);
        foreach (Control ch in c.Controls) AllowDropMsg(ch);
    }
    void HookDrop(Control c)
    {
        c.AllowDrop = true;
        c.DragEnter += (_, e) =>
        {
            var files = e.Data?.GetData(DataFormats.FileDrop) as string[];
            e.Effect = files != null && files.Length > 0 && IsDataFile(files[0]) ? DragDropEffects.Copy : DragDropEffects.None;
        };
        c.DragDrop += (_, e) =>
        {
            var files = e.Data?.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0) return;
            if (!IsDataFile(files[0])) { MessageBox.Show("엑셀(xlsx, xls) 또는 csv 파일만 올릴 수 있습니다."); return; }
            string f = files[0];
            BeginInvoke(new Action(() => ImportFile(f)));
        };
        foreach (Control ch in c.Controls) HookDrop(ch);
    }

    /// 파일 끌어놓기(드래그) 지원: dropped 가 있으면 파일 선택 창 없이 그 파일로 바로 진행
    void ImportFile(string dropped = null)
    {
        string path = dropped;
        if (path == null)
        {
            using var od = new OpenFileDialog { Title = "엑셀·CSV 파일 선택", Filter = "엑셀/CSV|*.xlsx;*.xls;*.csv;*.txt|모든 파일|*.*" };
            if (od.ShowDialog(this) != DialogResult.OK) return;
            path = od.FileName;
        }
        Table t;
        try { using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); t = Importer.Read(fs, path); }
        catch (Exception ex) { MessageBox.Show("파일을 읽지 못했습니다.\n" + ex.Message); return; }
        if (t.Headers.Length == 0) { MessageBox.Show("읽을 데이터가 없습니다. 첫 줄은 머리글이어야 합니다."); return; }

        string keyName = sid == 0 ? "" : Store.KeyColName(db, sid);
        using var f = new Form { Text = "엑셀·CSV 올리기", Width = 500, Height = 410, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false, Font = Font };
        f.Controls.Add(new Label { Left = 14, Top = 12, Width = 460, Height = 36, Text = Path.GetFileName(path) + "\n머리글 " + t.Headers.Length + "열, 데이터 " + t.Rows.Count + "행" });
        bool canSync = sid != 0 && keyName != "";
        var rMerge = new RadioButton { Left = 14, Top = 56, Width = 470, Height = 40, Text = canSync ? "추가·수정 (기준열 \"" + keyName + "\") — 권장: 같은 값은 수정, 없는 값은 추가, 파일에 없는 기존 행은 그대로 둠" : "추가·수정 — 이 시트에 기준열(바코드)이 없어 사용할 수 없음", Checked = canSync, Enabled = canSync };
        var rSync = new RadioButton { Left = 14, Top = 100, Width = 470, Height = 40, Text = canSync ? "동기화 (기준열 \"" + keyName + "\"): 없는 건 추가, 있는 건 수정, 파일에 없는 건 삭제" : "동기화 — 이 시트에 기준열(바코드)이 없어 사용할 수 없음", Enabled = canSync };
        var rNew = new RadioButton { Left = 14, Top = 144, Width = 470, Text = "새 시트로 만들기", Checked = !canSync && sid == 0 };
        var rApp = new RadioButton { Left = 14, Top = 170, Width = 470, Text = "현재 시트 아래에 그냥 추가", Checked = !canSync && sid != 0, Enabled = sid != 0 };
        var rRep = new RadioButton { Left = 14, Top = 196, Width = 470, Text = "현재 시트 내용을 덮어쓰기 (기존 행 삭제)", Enabled = sid != 0 };
        f.Controls.AddRange(new Control[] { rMerge, rSync, rNew, rApp, rRep });
        f.Controls.Add(new Label { Left = 14, Top = 230, Width = 200, Text = "새 시트 이름 (새 시트일 때)" });
        var tn = new TextBox { Left = 14, Top = 252, Width = 460, Text = Path.GetFileNameWithoutExtension(path) }; f.Controls.Add(tn);
        var ok = Ui.Btn("올리기", null, 1); ok.Left = 310; ok.Top = 302; ok.DialogResult = DialogResult.OK;
        var cc = Ui.Btn("취소", null); cc.Left = 396; cc.Top = 302; cc.DialogResult = DialogResult.Cancel;
        f.Controls.Add(ok); f.Controls.Add(cc); f.AcceptButton = ok; f.CancelButton = cc;
        if (f.ShowDialog(this) != DialogResult.OK) return;
        string mode = rMerge.Checked ? "merge" : rSync.Checked ? "sync" : rNew.Checked ? "new" : rRep.Checked ? "replace" : "append";
        if (mode == "sync" || mode == "merge")
        {
            bool dm = mode == "sync";
            try
            {
                var pre = Store.Sync(db, t, sid, true, Store.Admin, true, dm);
                string msg = (dm ? "동기화" : "추가·수정") + " 결과 미리보기\n\n추가 " + pre.Add + "건 (아래에 이어서)\n수정 " + pre.Update + "건\n변경 없음 " + pre.Same + "건" + (dm ? "\n삭제 " + pre.Delete + "건 (파일에 없는 행)" : "\n(파일에 없는 기존 행은 그대로 둡니다)")
                    + (pre.NoKey > 0 ? "\n기준값이 없어 건너뜀 " + pre.NoKey + "건" : "") + (pre.DupInFile > 0 ? "\n파일 안 중복 기준값 " + pre.DupInFile + "건 (마지막 줄 사용)" : "") + "\n\n진행할까요?";
                if (MessageBox.Show(msg, "엑셀 동기화", MessageBoxButtons.YesNo, pre.Delete > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Question) != DialogResult.Yes) return;
                var res2 = Store.Sync(db, t, sid, false, Store.Admin, true, dm);
                off = 0; sort = ""; RefreshSheetList(sid); LoadRows();
                MessageBox.Show((dm ? "동기화했습니다." : "추가·수정했습니다.") + "\n추가 " + res2.Add + " / 수정 " + res2.Update + (dm ? " / 삭제 " + res2.Delete : ""), "엑셀 올리기");
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "동기화 실패"); }
            return;
        }
        if (mode == "replace" && MessageBox.Show("현재 시트의 기존 행이 모두 삭제됩니다. 계속할까요?", "덮어쓰기", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try
        {
            var res = Store.Import(db, t, mode, sid, string.IsNullOrWhiteSpace(tn.Text) ? Path.GetFileNameWithoutExtension(path) : tn.Text.Trim(), Store.Admin);
            off = 0; sort = ""; RefreshSheetList(res.Sid); LoadRows();
            MessageBox.Show(res.Count + "행을 올렸습니다.", "엑셀 올리기");
        }
        catch (Exception ex) { MessageBox.Show("올리기 실패: " + ex.Message); }
    }

    void ExportFile()
    {
        var s = cbSheet.SelectedItem as SheetItem; if (s == null) return;
        using var sd = new SaveFileDialog { Title = "엑셀로 저장", Filter = "엑셀 (*.xlsx)|*.xlsx", FileName = Store.SafeName(s.Name) + ".xlsx" };
        if (sd.ShowDialog(this) != DialogResult.OK) return;
        try { File.WriteAllBytes(sd.FileName, Store.ExportXlsx(db, s.Id, s.Name)); db.Log(Store.Admin, "엑셀 받기", s.Name); MessageBox.Show("저장했습니다.\n" + sd.FileName); }
        catch (Exception ex) { MessageBox.Show("저장 실패: " + ex.Message); }
    }

    void SheetSettings()
    {
        var s = cbSheet.SelectedItem as SheetItem; if (s == null) { MessageBox.Show("시트가 없습니다."); return; }
        using var f = new Form { Text = "시트·열 설정 — " + s.Name, Width = 460, Height = 440, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false, Font = Font };
        var lb = new ListBox { Left = 14, Top = 14, Width = 270, Height = 340 }; f.Controls.Add(lb);
        List<(string Key, string Name)> cl = new();
        void Fill() { cl = Store.Columns(db, s.Id); lb.Items.Clear(); foreach (var c in cl) lb.Items.Add(c.Name); }
        Fill();
        int y = 14;
        void Add(string text, Action act, int kind = 0) { var b = Ui.Btn(text, (_, _) => act(), kind); b.Left = 296; b.Top = y; b.Width = 140; b.AutoSize = false; f.Controls.Add(b); y += 40; }
        Add("열 추가", () => { var n = Ui.Ask(f, "열 추가", "새 열 이름"); if (string.IsNullOrWhiteSpace(n)) return; Store.AddColumn(db, s.Id, n.Trim(), Store.Admin); Fill(); }, 1);
        Add("열 이름 변경", () => { if (lb.SelectedIndex < 0) return; var n = Ui.Ask(f, "열 이름 변경", "새 이름", cl[lb.SelectedIndex].Name); if (string.IsNullOrWhiteSpace(n)) return; Store.RenameColumn(db, s.Id, cl[lb.SelectedIndex].Key, n.Trim(), Store.Admin); Fill(); });
        Add("열 삭제", () => { if (lb.SelectedIndex < 0) return; if (MessageBox.Show("열 \"" + cl[lb.SelectedIndex].Name + "\" 과 그 데이터를 삭제할까요?", "열 삭제", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return; Store.DeleteColumn(db, s.Id, cl[lb.SelectedIndex].Key, Store.Admin); Fill(); }, 2);
        Add("선택 열을 기준열로", () => { if (lb.SelectedIndex < 0) return; Store.SetKeyCol(db, s.Id, cl[lb.SelectedIndex].Key, Store.Admin); MessageBox.Show("기준열: " + cl[lb.SelectedIndex].Name, "기준열 지정"); }, 5);
        y += 6;
        Add("시트 이름 변경", () => { var n = Ui.Ask(f, "시트 이름 변경", "새 이름", s.Name); if (string.IsNullOrWhiteSpace(n)) return; Store.RenameSheet(db, s.Id, n.Trim(), Store.Admin); f.DialogResult = DialogResult.OK; });
        Add("시트 삭제", () => { if (MessageBox.Show("시트 \"" + s.Name + "\" 와 모든 행을 삭제할까요?\n되돌릴 수 없습니다.", "시트 삭제", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return; Store.DeleteSheet(db, s.Id, Store.Admin); sid = 0; f.DialogResult = DialogResult.OK; }, 2);
        var close = Ui.Btn("닫기", null); close.Left = 296; close.Top = 330; close.Width = 140; close.AutoSize = false; close.DialogResult = DialogResult.OK; f.Controls.Add(close); f.CancelButton = close;
        f.ShowDialog(this);
        off = 0; sort = ""; RefreshSheetList(sid); LoadRows();
    }

    // ================= 사용자 / 기록 / 서버 정보 =================
    void BuildUsers(TabPage tp)
    {
        Ui.StyleGrid(gUsers); gUsers.SelectionMode = DataGridViewSelectionMode.FullRowSelect; gUsers.MultiSelect = false;
        gUsers.Columns.Add("id", "번호"); gUsers.Columns.Add("name", "아이디"); gUsers.Columns.Add("role", "권한"); gUsers.Columns.Add("created", "만든 날짜");
        gUsers.Columns[0].Width = 60; gUsers.Columns[1].Width = 200; gUsers.Columns[2].Width = 120; gUsers.Columns[3].Width = 180;
        var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = 124, ColumnCount = 1, RowCount = 3, Padding = new Padding(10, 6, 10, 4), Tag = "soft", BackColor = Ui.BlueSoft };
        top.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); top.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); top.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        var l1 = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
        l1.Controls.Add(Ui.Btn("사용자 추가", (_, _) => AddUser(), 1, 124));
        l1.Controls.Add(Ui.Btn("비밀번호 재설정", (_, _) => ResetPw(), 4, 124));
        l1.Controls.Add(Ui.Btn("삭제", (_, _) => DeleteUser(), 2, 124));
        lblClients.Margin = new Padding(16, 10, 0, 0); l1.Controls.Add(lblClients);
        var chkStruct = new CheckBox { Text = "클라이언트도 시트·열 만들기/바꾸기 허용 (끄면 관리자만)", AutoSize = true, Margin = new Padding(6, 6, 0, 0), Checked = db.ClientStructure() };
        chkStruct.CheckedChanged += (_, _) => { db.SetSetting("client_structure", chkStruct.Checked ? "1" : "0"); db.Log(Store.Admin, "클라이언트 시트·열 변경 허용", chkStruct.Checked ? "허용" : "차단"); };
        var l2 = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) }; l2.Controls.Add(chkStruct);
        var l3 = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
        l3.Controls.Add(new Label { Text = "권한: 관리자 = 전부 / 사용자 = 행 추가·수정·삭제, 올리기 / 조회전용 = 보기·엑셀 받기", AutoSize = true, ForeColor = Color.Gray, Margin = new Padding(6, 4, 0, 0) });
        top.Controls.Add(l1, 0, 0); top.Controls.Add(l2, 0, 1); top.Controls.Add(l3, 0, 2);
        tp.Controls.Add(gUsers); tp.Controls.Add(top);
    }
    static string RoleName(string r) => r == "admin" ? "관리자" : r == "viewer" ? "조회전용" : "사용자";
    void LoadUsers() { gUsers.Rows.Clear(); lblClients.Text = "클라이언트 계정 " + db.ClientCount() + " / " + Db.MaxClients + " (관리자 제외)"; foreach (var u in Store.Users(db)) { int i = gUsers.Rows.Add(u.Id, u.Name, RoleName(u.Role), u.Created); gUsers.Rows[i].Tag = u; } }
    void AddUser()
    {
        using var f = new Form { Text = "사용자 추가", Width = 380, Height = 280, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false, Font = Font };
        f.Controls.Add(new Label { Left = 14, Top = 14, Text = "아이디", AutoSize = true }); var n = new TextBox { Left = 14, Top = 34, Width = 340 }; f.Controls.Add(n);
        f.Controls.Add(new Label { Left = 14, Top = 70, Text = "비밀번호 (6자 이상, 클라이언트에게 알려줄 값)", AutoSize = true }); var p = new TextBox { Left = 14, Top = 90, Width = 250 }; f.Controls.Add(p);
        var gen = Ui.Btn("자동 생성", (_, _) => p.Text = Ui.GenPassword()); gen.Left = 270; gen.Top = 88; f.Controls.Add(gen);
        f.Controls.Add(new Label { Left = 14, Top = 126, Text = "권한", AutoSize = true });
        var r = new ComboBox { Left = 14, Top = 146, Width = 340, DropDownStyle = ComboBoxStyle.DropDownList }; r.Items.AddRange(new object[] { "사용자 (추가·수정·삭제)", "조회전용", "관리자" }); r.SelectedIndex = 0; f.Controls.Add(r);
        var ok = Ui.Btn("추가", null, 1); ok.Left = 190; ok.Top = 196; ok.DialogResult = DialogResult.OK; var cc = Ui.Btn("취소", null); cc.Left = 276; cc.Top = 196; cc.DialogResult = DialogResult.Cancel;
        f.Controls.Add(ok); f.Controls.Add(cc); f.AcceptButton = ok; f.CancelButton = cc;
        if (f.ShowDialog(this) != DialogResult.OK) return;
        if (string.IsNullOrWhiteSpace(n.Text) || p.Text.Length < 6) { MessageBox.Show("아이디와 6자 이상 비밀번호를 입력하세요."); return; }
        string role = r.SelectedIndex == 1 ? "viewer" : r.SelectedIndex == 2 ? "admin" : "user";
        if (role != "admin" && db.ClientCount() >= Db.MaxClients) { MessageBox.Show("클라이언트 계정은 최대 " + Db.MaxClients + "개까지 만들 수 있습니다.\n(관리자 계정은 제외)"); return; }
        try { db.AddUser(n.Text.Trim(), p.Text, role); db.Log(Store.Admin, "사용자 추가", n.Text.Trim() + " (" + role + ")"); }
        catch (Exception) { MessageBox.Show("이미 있는 아이디입니다."); return; }
        LoadUsers();
    }
    UserRec SelUser() => gUsers.CurrentRow?.Tag as UserRec;
    void ResetPw()
    {
        var u = SelUser(); if (u == null) return;
        var p = Ui.Ask(this, "비밀번호 재설정", u.Name + " 의 새 비밀번호 (6자 이상)", "", true);
        if (p == null) return; if (p.Length < 6) { MessageBox.Show("6자 이상이어야 합니다."); return; }
        db.SetPassword(u.Id, p); db.Log(Store.Admin, "비밀번호 재설정", u.Name); MessageBox.Show("재설정했습니다.");
    }
    void DeleteUser()
    {
        var u = SelUser(); if (u == null) return;
        if (MessageBox.Show(u.Name + " 사용자를 삭제할까요?", "사용자 삭제", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        Store.DeleteUser(db, u.Id, Store.Admin); LoadUsers();
    }

    void BuildAudit(TabPage tp)
    {
        Ui.StyleGrid(gAudit); gAudit.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        gAudit.Columns.Add("ts", "시각"); gAudit.Columns.Add("user", "사용자"); gAudit.Columns.Add("act", "작업"); gAudit.Columns.Add("det", "내용");
        gAudit.Columns[0].Width = 150; gAudit.Columns[1].Width = 110; gAudit.Columns[2].Width = 160; gAudit.Columns[3].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 52, Padding = new Padding(10, 6, 10, 4), BackColor = Ui.BlueSoft, Tag = "soft" };
        top.Controls.Add(Ui.Btn("새로고침", (_, _) => LoadAudit(), 0, 124));
        top.Controls.Add(new Label { Text = "최근 500건", AutoSize = true, ForeColor = Color.Gray, Margin = new Padding(10, 11, 0, 0) });
        tp.Controls.Add(gAudit); tp.Controls.Add(top);
    }
    void LoadAudit() { gAudit.Rows.Clear(); foreach (var a in Store.Audit(db, 500)) gAudit.Rows.Add(a[0], a[1], a[2], a[3]); }

    /// 전체 데이터 삭제(초기화): 관리자 비밀번호 확인 후 모든 시트·행 삭제 (계정·설정 유지, 삭제 전 자동 백업)
    void WipeAllFlow()
    {
        int sheets; long rows;
        using (var c = db.Open()) { sheets = Convert.ToInt32(Db.One(c, "SELECT COUNT(*) FROM sheets")); rows = Convert.ToInt64(Db.One(c, "SELECT COUNT(*) FROM rows")); }
        string who = Ui.WipeDialog(this, db, sheets, rows);
        if (who == null) return;
        if (MessageBox.Show(this, "정말 모든 데이터를 삭제할까요?\n\n시트 " + sheets.ToString("N0") + "개 / 행 " + rows.ToString("N0") + "건\n이 작업은 되돌릴 수 없습니다 (삭제 직전 DB 는 backup 폴더에 복사됩니다).", "전체 데이터 초기화", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        try
        {
            Cursor = Cursors.WaitCursor;
            var bk = Store.WipeAll(db, who);
            RefreshSheetList(0); LoadRows();
            MessageBox.Show(this, "모든 데이터를 삭제했습니다.\n\n삭제 전 백업: " + bk, "전체 데이터 초기화");
        }
        catch (Exception ex) { MessageBox.Show(this, "초기화하지 못했습니다.\n" + ex.Message, "전체 데이터 초기화"); }
        finally { Cursor = Cursors.Default; }
    }

    // ---------- 서버 정보·설정 탭 공용 배치 도우미 ----------
    // 한 쪽(Page) 안에 소제목(HeadL) → 항목 표(Grid/Fld: 글자 170px + 입력 + 설명) → 버튼 줄(Btns) 순으로 쌓아 모든 탭의 줄·간격을 같게 맞춤
    static FlowLayoutPanel Page() => new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(20, 6, 20, 16), BackColor = Color.White };
    static Label HeadL(string t) => new Label { Text = t, AutoSize = true, Tag = "head", ForeColor = Ui.BlueDark, Font = new Font("Malgun Gothic", 10.5f, FontStyle.Bold), Margin = new Padding(0, 16, 0, 6) };
    static Label Note(string t) => new Label { Text = t, AutoSize = true, MaximumSize = new Size(820, 0), ForeColor = Color.Gray, Margin = new Padding(12, 2, 0, 4) };
    static TableLayoutPanel Grid()
    {
        var g = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 3, Margin = new Padding(0, 0, 0, 4), Padding = new Padding(0), CellBorderStyle = TableLayoutPanelCellBorderStyle.Single };
        g.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180)); g.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); g.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        return g;
    }
    static void Fld(TableLayoutPanel g, string label, Control c, string hint = null)
    {
        int r = g.RowCount++; g.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        g.Controls.Add(new Label { Text = label, AutoSize = false, Dock = DockStyle.Fill, Height = 36, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10, 0, 0, 0), Margin = new Padding(0), Tag = "soft", BackColor = Ui.BlueSoft, Font = new Font("Malgun Gothic", 9.5f, FontStyle.Bold) }, 0, r);
        c.Anchor = AnchorStyles.Left; if (c.Margin == new Padding(3)) c.Margin = new Padding(8, 5, 8, 5);
        g.Controls.Add(c, 1, r);
        if (hint != null) g.Controls.Add(new Label { Text = hint, AutoSize = true, MaximumSize = new Size(460, 0), ForeColor = Color.Gray, Anchor = AnchorStyles.Left, Margin = new Padding(8, 5, 8, 5) }, 2, r);
    }
    static void Chk(TableLayoutPanel g, CheckBox c)
    {
        int r = g.RowCount++; g.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        c.AutoSize = true; c.Margin = new Padding(10, 7, 3, 7); g.Controls.Add(c, 0, r); g.SetColumnSpan(c, 3);
    }
    static FlowLayoutPanel Btns(params Button[] bs)
    {
        var f = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, MaximumSize = new Size(900, 0), Margin = new Padding(9, 2, 0, 2) };
        f.Controls.AddRange(bs); return f;
    }
    static FlowLayoutPanel Pair(Control a, Control b)
    {
        var f = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        a.Margin = new Padding(0, 4, 8, 3); b.Margin = new Padding(0, 1, 0, 1); f.Controls.Add(a); f.Controls.Add(b); return f;
    }

    void BuildServer(TabPage tp)
    {
        var p = Page();
        p.Controls.Add(HeadL("접속 주소"));
        foreach (var u in urls)
        {
            var ll = new LinkLabel { Text = u, AutoSize = true, Margin = new Padding(12, 0, 0, 4), Font = new Font("Malgun Gothic", 10f), Cursor = Cursors.Hand };
            string uu = u; ll.Click += (_, _) => { try { Process.Start(new ProcessStartInfo(uu) { UseShellExecute = true }); } catch { } };
            p.Controls.Add(ll);
        }
        p.Controls.Add(Note(https ? "HTTPS 사용 중 (config.json 의 인증서)" : "HTTP 로 실행 중 — 인터넷(외부)에서 쓰려면 [서버 설정]에서 HTTPS 인증서(pfx)나 Tailscale Funnel 을 설정하세요."));

        p.Controls.Add(HeadL("서버 정보"));
        var g = Grid();
        Label V(string t) => new Label { Text = t, AutoSize = true, MaximumSize = new Size(760, 0), Margin = new Padding(8, 9, 8, 9) };
        if (cfg.ExternalUrl != "") Fld(g, "외부 접속 주소", V(cfg.ExternalUrl));
        Fld(g, "데이터 폴더", V(dataDir)); Fld(g, "설정 파일", V(cfgPath)); Fld(g, "자동 백업", V("매일 1회, 데이터 폴더\\backup 에 14일 보관"));
        p.Controls.Add(g);

        Button B(string t, EventHandler h, int k = 0) => Ui.Btn(t, h, k, 150);
        p.Controls.Add(HeadL("바로가기"));
        p.Controls.Add(Btns(
            B("브라우저로 열기", (_, _) => OpenBrowser(), 1),
            B("데이터 폴더 열기", (_, _) => Process.Start(new ProcessStartInfo(dataDir) { UseShellExecute = true })),
            B("백업 폴더 열기", (_, _) => { var d = Path.Combine(dataDir, "backup"); Directory.CreateDirectory(d); Process.Start(new ProcessStartInfo(d) { UseShellExecute = true }); }),
            B("config.json 열기", (_, _) => Process.Start(new ProcessStartInfo("notepad.exe", "\"" + cfgPath + "\"") { UseShellExecute = true }), 4)));

        p.Controls.Add(HeadL("백업"));
        p.Controls.Add(Btns(B("지금 백업", (_, _) => { db.AutoBackup(); try { var f = Path.Combine(dataDir, "backup", "z2roN_Proj_" + DateTime.Now.ToString("yyyyMMdd") + ".db"); using var c = db.Open(); if (File.Exists(f)) File.Delete(f); Db.Run(c, "VACUUM INTO '" + f.Replace("'", "''") + "'"); MessageBox.Show("백업했습니다.\n" + f); } catch (Exception ex) { MessageBox.Show("백업 실패: " + ex.Message); } }, 3)));

        p.Controls.Add(HeadL("위험 작업 (관리자 확인 필요)"));
        p.Controls.Add(Btns(
            B("전체 데이터 초기화", (_, _) => WipeAllFlow(), 2),
            B("서버 종료", (_, _) => QuitServer(), 2)));
        tp.Controls.Add(p);
    }

    // ================= 도움말 (목차 + 내용 + PDF 내려받기) =================
    void BuildHelp(TabPage tp)
    {
        var secs = HelpDoc.Sections();
        var list = new ListBox { Dock = DockStyle.Left, Width = 230, IntegralHeight = false, Font = new Font("Malgun Gothic", 10f), BorderStyle = BorderStyle.None, ItemHeight = 28, DrawMode = DrawMode.OwnerDrawFixed };
        list.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return; bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var br = new SolidBrush(sel ? Ui.BlueLine : Color.White)) e.Graphics.FillRectangle(br, e.Bounds);
            TextRenderer.DrawText(e.Graphics, list.Items[e.Index].ToString(), e.Font, new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height), sel ? Ui.BlueDark : Color.FromArgb(50, 60, 80), TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        };
        list.Items.Add("전체 보기"); foreach (var (t, _) in secs) list.Items.Add(t);
        var rt = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Color.White, Font = new Font("Malgun Gothic", 10.5f), DetectUrls = false };
        void ShowSec(int i)
        {
            rt.Clear();
            using var fH = new Font("Malgun Gothic", 13f, FontStyle.Bold); using var fB = new Font("Malgun Gothic", 10.5f);
            void Add(string t, Font f, Color c) { rt.SelectionFont = f; rt.SelectionColor = c; rt.AppendText(t + "\n"); }
            for (int k = 0; k < secs.Count; k++)
            {
                if (i > 0 && k != i - 1) continue;
                Add(secs[k].Title, fH, Ui.BlueDark);
                foreach (var l in secs[k].Lines) Add(l.StartsWith("- ") ? "  • " + l.Substring(2) : l.StartsWith("    ") ? "        " + l.Trim() : l, fB, Color.FromArgb(40, 45, 60));
                Add("", fB, Color.Black);
            }
            rt.SelectionStart = 0; rt.ScrollToCaret();
        }
        list.SelectedIndexChanged += (_, _) => ShowSec(Math.Max(0, list.SelectedIndex));
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 52, Padding = new Padding(10, 7, 10, 4), Tag = "soft", BackColor = Ui.BlueSoft };
        bar.Controls.Add(Ui.Btn("도움말 PDF 저장", (_, _) => SaveHelpPdf(), 1, 150));
        bar.Controls.Add(Ui.Btn("인쇄용 웹페이지로 열기", (_, _) => OpenHelpHtml(), 0, 170));
        bar.Controls.Add(new Label { Text = Db.AppName + " v" + Db.Version + " 기준 도움말 (프로그램이 바뀌면 PDF 도 새 내용으로 만들어집니다)", AutoSize = true, ForeColor = Color.Gray, Margin = new Padding(12, 11, 0, 0) });
        tp.Controls.Add(rt); tp.Controls.Add(list); tp.Controls.Add(bar);
        list.SelectedIndex = 0;
    }

    void SaveHelpPdf()
    {
        using var sd = new SaveFileDialog { Filter = "PDF|*.pdf", FileName = Db.AppName + "_도움말_v" + Db.Version + ".pdf", Title = "도움말 PDF 저장" };
        if (sd.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            Cursor = Cursors.WaitCursor; if (File.Exists(sd.FileName)) File.Delete(sd.FileName);
            HelpDoc.SavePdf(sd.FileName);
            if (!File.Exists(sd.FileName)) throw new IOException("PDF 파일이 만들어지지 않았습니다.");
            Process.Start(new ProcessStartInfo(sd.FileName) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            if (MessageBox.Show(this, "PDF 를 만들지 못했습니다.\n" + ex.Message + "\n\n대신 인쇄용 웹페이지로 열까요? (브라우저에서 Ctrl+P → PDF 로 저장)", "도움말 PDF", MessageBoxButtons.YesNo) == DialogResult.Yes) OpenHelpHtml();
        }
        finally { Cursor = Cursors.Default; }
    }

    void OpenHelpHtml()
    {
        try
        {
            var f = Path.Combine(Path.GetTempPath(), Db.AppName + "_도움말.html");
            File.WriteAllText(f, HelpDoc.Html(), new UTF8Encoding(true));
            Process.Start(new ProcessStartInfo(f) { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show(this, "열지 못했습니다: " + ex.Message); }
    }

    // ================= 서버 설정 (DDNS · 포트 · HTTPS · 시작) =================
    void BuildSettings(TabPage tp)
    {
        var p = Page();

        // 입력 칸들 (저장 버튼이 읽음)
        var tTitle = new TextBox { Text = Db.AppName, Width = 260 };
        var tPub = new TextBox { Text = cfg.PublicUrl, Width = 420 };
        var tDdns = new TextBox { Text = cfg.Ddns, Width = 360 };
        var tOut = new TextBox { Text = cfg.ExternalPort > 0 ? cfg.ExternalPort.ToString() : "", Width = 100 };
        var tPort = new TextBox { Text = cfg.Port.ToString(), Width = 100 };
        var tPfx = new TextBox { Text = cfg.Pfx, Width = 360 };
        var tPfxPw = new TextBox { Text = cfg.PfxPassword, Width = 200, UseSystemPasswordChar = true };
        var chkClose = new CheckBox { Text = "창의 X 를 누르면 숨기지 않고 프로그램 종료", Checked = cfg.CloseExits };
        var chkDg = new CheckBox { Text = "자리수 검사 사용 (지정 자리수가 아니면 경고음)", Checked = cfg.DigitOn };
        var tDg = new TextBox { Text = cfg.DigitLens, Width = 160 };

        // 1. 프로그램 이름
        p.Controls.Add(HeadL("프로그램 이름"));
        var g1 = Grid();
        Fld(g1, "프로그램 이름", Pair(tTitle, Ui.Btn("이름 저장", (_, _) =>
        {
            db.SetTitle(tTitle.Text); tTitle.Text = Db.AppName; lblName.Text = Db.AppName;
            Text = Db.AppName + " 서버 v" + Db.Version; tray.Text = Db.AppName + " 서버";
            MessageBox.Show("프로그램 이름을 \"" + Db.AppName + "\" 로 바꿨습니다. 웹·폰 화면은 새로고침하면 바뀝니다.");
        }, 1)), "서버 창·웹·폰 화면 맨 위에 표시");
        p.Controls.Add(g1);

        // 2. Tailscale
        p.Controls.Add(HeadL("외부 접속 ① Tailscale Funnel (권장: 무료 HTTPS, 공유기 설정 불필요)"));
        var g2 = Grid(); Fld(g2, "외부 주소(https://…)", tPub); p.Controls.Add(g2);
        void Found(string text)
        {
            var m = Regex.Match(text ?? "", @"https://[A-Za-z0-9.\-]+\.ts\.net");
            if (m.Success) { tPub.Text = m.Value; MessageBox.Show("외부 주소를 찾았습니다: " + m.Value + "\n[설정 저장]을 누르면 서버 정보에 표시됩니다."); }
        }
        p.Controls.Add(Btns(
            Ui.Btn("Tailscale 상태", async (_, _) => { var r = await Task.Run(() => RunTs("status")); MessageBox.Show(r.Text == "" ? "(출력 없음)" : r.Text, "Tailscale 상태"); }, 3),
            Ui.Btn("Funnel 켜기 (외부 HTTPS 주소 만들기)", async (_, _) =>
            {
                var r = await Task.Run(() => RunTs("funnel --bg " + cfg.Port));
                MessageBox.Show(r.Text == "" ? "(출력 없음)" : r.Text + (r.Code != 0 ? "\n\n※ 안내에 웹 주소(login.tailscale.com…)가 있으면 브라우저로 열어 Funnel 을 허용한 뒤 다시 누르세요." : ""), "Funnel 켜기"); Found(r.Text);
            }, 1),
            Ui.Btn("Funnel 주소 확인", async (_, _) => { var r = await Task.Run(() => RunTs("funnel status")); MessageBox.Show(r.Text == "" ? "(출력 없음 — Funnel 이 꺼져 있을 수 있습니다)" : r.Text, "Funnel 상태"); Found(r.Text); }, 3),
            Ui.Btn("Funnel 끄기", async (_, _) => { var r = await Task.Run(() => RunTs("funnel reset")); MessageBox.Show(r.Text == "" ? "끝냈습니다." : r.Text, "Funnel 끄기"); }, 2),
            Ui.Btn("Tailscale 내려받기 페이지", (_, _) => { try { Process.Start(new ProcessStartInfo("https://tailscale.com/download/windows") { UseShellExecute = true }); } catch { } })));
        p.Controls.Add(Note("Tailscale 을 설치·로그인한 뒤 [Funnel 켜기]를 누르세요. 자세한 순서는 사용설명서의 'Tailscale 설정'을 보세요."));

        // 3. DDNS
        p.Controls.Add(HeadL("외부 접속 ② 공유기 DDNS (대안)"));
        var g3 = Grid(); Fld(g3, "DDNS 주소", tDdns); Fld(g3, "공유기 외부 포트", tOut, "비우면 서버 포트와 같다고 봅니다"); Fld(g3, "이 PC 서버 포트", tPort, "공유기에서 [외부 포트 → 이 PC 내부 IP : 서버 포트]로 포트 포워딩");
        p.Controls.Add(g3);
        p.Controls.Add(Btns(Ui.Btn("DDNS 연결 확인", async (_, _) => await CheckDdns(tDdns.Text.Trim()), 3)));

        // 4. HTTPS
        p.Controls.Add(HeadL("HTTPS 인증서 (인터넷에서 쓸 때 권장)"));
        var g4 = Grid();
        Fld(g4, "인증서 파일(.pfx)", Pair(tPfx, Ui.Btn("찾기", (_, _) => { using var od = new OpenFileDialog { Filter = "인증서 (*.pfx)|*.pfx" }; if (od.ShowDialog(this) == DialogResult.OK) tPfx.Text = od.FileName; })));
        Fld(g4, "인증서 암호", tPfxPw);
        p.Controls.Add(g4);
        p.Controls.Add(Btns(Ui.Btn("자체 서명 인증서 만들기", (_, _) =>
        {
            string host = tDdns.Text.Trim(); if (host == "") { MessageBox.Show("먼저 DDNS 주소를 입력하세요."); return; }
            string pw = tPfxPw.Text; if (pw == "") { pw = Ui.GenPassword(); tPfxPw.Text = pw; }
            string path = Path.Combine(AppContext.BaseDirectory, "z2roN_Proj.pfx");
            try { Ui.MakeSelfSigned(host, pw, path); tPfx.Text = path; MessageBox.Show("인증서를 만들었습니다.\n" + path + "\n[설정 저장] 후 다시 시작하세요.\n※ 자체 서명이라 브라우저에 경고가 나옵니다. 경고 없이 쓰려면 Let's Encrypt(win-acme) 인증서를 쓰세요."); }
            catch (Exception ex) { MessageBox.Show("만들지 못했습니다: " + ex.Message); }
        }, 4)));

        // 5. 창 동작
        p.Controls.Add(HeadL("창 동작"));
        var g5 = Grid(); Chk(g5, chkClose); p.Controls.Add(g5);

        // 6. 바코드 자리수
        p.Controls.Add(HeadL("바코드 자리수 알림 (이 서버 창에서만 적용)"));
        var g6 = Grid(); Chk(g6, chkDg); Fld(g6, "지정 자리수 (숫자)", tDg, "예: 13   (여러 개는 쉼표로 13,8,12) — 자리수가 달라도 경고음만 울리고 조회·추가는 그대로 진행합니다"); p.Controls.Add(g6);
        p.Controls.Add(Btns(Ui.Btn("경고음 들어보기", (_, _) => Ui.Alarm(), 3)));

        // 7. Windows
        p.Controls.Add(HeadL("Windows 설정"));
        p.Controls.Add(Btns(
            Ui.Btn("방화벽 포트 열기", (_, _) => OpenFirewall(), 4),
            Ui.Btn("Windows 시작 때 자동 실행 켜기", (_, _) => SetAutoStart(true), 4),
            Ui.Btn("자동 실행 끄기", (_, _) => SetAutoStart(false))));
        p.Controls.Add(Note("• 방화벽 포트 열기: 관리자 권한 확인 창이 뜹니다.\n• 자동 실행: 로그인할 때 트레이로 조용히 시작합니다. 서버 PC 는 Windows 자동 로그인(netplwiz)을 함께 쓰세요.\n• 이 프로그램은 일반 프로그램이라 작업 관리자 > 프로세스(또는 백그라운드 프로세스)의 z2roN_Proj 에서 [작업 끝내기]로 종료할 수 있습니다."));

        // 아래 고정 막대: 스크롤해도 항상 보이는 저장 버튼
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(16, 6, 16, 4), Tag = "soft", BackColor = Ui.BlueSoft };
        bar.Controls.Add(Ui.Btn("설정 저장", (_, _) =>
        {
            if (!int.TryParse(tPort.Text.Trim(), out var np) || np < 1 || np > 65535) { MessageBox.Show("서버 포트는 1~65535 숫자여야 합니다."); return; }
            int ne = 0; if (tOut.Text.Trim() != "" && (!int.TryParse(tOut.Text.Trim(), out ne) || ne < 1 || ne > 65535)) { MessageBox.Show("외부 포트는 1~65535 숫자여야 합니다."); return; }
            var dgl = System.Text.RegularExpressions.Regex.Matches(tDg.Text, @"\d+").Select(m => m.Value).Where(v => int.Parse(v) > 0).ToList();
            if (chkDg.Checked && dgl.Count == 0) { MessageBox.Show("지정 자리수를 숫자로 입력하세요. (예: 13)"); return; }
            cfg.DigitOn = chkDg.Checked; cfg.DigitBlock = false; cfg.DigitLens = dgl.Count == 0 ? "13" : string.Join(",", dgl);
            cfg.PublicUrl = tPub.Text.Trim().TrimEnd('/'); cfg.Ddns = tDdns.Text.Trim(); cfg.ExternalPort = ne; cfg.Port = np; cfg.Pfx = tPfx.Text.Trim(); cfg.PfxPassword = tPfxPw.Text; cfg.CloseExits = chkClose.Checked;
            cfg.Save();
            if (MessageBox.Show("저장했습니다. 포트·인증서 변경은 서버를 다시 시작해야 적용됩니다.\n지금 다시 시작할까요?", "설정 저장", MessageBoxButtons.YesNo) == DialogResult.Yes) Restart();
        }, 1));
        bar.Controls.Add(Ui.Btn("서버 다시 시작", (_, _) => Restart(), 4));
        bar.Controls.Add(new Label { Text = "입력을 바꾼 뒤 [설정 저장]을 눌러야 적용됩니다.", AutoSize = true, ForeColor = Color.Gray, Margin = new Padding(14, 10, 0, 0) });
        tp.Controls.Add(p); tp.Controls.Add(bar);
    }

    static string FindTailscale()
    {
        foreach (var p in new[] { @"C:\Program Files\Tailscale\tailscale.exe", @"C:\Program Files (x86)\Tailscale\tailscale.exe" }) if (File.Exists(p)) return p;
        return "tailscale";
    }

    /// tailscale 명령을 창 없이 실행하고 출력 글자를 돌려줌 (최대 25초)
    static (int Code, string Text) RunTs(string args)
    {
        try
        {
            var psi = new ProcessStartInfo(FindTailscale(), args) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
            using var pr = Process.Start(psi);
            var so = pr.StandardOutput.ReadToEndAsync(); var se = pr.StandardError.ReadToEndAsync();
            bool ok = pr.WaitForExit(25000);
            if (!ok) { try { pr.Kill(); } catch { } }
            Task.WaitAll(new Task[] { so, se }, 3000);
            string text = ((so.IsCompleted ? so.Result : "") + "\n" + (se.IsCompleted ? se.Result : "")).Trim();
            if (!ok) text += "\n\n(25초 안에 끝나지 않아 중단했습니다)";
            return (ok ? pr.ExitCode : -1, text);
        }
        catch (Exception ex) { return (-2, "Tailscale 을 실행하지 못했습니다. 설치·로그인되어 있는지 확인하세요.\n" + ex.Message); }
    }

    async Task CheckDdns(string host)
    {
        if (host == "") { MessageBox.Show("DDNS 주소를 입력하세요."); return; }
        string pub = "", ddnsIps = "", note = "";
        try { using var hc = new HttpClient { Timeout = TimeSpan.FromSeconds(8) }; pub = (await hc.GetStringAsync("https://api.ipify.org")).Trim(); } catch { pub = ""; }
        try { ddnsIps = string.Join(", ", (await Dns.GetHostAddressesAsync(host)).Where(a => a.AddressFamily == AddressFamily.InterNetwork).Select(a => a.ToString())); } catch { ddnsIps = ""; }
        if (pub == "") note = "이 PC 의 공인 IP 를 확인하지 못했습니다. 인터넷 연결을 확인하세요.";
        else if (ddnsIps == "") note = "DDNS 주소가 IP 로 변환되지 않습니다. 주소 철자와 공유기 DDNS 등록 상태를 확인하세요.";
        else if (ddnsIps.Split(',').Select(x => x.Trim()).Contains(pub)) note = "일치합니다. DDNS 가 이 공유기를 가리킵니다.\n포트 포워딩과 방화벽이 열려 있으면 외부에서 접속할 수 있습니다.";
        else note = "일치하지 않습니다.\n• DDNS 갱신이 아직 안 됐을 수 있습니다(공유기 DDNS 상태 확인).\n• 공유기 WAN IP 가 위 공인 IP 와 다르면 통신사 CGNAT 일 수 있어 외부 접속이 안 됩니다.";
        MessageBox.Show("이 PC 의 공인 IP: " + (pub == "" ? "(확인 실패)" : pub) + "\nDDNS(" + host + ") IP: " + (ddnsIps == "" ? "(확인 실패)" : ddnsIps) + "\n\n" + note, "DDNS 연결 확인");
    }

    void Restart()
    {
        try { Process.Start(new ProcessStartInfo(Environment.ProcessPath, "--restart --tray") { UseShellExecute = false }); }
        catch (Exception ex) { MessageBox.Show("다시 시작하지 못했습니다: " + ex.Message); return; }
        ReallyExit = true; tray.Visible = false; Application.Exit();
    }

    void OpenFirewall()
    {
        try
        {
            Process.Start(new ProcessStartInfo("netsh", "advfirewall firewall add rule name=\"z2roN_Proj\" dir=in action=allow protocol=TCP localport=" + cfg.Port) { Verb = "runas", UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden });
            MessageBox.Show("방화벽 규칙을 요청했습니다. (허용 창에서 [예]를 눌렀다면 적용됩니다)\n포트: " + cfg.Port);
        }
        catch (Exception ex) { MessageBox.Show("실행하지 못했습니다: " + ex.Message); }
    }

    void SetAutoStart(bool on)
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (on) k.SetValue("z2roN_Proj", "\"" + Environment.ProcessPath + "\" --tray"); else k.DeleteValue("z2roN_Proj", false);
            MessageBox.Show(on ? "Windows 로그인 때 자동으로 시작합니다." : "자동 실행을 껐습니다.");
        }
        catch (Exception ex) { MessageBox.Show("설정하지 못했습니다: " + ex.Message); }
    }
}
