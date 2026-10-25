# DaikonLine — DESIGN.md

画面の最上端に細い色付きラインを常に表示し、**今キーボード入力を受け取っているアプリの IME 入力モード**をひと目で分かるようにする常駐アプリ。

元の DESIGN.md を土台に、実装方式の具体化と以下の改善を加えた（★ が元の案からの変更・追加）。

- ★ IME 判定の方式と判定ルールを具体化（5 章）
- ★ 誤った色を出さないための「灰色」のルールを明確化（5.4）
- ★ キーボードフックを使わない（入力内容に一切触れない設計）
- ★ 全画面の動画・スライドショーではラインを隠す（設定で変更可）
- ★ トレイアイコンにも現在のモードを表示
- ★ Phase 2 の検証用に「診断ウィンドウ」を用意
- ★ 実装フェーズの順番を入れ替え、IME 判定の実証を最初に行う

---

## 1. 概要

| 判定結果 | 表示色 | HEX |
|---|---|---|
| 日本語入力（ひらがな・カタカナ） | 緑 | `#18B957` |
| 英数字入力（IME オフ・全角英数・英語キーボード） | 赤 | `#E54848` |
| 不明・取得失敗 | 灰 | `#808080` |

アプリ名は **DaikonLine**（exe は `DaikonLine.exe`）。

## 2. 目的

IME の状態を確かめるためにタスクバー右下や入力欄を見る手間をなくす。視線を大きく動かさずに、打ち始める前にモードが分かる状態にする。

**最優先の原則：間違った色を出すくらいなら灰色にする。** 緑・赤が出ているときは信用できる、という状態を守る。

## 3. 対象環境と技術構成

DaikonDock と同じ構成にする。

| 項目 | 設計 | 理由 |
|---|---|---|
| OS | Windows 11（Windows 10 でも動けばよい） | |
| ランタイム | .NET Framework 4.8 | Windows に標準で入っている |
| 言語 | C# 5 | 利用者が Windows 付属の csc.exe でビルドする |
| UI | WinForms（ラインは自前描画、設定画面のみ標準コントロール） | |
| 外部パッケージ | なし | 単一 exe で配れる |
| 常駐 | NotifyIcon（タスクトレイ） | |
| ビルド | `build.bat`（csc.exe）→ `dist\DaikonLine.exe` | |
| 開発 | Rider（`src\DaikonLine.csproj`、net48 / LangVersion 5） | |
| 対象 IME | Microsoft IME（新しい版・以前のバージョンの両方を検証） | 他社製 IME は動作保証しない |
| 表示言語 | 日本語のみ | 日本語 IME が前提のアプリのため。DaikonDock の lang 機構は持ち込まない |

## 4. 表示仕様

### 4.1 ライン

- 各モニターの最上端に、モニターの横幅いっぱいの水平ラインを表示する。
- 太さの既定は **4 物理ピクセル**（設定で 1〜16）。DPI 倍率に関係なく物理ピクセルで指定する（4 章 4.4）。
- ウィンドウの拡張スタイル：

| スタイル | 目的 |
|---|---|
| `WS_EX_LAYERED` + `WS_EX_TRANSPARENT` | クリックを背後のウィンドウに通す（別プロセスのウィンドウにも通るのはこの組み合わせだけ） |
| `WS_EX_NOACTIVATE` | フォーカスを奪わない |
| `WS_EX_TOOLWINDOW` | Alt+Tab・タスクバーに出さない |
| `WS_EX_TOPMOST` | 最前面 |

- `WS_EX_LAYERED` を付けるので、作成直後に `SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA)` を呼ぶ（呼ばないと何も表示されない）。
- AppBar にはしない。作業領域を縮めず、最大化したウィンドウの上端 4 px に重ねる。クリックは透過するので、最大化したブラウザのタブなども普通に押せる。
- 色が変わったときだけ再描画する。

### 4.2 最前面の維持

- 他のアプリが TopMost ウィンドウを出すと、ラインがその下に回ることがある。
- フォアグラウンドウィンドウが変わったとき（5.3 の WinEvent）に `SetWindowPos(HWND_TOPMOST, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE)` で前面に戻す。タイマーでの連打はしない。
- スタートメニュー・通知センター・UAC・ロック画面・排他的フルスクリーンの上には出さない（出なくてよい）。

