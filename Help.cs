using System.Drawing.Printing;
using System.Text;

namespace ZSheet;

/// 도움말 내용(한 곳에서 관리). 서버 창 [도움말] 탭과 PDF/HTML 내려받기가 모두 이 내용을 씀 → 기능이 바뀌면 여기만 고치면 새 버전 PDF 에 자동 반영
static class HelpDoc
{
    // 줄 앞 표시: "- " 항목, 4칸 들여쓰기 하위 항목, 그 외 일반 문장
    public static List<(string Title, string[] Lines)> Sections() => new()
    {
        ("1. 시작하기", new[] {
            "서버 = Windows PC 1대에서 이 프로그램을 실행해 둡니다. 데이터는 프로그램 폴더의 data 폴더(파일 1개)에 저장되어 SQL 설치가 필요 없습니다.",
            "클라이언트(PC·폰)는 앱 설치 없이 웹 브라우저로 접속합니다. 주소는 [서버 정보] 탭 또는 맨 위 막대의 접속 주소입니다. (예: http://192.168.0.10:8080)",
            "- 처음 실행하면 관리자 아이디·암호를 만듭니다. 이후 서버 창을 열 때마다 관리자 로그인이 필요합니다.",
            "- 창의 X 를 누르면 트레이로 숨고 서버는 계속 실행됩니다. 완전히 끄려면 [서버 정보] > [서버 종료] 또는 트레이 아이콘 우클릭 > 서버 종료.",
            "- .NET 런타임이 없는 PC 는 tools\\install_runtime_and_run.bat 를 실행하면 자동 설치 후 시작합니다(경량판)." }),
        ("2. 서버 설정 (설정 탭)", new[] {
            "- 프로그램 이름: 서버 창·웹·폰 맨 위에 표시되는 이름을 바꿉니다. [이름 저장].",
            "- 서버 포트: 이 PC 가 듣는 포트(기본 8080). 바꾸면 [설정 저장] 후 다시 시작해야 적용됩니다.",
            "- 방화벽 포트 열기: 같은 내부망의 폰·PC 가 접속하려면 한 번 눌러 주세요(관리자 확인 창).",
            "- Windows 시작 때 자동 실행: 켜 두면 로그인 때 트레이로 조용히 시작합니다.",
            "- 바코드 자리수: 지정 자리수(예: 13)가 아닌 바코드를 스캔하면 경고음만 울리고 조회·추가는 계속됩니다. 이 서버 창에만 적용되며 PC·폰은 각자 따로 설정합니다.",
            "- 입력을 바꾼 뒤에는 아래 고정 막대의 [설정 저장]을 꼭 누릅니다." }),
        ("3. 외부(인터넷)에서 접속하기", new[] {
            "방법 ① Tailscale Funnel (권장: 무료 HTTPS, 공유기 설정 불필요)",
            "- 서버 PC 에 Tailscale 을 설치·로그인 → [Funnel 켜기] → 나온 https 주소가 외부 주소로 자동 입력됩니다 → [설정 저장].",
            "- 폰 카메라 바코드 스캔은 HTTPS 에서만 동작하므로 외부·카메라를 쓰려면 이 방법이 가장 쉽습니다.",
            "방법 ② 공유기 DDNS",
            "- DDNS 주소·외부 포트를 입력하고, 공유기에서 [외부 포트 → 서버 PC 내부 IP : 서버 포트] 포트 포워딩을 설정합니다. [DDNS 연결 확인]으로 점검.",
            "- 인터넷에 열 때는 HTTPS 인증서(pfx)를 지정하세요. [자체 서명 인증서 만들기]는 브라우저 경고가 나옵니다." }),
        ("4. 데이터 탭 사용법", new[] {
            "- 시트 = 엑셀 한 장(표). 위쪽 [시트]에서 고르고 [새 시트]로 만듭니다. 셀을 두 번 누르거나 F2 로 고치면 바로 저장됩니다.",
            "- [엑셀 올리기]: 파일을 끌어다 창에 놓아도 됩니다. 첫 줄은 머리글이어야 하고 기준열(바코드)이 같아야 합니다.",
            "    · 추가·수정(권장): 같은 바코드는 수정, 없는 바코드는 추가, 파일에 없는 기존 행은 그대로 둠",
            "    · 동기화: 파일이 기준. 파일에 없는 행은 삭제됩니다",
            "    · 새 시트로 만들기 / 현재 시트 아래에 추가 / 덮어쓰기(기존 행 삭제)",
            "- [엑셀 받기]: 현재 시트를 xlsx 로 저장. [시트·열 설정]: 시트 이름 바꾸기, 열 추가·이름 변경·삭제.",
            "- 검색칸: 모든 열에서 검색. 머리글을 누르면 정렬. 한 쪽에 200행씩 보이며 ◀ ▶ 로 넘깁니다.",
            "- 바코드 조회: 스캐너로 찍거나 입력 후 Enter. [상품 시트 만들기]는 바코드·판매가1~10·상품명·입고단가·단위 열을 가진 시트를 만듭니다." }),
        ("5. 같은 바코드가 여러 개일 때 (상품 도구)", new[] {
            "- [단위 우선순위]: 개·갑·박스·묶음·속·장 중 스캔할 때 먼저 쓸 단위를 정합니다. 정한 단위만으로 진행됩니다.",
            "- [중복 바코드 지정]: 단위가 같고 상품명만 다른 경우 어떤 상품을 쓸지 관리자가 지정합니다. 클라이언트는 고를 수 없고 정한 대로 진행됩니다.",
            "    · 바코드별 보기: 왼쪽에서 바코드를 고르고 오른쪽 후보 중 사용할 행을 골라 [이 행으로 지정]",
            "    · 전체 한눈에 보기: 모든 후보를 한 표에 펼쳐 열 머리글(예: 단위)로 정렬하고 체크박스로 고른 뒤 [확인]으로 한꺼번에 지정. [이 단위로 체크]로 단위별 일괄 체크도 가능(같은 바코드에 2개 이상 체크하면 건너뜀)" }),
        ("6. 사용자 관리 (사용자 탭)", new[] {
            "- [사용자 추가]로 클라이언트 계정을 만듭니다(최대 10명, 관리자 제외). 비밀번호 6자 이상, [자동 생성] 가능.",
            "- 권한: 관리자 = 전부 / 사용자 = 행 추가·수정·삭제, 올리기 / 조회전용 = 보기·엑셀 받기.",
            "- [비밀번호 재설정]·[삭제]는 표에서 사용자를 고른 뒤 누릅니다.",
            "- '클라이언트도 시트·열 만들기/바꾸기 허용' 체크를 끄면 관리자만 구조를 바꿀 수 있습니다.",
            "- 관리자 비밀번호를 잊었을 때: 로그인 창의 '비밀번호를 잊으셨나요?' 또는 tools\\reset_admin.bat (서버 PC 의 Windows 관리자 승인 필요)." }),
        ("7. PC·폰(웹) 사용법", new[] {
            "- 브라우저에서 서버 주소로 접속 → 받은 아이디·암호로 로그인.",
            "- 바코드 조회: 스캐너·카메라로 스캔. 카메라는 HTTPS(Tailscale) 접속에서 동작합니다.",
            "- DB 보기: 별도 화면에서 시트 내용을 봅니다. 관리자가 새 파일을 올렸으면 [데이터 갱신]을 눌러 최신으로 불러옵니다(자동 갱신 없음).",
            "- 엑셀 올리기(권한 있는 사용자): 파일 선택 또는 끌어놓기 후 올리기 방식을 고릅니다. 덮어쓰기는 관리자만 가능.",
            "- 가격POP: 작업명과 [할인가 계산 기준 가격](소비자가·판매가1~10·입고가 중 선택)을 먼저 정합니다. 입고가 기준이면 마진율 %, 그 밖의 가격이면 할인율 %를 적용합니다. 그다음 바코드를 스캔해 56×32mm 라벨 이미지를 만듭니다(할인가는 빨간색으로 표시). 폰에서 저장한 작업은 PC 에서 불러와 출력합니다. 저장 후 수정 불가, 삭제는 관리자만.",
            "- 폰 화면 키보드: 바코드 조회·매입·가격POP 스캔 줄의 [키보드 켜짐/꺼짐] 버튼으로 끌 수 있습니다. 끄면 스캔 후에도 키보드가 올라오지 않습니다(직접 입력하려면 다시 켜세요).",
            "- 가격POP 스캔: 바코드를 스캔하면 바로 추가됩니다. [마지막 입력 삭제]로 직전 입력을 취소하고, [삭제 스캔 모드]를 켜고 스캔하면 그 상품이 목록에서 삭제됩니다.",
            "- 수량점검: 폰 메뉴 [수량점검] → 작업명 입력(상품 시트는 선택) → 바코드 스캔(바로 추가, 수량칸에 한 번에 더할 수량) → [엑셀 내보내기]에서 [내 파일로 저장] 또는 [공유]. 바코드·수량·매입가가 담긴 xlsx 가 폰에 만들어지며 서버로는 올라가지 않습니다.",
            "- 매입(입고): 바코드·상품명·수량·입고단가를 미리 올려 두고 스캔하며 수량을 확인합니다." }),
        ("8. 백업과 초기화", new[] {
            "- 자동 백업: 매일 1회, 데이터 폴더\\backup 에 14일 보관. [서버 정보] > [지금 백업]으로 즉시 백업.",
            "- 복원: 서버를 끄고 backup 폴더의 .db 파일을 data 폴더의 DB 파일과 교체합니다.",
            "- 전체 데이터 초기화(서버 정보 탭, 관리자 전용): 관리자 아이디·비밀번호 → \"초기화\" 입력 → 최종 확인 순서. 시트·행·구매·POP·기록이 삭제되며 계정과 설정은 유지, 삭제 직전 자동 백업됩니다." }),
        ("9. 화면 스킨", new[] {
            "- 서버 창 맨 위 오른쪽의 색 네모(파랑 / 흑백 / 파스텔)를 누르면 바로 바뀌고 다음 실행에도 유지됩니다.",
            "- 버튼 색 규칙: 파랑 = 만들기·올리기·저장, 초록 = 조회·받기·확인, 주황 = 설정·도구, 보라 = 상품 전용, 빨간 글자 = 삭제·종료·초기화." }),
        ("10. 문제가 생겼을 때", new[] {
            "- 폰에서 연결 안 됨: 같은 와이파이인지, 서버 PC 방화벽(포트 열기), 주소의 IP·포트를 확인. 실행 폴더의 errors.log 도 확인하세요.",
            "- 서버 창에 파일 끌어놓기가 안 됨: 서버를 '관리자 권한 없이' 실행하거나 [엑셀 올리기] 버튼을 쓰세요.",
            "- GitHub 빌드 zip 압축 해제 오류: Releases 의 zip 을 받거나 경량판을 쓰세요.",
            "- 30만 행 이상도 올릴 수 있습니다(올리기 시간이 걸릴 수 있음)." }),
    };

