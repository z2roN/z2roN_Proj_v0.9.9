using System.Text.Json.Nodes;

namespace ZSheet;

/// config.json (프로그램 폴더). 서버 화면의 [서버 설정]에서 고칩니다.
public class Config
{
    public int Port = 8080;               // 이 PC 에서 서버가 듣는 포트
    public int ExternalPort;              // 공유기에서 밖으로 열어 둔 포트(0 이면 Port 와 같음)
    public string PublicUrl = "";         // 외부에서 접속하는 전체 주소 (예: Tailscale Funnel 의 https://pc.xxxx.ts.net)
    public string Ddns = "";              // 공유기 DDNS 주소 (예: myhome.iptime.org)
    public string Pfx = "", PfxPassword = "";   // HTTPS 인증서
    public string DataDir = "data";
    public bool DigitOn, DigitBlock;      // 서버 창 바코드 자리수 검사 (이 서버 창에서만 적용)
    public string DigitLens = "13";       // 지정 자리수 (여러 개는 쉼표: 13,8,12)
    public int Skin;                      // 화면 스킨 (0 파랑, 1 흑백, 2 파스텔)
    public bool CloseExits;               // 창 닫기(X) 때 숨기지 않고 프로그램 종료

    public List<int> DigitList() => DigitLens.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(x => int.TryParse(x, out var n) ? n : 0).Where(n => n > 0).ToList();

    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "config.json");
    public int OutPort => ExternalPort > 0 ? ExternalPort : Port;
    public bool Https => Pfx != "" && File.Exists(Pfx);
    public string FullDataDir => Path.IsPathRooted(DataDir) ? DataDir : Path.Combine(AppContext.BaseDirectory, DataDir);
    public string ExternalUrl => PublicUrl != "" ? PublicUrl : Ddns == "" ? "" : (Https ? "https" : "http") + "://" + Ddns + ":" + OutPort;

    public static Config Load()
    {
        var c = new Config();
        try
        {
            if (File.Exists(FilePath) && JsonNode.Parse(File.ReadAllText(FilePath)) is JsonObject o)
            {
                if (int.TryParse(o["port"]?.ToString(), out var p) && p > 0 && p < 65536) c.Port = p;
                if (int.TryParse(o["externalPort"]?.ToString(), out var ep) && ep > 0 && ep < 65536) c.ExternalPort = ep;
                c.PublicUrl = (o["publicUrl"]?.ToString() ?? "").Trim().TrimEnd('/'); c.Ddns = o["ddns"]?.ToString() ?? ""; c.Pfx = o["pfx"]?.ToString() ?? ""; c.PfxPassword = o["pfxPassword"]?.ToString() ?? "";
                var dd = o["dataDir"]?.ToString(); if (!string.IsNullOrWhiteSpace(dd)) c.DataDir = dd;
                c.CloseExits = o["closeExits"]?.ToString() == "true";
                if (int.TryParse(o["skin"]?.ToString(), out var sk) && sk >= 0 && sk <= 2) c.Skin = sk;
                c.DigitOn = o["digitOn"]?.ToString() == "true"; c.DigitBlock = o["digitBlock"]?.ToString() == "true"; var dl = o["digitLens"]?.ToString(); if (!string.IsNullOrWhiteSpace(dl)) c.DigitLens = dl;
            }
        }
        catch { }
        if (!File.Exists(FilePath)) c.Save();
        return c;
    }

    public void Save()
    {
        var o = new JsonObject { ["port"] = Port, ["externalPort"] = ExternalPort, ["publicUrl"] = PublicUrl, ["ddns"] = Ddns, ["pfx"] = Pfx, ["pfxPassword"] = PfxPassword, ["dataDir"] = DataDir, ["closeExits"] = CloseExits, ["skin"] = Skin, ["digitOn"] = DigitOn, ["digitBlock"] = DigitBlock, ["digitLens"] = DigitLens };
        File.WriteAllText(FilePath, o.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }
}