### 4.3 ★ 全画面アプリの上では隠す

全画面の動画・スライドショー・ゲームの上に色の線が出続けるのは邪魔なので、既定では隠す（設定 `hideInFullscreen`、既定 `true`）。

- 判定はモニターごと：フォアグラウンドウィンドウの矩形がそのモニターの**画面全体（作業領域ではない）**を覆い、かつ最大化状態（`IsZoomed`）でなければ「全画面」とみなし、そのモニターのラインだけ隠す。
- デスクトップ（`Progman` / `WorkerW`）は全画面扱いしない。
- 最大化ウィンドウは作業領域（タスクバーを除く）までなので、全画面とは区別できる。
- タスクバーの自動非表示を使っている環境では誤判定の可能性あり（Phase 2 で確認）。

### 4.4 マルチモニターと DPI

- 起動時に `SetProcessDpiAwarenessContext(PER_MONITOR_AWARE_V2)` を呼ぶ。失敗したら `SetProcessDpiAwareness(PER_MONITOR)`、さらに `SetProcessDPIAware` に落とす。これで `Screen.AllScreens` の座標が物理ピクセルになり、倍率の違うモニターでも 4 px が 4 px のまま出る。
- モニター 1 台につき `LineForm` を 1 つ作る。
- 対象モニター（設定 `monitors`）：

| 値 | 動作 |
|---|---|
| `all`（既定） | すべてのモニターに同じ色で表示 |
| `primary` | メインモニターだけ |
| `active` | フォアグラウンドウィンドウがあるモニターだけ ★ |

- `SystemEvents.DisplaySettingsChanged`（接続・切断・解像度・倍率の変更）で `LineForm` をすべて作り直す。

### 4.5 ★ トレイアイコン

- トレイアイコンにも現在の状態を出す（16px 程度の角丸四角を緑／赤／灰で塗り、白で「あ」「A」「?」）。ラインを非表示にしている間や、全画面で隠れている間の確認に使える。
- 3 種類のアイコンは起動時（と色の設定変更時）に作っておき、状態が変わったら差し替えるだけにする。作った `HICON` は終了時・作り直し時に `DestroyIcon` する。
- ツールチップ：`DaikonLine: あ 日本語`（63 文字まで）。

## 5. IME 状態の取得

### 5.1 判定対象

**フォアグラウンドウィンドウのスレッドで、キーボードフォーカスを持っているウィンドウ**の IME 状態。本アプリ自身の IME や、システム全体の既定値は見ない。

### 5.2 ★ 判定方式（IMM32 + WM_IME_CONTROL）

別プロセスの入力コンテキストは `ImmGetContext` では読めない。そこで、対象ウィンドウの「既定の IME ウィンドウ」にメッセージを送って問い合わせる（DaikonPalette の `ImeReader.cs` や AutoHotkey の IME.ahk と同じ、実績のある方式）。

1. `hwndFg = GetForegroundWindow()`。NULL なら **不明**。
2. `tid = GetWindowThreadProcessId(hwndFg, out pid)`。
   - `pid` が自分自身（トレイメニューや設定画面を開いている）なら**判定しない**（前の状態を保つ）。
3. `GetGUIThreadInfo(tid, ref gti)` で `hwndFocus` を取る。NULL なら `hwndActive`、それも NULL なら `hwndFg` を使う → `target`。
   - UWP アプリ（`hwndFg` のクラスが `ApplicationFrameWindow`）は、子の `Windows.UI.Core.CoreWindow` を探してそちらを `target` にする。
4. `hkl = GetKeyboardLayout(target のスレッド)`。
5. `imeWnd = ImmGetDefaultIMEWnd(target)`。NULL なら 7 の表で判定。
6. `SendMessageTimeout(imeWnd, WM_IME_CONTROL, IMC_GETOPENSTATUS, 0, SMTO_ABORTIFHUNG, 50ms)` でオン／オフ、オンなら続けて `IMC_GETCONVERSIONMODE` で変換モードを取る。
7. 判定：

