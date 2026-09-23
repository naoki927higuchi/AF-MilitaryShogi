# AF-MilitaryShogi 作業規約

- 採用した共通規約：W:/dev/00.規約・運用資料 の CHANGELOG.md `2026-09-23 13:23:07 +09:00` 時点。以後の共通規約の改訂は、ユーザーの指示がない限り本プロジェクトへ反映しない。
- 優先順位：ユーザーの明示指示 > 本規約 > 共通規約。
- 運用・メンテナンス中に見つかった問題で規約を追加する場合は、まず本規約に記録して本プロジェクトにのみ適用する。共通規約とするかはユーザーが都度決定する。

## プロジェクト固有の決定事項

未定の項目は「未定」と書き、決定時に更新する。

| 項目 | 内容 |
|---|---|
| 対象プラットフォーム | Windows |
| 製品プロジェクト | Unityプロジェクト（Unity 6.6 = 6000.6.2f1、Built-in Render Pipeline、Mono、Windows x64） |
| バージョン設定元 | `VERSION.txt`（3桁）。`GameBootstrap.Version` と一致しないとビルドが失敗する。ビルド時に `PlayerSettings.bundleVersion` へ反映 |
| Windowsリリース出力 | `bin/Release-<Version>/AF-MilitaryShogi.exe`（`Build-Windows.ps1`） |
| Androidリリース出力 | `bin/Android/Release-<Version>/AF-MilitaryShogi-<Version>.apk`（`Build-Android.ps1`、1.3.0〜）。ARM64/IL2CPP Release、デバッグ不可、シンボルなし。SHA256・マニフェスト・署名の検証結果を併置。Git管理外。一般配布しない |
| Android署名 | 専用のローカルRelease鍵。鍵とDPAPI暗号化パスワードは `.local/android-signing/`（Git管理外）。同一アプリの更新は同じ鍵で行う |
| Windows配布ZIP | `Package-Windows.ps1` で `Builds/Package/` に作成・検証（100MB未満、デバッグ成果物なし、同梱README）→ 公開準備で `Prepare-Release.ps1` が `Distribution/` へZIP・SHA256・JSONを配置 |
| 検証方法 | `Run-Tests.ps1`（ルール・エンジン・CPU・情報境界・強さ/戦い方・あそびかた・プリセット・全局の自動テスト、.NET 9 SDK）、`Build-Windows.ps1`（アプリアイコン設定の確認を含む）、`Run-AutoTest.ps1`（ビルドしたEXEで自動対局を2回：表示モード切替あり/なしの指紋比較、再起動後のユーザーデータ保持、敵駒表面の描画検査、UI状態遷移ストレステスト、損失表示・レイアウト・効果音同期・経過時間・あそびかた一時停止・ロゴ・アイコンの検査、スクリーンショット）。人の手による実プレイ確認・聴感確認は別途 |
| 公開先 | `Distribution/` |
| 利用者が別途用意するランタイム・外部ツール | なし（Unityプレイヤー同梱） |
| 製品ライセンス | 公開時に決定 |
| 第三者ライセンス表記 | 公開時に決定。駒テクスチャには昭和書体「闘龍」（KSO闘龍）で描画した文字が含まれる（フォントファイルは同梱しない。使用許諾 https://designpocket.jp/font/detail/23984 ）。フォントの使用許諾と配布物は2026-09-24に監査済み（判定A、Tests/ACCEPTANCE-1.3.0.md）。製品ライセンスは公開時に決定 |

## 素材の管理（本プロジェクト固有）

- 人間（ユーザー）が提供した原本は `Reference/UserProvided/` に置き、非破壊で保持する。加工・合成・生成したものと混在させない。
- ユーザーが W:/dev 直下に新しい素材を置いた場合は、作業開始時に `Reference/UserProvided/` の該当フォルダーへ移動してから使う。移動したら `Reference/UserProvided/README.md` の一覧（ファイル名・受領日・SHA256）を更新する。
- ゲーム用に加工・生成したリソースは `Assets/Generated/`（ランタイム読込は `Assets/Generated/Resources/`）、ゲームに入らない確認用の生成物は `Generated/` に置く。
- 駒・盤テクスチャは `Tools/TextureGen/generate_textures.py` で原本から機械的・再現可能に生成する。手作業で画像を編集しない。生成物の入力ハッシュは `Generated/texture_generation_manifest.json` に記録される。
- 昭和書体「闘龍」（KSO闘龍）は、正規ライセンスで開発PCのOSへインストールしたものだけを生成時に読み込む（1ライセンス/1PC）。フォントファイルをリポジトリ・Reference・Assets・ビルド・Distribution・APKへ置かない（`.gitignore` でフォントファイルと `Reference/UserProvided/Fonts/` を除外）。ビルドやCI・端末へフォントをコピー・インストールする構成にしない。
- 生成スクリプトはKSO闘龍がなくても止めず、OSの日本語フォントへ代替して完走する（代替した旨を表示し、`Generated/texture_generation_manifest.json` に requested/actual/fallback を記録）。代替フォントの生成物で正式な生成済みテクスチャを上書き・コミットしない。通常のビルド・配布ZIP作成は生成スクリプトを実行しない（2026-09-24〜）。
- 効果音は外部の音源・効果音素材を使わず、`Tools/AudioGen/generate_sfx.py` で固定Seedから再現可能に生成する（`audio_manifest.json`）。木の乾いた物理音に限り、銃声・爆発・サイレン・電子音・単一周波数の音、BGMを入れない（1.2.0〜）。

