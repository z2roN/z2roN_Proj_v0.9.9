namespace ZSheet;

/// 서버 관리 창의 상품 도구: 단위 우선순위, 중복 바코드 지정
static class ProductForms
{
    public static readonly string[] ProductHeads = { "바코드", "상품명", "단위", "입고단가", "판매가1", "판매가2", "판매가3", "판매가4", "판매가5", "판매가6", "판매가7", "판매가8", "판매가9", "판매가10" };

    static Form Dlg(string title, int w, int h) => new Form { Text = title, Width = w, Height = h, StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false, MinimizeBox = false, Font = new Font("Malgun Gothic", 9.5f), BackColor = Color.White };

    /// 같은 바코드가 여러 단위로 있을 때 어느 단위를 쓸지 순서 정하기 (위에 있을수록 우선)
    public static void UnitOrder(IWin32Window owner, Db db, long sid)
    {
        var (_, uk, _) = Product.Cols(db, sid);
        if (uk == null) { MessageBox.Show("이 시트에는 '단위' 열이 없습니다."); return; }
        var units = Product.Units(db, sid);
        var list = Product.UnitOrder(db, sid).ToList();
        foreach (var u in units) if (!list.Contains(u, StringComparer.OrdinalIgnoreCase)) list.Add(u);
        using var f = Dlg("단위 우선순위", 480, 440); f.FormBorderStyle = FormBorderStyle.FixedDialog; f.MaximizeBox = false;
        f.Controls.Add(new Label { Left = 14, Top = 12, Width = 440, Height = 60, Text = "같은 바코드가 여러 단위(개, 갑, 박스…)로 등록되어 있을 때, 맨 위 단위의 행만 바코드 스캔 결과로 쓰입니다.\n▲▼ 로 순서를 바꾼 뒤 [저장]을 누르세요." });
        var lb = new ListBox { Left = 14, Top = 80, Width = 300, Height = 260, Font = new Font("Malgun Gothic", 11f) };
        foreach (var u in list) lb.Items.Add(u);
        if (lb.Items.Count > 0) lb.SelectedIndex = 0;
        f.Controls.Add(lb);
        void Move(int d)
        {
            int i = lb.SelectedIndex, j = i + d; if (i < 0 || j < 0 || j >= lb.Items.Count) return;
            var x = lb.Items[i]; lb.Items[i] = lb.Items[j]; lb.Items[j] = x; lb.SelectedIndex = j;
        }
        var up = Ui.Btn("▲ 위로", (_, _) => Move(-1)); up.Left = 326; up.Top = 80; f.Controls.Add(up);
        var dn = Ui.Btn("▼ 아래로", (_, _) => Move(1)); dn.Left = 326; dn.Top = 120; f.Controls.Add(dn);
        var save = Ui.Btn("저장", (_, _) =>
        {
            Product.SetUnitOrder(db, sid, lb.Items.Cast<object>().Select(x => x.ToString()).ToList(), Store.Admin);
            MessageBox.Show("저장했습니다.\n우선순위: " + string.Join(" > ", lb.Items.Cast<object>()), "단위 우선순위"); f.Close();
        }, 1);
        save.Left = 326; save.Top = 300; f.Controls.Add(save);
        var close = Ui.Btn("닫기", (_, _) => f.Close()); close.Left = 326; close.Top = 340; f.Controls.Add(close);
        f.ShowDialog(owner);
    }

    static string StateText(string st) => st switch { "pick" => "관리자 지정", "unit" => "단위 우선순위로 결정", "conflict" => "충돌 — 지정 필요", _ => st };