| 条件 | 結果 |
|---|---|
| `hkl` の言語が日本語（0x0411）以外で、IME でもない（英語キーボードなど） | **英数（赤）** ★ |
| `hkl` が日本語以外の IME（中国語 IME など） | 不明（灰）※対象外 |
| 問い合わせ失敗・タイムアウト・`imeWnd` が取れない | 不明（灰） |
| オープン状態 = 0（IME オフ） | **英数（赤）** |
| オープン状態 = 1、変換モードに `IME_CMODE_NATIVE`（0x1）あり | **日本語（緑）**（ひらがな・全角カタカナ・半角カタカナ） |
| オープン状態 = 1、`IME_CMODE_NATIVE` なし | **英数（赤）**（全角英数・半角英数） |

定数：`WM_IME_CONTROL = 0x0283`、`IMC_GETCONVERSIONMODE = 0x0001`、`IMC_GETOPENSTATUS = 0x0005`。

判定方式は `IImeProbe` インターフェースの裏に隠し、方式を差し替え・追加できるようにする（5.5）。

```csharp
internal enum ImeMode { Unknown, Japanese, Alphanumeric }

internal sealed class ImeSnapshot
{
    public ImeMode Mode;
    public IntPtr Target;        // 判定したウィンドウ
    public string ProcessName;   // 診断用
    public string ClassName;     // 診断用
    public IntPtr Hkl;           // 診断用
    public int OpenStatus;       // -1 = 取得失敗
    public int ConversionMode;   // -1 = 取得失敗
    public string Failure;       // 失敗理由（診断用）
}

internal interface IImeProbe
{
    /// <returns>null = 判定しない（自分自身が前面など）</returns>
    ImeSnapshot Probe();
}
```

### 5.3 更新タイミング

イベント通知と定期チェックを組み合わせる。

| きっかけ | 仕組み |
|---|---|
| フォアグラウンドの変更 | `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` |
| 入力フォーカスの変更 | `SetWinEventHook(EVENT_OBJECT_FOCUS)` |
| IME のオン／オフ・モード変更 | 他プロセスの IME の変化を知らせるイベントは無いので、**定期チェック**（既定 100 ms、50〜1000 ms） |

- WinEvent は `WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS` で設定する。DLL を他プロセスに注入しない。
- ★ **キーボードフック（`WH_KEYBOARD_LL`）は使わない。** 半角/全角キーを拾えば反応は少し速くなるが、100 ms のポーリングで目標の 200 ms に収まるうえ、「キー入力を一切見ていない」と言い切れる方を優先する（ウイルス対策ソフトに怪しまれにくい利点もある）。

**スレッド構成**

```
UI スレッド                         監視スレッド（バックグラウンド）
 ├ WinEvent のコールバック ──Set()──▶ AutoResetEvent を WaitOne(間隔)
 │                                   │  ├ 起こされた or 間隔が経過
 │                                   │  ├ IImeProbe.Probe()
 │                                   │  └ 前回と結果が違うときだけ
 │◀────────── BeginInvoke ───────────┘     UI スレッドへ通知
 └ LineForm の色・トレイアイコンを更新
```

- `SendMessageTimeout` は応答しないアプリで最大 50 ms 待つことがあるので、UI スレッドでは呼ばない。
- 状態が変わらなければ UI スレッドには何も届かない（再描画しない）。
- 次のときは監視を止める：画面ロック中（`SystemEvents.SessionSwitch`）、スリープ中（`PowerModeChanged`）。再開時はすぐに 1 回判定する。
- ラインを非表示にしている間も監視は続ける（トレイアイコンで状態を確認できるようにするため。4.5）。

### 5.4 ★ 灰色にするときのルール

一瞬の取得失敗で灰色がチカチカしないよう、また前のアプリの色を新しいアプリに持ち越さないよう、次のルールにする。

- 同じ `target` で失敗が **2 回続いたら** 灰色にする（1 回だけの失敗では前の色を保つ）。
- フォアグラウンドが変わった直後は 30 ms 間隔で再試行し、**200 ms 以内に取れなければ灰色**にする。その間だけ前の色が残る。
- 自分自身が前面のときは、前の状態のまま（灰色にしない）。

### 5.5 取れない可能性があるアプリ（Phase 1 で検証）