## ルールとCPUの不変条件（本プロジェクト固有）

- 対局ルールは README.md「採用ルール」の1種類に固定する。ローカルルール切り替えは実装しない。ルールの曖昧点はユーザーに確認し、決定をREADME.mdに記録する。
- CPU（`MilitaryShogi.Cpu`）は `MilitaryShogi.Rules` と `MilitaryShogi.Observation` だけを参照する。`MilitaryShogi.Engine` への参照・リフレクション等で敵駒の真の種類へ到達する経路を作らない。テスト（Boundary系）で検査する。
- 敵駒の表面テクスチャを敵駒の表示オブジェクトへ渡すコードを書かない。唯一の例外は研究モードの「CPU駒の正体を表示」ON時の盤上駒（1.2.0〜、`GameSession.ResearchTrueKind` 経由）。対戦モードでは常に不可、損失表示の敵駒は常に裏面。真値をCPU・PlayerKnownFactsへ渡さない。戦闘演出は駒種に依存させない。撃破した敵駒の正体も公開しない。
- Seedは Player Formation Seed / CPU Formation Seed / CPU Decision Seed を分離し、乱数は `DeterministicRandom` を使う（`System.Random` をゲームロジックに使わない）。
- 表示モード（対戦／研究）とあそびかたはPresentation層の状態とし、切替で `GameSession`（盤面・手番・CPU Knowledge・Seed・履歴）や乱数状態を変更・再生成しない（1.1.0〜、EXE自動検証で指紋比較）。起動時は対戦モード。
- 対戦モードにはSeed・CPU評価値・推定確率などの研究情報を表示しない。1.1.1の受入修正では、敵駒ホバーにEnemy番号・位置・移動・戦闘の客観的事実と公開観測から一意に判明した駒種を表示する。研究機能は研究モードに残す。
- プレイヤーの判明情報は Observation の PlayerKnownFacts で管理し、CPU Knowledgeとは分離する。敵の真のPieceType・CPU確率を参照せず、公開移動（経路・飛び越し・突入口）と戦闘結果のみから候補を絞る。候補が1種類の時だけ「判明」とし、撃破後も保持する。敵駒の盤上・損失表示のテクスチャは常に共通裏面。
- 敵駒ツールチップは投影した駒のBoundsを基準に配置し、駒の周囲に余白を取り、右→左→上下の空き領域と主要UIを考慮する。マウス座標へ固定pxを加算する方式に戻さない。
- 1.1.1はUI/UXのパッチ。CPU・評価・難易度・戦い方・配置思想・初期配置・ルールの変更を含めない。1.2.0以降は1.1.1を既知の正常な基準点とする。
- CPUの強さ・戦い方は選択の精度とリスクの取り方だけを変え、敵駒推定（CpuKnowledge）とルール理解を変えない（テストで検査）。「中・バランス」は1.0.0と同じ判断を保つ。
- あそびかたの戦闘相性・移動図は `RuleReference`（勝敗表・移動生成）から作り、手書きの表を持たない。
- 1.2.0の表示・操作系の追加（プリセット・設定・経過時間・演出・効果音・レイアウト）はCPUの強さ・2手読み・評価・重み・配置思想・Knowledge・Decision Seed・情報隠蔽を変更しない。1.1.1と同じSeedでCPU評価指標と実EXEの指紋が一致することを確認する。
- 配置プリセットはSeedではなく各マスのPieceTypeを形式バージョン付きで保存し、保存時・読込時に配置規則で検証して不正なデータを適用しない。読込は自軍配置だけを変え、CPUのSeed・配置・Knowledgeに触れない。
- ユーザーデータ（プリセット・設定・UIエラーログ）は `Application.persistentDataPath`（`-dataDir` で変更可）に置き、自動検証は専用フォルダーを使って実ユーザーのデータに触れない。
- 盤面レイアウトは盤＋左右の損失置き場（31枚分）を1つの視覚グループとして、カメラ角度・パースを保ったまま投影オフセットでプレイ領域の中央へ置く。固定pxの上下調整をしない。損失置き場の自軍・敵軍は面と並び順以外のTransform・影・卓面距離を共通にする。
- Modal UI（設定・あそびかた・確認ダイアログ等）は `ModalInput` に登録し、最前面のModalだけが入力を持つ。背後のIMGUIは `ModalInput.Background` の中で描き、盤面などUpdateで読む入力は `ModalInput.PointerBlocked` を確認する。背後ボタンを個別に無効化しない。Modal外クリックは閉じずに消費し、閉じたクリックを背後へ渡さない。Modal（入力）と一時停止（時間）は別概念として扱う（1.2.2〜）。
- Android版（1.3.0〜）は対戦モードのみ。研究モード・思考モニター・Seed操作・CPU真値表示・研究用の履歴とショートカットを載せない。ゲーム本体はPCと共通コードとし、Android専用なのはPresentation（`MobileUi`）だけ。画面回転・バックグラウンドはPresentation／一時停止だけで扱い、GameSession・CPU・乱数・経過時間を作り直さない。
- 双方の占領可能駒（大将〜少佐）が0になったら引き分け。プレイヤーだけ0になったら審判が一局に一度だけ投了を確認する。CPUは投了しない。CPU側だけ0になったことをプレイヤーへ通知しない（1.3.0〜）。
- IMGUIの描画は `UiGuard` で包み、例外で入力状態（hotControl・keyboardControl・GUI.enabled・matrix）が残らないようにする。あそびかたと一時停止・timeScaleの整合は `Presentation` のウォッチドッグで保つ。見えないオーバーレイが入力を塞ぐ状態を作らない（EXE自動検証のストレステストで検査）。

