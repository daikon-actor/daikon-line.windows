# DaikonLine

画面のいちばん上に細い色のラインを出して、今の入力モードをひと目で分かるようにする常駐アプリです。

| ラインの色 | 入力モード |
|---|---|
| 緑 | **あ** 日本語（ひらがな・カタカナ） |
| 赤 | **A** 英数（IME オフ・全角英数・英語キーボード） |
| 灰 | 不明（入力モードを取得できないアプリ） |

- 入力しているアプリ（メモ帳・Edge・Chrome・VS Code・Word・Excel など）の入力モードを表示します
- ラインはクリックを通すので、下にあるタブやボタンもそのまま押せます
- 全画面の動画やスライドショーの上ではラインが隠れます
- タスクトレイのアイコンにも、今の入力モードが「あ」「A」「?」で出ます
- 入力した文字は読みません。記録も通信もしません

## ビルド（インストール不要）

1. `build.bat` をダブルクリック
2. Windows 付属の `csc.exe`（.NET Framework 4.x）でビルドされ、`dist\DaikonLine.exe` ができます
3. `dist` フォルダを好きな場所（例: `C:\Tools\DaikonLine`）に置いて `DaikonLine.exe` を実行します

.NET SDK や Visual Studio は不要です。

設定は `dist\data\settings.json` に保存されるので、`dist` は `C:\Program Files` ではなく、自分で書き込める場所に置いてください。

## 使い方

起動すると、画面の上端にラインが、タスクトレイにアイコンが出ます。ウィンドウは開きません。

| 操作 | 内容 |
|---|---|
| トレイアイコンを左クリック | ラインの表示 / 非表示 |
| トレイアイコンを右クリック | メニュー |

### メニュー

| 項目 | 内容 |
|---|---|
| ラインを表示する | ラインの表示 / 非表示（トレイアイコンは表示されたまま） |
| 設定... | 色・太さ・透過率・表示するモニター・更新間隔・全画面での表示 |
| 診断ウィンドウ | 入力モードの判定の中身を表示（下記） |
| Windows 起動時に実行 | PC の起動時に自動で起動する |
| 終了 | DaikonLine を終了 |

### 設定

| 項目 | 既定 | 内容 |
|---|---|---|
| 日本語 / 英数 / 不明の色 | 緑 / 赤 / 灰 | ラインとトレイアイコンの色 |
| ラインの太さ | 4 px | 1〜16 px。表示倍率に関係なく、画面のピクセル数で指定 |
| 透過率 | 0 % | 0〜90 %。大きくするほど下の画面が透けて見える |
| 表示するモニター | すべて | すべて / メインのモニターだけ / 操作中のモニターだけ |
| 更新間隔 | 100 ms | 50〜1000 ms。入力モードを確かめる間隔 |
| 全画面のアプリの上では隠す | オン | 動画・スライドショー・ゲームなど |

設定画面での変更は、その場でラインに反映されます。「OK」で保存、「キャンセル」で元に戻ります。

`data\settings.json` をメモ帳で直接書き換えることもできます（DaikonLine の再起動で反映）。

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

- `monitors` は `all` / `primary` / `active`
- 色は `#RRGGBB` の形で書きます
- 書き方が間違っている項目は既定値になります。範囲外の数値は範囲内に丸めます

## 灰色になるとき

次のアプリでは入力モードを取得できず、灰色になることがあります。間違った色を出すよりは灰色にする、という方針です。

- 管理者として実行しているアプリ（Windows の制限で問い合わせが届きません）
- Windows Terminal、コマンドプロンプト、スタートメニューの検索など、一部のアプリ
- 日本語以外の IME（中国語・韓国語の IME など）

英語キーボードに切り替えているときは赤になります。

## 診断ウィンドウ

あるアプリで色がおかしいときは、次の手順で状況を確認できます。

1. メニューの「診断ウィンドウ」を開く
2. 確かめたいアプリの入力欄をクリックし、IME を切り替えてみる
3. 診断ウィンドウに戻って「コピー」を押す

プロセス名・ウィンドウのクラス名・IME の状態などがコピーされます。ウィンドウのタイトルや入力した文字は含まれません。

## 注意

- Microsoft IME で動作を確認しています。Google 日本語入力・ATOK などは動作保証外です
- UAC の画面・ロック画面・排他的フルスクリーンのゲームの上には表示されません
- 二重起動はしません。タスクバーと Alt+Tab には出ません

## フォルダ構成（dist）

```
DaikonLine.exe
README.md
LICENSE.md
data\
└─ settings.json   設定（設定画面で OK を押したときに作られる）
```

## 開発者向け

設計は `docs\DESIGN.md` にあります。

```
DaikonLine.sln
├─ build.bat              利用者向けの正式ビルド（csc.exe / C# 5）→ dist\
├─ Directory.Build.props  Rider でのビルド出力を build\ にまとめる
├─ .editorconfig          コーディング規約
├─ docs\DESIGN.md         設計
└─ src\
   ├─ DaikonLine.csproj   Rider 用（net48 / LangVersion 5）
   ├─ Program.cs          起動・二重起動防止・DPI 対応の宣言
   ├─ AppController.cs    つなぎ役（トレイ・メニュー・設定・監視・ライン）
   ├─ Ime\
   │  ├─ ImeSnapshot.cs   判定結果（ImeMode / ImeSnapshot）
   │  ├─ IImeProbe.cs     判定方式のインターフェース
   │  ├─ Imm32Probe.cs    WM_IME_CONTROL による判定
   │  └─ ImeMonitor.cs    WinEvent・監視スレッド・灰色のルール
   ├─ Line\
   │  ├─ LineForm.cs      1 モニター分のライン
   │  └─ LineManager.cs   モニターごとのライン・全画面判定・最前面の維持
   ├─ TrayIcons.cs        状態別のトレイアイコン
   ├─ SettingsForm.cs     設定画面
   ├─ DiagnosticsForm.cs  診断ウィンドウ
   ├─ Config.cs           設定と settings.json の読み書き
   ├─ AppIcon.cs          埋め込みアイコンの読み込み
   ├─ NativeMethods.cs    Windows API の宣言
   ├─ AssemblyInfo.cs     バージョン
   └─ Resources\
      ├─ app.png          アイコンの元画像
      └─ app.ico          exe のアイコン（app.png から作成）
```

### ルール

- Windows 付属の csc.exe は C# 5 までなので、C# 6 以降の構文（`$"..."`、`?.`、`=>` で書くメンバー、`{ get; }` だけのプロパティ、`out var`、`nameof` など）は使わない。Rider 用の csproj は `LangVersion=5` なのでエラーになる
- `.cs` は UTF-8（BOM 付き）・CRLF。`build.bat` は ASCII 文字だけで書く
- 外部ライブラリは使わない（.NET Framework 付属のものだけ）
- バージョンは `src\AssemblyInfo.cs` で管理する
- `src` の下にフォルダを増やしても、`build.bat` は `/recurse` でまとめてコンパイルするので修正は不要

## ライセンス

MIT License（`LICENSE.md`）