| アプリ | 予想 | 備考 |
|---|---|---|
| メモ帳、Edge、Chrome、VS Code、Word、Excel | ○ | |
| エクスプローラー（アドレスバー・名前の変更） | ○ | |
| 新しい Teams / Outlook（WebView2） | ○ の見込み | |
| 設定アプリなど UWP | △ | 5.2 の CoreWindow 処理が必要 |
| スタートメニュー・検索 | △ | 別プロセス（SearchHost）。前面ウィンドウの扱いを確認 |
| Windows Terminal、コマンドプロンプト | △ | Terminal は TSF 専用、conhost はフォーカスが取れない可能性 |
| Java アプリ（IntelliJ など） | 要検証 | |
| 管理者として実行中のアプリ | × → 灰 | UIPI でメッセージが届かない。想定どおり |

**TSF での補完について**：TSF の状態（`GUID_COMPARTMENT_KEYBOARD_OPENCLOSE` など）は、そのアプリのプロセス内からしか読めない。他プロセスから読むには DLL 注入が必要になり、9 章の非機能要件（軽さ・安全性）に反するので採用しない。

△ のアプリで取れなかった場合の候補として、**タスクバーの入力インジケーター（「あ」「A」の表示）を UI Automation で読む**方式を評価する。これは常に前面のアプリの状態を表しているが、Windows の更新で構造が変わりうるので、採用する場合も「IMM32 で取れないときだけの補助」にする。

## 6. 常駐・操作

起動後はラインとトレイアイコンだけを出す。メインウィンドウは無い。

### 6.1 トレイ

- **左クリック**：ラインの表示 / 非表示を切り替え
- **右クリックメニュー**：

| 項目 | 動作 |
|---|---|
| DaikonLine 1.0.0 | バージョン表示（押せない） |
| 表示する（チェック） | ラインの表示 / 非表示 |
| 設定... | 設定画面を開く |
| 診断ウィンドウ ★ | 判定の中身をリアルタイム表示（6.3） |
| Windows 起動時に実行（チェック） | `HKCU\...\Run` に登録 / 削除（DaikonDock と同じ） |
| 終了 | フック解除・監視スレッド停止・トレイ削除をして終了 |

- Explorer が再起動したときは、NotifyIcon が `TaskbarCreated` を受けて自動でトレイに戻る（WinForms 標準の動作）。

### 6.2 設定画面

小さなダイアログ 1 枚。変更はその場でラインに反映し（プレビューを兼ねる）、「OK」で保存、「キャンセル」で元に戻す。

| 項目 | コントロール | 範囲・既定 |
|---|---|---|
| 日本語の色 / 英数の色 / 不明の色 | 色見本ボタン（`ColorDialog`） | `#18B957` / `#E54848` / `#808080` |
| ラインの太さ | NumericUpDown | 1〜16 px、既定 4 |
| 透過率 | NumericUpDown | 0〜90 %、既定 0（不透明）。`SetLayeredWindowAttributes` の α に反映。90 % を上限にして、ラインが見えなくならないようにする |
| 表示するモニター | ComboBox | すべて / メインだけ / 操作中のモニターだけ |
| 更新間隔 | NumericUpDown | 50〜1000 ms、既定 100 |
| 全画面アプリの上では隠す | CheckBox | 既定オン |
| 既定に戻す | Button | |

### 6.3 ★ 診断ウィンドウ

Phase 1 の検証と、利用者からの不具合報告に使う。前面アプリが変わっても隠れないよう TopMost・非アクティブ表示にする。

- 表示する内容：プロセス名、フォーカスウィンドウのクラス名、HKL、オープン状態、変換モード（ビット表示）、判定結果、失敗理由、1 回の判定にかかった時間
- 「コピー」ボタンで上記をテキストでクリップボードへ
- **ウィンドウタイトルは表示しない**（ファイル名やメールの件名など個人的な情報が含まれやすいため）

## 7. 設定ファイル

- 場所：exe と同じフォルダの `data\settings.json`（DaikonDock / DaikonPalette と同じく持ち運べる形）。
- 初回は存在しないので既定値で動き、設定画面で OK を押したときに作る。build.bat は何もコピーしない。
- 保存は一時ファイルに書いてから置き換える（書き込み途中で落ちても壊れないように）。
- 書き込めない場所（`C:\Program Files` など）に置かれていたら、保存時にその旨を表示する。
- 読み込みは `JavaScriptSerializer`。キー名の大文字小文字は区別しない。無い項目・型違い・範囲外は既定値に戻す（範囲外は丸める）。

