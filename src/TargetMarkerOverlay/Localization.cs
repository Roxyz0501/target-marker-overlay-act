using System;
using System.Collections.Generic;
using System.Globalization;

namespace TargetMarkerOverlay
{
    public static class Localization
    {
        public const string English = "en";
        public const string Japanese = "ja";
        public const string Chinese = "zh-CN";
        public const string Korean = "ko";

        private static readonly Dictionary<string, Dictionary<string, string>> Resources =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                [English] = D(
                    "Language","Language","Display","Display","Sort","Sort order","Support","Support","Update","Updates",
                    "Enabled","Show overlay","HideEmpty","Hide when no markers","ShowName","Show character names","Anonymous","Anonymize character names","Locked","Lock overlay (click-through)",
                    "OverlayOpacity","Overlay opacity","BackgroundOpacity","Background opacity","EchoTitle","Echo chat control","EchoToggle","Toggle display with the specified echo message","EchoText","Echo message","EchoExample","Example: /echo {0}","ClearDisplay","Clear display",
                    "ResizeHint","When unlocked, drag the header to move and an edge to resize.\r\nWhen locked, click-through prevents interference with the game.","ActivityWaiting","Waiting for NetworkTargetMarker",
                    "SortPrimary","Primary sort","MarkerPriority","Marker type","RolePriority","Role","JobPriority","Job","LowerFirst","{0} (lower values first)","ColumnItem","Item","ColumnPriority","Priority",
                    "Attack","Attack","Bind","Bind","Stop","Stop","Shape","Shape","Tank","Tank","Healer","Healer","Melee","Melee DPS","Ranged","Physical ranged DPS","Caster","Magical ranged DPS","Other","Other","Square","Square","Circle","Circle","Plus","Plus","Triangle","Triangle",
                    "SortMarker","Marker first","SortRole","Role first","SortJob","Job first","HeaderSubtitle","Configure display, locking, priorities, and updates  •  Author: Roxyz0501",
                    "SupportTitle","Support Roxyz0501's development","SupportDescription","If you enjoy this plugin, you can optionally support development through Ko-fi.","SupportAssurance","Support is entirely optional. Every feature is available with no differences if you do not support.","SupportButton","Support Roxyz0501 on Ko-fi",
                    "SafeLinkError","The support link failed security validation.","LinkOpened","Ko-fi was opened in your default browser.","LinkFailed","Could not open the link. Open the URL above in your browser.\r\n{0}",
                    "UpdateTitle","Plugin updates","CheckStartup","Check for updates at startup","CheckNow","Check now","UpdateNow","Prepare update","Later","Later","CurrentVersion","Current version: {0}","LatestVersion","Latest version: {0}","ReleaseNotes","Release notes",
                    "UpdateRepoMissing","The GitHub update source is not configured. Normal plugin features remain available.","UpdateChecking","Checking for updates…","UpdateNone","You are using the latest stable version.","UpdateAvailable","A stable update is available: {0} → {1}","UpdateFailed","Update check failed: {0}","UpdateDownloading","Downloading and validating the update…","UpdatePrepared","The verified DLL is staged at {0}. This plugin will not start an installer after ACT exits. Replace the plugin DLL manually after closing ACT, then restart ACT.","UpdateCorrupt","The update package was rejected: {0}","UpdateSkipped","This version was postponed.",
                    "OverlayTitle","TARGET MARKERS","Unlocked","UNLOCKED  •  DRAG / RESIZE","MarkerWaiting","Waiting for markers","StatusReady","Enabled (waiting for NetworkTargetMarker)","StatusStopped","Stopped","StatusEcho","Display changed to {0} by echo","SaveError","Could not save settings: {0}","ParseError","Parse error: {0}","StateCleared","State cleared","ZoneCleared","State cleared after zone change","PlayerCleared","State cleared after player change","ProcessCleared","State cleared after game process change"
                ),
                [Japanese] = D(
                    "Language","言語","Display","表示","Sort","並び順","Support","支援","Update","更新",
                    "Enabled","オーバーレイを表示する","HideEmpty","マーカーがないときは隠す","ShowName","キャラクター名を表示する","Anonymous","キャラクター名を匿名化する","Locked","オーバーレイを固定（クリック透過）",
                    "OverlayOpacity","オーバーレイの透明度","BackgroundOpacity","背景の透明度","EchoTitle","echoチャット連動","EchoToggle","指定したechoチャットで表示を切り替える","EchoText","echoの本文","EchoExample","使用例: /echo {0}","ClearDisplay","表示をクリア",
                    "ResizeHint","固定OFF中はヘッダーをドラッグして移動、縁をドラッグしてリサイズできます。\r\n固定ON中はゲーム操作を妨げないようクリック透過になります。","ActivityWaiting","NetworkTargetMarker 待機中",
                    "SortPrimary","最優先の並び方","MarkerPriority","マーカー種別","RolePriority","ロール","JobPriority","ジョブ","LowerFirst","{0}（小さいほど上）","ColumnItem","項目","ColumnPriority","優先度",
                    "Attack","攻撃","Bind","足止め","Stop","禁止","Shape","汎用","Tank","タンク","Healer","ヒーラー","Melee","近接DPS","Ranged","遠隔物理DPS","Caster","遠隔魔法DPS","Other","その他","Square","四角","Circle","丸","Plus","十字","Triangle","三角",
                    "SortMarker","マーカー優先","SortRole","ロール優先","SortJob","ジョブ優先","HeaderSubtitle","表示・固定・優先度・更新を調整できます  •  作者: Roxyz0501",
                    "SupportTitle","Roxyz0501の開発を支援","SupportDescription","このプラグインを気に入っていただけた場合は、Ko-fiから任意で開発を支援できます。","SupportAssurance","支援は完全に任意です。支援しなくても全機能を利用でき、機能差はありません。","SupportButton","Ko-fiでRoxyz0501を支援する",
                    "SafeLinkError","支援リンクの安全性を確認できませんでした。","LinkOpened","既定のブラウザでKo-fiを開きました。","LinkFailed","リンクを開けませんでした。ブラウザで上記URLを開いてください。\r\n{0}",
                    "UpdateTitle","プラグインの更新","CheckStartup","起動時に更新を確認する","CheckNow","今すぐ確認","UpdateNow","更新を準備","Later","後で","CurrentVersion","現在のバージョン: {0}","LatestVersion","最新バージョン: {0}","ReleaseNotes","更新内容",
                    "UpdateRepoMissing","GitHubの更新元が未設定です。通常機能はそのまま利用できます。","UpdateChecking","更新を確認しています…","UpdateNone","最新の安定版を使用しています。","UpdateAvailable","安定版の更新があります: {0} → {1}","UpdateFailed","更新確認に失敗しました: {0}","UpdateDownloading","更新をダウンロードして検証しています…","UpdatePrepared","検証済みDLLを次の場所に準備しました: {0}。ACT終了後のインストーラーは起動しません。ACTを終了してからDLLを手動で置き換え、ACTを再起動してください。","UpdateCorrupt","更新パッケージを拒否しました: {0}","UpdateSkipped","このバージョンを後回しにしました。",
                    "OverlayTitle","ターゲットマーカー","Unlocked","固定解除中  •  移動 / サイズ変更","MarkerWaiting","マーカー待機中","StatusReady","有効（NetworkTargetMarker待機中）","StatusStopped","停止","StatusEcho","echoで表示を{0}に変更","SaveError","設定を保存できません: {0}","ParseError","解析エラー: {0}","StateCleared","状態をクリア","ZoneCleared","ゾーン移動で状態をクリア","PlayerCleared","プレイヤー切替で状態をクリア","ProcessCleared","ゲームプロセス切替で状態をクリア"
                ),
                [Chinese] = D(
                    "Language","语言","Display","显示","Sort","排序","Support","支持","Update","更新",
                    "Enabled","显示悬浮窗","HideEmpty","无标记时隐藏","ShowName","显示角色名","Anonymous","匿名化角色名","Locked","锁定悬浮窗（鼠标穿透）",
                    "OverlayOpacity","悬浮窗透明度","BackgroundOpacity","背景透明度","EchoTitle","Echo聊天联动","EchoToggle","使用指定Echo消息切换显示","EchoText","Echo消息","EchoExample","示例: /echo {0}","ClearDisplay","清除显示",
                    "ResizeHint","未锁定时拖动标题栏移动，拖动边缘调整大小。\r\n锁定后启用鼠标穿透，不影响游戏操作。","ActivityWaiting","正在等待NetworkTargetMarker",
                    "SortPrimary","主要排序","MarkerPriority","标记类型","RolePriority","职责","JobPriority","职业","LowerFirst","{0}（数值越小越靠前）","ColumnItem","项目","ColumnPriority","优先级",
                    "Attack","攻击","Bind","止步","Stop","禁止","Shape","通用","Tank","防护职业","Healer","治疗职业","Melee","近战职业","Ranged","远程物理职业","Caster","远程魔法职业","Other","其他","Square","方形","Circle","圆形","Plus","十字","Triangle","三角",
                    "SortMarker","标记优先","SortRole","职责优先","SortJob","职业优先","HeaderSubtitle","调整显示、锁定、优先级和更新  •  作者: Roxyz0501",
                    "SupportTitle","支持Roxyz0501的开发","SupportDescription","如果你喜欢本插件，可以通过Ko-fi自愿支持开发。","SupportAssurance","支持完全自愿。不支持也可使用全部功能，功能没有任何差异。","SupportButton","在Ko-fi支持Roxyz0501",
                    "SafeLinkError","支持链接未通过安全验证。","LinkOpened","已在默认浏览器中打开Ko-fi。","LinkFailed","无法打开链接。请在浏览器中打开上方网址。\r\n{0}",
                    "UpdateTitle","插件更新","CheckStartup","启动时检查更新","CheckNow","立即检查","UpdateNow","准备更新","Later","稍后","CurrentVersion","当前版本: {0}","LatestVersion","最新版本: {0}","ReleaseNotes","更新内容",
                    "UpdateRepoMissing","尚未配置GitHub更新源。插件的正常功能仍可使用。","UpdateChecking","正在检查更新…","UpdateNone","当前已是最新稳定版。","UpdateAvailable","有新的稳定版: {0} → {1}","UpdateFailed","检查更新失败: {0}","UpdateDownloading","正在下载并验证更新…","UpdatePrepared","已验证的DLL已准备在此处: {0}。插件不会在ACT退出后启动安装程序。请关闭ACT后手动替换插件DLL，再重新启动ACT。","UpdateCorrupt","更新包已被拒绝: {0}","UpdateSkipped","已暂缓此版本。",
                    "OverlayTitle","目标标记","Unlocked","未锁定  •  拖动 / 调整大小","MarkerWaiting","等待标记","StatusReady","已启用（等待NetworkTargetMarker）","StatusStopped","已停止","StatusEcho","已通过echo将显示切换为{0}","SaveError","无法保存设置: {0}","ParseError","解析错误: {0}","StateCleared","状态已清除","ZoneCleared","区域切换后已清除状态","PlayerCleared","角色切换后已清除状态","ProcessCleared","游戏进程切换后已清除状态"
                ),
                [Korean] = D(
                    "Language","언어","Display","표시","Sort","정렬","Support","후원","Update","업데이트",
                    "Enabled","오버레이 표시","HideEmpty","마커가 없을 때 숨기기","ShowName","캐릭터 이름 표시","Anonymous","캐릭터 이름 익명화","Locked","오버레이 잠금(클릭 통과)",
                    "OverlayOpacity","오버레이 불투명도","BackgroundOpacity","배경 불투명도","EchoTitle","Echo 채팅 연동","EchoToggle","지정한 Echo 메시지로 표시 전환","EchoText","Echo 메시지","EchoExample","예: /echo {0}","ClearDisplay","표시 지우기",
                    "ResizeHint","잠금 해제 시 헤더를 끌어 이동하고 가장자리를 끌어 크기를 조절합니다.\r\n잠그면 클릭이 통과되어 게임 조작을 방해하지 않습니다.","ActivityWaiting","NetworkTargetMarker 대기 중",
                    "SortPrimary","우선 정렬","MarkerPriority","마커 종류","RolePriority","역할","JobPriority","직업","LowerFirst","{0} (값이 작을수록 위)","ColumnItem","항목","ColumnPriority","우선순위",
                    "Attack","공격","Bind","속박","Stop","금지","Shape","일반","Tank","탱커","Healer","힐러","Melee","근거리 DPS","Ranged","원거리 물리 DPS","Caster","원거리 마법 DPS","Other","기타","Square","사각형","Circle","원형","Plus","십자","Triangle","삼각형",
                    "SortMarker","마커 우선","SortRole","역할 우선","SortJob","직업 우선","HeaderSubtitle","표시, 잠금, 우선순위 및 업데이트 설정  •  제작자: Roxyz0501",
                    "SupportTitle","Roxyz0501의 개발 후원","SupportDescription","플러그인이 마음에 들면 Ko-fi에서 선택적으로 개발을 후원할 수 있습니다.","SupportAssurance","후원은 완전히 선택 사항입니다. 후원하지 않아도 모든 기능을 동일하게 사용할 수 있습니다.","SupportButton","Ko-fi에서 Roxyz0501 후원하기",
                    "SafeLinkError","후원 링크의 안전성을 확인하지 못했습니다.","LinkOpened","기본 브라우저에서 Ko-fi를 열었습니다.","LinkFailed","링크를 열 수 없습니다. 브라우저에서 위 URL을 여세요.\r\n{0}",
                    "UpdateTitle","플러그인 업데이트","CheckStartup","시작할 때 업데이트 확인","CheckNow","지금 확인","UpdateNow","업데이트 준비","Later","나중에","CurrentVersion","현재 버전: {0}","LatestVersion","최신 버전: {0}","ReleaseNotes","업데이트 내용",
                    "UpdateRepoMissing","GitHub 업데이트 원본이 설정되지 않았습니다. 일반 기능은 계속 사용할 수 있습니다.","UpdateChecking","업데이트 확인 중…","UpdateNone","최신 안정 버전을 사용 중입니다.","UpdateAvailable","새 안정 버전이 있습니다: {0} → {1}","UpdateFailed","업데이트 확인 실패: {0}","UpdateDownloading","업데이트 다운로드 및 검증 중…","UpdatePrepared","검증된 DLL을 다음 위치에 준비했습니다: {0}. ACT 종료 후 설치 프로그램을 실행하지 않습니다. ACT를 종료한 뒤 DLL을 직접 교체하고 다시 시작하세요.","UpdateCorrupt","업데이트 패키지가 거부되었습니다: {0}","UpdateSkipped","이 버전을 나중으로 미뤘습니다.",
                    "OverlayTitle","대상 마커","Unlocked","잠금 해제  •  이동 / 크기 조절","MarkerWaiting","마커 대기 중","StatusReady","활성화됨(NetworkTargetMarker 대기 중)","StatusStopped","중지됨","StatusEcho","echo로 표시를 {0}(으)로 변경","SaveError","설정을 저장할 수 없습니다: {0}","ParseError","분석 오류: {0}","StateCleared","상태 지움","ZoneCleared","지역 이동으로 상태 지움","PlayerCleared","플레이어 변경으로 상태 지움","ProcessCleared","게임 프로세스 변경으로 상태 지움"
                )
            };

        public static string Normalize(string language)
        {
            if (string.IsNullOrWhiteSpace(language)) return English;
            if (language.StartsWith("ja", StringComparison.OrdinalIgnoreCase)) return Japanese;
            if (language.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return Chinese;
            if (language.StartsWith("ko", StringComparison.OrdinalIgnoreCase)) return Korean;
            return English;
        }

        public static string FromUiCulture(CultureInfo culture) => Normalize(culture?.Name);

        public static string Get(string language, string key, params object[] args)
        {
            var normalized = Normalize(language);
            string value;
            if (!Resources[normalized].TryGetValue(key, out value) && !Resources[English].TryGetValue(key, out value)) value = key;
            return args == null || args.Length == 0 ? value : string.Format(CultureInfo.CurrentCulture, value, args);
        }

        public static string LanguageName(string code, string uiLanguage)
        {
            switch (Normalize(code))
            {
                case Japanese: return "日本語";
                case Chinese: return "简体中文";
                case Korean: return "한국어";
                default: return "English";
            }
        }

        private static Dictionary<string, string> D(params string[] pairs)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i + 1 < pairs.Length; i += 2) result[pairs[i]] = pairs[i + 1];
            return result;
        }
    }
}