    public static string Html()
    {
        string E(string s) => System.Net.WebUtility.HtmlEncode(s);
        var sb = new StringBuilder("<!doctype html><html lang=\"ko\"><head><meta charset=\"utf-8\"><title>" + E(Db.AppName) + " 도움말</title><style>body{font-family:'Malgun Gothic',sans-serif;max-width:800px;margin:24px auto;line-height:1.6;color:#223}h1{color:#1b3f9c}h2{color:#2b63d9;border-bottom:2px solid #d9e5fb;padding-bottom:4px;margin-top:28px}p{margin:4px 0}.i{margin-left:18px}.i2{margin-left:44px;color:#445}</style></head><body>");
        sb.Append("<h1>" + E(Db.AppName) + " 도움말 <small style=\"font-size:60%;color:#789\">v" + E(Db.Version) + "</small></h1>");
        foreach (var (t, ls) in Sections())
        {
            sb.Append("<h2>" + E(t) + "</h2>");
            foreach (var l in ls) { string c = l.StartsWith("    ") ? "i2" : l.StartsWith("- ") ? "i" : ""; sb.Append("<p class=\"" + c + "\">" + E(l.Trim()) + "</p>"); }
        }
        return sb.Append("</body></html>").ToString();
    }

    /// Windows 기본 'Microsoft Print to PDF' 프린터로 PDF 파일 생성 (별도 프로그램 불필요). 실패하면 예외
    public static void SavePdf(string path)
    {
        var items = new List<(string Text, int Kind)>();   // 0 제목, 1 소제목, 2 본문, 3 항목, 4 하위 항목
        items.Add((Db.AppName + " 도움말  v" + Db.Version, 0));
        foreach (var (t, ls) in Sections())
        {
            items.Add((t, 1));
            foreach (var l in ls) items.Add((l.Trim(), l.StartsWith("    ") ? 4 : l.StartsWith("- ") ? 3 : 2));
        }
        using var pd = new PrintDocument();
        pd.PrinterSettings.PrinterName = "Microsoft Print to PDF";
        if (!pd.PrinterSettings.IsValid) throw new InvalidOperationException("'Microsoft Print to PDF' 프린터를 찾을 수 없습니다.");
        pd.PrinterSettings.PrintToFile = true; pd.PrinterSettings.PrintFileName = path;
        pd.DocumentName = Db.AppName + " 도움말";
        pd.DefaultPageSettings.Margins = new Margins(70, 70, 70, 80);
        using var fT = new Font("Malgun Gothic", 18f, FontStyle.Bold); using var fH = new Font("Malgun Gothic", 12.5f, FontStyle.Bold); using var fB = new Font("Malgun Gothic", 10f); using var fF = new Font("Malgun Gothic", 8.5f);
        int idx = 0, page = 0;
        pd.PrintPage += (_, e) =>
        {
            page++;
            var g = e.Graphics; var m = e.MarginBounds; float y = m.Top;
            while (idx < items.Count)
            {
                var (text, kind) = items[idx];
                var font = kind == 0 ? fT : kind == 1 ? fH : fB;
                float indent = kind == 3 ? 14 : kind == 4 ? 38 : 0;
                float gapTop = kind == 1 ? 14 : kind == 0 ? 0 : 3;
                var brush = kind == 0 || kind == 1 ? Brushes.MidnightBlue : Brushes.Black;
                var sz = g.MeasureString(text, font, (int)(m.Width - indent));
                if (y + gapTop + sz.Height > m.Bottom && y > m.Top) { e.HasMorePages = true; break; }
                y += gapTop;
                g.DrawString(text, font, brush, new RectangleF(m.Left + indent, y, m.Width - indent, sz.Height + 2));
                y += sz.Height + (kind == 1 ? 4 : 0);
                if (kind == 1) g.DrawLine(Pens.LightSteelBlue, m.Left, y, m.Right, y);
                idx++;
            }
            g.DrawString(Db.AppName + " v" + Db.Version + "  ·  " + page, fF, Brushes.Gray, m.Left, m.Bottom + 24);
            if (idx >= items.Count) e.HasMorePages = false;
        };
        pd.Print();
    }
}