```json
{
  "visible": true,
  "thickness": 4,
  "transparency": 0,
  "monitors": "all",
  "pollIntervalMs": 100,
  "hideInFullscreen": true,
  "colors": {
    "japanese": "#18B957",
    "alphanumeric": "#E54848",
    "unknown": "#808080"
  }
}
```

## 8. フォルダ・ソース構成

```
daikon-line.windows/
├─ build.bat               利用者向けの正式ビルド（csc.exe）→ dist\
├─ DaikonLine.sln          Rider 用
├─ Directory.Build.props   obj / bin を build\ に出す（/recurse が生成物を拾わないように）
├─ .editorconfig / .gitattributes / .gitignore
├─ README.md               利用者向けの説明
├─ LICENSE.md              MIT License
├─ docs/DESIGN.md          このファイル
└─ src/
   ├─ DaikonLine.csproj    Rider 用（net48 / LangVersion 5 / 外部パッケージなし）
   ├─ AssemblyInfo.cs      バージョン（ここだけで管理）
   ├─ Program.cs           起動・二重起動防止（Mutex）・DPI 対応の宣言
   ├─ AppController.cs     つなぎ役。トレイ・メニュー・設定・各部品を結び付ける
   ├─ Ime/
   │  ├─ ImeSnapshot.cs    ImeMode / ImeSnapshot
   │  ├─ IImeProbe.cs      判定方式のインターフェース
   │  ├─ Imm32Probe.cs     5.2 の WM_IME_CONTROL 方式
   │  └─ ImeMonitor.cs     WinEvent・監視スレッド・5.4 の灰色ルール・変化の通知
   ├─ Line/
   │  ├─ LineForm.cs       1 モニター分のライン
   │  └─ LineManager.cs    モニターの列挙・LineForm の作成と破棄・全画面判定・最前面の維持
   ├─ TrayIcons.cs         状態別トレイアイコンの生成と破棄
   ├─ SettingsForm.cs      設定画面
   ├─ DiagnosticsForm.cs   診断ウィンドウ
   ├─ Config.cs            LineSettings と settings.json の読み書き
   ├─ AppIcon.cs           埋め込みアイコンの読み込み
   ├─ NativeMethods.cs     Win32 API の宣言
   └─ Resources/
      ├─ app.png           アイコンの元画像
      └─ app.ico           exe のアイコン
```

依存の向きは `AppController` → 各部品の一方向。`ImeMonitor` は表示を知らず、`ImeChanged` イベントで `ImeSnapshot` を渡すだけ。`LineManager` は IME を知らず、色を受け取るだけ。

### build.bat

DaikonDock の build.bat から次のように変える（今フォルダにある build.bat / README.md は DaikonPalette のものなので置き換える）。