    /// 같은 바코드가 여러 줄인 것을 보고, 사용할 행(단위+상품명)을 관리자가 지정
    public static void Dups(IWin32Window owner, Db db, long sid)
    {
        var (ck, uk, nk) = Product.Cols(db, sid);
        if (ck == null) { MessageBox.Show("이 시트에는 바코드 열이 없습니다."); return; }
        var cols = Store.Columns(db, sid).Where(x => x.Key != ck).ToList();
        using var f = Dlg("중복 바코드 지정", 1240, 740); f.MinimumSize = new Size(960, 520);

        var chk = new CheckBox { Text = "충돌(지정 필요)만 보기", Checked = true, AutoSize = true, Margin = new Padding(8, 9, 8, 0) };
        var gL = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, MultiSelect = false }; Ui.StyleGrid(gL); gL.SelectionMode = DataGridViewSelectionMode.FullRowSelect; gL.RowHeadersVisible = false;
        gL.Columns.Add("code", "바코드"); gL.Columns.Add("state", "상태"); gL.Columns.Add("n", "후보");
        gL.Columns[0].Width = 150; gL.Columns[1].Width = 150; gL.Columns[2].Width = 50;
        var left = new Panel { Dock = DockStyle.Left, Width = 380 };
        var topL = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, BackColor = Ui.BlueSoft }; topL.Controls.Add(chk);
        left.Controls.Add(gL); left.Controls.Add(topL);

        var gR = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, MultiSelect = false }; Ui.StyleGrid(gR); gR.SelectionMode = DataGridViewSelectionMode.FullRowSelect; gR.RowHeadersVisible = false;
        gR.Columns.Add("res", "스캔 결과");
        foreach (var c in cols) gR.Columns.Add(c.Key, c.Name);
        gR.Columns[0].Width = 90;
        // 열 머리글을 누르면 정렬(예: 단위), 머리글을 끌면 열 위치 변경
        foreach (var g0 in new[] { gL, gR }) { g0.AllowUserToOrderColumns = true; foreach (DataGridViewColumn c0 in g0.Columns) c0.SortMode = DataGridViewColumnSortMode.Automatic; }
        var lbl = new Label { Dock = DockStyle.Top, Height = 46, Padding = new Padding(8, 6, 8, 0), Text = "왼쪽에서 바코드를 고르면 후보가 보입니다. 사용할 행을 선택하고 [이 행으로 지정]을 누르면, 그 바코드는 그 행의 상품으로만 스캔됩니다." };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(6), BackColor = Ui.BlueSoft };
        var right = new Panel { Dock = DockStyle.Fill };
        right.Controls.Add(gR); right.Controls.Add(lbl); right.Controls.Add(bar);

        // ---- 전체 한눈에 보기: 모든 중복 후보를 한 표에 놓고 → 열 머리글로 정렬(예: 단위) → 체크 → [확인]으로 한꺼번에 지정 ----
        var chk2 = new CheckBox { Text = "충돌(지정 필요)만 보기", Checked = true, AutoSize = true, Margin = new Padding(16, 9, 8, 0) };
        var cbUnit = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110, Margin = new Padding(3, 6, 3, 3) };
        var gA = new DataGridView { Dock = DockStyle.Fill, MultiSelect = false }; Ui.StyleGrid(gA); gA.ReadOnly = false; gA.RowHeadersVisible = false; gA.SelectionMode = DataGridViewSelectionMode.FullRowSelect; gA.AllowUserToAddRows = false; gA.AllowUserToOrderColumns = true;
        gA.Columns.Add(new DataGridViewCheckBoxColumn { Name = "chk", HeaderText = "선택", Width = 52 });
        gA.Columns.Add("code", "바코드"); gA.Columns.Add("state", "상태");
        foreach (var cx in cols) gA.Columns.Add(cx.Key, cx.Name);
        foreach (DataGridViewColumn c0 in gA.Columns) { c0.SortMode = DataGridViewColumnSortMode.Automatic; c0.ReadOnly = c0.Name != "chk"; }
        gA.Columns[1].Width = 140; gA.Columns[2].Width = 150;
        var lblAll = new Label { AutoSize = true, ForeColor = Color.Gray, Margin = new Padding(12, 11, 0, 0) };
        var lblSel = new Label { AutoSize = true, ForeColor = Ui.BlueDark, Font = new Font("Malgun Gothic", 9.5f, FontStyle.Bold), Margin = new Padding(14, 11, 0, 0), Text = "체크 0건" };
        gA.CurrentCellDirtyStateChanged += (_, _) => { if (gA.IsCurrentCellDirty) gA.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        gA.CellValueChanged += (_, e) => { if (e.ColumnIndex == 0) lblSel.Text = "체크 " + gA.Rows.Cast<DataGridViewRow>().Count(r => r.Cells[0].Value is true) + "건"; };

        List<DupInfo> dups = new(); DupInfo cur = null; bool filling = false;
        void FillAll()
        {
            gA.Rows.Clear(); var units = new List<string>();
            foreach (var d in dups)
            {
                if (chk.Checked && d.State != "conflict") continue;
                foreach (var cd in d.Cands)
                {
                    var vals = new object[cols.Count + 3];
                    bool used = d.Resolved != null && d.Resolved.Id == cd.Id;
                    vals[0] = false; vals[1] = d.Code; vals[2] = StateText(d.State) + (used ? "  ✔ 사용" : "");
                    for (int j = 0; j < cols.Count; j++) vals[j + 3] = cd.Data[cols[j].Key]?.ToString() ?? "";
                    int ri = gA.Rows.Add(vals); gA.Rows[ri].Tag = Tuple.Create(d, cd);
                    if (used) gA.Rows[ri].DefaultCellStyle.BackColor = Color.FromArgb(223, 244, 232); else if (d.State == "conflict") gA.Rows[ri].DefaultCellStyle.ForeColor = Ui.Danger;
                    if (!string.IsNullOrEmpty(cd.Unit) && !units.Contains(cd.Unit)) units.Add(cd.Unit);
                }
            }
            var ord = Product.UnitOrder(db, sid);
            units = units.OrderBy(u => { int i = ord.IndexOf(u); return i < 0 ? 999 : i; }).ThenBy(u => u).ToList();
            var keepU = cbUnit.SelectedItem as string; cbUnit.Items.Clear(); foreach (var u in units) cbUnit.Items.Add(u);
            if (keepU != null && units.Contains(keepU)) cbUnit.SelectedItem = keepU; else if (cbUnit.Items.Count > 0) cbUnit.SelectedIndex = 0;
            lblAll.Text = "행 " + gA.Rows.Count + "개 · 열 머리글을 누르면 정렬, 머리글을 끌면 열 위치 변경";
            lblSel.Text = "체크 0건";
        }
        Dictionary<string, List<DataGridViewRow>> GroupRows()
        {
            var g = new Dictionary<string, List<DataGridViewRow>>(StringComparer.OrdinalIgnoreCase);
            foreach (DataGridViewRow r in gA.Rows) if (r.Tag is Tuple<DupInfo, Cand> t) { if (!g.TryGetValue(t.Item1.Code, out var l)) g[t.Item1.Code] = l = new(); l.Add(r); }
            return g;
        }
        void CheckByUnit()
        {
            if (cbUnit.SelectedItem is not string u) { MessageBox.Show(f, "체크할 단위를 고르세요."); return; }
            int hit = 0, amb = 0;
            foreach (var kv in GroupRows())
            {
                var match = kv.Value.Where(r => ((Tuple<DupInfo, Cand>)r.Tag).Item2.Unit == u).ToList();
                foreach (var r in kv.Value) r.Cells[0].Value = false;
                if (match.Count == 1) { match[0].Cells[0].Value = true; hit++; } else if (match.Count > 1) amb++;
            }
            MessageBox.Show(f, "단위 [" + u + "] 로 " + hit + "건을 체크했습니다." + (amb > 0 ? "\n같은 단위가 여러 줄(상품명만 다름)이라 자동으로 고를 수 없는 " + amb + "건은 체크하지 않았습니다. 직접 체크하세요." : "") + "\n\n내용을 확인하고 [확인 — 체크한 행으로 지정]을 누르세요.", "단위로 체크");
        }
        void ApplyChecked()
        {
            var groups = new Dictionary<string, List<Cand>>(StringComparer.OrdinalIgnoreCase);
            foreach (DataGridViewRow r in gA.Rows)
                if (r.Cells[0].Value is true && r.Tag is Tuple<DupInfo, Cand> t) { if (!groups.TryGetValue(t.Item1.Code, out var l)) groups[t.Item1.Code] = l = new(); l.Add(t.Item2); }
            if (groups.Count == 0) { MessageBox.Show(f, "체크한 행이 없습니다."); return; }
            var multi = groups.Where(x => x.Value.Count > 1).Select(x => x.Key).ToList();
            string msg = "체크한 " + groups.Count + "개 바코드 중 " + (groups.Count - multi.Count) + "개를 그 행의 상품으로 지정합니다." +
                (multi.Count > 0 ? "\n\n※ 한 바코드에 2개 이상 체크된 " + multi.Count + "건은 건너뜁니다 (예: " + string.Join(", ", multi.Take(3)) + ")" : "") + "\n\n계속할까요?";
            if (MessageBox.Show(f, msg, "지정 확인", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            int ok = 0;
            foreach (var x in groups) if (x.Value.Count == 1) { Product.SetPick(db, sid, x.Key, x.Value[0].Unit, x.Value[0].Name, Store.Admin); ok++; }
            Fill(null);
            MessageBox.Show(f, ok + "건을 지정했습니다." + (multi.Count > 0 ? "\n건너뜀 " + multi.Count + "건 (한 바코드에 여러 개 체크)" : ""), "지정 완료");
        }
        void ShowCands()
        {
            gR.Rows.Clear(); cur = null;
            if (gL.CurrentRow == null || gL.CurrentRow.Tag is not DupInfo di) return;
            cur = di;
            foreach (var cd in di.Cands)
            {
                var vals = new object[cols.Count + 1];
                vals[0] = di.Resolved != null && di.Resolved.Id == cd.Id ? "✔ 사용" : "";
                for (int j = 0; j < cols.Count; j++) vals[j + 1] = cd.Data[cols[j].Key]?.ToString() ?? "";
                int ri = gR.Rows.Add(vals); gR.Rows[ri].Tag = cd;
                if (vals[0].ToString() != "") gR.Rows[ri].DefaultCellStyle.BackColor = Color.FromArgb(223, 244, 232);
            }
        }
        void Fill(string keep)
        {
            filling = true;
            try
            {
                dups = Product.Dups(db, sid); gL.Rows.Clear();
                foreach (var d in dups)
                {
                    if (chk.Checked && d.State != "conflict") continue;
                    int i = gL.Rows.Add(d.Code, StateText(d.State), d.Cands.Count); gL.Rows[i].Tag = d;
                    if (d.State == "conflict") gL.Rows[i].DefaultCellStyle.ForeColor = Ui.Danger;
                    if (keep != null && string.Equals(d.Code, keep, StringComparison.OrdinalIgnoreCase)) gL.CurrentCell = gL.Rows[i].Cells[0];
                }
                f.Text = "중복 바코드 지정 — 충돌 " + dups.Count(x => x.State == "conflict") + "건 / 중복 전체 " + dups.Count + "건";
            }
            finally { filling = false; }
            ShowCands(); FillAll();
        }
        gL.SelectionChanged += (_, _) => { if (!filling) ShowCands(); };
        chk.CheckedChanged += (_, _) => { if (chk2.Checked != chk.Checked) chk2.Checked = chk.Checked; Fill(null); };
        chk2.CheckedChanged += (_, _) => { chk.Checked = chk2.Checked; };

        bar.Controls.Add(Ui.Btn("이 행으로 지정", (_, _) =>
        {
            if (cur == null || gR.CurrentRow?.Tag is not Cand cd) { MessageBox.Show("지정할 행을 선택하세요."); return; }
            Product.SetPick(db, sid, cur.Code, cd.Unit, cd.Name, Store.Admin); Fill(cur.Code);
        }, 1));
        bar.Controls.Add(Ui.Btn("지정 해제", (_, _) => { if (cur == null) return; Product.ClearPick(db, sid, cur.Code, Store.Admin); Fill(cur.Code); }));
        bar.Controls.Add(Ui.Btn("닫기", (_, _) => f.Close()));
        bar.Controls.Add(new Label { Text = "※ 지정해도 다른 행은 지워지지 않습니다. 엑셀 동기화를 해도 지정은 유지됩니다.", AutoSize = true, ForeColor = Color.Gray, Margin = new Padding(12, 9, 0, 0) });

        // 보기 방식 2가지: [바코드별 보기] 왼쪽에서 바코드를 고르며 하나씩 / [전체 한눈에 보기] 한 표에서 정렬·체크 후 한꺼번에
        var splitView = new Panel { Dock = DockStyle.Fill }; splitView.Controls.Add(right); splitView.Controls.Add(left);
        var allView = new Panel { Dock = DockStyle.Fill, Visible = false };
        var allTop = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(6, 3, 6, 3), BackColor = Ui.BlueSoft };
        allTop.Controls.Add(new Label { Text = "단위로 한번에 체크:", AutoSize = true, Margin = new Padding(6, 11, 2, 0) }); allTop.Controls.Add(cbUnit);
        allTop.Controls.Add(Ui.Btn("이 단위로 체크", (_, _) => CheckByUnit(), 3, 130)); allTop.Controls.Add(Ui.Btn("체크 모두 해제", (_, _) => { foreach (DataGridViewRow r in gA.Rows) r.Cells[0].Value = false; }, 0, 130)); allTop.Controls.Add(chk2);
        var allBottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(6), BackColor = Ui.BlueSoft };
        allBottom.Controls.Add(Ui.Btn("확인 — 체크한 행으로 지정", (_, _) => ApplyChecked(), 1, 220)); allBottom.Controls.Add(Ui.Btn("닫기", (_, _) => f.Close(), 0, 90)); allBottom.Controls.Add(lblSel); allBottom.Controls.Add(lblAll);
        allView.Controls.Add(gA); allView.Controls.Add(allBottom); allView.Controls.Add(allTop);
        var bView = Ui.Btn("바코드별 보기", null, 1, 150); var bAll = Ui.Btn("전체 한눈에 보기 (정렬·체크)", null, 0, 220);
        void Mode(bool all) { splitView.Visible = !all; allView.Visible = all; Ui.StyleBtn(bView, all ? 0 : 1); Ui.StyleBtn(bAll, all ? 1 : 0); }
        bView.Click += (_, _) => Mode(false); bAll.Click += (_, _) => Mode(true);
        var modeBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(6, 5, 6, 3), BackColor = Color.White };
        modeBar.Controls.Add(bView); modeBar.Controls.Add(bAll);
        f.Controls.Add(splitView); f.Controls.Add(allView); f.Controls.Add(modeBar);   // 가운데(Fill) 먼저
        f.Load += (_, _) => Fill(null);
        f.ShowDialog(owner);
    }
}