## 構成管理

- バージョンはSemantic Versioningの `メジャー.マイナー.パッチ`。最初のビルド成果物は `1.0.0`。上位の番号を増やすときは下位を0に戻す。
- メジャー更新は互換性を破棄する変更で、ユーザーの指示または定義された明確な設計変更に基づく場合のみ行う。マイナー更新（互換性を保つ機能追加）・パッチ更新（不具合修正・軽微なリファクタリング・調整）は開発者の判断で行う。
- ソリューションルートの `HISTORY.md` に各バージョンの日時と変更概要を記録する。バージョンをインクリメントしてHISTORY.mdを更新するタイミングで、バージョン更新用のGitコミットを作成する。
- 成果物・バージョン設定元・HISTORY.mdの版数を一致させる。規約・配布手順だけの変更で既存バイナリの版数を変更しない。

## Git・GitHub

- ローカルGit管理はソリューション単位で行う。初回コミット前に.gitignoreを整備し、秘密情報・実環境の接続設定・ビルド出力・開発用ZIPを追加しない。
- 作業開始時に `git status` を確認し、未コミット変更を勝手に破棄しない。既存履歴を書き換えず、新しいコミットを追加する。
- まとまった変更と必要な検証の完了時に、個別の指示を待たずにローカルコミットを作成する。
- GitHubリポジトリ作成・リモート設定・push・Public化はユーザーの指示がある場合のみ、指示の範囲で行う。ローカルコミットを自動pushの指示として扱わない。

## Windows

- 開発用ZIPは作成しない。Releaseは上表のバージョン別出力先へEXEと必要なDLL・データを直接出力し、その場所のEXEを起動・検証する。完了報告の起動リンクもそのEXEを指す。
- 異なるバージョンの出力を混在させず、旧バージョンの出力を上書きしない。出力と中間生成物はGit管理対象外。
- MSIXやコンテキストメニュー登録など、インストールが必要な検証は別途行う。
- 公開準備の時点でだけ、検証済みReleaseから配布ZIPを作成・検証し、ZIP・SHA256・版数と変更概要を `Distribution/` に置いてGit管理する。`Distribution/README.md` の最新版案内を同じコミットで更新する。
- 配布ZIPは1ファイル100MB未満とする。ランタイムや外部ツールは同梱せず、上表の必要物・入手先・導入方法を `Distribution/README.md` と同梱READMEに記載する。100MB未満にできない場合はユーザーに相談する。
- 公開済みZIPを同じ版数で上書きしない。旧ZIPを削除しない。公開準備・コミット・pushは分けて実行する。
- 第三者ライセンス表記は実際に同梱するコンポーネントに限る。
