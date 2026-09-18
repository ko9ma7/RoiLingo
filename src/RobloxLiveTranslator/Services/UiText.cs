namespace RobloxLiveTranslator.Services;

public static class UiText
{
    private static readonly Dictionary<string, Dictionary<string, string>> Texts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ko-KR"] = new()
        {
            ["target"]="대상", ["roi"]="ROI", ["quick"]="빠른번역", ["quick.region"]="영역 선택 번역 (Ctrl+Alt+T)", ["quick.window"]="활성 창 전체 번역 (Ctrl+Alt+W)", ["quick.clipboard"]="클립보드 텍스트 번역 (Ctrl+Alt+V)", ["start"]="시작", ["stop"]="중지", ["overlay"]="오버레이",
            ["live"]="번역창", ["settings"]="설정", ["source"]="원문", ["translate"]="→ 번역", ["mode"]="방식",
            ["advanced"]="고급 설정", ["save"]="저장", ["close"]="닫기", ["ready"]="대상 → ROI → 시작",
            ["tab.general"]="설정 / ROI", ["tab.web"]="번역 웹 / 교차 검증", ["tab.api"]="번역 API / 로컬", ["tab.history"]="번역 기록",
            ["strategy.web"]="무료 · Web 3종 교차검증", ["strategy.hybrid"]="API/로컬 우선 · Web 보조검증", ["strategy.api"]="API/로컬만 (안정 우선)", ["strategy.max"]="전체 Provider 교차검증",
            ["auto"]="자동 감지", ["ko"]="한국어", ["en"]="영어", ["ja"]="일본어", ["zh-CN"]="중국어(간체)", ["zh-TW"]="중국어(번체)",
            ["es"]="스페인어", ["fr"]="프랑스어", ["de"]="독일어", ["ru"]="러시아어", ["pt"]="포르투갈어", ["it"]="이탈리아어",
            ["vi"]="베트남어", ["th"]="태국어", ["id"]="인도네시아어", ["hi"]="힌디어", ["ar"]="아랍어"
        },
        ["en-US"] = new()
        {
            ["target"]="Target", ["roi"]="ROI", ["quick"]="Quick", ["quick.region"]="Translate selected area (Ctrl+Alt+T)", ["quick.window"]="Translate active window (Ctrl+Alt+W)", ["quick.clipboard"]="Translate clipboard (Ctrl+Alt+V)", ["start"]="Start", ["stop"]="Stop", ["overlay"]="Overlay",
            ["live"]="Live", ["settings"]="Settings", ["source"]="Source", ["translate"]="→ Target", ["mode"]="Mode",
            ["advanced"]="Advanced settings", ["save"]="Save", ["close"]="Close", ["ready"]="Target → ROI → Start",
            ["tab.general"]="General / ROI", ["tab.web"]="Web / Cross-check", ["tab.api"]="API / Local", ["tab.history"]="History",
            ["strategy.web"]="Free · 3 Web translators", ["strategy.hybrid"]="API/local first · Web verify", ["strategy.api"]="API/local only", ["strategy.max"]="All providers cross-check",
            ["auto"]="Auto detect", ["ko"]="Korean", ["en"]="English", ["ja"]="Japanese", ["zh-CN"]="Chinese (Simplified)", ["zh-TW"]="Chinese (Traditional)",
            ["es"]="Spanish", ["fr"]="French", ["de"]="German", ["ru"]="Russian", ["pt"]="Portuguese", ["it"]="Italian",
            ["vi"]="Vietnamese", ["th"]="Thai", ["id"]="Indonesian", ["hi"]="Hindi", ["ar"]="Arabic"
        },
        ["ja-JP"] = new()
        {
            ["target"]="対象", ["roi"]="ROI", ["quick"]="クイック", ["quick.region"]="範囲を翻訳 (Ctrl+Alt+T)", ["quick.window"]="アクティブウィンドウ翻訳 (Ctrl+Alt+W)", ["quick.clipboard"]="クリップボード翻訳 (Ctrl+Alt+V)", ["start"]="開始", ["stop"]="停止", ["overlay"]="オーバーレイ",
            ["live"]="翻訳ウィンドウ", ["settings"]="設定", ["source"]="原文", ["translate"]="→ 翻訳", ["mode"]="方式",
            ["advanced"]="詳細設定", ["save"]="保存", ["close"]="閉じる", ["ready"]="対象 → ROI → 開始",
            ["tab.general"]="設定 / ROI", ["tab.web"]="Web / 相互確認", ["tab.api"]="API / ローカル", ["tab.history"]="翻訳履歴",
            ["strategy.web"]="無料 · Web 3種相互確認", ["strategy.hybrid"]="API/ローカル優先 · Web確認", ["strategy.api"]="API/ローカルのみ", ["strategy.max"]="全Provider相互確認",
            ["auto"]="自動検出", ["ko"]="韓国語", ["en"]="英語", ["ja"]="日本語", ["zh-CN"]="中国語(簡体)", ["zh-TW"]="中国語(繁体)",
            ["es"]="スペイン語", ["fr"]="フランス語", ["de"]="ドイツ語", ["ru"]="ロシア語", ["pt"]="ポルトガル語", ["it"]="イタリア語",
            ["vi"]="ベトナム語", ["th"]="タイ語", ["id"]="インドネシア語", ["hi"]="ヒンディー語", ["ar"]="アラビア語"
        },
        ["zh-CN"] = new()
        {
            ["target"]="目标", ["roi"]="ROI", ["quick"]="快速翻译", ["quick.region"]="选择区域翻译 (Ctrl+Alt+T)", ["quick.window"]="翻译活动窗口 (Ctrl+Alt+W)", ["quick.clipboard"]="翻译剪贴板 (Ctrl+Alt+V)", ["start"]="开始", ["stop"]="停止", ["overlay"]="浮层",
            ["live"]="翻译窗", ["settings"]="设置", ["source"]="源语言", ["translate"]="→ 翻译", ["mode"]="模式",
            ["advanced"]="高级设置", ["save"]="保存", ["close"]="关闭", ["ready"]="目标 → ROI → 开始",
            ["tab.general"]="设置 / ROI", ["tab.web"]="网页 / 交叉验证", ["tab.api"]="API / 本地", ["tab.history"]="翻译记录",
            ["strategy.web"]="免费 · 3个网页翻译交叉验证", ["strategy.hybrid"]="API/本地优先 · 网页验证", ["strategy.api"]="仅API/本地", ["strategy.max"]="全部Provider交叉验证",
            ["auto"]="自动检测", ["ko"]="韩语", ["en"]="英语", ["ja"]="日语", ["zh-CN"]="中文(简体)", ["zh-TW"]="中文(繁体)",
            ["es"]="西班牙语", ["fr"]="法语", ["de"]="德语", ["ru"]="俄语", ["pt"]="葡萄牙语", ["it"]="意大利语",
            ["vi"]="越南语", ["th"]="泰语", ["id"]="印尼语", ["hi"]="印地语", ["ar"]="阿拉伯语"
        }
    };


    private static readonly string[][] Literals =
    [
        ["OCR / 감시 설정", "OCR / Monitoring", "OCR / 監視設定", "OCR / 监控设置"],
        ["자동 기록 / 표시", "Auto history / display", "自動履歴 / 表示", "自动记录 / 显示"],
        ["ROI 목록", "ROI list", "ROI一覧", "ROI列表"],
        ["빠름", "Fast", "高速", "快速"],
        ["균형", "Balanced", "バランス", "均衡"],
        ["정확도 우선", "Accuracy", "精度優先", "精度优先"],
        ["화면 검사 간격(ms)", "Polling interval (ms)", "画面確認間隔(ms)", "画面检查间隔(ms)"],
        ["변화 감도 (0.01~0.20)", "Change sensitivity (0.01~0.20)", "変化感度 (0.01~0.20)", "变化灵敏度 (0.01~0.20)"],
        ["창 캡처 방식", "Window capture mode", "ウィンドウキャプチャ方式", "窗口捕获模式"],
        ["백그라운드 우선 (가려진/비활성 창 권장)", "Background first (recommended for covered/inactive windows)", "バックグラウンド優先", "后台优先（推荐）"],
        ["백그라운드만 (화면 복사 사용 안 함)", "Background only (no screen fallback)", "バックグラウンドのみ", "仅后台捕获"],
        ["자동 + 전면 화면 폴백", "Auto + foreground screen fallback", "自動 + 前面画面フォールバック", "自动 + 前台屏幕回退"],
        ["OCR / 번역 결과 자동 저장", "Auto-save OCR / translation history", "OCR / 翻訳結果を自動保存", "自动保存OCR / 翻译结果"],
        ["실시간 번역 창 열기/다시 열기", "Open/reopen live translation window", "リアルタイム翻訳ウィンドウを開く", "打开/重新打开实时翻译窗"],
        ["게임 오버레이 직접 편집", "Edit game overlay directly", "ゲームオーバーレイを直接編集", "直接编辑游戏浮层"],
        ["게임 오버레이 세부 설정", "Overlay details", "オーバーレイ詳細設定", "浮层详细设置"],
        ["선택 ROI 삭제", "Delete selected ROI", "選択ROIを削除", "删除选中ROI"],
        ["복사 버튼/클립보드 폴백", "Copy button / clipboard fallback", "コピーボタン / クリップボード", "复制按钮 / 剪贴板回退"],
        ["번역 캐시 비우기", "Clear translation cache", "翻訳キャッシュを削除", "清除翻译缓存"],
        ["번역 결과 읽기 테스트", "Test translation result reading", "翻訳結果読取テスト", "测试读取翻译结果"],
        ["번역 사이트 초기화/새로고침", "Initialize/refresh translator sites", "翻訳サイト初期化/更新", "初始化/刷新翻译网站"],
        ["번역 실행 전략", "Translation strategy", "翻訳実行戦略", "翻译策略"],
        ["API/로컬 연결 테스트", "Test API/local providers", "API/ローカル接続テスト", "测试API/本地连接"],
        ["로컬 사용량 카운터 초기화", "Reset local usage counters", "ローカル使用量をリセット", "重置本地用量计数"],
        ["API 설정/비밀키 저장", "Save API settings/secrets", "API設定/秘密鍵を保存", "保存API设置/密钥"],
        ["번역 기록", "Translation history", "翻訳履歴", "翻译记录"],
        ["CSV 내보내기", "Export CSV", "CSV書き出し", "导出CSV"],
        ["로그 폴더 열기", "Open log folder", "ログフォルダを開く", "打开日志文件夹"],
        ["실행 로그 (자동 저장)", "Runtime log (auto-save)", "実行ログ (自動保存)", "运行日志（自动保存）"],
        ["검색", "Search", "検索", "搜索"],
        ["시간", "Time", "時間", "时间"],
        ["번역", "Translation", "翻訳", "翻译"],
        ["선택", "Selected", "選択", "选择"],
        ["일치도", "Agreement", "一致度", "一致度"],
        ["사용", "Enabled", "使用", "启用"],
        ["이름", "Name", "名前", "名称"],
        ["이벤트", "Event", "イベント", "事件"],
        ["웹 타임아웃(ms)", "Web timeout (ms)", "Webタイムアウト(ms)", "网页超时(ms)"],
        ["API 타임아웃(ms)", "API timeout (ms)", "APIタイムアウト(ms)", "API超时(ms)"],
        ["일 요청 한도", "Daily request limit", "1日リクエスト上限", "每日请求上限"],
        ["월 문자 한도", "Monthly character limit", "月間文字上限", "每月字符上限"]
    ];

    private static int LocaleIndex(string locale) => NormalizeLocale(locale) switch
    {
        "en-US" => 1,
        "ja-JP" => 2,
        "zh-CN" => 3,
        _ => 0
    };

    public static string TranslateLiteral(string locale, string current)
    {
        if (string.IsNullOrWhiteSpace(current)) return current;
        var index = LocaleIndex(locale);
        foreach (var row in Literals)
            if (row.Any(x => string.Equals(x, current, StringComparison.Ordinal)))
                return row[index];
        return current;
    }

    public static string NormalizeLocale(string? locale) => locale switch
    {
        "en" or "en-US" => "en-US",
        "ja" or "ja-JP" => "ja-JP",
        "zh" or "zh-CN" => "zh-CN",
        _ => "ko-KR"
    };

    public static string Get(string locale, string key)
    {
        locale = NormalizeLocale(locale);
        if (Texts.TryGetValue(locale, out var map) && map.TryGetValue(key, out var value)) return value;
        return Texts["ko-KR"].TryGetValue(key, out var fallback) ? fallback : key;
    }
}