- 出力を `dist\DaikonLine.exe` に、リソース名を `DaikonLine.app.ico` に
- `/recurse:src\*.cs` でサブフォルダ（`Ime\`、`Line\`）も含めてコンパイル
- 言語の選択と `lang\` `data\` のコピーは無し
- `README.md`、`LICENSE.md` は毎回 `dist\` にコピー
- `/nopause` は残す

## 9. 非機能要件

| 項目 | 目標 |
|---|---|
| 入力遅延 | 発生させない（キー入力の経路に一切入らない） |
| CPU | 待機時の平均 0.1% 未満（判定 1 回は数十 µs の見込み、100 ms 間隔） |
| メモリ | ワーキングセット 30 MB 未満 |
| 切り替えの反映 | 200 ms 以内（ポーリング 100 ms + 描画） |
| 権限 | 一般ユーザー権限で動く。管理者を要求しない |
| プライバシー | キー入力・入力文字列・ウィンドウタイトルを読まない、記録しない。通信しない |
| 安定性 | 判定・描画の例外でアプリを落とさない（監視スレッド内は try/catch で握りつぶして不明扱い） |
| 復旧 | モニターの抜き差し、DPI 変更、スリープ復帰、ロック解除、Explorer の再起動のあとも表示が戻る |
| 多重起動 | Mutex で防ぐ |
| 終了 | 終了後にライン・フック・スレッドが残らない |

## 10. 実装フェーズ ★順番を変更

元の案では「表示の試作」が先だったが、**判定の精度が出なければアプリ自体が成り立たない**ので、判定の実証を最初にする。表示の試作は小さいので Phase 1 に含める。

### Phase 1 — IME 判定の実証（最優先）

- `Imm32Probe`、`ImeMonitor`、診断ウィンドウ、トレイ（終了だけ）、メインモニターに 1 本のライン
- Microsoft IME の新しい版・以前のバージョンの両方で、5.5 の表のアプリを検証して表を埋める
- 確認すること：入力欄間・アプリ間のフォーカス移動、半角/全角キー・英数キー・Ctrl+Space での切り替え、全角英数への切り替え
- **先に進む条件**：メモ帳・Edge・Chrome・VS Code・Word・Excel・エクスプローラーで誤判定が無いこと。それ以外のアプリは「正しい色」か「灰色」のどちらかであること（逆の色が出ないこと）
- 条件を満たさなければ、入力インジケーターの UI Automation 方式（5.5）を評価してから進む

### Phase 2 — 表示の仕上げ

- 全モニター対応、Per-Monitor DPI、表示モニターの選択
- クリック透過・非アクティブ・Alt+Tab に出ないことの確認
- 最前面の維持、全画面での非表示
- モニター変更・DPI 変更への追従

### Phase 3 — 常用機能

- 設定画面と settings.json
- トレイアイコンの状態表示
- 自動起動、表示 / 非表示
- ロック・スリープ時の監視停止と再開
- 例外処理

### Phase 4 — 配布・品質確認

- build.bat と Rider の両方でビルドが通ること
- 実機で CPU・メモリ・反映速度を計測
- スリープ復帰・モニター抜き差し・Explorer 再起動のテスト
- README の整備

## 11. 受け入れ基準

1. Windows 11 で起動すると、すべてのモニターの上端に 4 px のラインが出る。
2. 対応アプリの入力欄で「あ」なら緑、「A」なら赤になる。
3. IME を切り替えてから 200 ms 以内に色が変わる。
4. ラインの上をクリックすると、背後のウィンドウがクリックされる。キー入力を妨げない。
5. ウィンドウの最大化・アプリの切り替えのあともラインが残る。
6. 取得できないアプリでは灰色になり、逆の色は出ない。
7. 全画面の動画の上ではラインが隠れ、全画面を抜けると戻る（`hideInFullscreen` が既定のとき）。
8. トレイから終了でき、終了後にラインもプロセスも残らない。
9. 一般ユーザー権限で動く。

## 12. 採用しなかった案

| 案 | 見送った理由 |
|---|---|
| 低レベルキーボードフックで IME キーを検知 | ポーリングで目標を満たせる。入力に触れない設計を優先 |
| TSF の状態を読む | 他プロセスからは読めず、DLL 注入が必要になる |
| AppBar にして作業領域を確保する | 4 px のために全アプリの配置を変えるのは大げさ。クリック透過で十分 |
| 全角英数を別の色にする | 「英字が入る」点では英数と同じ。色が増えると一目で分からなくなる。要望があれば設定で追加する |
| カーソル（キャレット）の近くに表示 | キャレット位置が取れないアプリが多く、安定しない |
| 設定を JSON の手書きだけにする（DaikonDock 方式） | 色の指定は画面で選べる方が分かりやすい。JSON の手書きも可能（再起動で反映） |
| 英語 UI | 日本語 IME 向けのアプリのため不要 |
| .NET 8 / 外部パッケージ | DaikonDock と同じ理由（ランタイム・DLL が増え、csc.exe だけでビルドできなくなる） |

## 13. 既知の制限・未解決事項

- 管理者として実行中のアプリ、UAC、ロック画面、排他的フルスクリーンでは判定・表示できない（灰色、または表示されない）。
- 独自の入力方式を使うアプリや TSF 専用のアプリでは取れない可能性がある（Phase 1 で確定する）。
- 他社製 IME（Google 日本語入力、ATOK など）は動作保証しない。WM_IME_CONTROL に応える IME なら動く見込みなので、Phase 1 の余力があれば確認する。
- タスクバー自動非表示の環境での全画面判定は要確認。

## 14. スコープ外

- 入力文字列の監視・保存
- IME の自動切り替え、アプリごとの IME 状態の強制
- macOS / Linux 対応
- 外部サーバー・クラウドとの連携
