# Target Marker Overlay for ACT

作者: `Roxyz0501`

Repository: [Roxyz0501/target-marker-overlay-act](https://github.com/Roxyz0501/target-marker-overlay-act)

FFXIVのターゲットマーカー（攻撃1～8、足止め1～3、禁止1～2、汎用マーカー）が誰についているかを表示するACTプラグインです。

## 主な機能

- FFXIV ACT Pluginの `NetworkTargetMarker`（ログタイプ29）を直接追跡
- `Add` / `Update` / `Delete` を処理し、解除時はその場で表示から削除
- ゾーン移動、キャラクター切替、ゲームプロセス切替、対象消失でも状態をクリア
- ジョブアイコン、キャラクター名、マーカーアイコンを表示
- キャラクター名の表示ON/OFF、匿名表示（`Player 01` 形式）
- マーカーがないときだけ自動的に隠す設定
- オーバーレイ全体のON/OFF、全体透明度20～100%、背景透明度0～100%
- 固定OFF時はヘッダーをドラッグして移動、ウィンドウ端をドラッグしてリサイズ
- 固定ON時はクリック透過になり、ゲーム操作を妨げない
- Aether Rangeと同じ半透明のリサイズガイドを右下へ常時表示
- 名前を非表示にすると、ジョブアイコン＋マーカーアイコンだけの横幅108pxまで縮小可能
- 任意の `/echo` 本文でオーバーレイ表示をON/OFF（既定OFF）
- 英語、日本語、簡体字中国語、韓国語を設定画面から即時切替
- 起動時または手動でGitHub Releasesの安定版更新を確認できる安全な更新基盤
- リサイズ中も角丸領域と描画バッファを更新し、透明部分に残像を残さない
- マーカー、ロール、ジョブごとに数値で優先度を指定可能
- 「マーカー優先」「ロール優先」「ジョブ優先」を切替可能
- 位置、サイズ、設定を `%APPDATA%\Advanced Combat Tracker\Config\TargetMarkerOverlay.xml` に保存

## インストール

前提として、ACTと最新版のFFXIV Parsing Pluginが必要です。ゲームはフルスクリーンではなく、仮想フルスクリーン（ボーダーレス）で起動してください。

1. [最新のGitHub Release](https://github.com/Roxyz0501/target-marker-overlay-act/releases/latest)から `TargetMarkerOverlay-vX.Y.Z.zip` をダウンロードし、`TargetMarkerOverlay.dll` を任意のプラグイン用フォルダへコピーします。
2. ACTの `Plugins` タブで `Browse...` を押し、コピーしたDLLを選択します。
3. FFXIV Parsing Pluginより後ろの順番で本プラグインを有効にします。
4. `Target Marker Overlay` タブを開き、最初は「位置とサイズを固定」をOFFにします。
5. 実際のマーカー表示で見た目と位置を調整し、完了したら固定をONにします。

アンロック中はオーバーレイ上部に `UNLOCKED` と表示されます。固定後はクリック透過になるため、解除はACT側の設定タブから行います。

echo連動を使う場合は「指定したechoチャットで表示を切り替える」をONにし、本文を設定します。既定値ならゲーム内で `/echo TargetMarker` を実行するたびに表示がON/OFFされます。echo以外のチャットやログインポートには反応しません。

## 任意支援

設定画面の「支援」タブから、[Ko-fiでRoxyz0501の開発を支援](https://ko-fi.com/roxyz0501)できます。

支援は完全に任意です。支援しなくても本プラグインの全機能を利用でき、機能差はありません。リンクは「Ko-fiでRoxyz0501を支援する」ボタンを明示的に押した場合だけ既定のブラウザで開きます。起動時のポップアップや自動遷移、繰り返し通知、機能制限はありません。

## 言語設定

専用の「Language／言語／语言／언어」タブで、`en`、`ja`、`zh-CN`、`ko` を選択できます。変更は設定画面とオーバーレイへ即時反映され、Configの `Language` に保存されます。

Roxyzs ACT Pluginポータル内では、ポータルの言語を一時的に使い、子プラグインの言語・更新・支援タブを隠します。ホストは初期化直後と選択変更時に `SetHostedLanguage(string code)` をUIスレッドから呼び出せます。指定はメモリだけに保持し、単体で選んだ保存言語は変更しません。`null` または空文字で単体表示と保存言語へ戻ります。支援タブは動的に機能タブを追加した場合も右端に表示します。

旧Configなどで `Language` が未設定の場合に限り、初回起動時にWindowsのUIカルチャーを確認します。`ja` 系は日本語、`zh` 系は簡体字中国語、`ko` 系は韓国語、それ以外は英語を選択してConfigへ保存します。保存後はOS言語で上書きしません。未対応言語と不足した翻訳キーのフォールバックは英語です。

## 更新機能とGitHub Releases

「更新」タブの「更新する」を押すと、公式Release ZIPとSHA-256をダウンロードして検証し、更新を準備します。準備完了後にACTを終了すると、補助アップデータが登録先DLLをバックアップして自動置換し、終了します。数秒待ってからACTを再起動してください。通常の更新で手動コピーは不要です。作者・インストール済みの現在版・GitHubリンクも更新タブに表示します。

ACTが登録している `pluginFile` を更新先として使います。登録先が不明な場合は更新を中止し、shadow copyの場所へ推測で書き込みません。使用中のDLLは上書きしません。補助アップデータは明示操作時のみ起動し、ACT終了、更新完了、エラー、または24時間の待機期限で終了します。設定タブを閉じるだけでは更新されません。更新先は書き込み可能なローカルフォルダーにしてください。

更新前のDLLは同じフォルダーの `TargetMarkerOverlay.dll.backup-*` に残します。置換失敗時はバックアップから復元します。ディスク故障やアクセス権変更などで復元も失敗した場合は、ACTを終了してからバックアップを戻してください。準備ファイルと診断ログは `%TEMP%\TargetMarkerOverlayUpdate\` にあります。設定ファイルは更新対象に含めません。

更新確認は起動時設定ONまたは「今すぐ確認」の操作時だけです。公式 `releases/latest` へHEADを1回送り、同版ならREST APIは0回です。新しい安定版があれば更新内容をREST APIで1回取得します。APIが403/429でも公式最新タグの判定は保持し、「更新する」でタグ固定ZIPとSHA-256の検証を続行できます。利用制限と一般の通信障害は4言語で区別し、連続リトライや定期ポーリングはしません。

v1.4.2以前から今回の版へ移行する際は、旧版の更新機能に自動置換がないため一度だけACT終了後にDLLを置き換えてください。v1.5.0以降は上記の自動置換を利用できます。

更新元は本プラグイン専用の公開リポジトリ [Roxyz0501/target-marker-overlay-act](https://github.com/Roxyz0501/target-marker-overlay-act) です。認証トークンを使わずGitHub Releases APIから安定版を確認します。

```csharp
public const string RepositoryOwner = "Roxyz0501";
public const string RepositoryName = "target-marker-overlay-act";
```

### Release assetの作成

1. プラグインと `TargetMarkerOverlay.Updater.csproj` の `Version` をReleaseタグと同じSemVerへ更新します。
2. Releaseビルド後、次を実行します。

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\package-release.ps1 -Version 1.6.1
```

3. テスト成功後に新しい `v1.6.1` タグをpushすると、GitHub Actionsが公式ACT参照でビルド・テストし、次の2ファイルを安定版Releaseへ公開します。既存タグ・配布物は上書きしません。

   - `TargetMarkerOverlay-v1.6.1.zip`
   - `SHA256SUMS.txt`

ZIP直下には `TargetMarkerOverlay.dll`、`TargetMarkerOverlay.Updater.exe`、`README.md`、`THIRD_PARTY_LICENSES.md` だけを置けます。更新時は対象リポジトリ・安定版タグ・asset名・HTTPSリダイレクト・SHA-256・ZIP内パス・重複やサイズ上限・DLLとUpdaterのアセンブリ名・会社名・バージョンを検証します。補助アップデータはこの検証済みZIP内のものを使い、既存フォルダーの古いUpdaterには依存しません。SHA-256は配布物の整合性検証であり電子署名ではありません。GitHubトークンは使用・埋め込みしません。

共通タブ・更新ページ・言語ページは `src/TargetMarkerOverlay/Shared/` に正本とバイト単位で同じソースを同梱しています。更新ページのフォントやレイアウトは個別に上書きしません。

### 更新機能のテスト

```powershell
dotnet run --project .\tests\CoreTests\CoreTests.csproj -c Release
```

OS言語マッピング、英語フォールバック、旧Config互換、SemVer、Releaseレスポンス、GitHub URL許可リスト、SHA-256、正常ZIP、Zip Slip拒否、設定の60秒周期保存、変更なしの書き込み抑制、保存失敗と終了時flush、処理中の終了待ち、再ロードを検証します。

共通ページは4言語の往復、同じ言語の再適用、360/720px幅、ホスト時の非表示、保存言語の復元、動的タブ追加も検証します。`tests/SharedPageReference` はFood Buff Overlay v1.6.0のDLLを第1引数に受け取り、画面を表示せず4言語・2幅・6状態の描画をピクセル比較します。比較対象DLLはこのリポジトリには同梱しません。

## 並び順

既定値は次の順です。

- マーカー: Attack → Bind → Stop → Shape
- ロール: Tank → Healer → Melee → Ranged → Caster
- 同じ分類内: 設定画面のジョブ優先度（数値が小さいほど上）

すべて設定画面の「並び順」タブから変更できます。たとえばPLDを10、WARを20にすると、同条件ではPLDが先に表示されます。

## ビルド

.NET Framework 4.8 Developer Packと.NET SDKを使用します。

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

ACTを標準以外の場所に置いている場合:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1 -ActPath "D:\ACT\Advanced Combat Tracker.exe"
```

出力先は `src\TargetMarkerOverlay\bin\Release\net48\TargetMarkerOverlay.dll` です。ジョブアイコンはDLLへ埋め込まれるため、追加ファイルは不要です。

## 状態追跡について

付与と解除を画面認識や一定時間のタイムアウトで推測せず、FFXIV Parsing Pluginが出す付与・更新・削除イベントをそのまま状態へ反映します。同じ対象への付け替え、同じマーカーの別対象への移動も一意になるよう処理しています。

プラグインをマーカー付与後に途中ロードした場合、過去イベントは再送されないため、次にそのマーカーが変更された時点から表示されます。

## 注意

ACTを含む外部ツールの利用はFINAL FANTASY XIVの利用規約上の扱いを理解したうえで、自己責任で行ってください。ゲームへの入力送信や自動マーキングは行いません。

## ライセンス

このプロジェクト本体のライセンスは現時点では未設定です。第三者素材には各権利者の条件が適用されます。詳細は [THIRD_PARTY_LICENSES.md](THIRD_PARTY_LICENSES.md) を確認してください。

## アイコン素材

ジョブアイコンは [xivapi/classjob-icons](https://github.com/xivapi/classjob-icons) のMITライセンス素材を使用しています。詳細は [THIRD_PARTY_LICENSES.md](THIRD_PARTY_LICENSES.md) を参照してください。

ターゲットマーカーは、スクウェア・エニックスの[FFXIV公式UIガイド「ターゲットマーカーの使い方」](https://jp.finalfantasyxiv.com/uiguide/battle/battle-target/targetmarker_how.html)に掲載されているゲーム内UI画像を使用しています。独自に似せて描いた記号ではなく、公式画像内の各マーカー領域を表示しています。利用にあたっては[ファイナルファンタジーXIV 著作物利用許諾条件](https://support.jp.square-enix.com/rule.php?id=5381&la=0&tag=authc)も確認してください。
