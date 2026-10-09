using System.Text;
using System.Text.RegularExpressions;

namespace KksDllStringExtractor;

internal static class Classifier
{
    // A script label is not reliable language identification: Han can be Chinese/Japanese/Korean,
    // and Latin can be English, French, Spanish, Vietnamese, German, etc.
    static readonly (string Key, string Label)[] ScriptLabels = {
        ("Kana", "日文假名"), ("Han", "汉字（中/日/韩未判定）"),
        ("Hangul", "韩文谚文"), ("Latin", "拉丁字母（英/欧/越等未判定）"),
        ("Cyrillic", "西里尔字母（俄/乌等未判定）"), ("Greek", "希腊字母"),
        ("Arabic", "阿拉伯字母系"), ("Hebrew", "希伯来字母"),
        ("Thai", "泰文字母"), ("Devanagari", "天城文字母（印地语等）"),
        ("Bengali", "孟加拉字母"), ("Tamil", "泰米尔字母"),
        ("Telugu", "泰卢固字母"), ("Gujarati", "古吉拉特字母"),
        ("Gurmukhi", "古木基字母"), ("Kannada", "卡纳达字母"),
        ("Malayalam", "马拉雅拉姆字母"), ("Georgian", "格鲁吉亚字母"),
        ("Armenian", "亚美尼亚字母"), ("Ethiopic", "埃塞俄比亚字母"),
        ("Khmer", "高棉文字母"), ("Lao", "老挝文字母"),
        ("Myanmar", "缅甸文字母"), ("Sinhala", "僧伽罗字母"),
        ("Tibetan", "藏文字母"), ("OtherUnicode", "其他文字系统")
    };
    static readonly HashSet<string> UIWords = new(StringComparer.OrdinalIgnoreCase) {
        "OK", "Cancel", "Save", "Load", "Close", "Open", "Yes", "No", "On", "Off", "Apply", "Reset", "Update",
        "Enable", "Disable", "Enabled", "Disabled", "Settings", "Setting", "Options", "Option", "Back", "Next",
        "Start", "Stop", "Delete", "Remove", "Add", "Edit", "Select", "Search", "Help", "About", "Done",
        "Error", "Warning", "Notice", "Confirm", "Import", "Export", "Default", "None", "All", "Auto",
        "Exit", "Retry", "Clear", "Copy", "Paste", "Refresh", "Language", "Translation", "Name", "Description"
    };
    static readonly Regex Technical = new(@"^(?:\s*(?:https?://|ftp://|file://|[A-Za-z]:[\\/]|\\\\|[\w.+-]+@[\w.-]+|[\w.-]+\.(?:dll|exe|json|xml|png|jpg|jpeg|zip|zipmod|asset|bundle|unity3d|log))|\s*(?:[a-zA-Z][a-zA-Z0-9+.-]*://)|\s*(?:/[\w./-]+){2,}|\s*\{\d+(?::[^}]*)?\}\s*$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex TechToken = new(@"^(?:[a-zA-Z_][a-zA-Z0-9_]*[.\\/:$]+)+[a-zA-Z0-9_.-]+$|^[a-zA-Z_][a-zA-Z0-9_]*\([^)]*\)$", RegexOptions.Compiled);
    static readonly Regex CallContext = new(@"(?:OnGUI|DrawGUI|GUILayout|GUIStyle|GUI\.|DrawWindow|WindowFunc|DrawUI|ShowDialog|ShowPopup|ShowNotification|OnDraw|DrawSettings|DrawMenu|DrawLabel|DrawButton|\b(?:CreateUI|SetupUI|ShowUI|DrawOverlay|OpenSettings|OpenMenu|UISettings|UIWindow|UIHandler)\b)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex UiTypeContext = new(@"(?:^|[./+])(?:[A-Za-z0-9_]*(?:GUI|UI|Window|Dialog|Panel|Menu))(?=$|[./+:])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex ConfigContext = new(@"(?:\b(?:Config(?:uration|Entry|File|Manager)?|Settings?|Options?)\b|BindConfig|GetConfig|SetConfig)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex LogContext = new(@"(?:LogDebug|LogInfo|LogWarning|LogError|Logger|Debug\.Log|Trace|Exception|Throw|Assert|ReportError)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Values that look like Unity hierarchy addresses, internal identifiers, or file paths
    // never enter UI subsets, but always remain in AllStrings and per-plugin complete archives.
    static readonly Regex UnityPath = new(@"(?:^|/)(?:Canvas|Template|Viewport|Content|SubMenu|Root|Image|Panel|ScrollView)(?:/|$)|^(?:[^/\\\r\n]+[/\\]){2,}[^/\\\r\n]+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex InternalId = new(@"^(?:\d{2,}_[\w-]+|(?:cvs|img|btn|tgl|txt|rect|trf|go)[A-Z][\w]*|[a-z]{2,}[A-Z][A-Za-z0-9]*|[A-Za-z0-9]+(?:_[A-Za-z0-9]+){2,})$", RegexOptions.Compiled);
    static readonly Regex ExtendedTechnical = new(@"^(?:[\w+.-]+[/\\][\w./\\:-]+|[\w.-]+\.(?:prefab|unity3d|dll|json|png|asset|zipmod|xml|cfg)|(?:[\w.-]+/){2,}[\w.-]+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static Analyzed Analyze(RawRow row)
    {
        string value = row.Text;
        var scripts = new HashSet<string>(StringComparer.Ordinal);
        bool hasLetter = false;
        foreach (var rune in value.EnumerateRunes())
        {
            if (!Rune.IsLetter(rune)) continue;
            hasLetter = true;
            scripts.Add(Script(rune.Value));
        }
        var sorted = ScriptLabels.Select(x => x.Key).Where(scripts.Contains).ToList();
        string script = sorted.Count == 0 ? "NoLetters" : sorted.Count == 1 ? sorted[0] : "Mixed";
        string hint = sorted.Count == 0 ? "无字母/符号/数字" : string.Join(" + ", ScriptLabels.Where(x => scripts.Contains(x.Key)).Select(x => x.Label));

        string t = value.Trim();
        bool resource = row.Kind.StartsWith("RESOURCE", StringComparison.OrdinalIgnoreCase);
        bool isDocument = row.Kind == "RESOURCE_DOCUMENT";
        bool technical = Technical.IsMatch(t) || TechToken.IsMatch(t) ||
            UnityPath.IsMatch(t) || InternalId.IsMatch(t) || ExtendedTechnical.IsMatch(t) ||
            (t.Length >= 40 && !t.Any(char.IsWhiteSpace) && Regex.IsMatch(t, @"^[\w+/=.-]+$")) ||
            (t.StartsWith("<", StringComparison.Ordinal) && t.EndsWith(">", StringComparison.Ordinal));
        bool technicalCall = row.Usage == "TechnicalCall";
        bool directUi = row.Usage == "DirectUI";
        bool directConfig = row.Usage == "DirectConfig";
        bool contextUi = CallContext.IsMatch(row.Method) || UiTypeContext.IsMatch(row.Source);
        bool contextConfig = ConfigContext.IsMatch(row.Method) || ConfigContext.IsMatch(row.Source);
        bool log = LogContext.IsMatch(row.Method) && !directUi && !directConfig;
        bool label = UIWords.Contains(t);
        bool spaced = t.Any(char.IsWhiteSpace);
        bool nonLatin = scripts.Any(x => x != "Latin");

        int score = 0;
        var why = new List<string>();
        if (directUi) { score += 8; why.Add("direct UI API: " + row.CallTarget); }
        if (directConfig) { score += 8; why.Add("direct configuration API: " + row.CallTarget); }
        if (contextUi && !directUi) { score += 2; why.Add("UI-related method/class"); }
        if (contextConfig && !directConfig) { score += 2; why.Add("configuration context"); }
        if (label) { score += 3; why.Add("common UI label"); }
        if (spaced && hasLetter) { score += 2; why.Add("word spacing"); }
        if (nonLatin) { score += 2; why.Add("non-Latin script"); }
        if (resource && !isDocument) { score += 1; why.Add("resource string"); }
        if (log) { score -= 5; why.Add("log/debug context"); }
        if (technicalCall) { score -= 12; why.Add("asset/object API: " + row.CallTarget); }
        if (technical) { score -= 18; why.Add("Unity path/identifier/technical text"); }
        if (isDocument) { score -= 10; why.Add("entire embedded document"); }
        if (!hasLetter) { score -= 8; why.Add("no letters"); }
        if (t.Length > 2500) { score -= 8; why.Add("large block"); }

        bool candidate = hasLetter && !technical && !technicalCall && !isDocument && t.Length <= 2500 &&
            (directUi || directConfig || nonLatin || spaced || label || score >= 4);
        bool isUi = candidate && !log &&
            (directUi || directConfig || contextUi && score >= 4 || contextConfig && score >= 5 || label && score >= 5);
        string confidence = !isUi ? "Uncertain" :
            (directUi || directConfig) && score >= 7 ? "High" : "Medium";
        string category = technical || technicalCall ? "Technical" : isDocument ? "ResourceDocument" :
            directConfig ? "Configuration" : directUi ? "UI" : log ? "LogOrDebug" :
            resource ? "ResourceText" : "General";
        return new Analyzed(row, script, hint, category, confidence, score, string.Join("; ", why), candidate, isUi);
    }
    static string Script(int cp)
    {
        if (cp is >= 0x3040 and <= 0x30FF or >= 0x31F0 and <= 0x31FF or >= 0xFF66 and <= 0xFF9F) return "Kana";
        if (cp is >= 0x3400 and <= 0x9FFF or >= 0x20000 and <= 0x3134F or >= 0xF900 and <= 0xFAFF) return "Han";
        if (cp is >= 0x1100 and <= 0x11FF or >= 0x3130 and <= 0x318F or >= 0xAC00 and <= 0xD7AF) return "Hangul";
        if (cp is >= 0x0041 and <= 0x005A or >= 0x0061 and <= 0x007A or >= 0x00C0 and <= 0x024F or >= 0x1E00 and <= 0x1EFF or >= 0x2C60 and <= 0x2C7F or >= 0xA720 and <= 0xA7FF or >= 0xFF21 and <= 0xFF5A) return "Latin";
        if (cp is >= 0x0400 and <= 0x052F or >= 0x2DE0 and <= 0x2DFF or >= 0xA640 and <= 0xA69F) return "Cyrillic";
        if (cp is >= 0x0370 and <= 0x03FF or >= 0x1F00 and <= 0x1FFF) return "Greek";
        if (cp is >= 0x0600 and <= 0x06FF or >= 0x0750 and <= 0x077F or >= 0x08A0 and <= 0x08FF or >= 0xFB50 and <= 0xFDFF or >= 0xFE70 and <= 0xFEFF) return "Arabic";
        if (cp is >= 0x0590 and <= 0x05FF) return "Hebrew";
        if (cp is >= 0x0E00 and <= 0x0E7F) return "Thai";
        if (cp is >= 0x0900 and <= 0x097F) return "Devanagari";
        if (cp is >= 0x0980 and <= 0x09FF) return "Bengali";
        if (cp is >= 0x0A00 and <= 0x0A7F) return "Gurmukhi";
        if (cp is >= 0x0A80 and <= 0x0AFF) return "Gujarati";
        if (cp is >= 0x0B80 and <= 0x0BFF) return "Tamil";
        if (cp is >= 0x0C00 and <= 0x0C7F) return "Telugu";
        if (cp is >= 0x0C80 and <= 0x0CFF) return "Kannada";
        if (cp is >= 0x0D00 and <= 0x0D7F) return "Malayalam";
        if (cp is >= 0x10A0 and <= 0x10FF or >= 0x1C90 and <= 0x1CBF) return "Georgian";
        if (cp is >= 0x0530 and <= 0x058F) return "Armenian";
        if (cp is >= 0x1200 and <= 0x137F or >= 0x1380 and <= 0x139F) return "Ethiopic";
        if (cp is >= 0x1780 and <= 0x17FF or >= 0x19E0 and <= 0x19FF) return "Khmer";
        if (cp is >= 0x0E80 and <= 0x0EFF) return "Lao";
        if (cp is >= 0x1000 and <= 0x109F or >= 0xAA60 and <= 0xAA7F) return "Myanmar";
        if (cp is >= 0x0D80 and <= 0x0DFF) return "Sinhala";
        if (cp is >= 0x0F00 and <= 0x0FFF) return "Tibetan";
        return "OtherUnicode";
    }
}
