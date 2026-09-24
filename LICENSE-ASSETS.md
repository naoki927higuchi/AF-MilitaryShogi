# ライセンス（コード以外の素材・例外） / Licensing of assets and exceptions

このリポジトリは、対象ごとに条件が異なります。  
This repository uses different terms for different kinds of files.

| 対象 / What | 条件 / Terms |
|---|---|
| ソースコード（`Assets/Scripts/`、`Assets/Editor/`、`Tests/`、`Tools/`、`*.ps1` など）<br>Source code | [MIT License](LICENSE)（Copyright (c) 2026 af） |
| 作者が作成したコード以外の素材（下記の除外を除く）<br>Author's non-code assets (except those excluded below) | [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/deed.ja)（表示：af / afさんのAIラボ） |
| ブランド素材（除外）<br>Brand assets (excluded) | **許諾しません。** Not licensed. |
| 購入フォントで描いた文字画像（除外）<br>Images drawn with a purchased font (excluded) | **許諾しません。** Not licensed. |
| 第三者のソフトウェア・素材<br>Third-party software and assets | 元のライセンスのとおり / Their own licenses |

## CC BY 4.0 の対象 / Covered by CC BY 4.0

- 効果音：`Assets/Generated/Resources/Audio/`（`Tools/AudioGen` で合成）
- 盤・卓の木目、駒の裏面・側面：`Assets/Generated/Resources/Textures/Board/board_wood.png`、`table_wood.png`、`Assets/Generated/Resources/Textures/Pieces/piece_back.png`、`piece_side.png`
- 参考画像（外部チャットの画像生成機能で作者が作成）：`Reference/UserProvided/Images/` のうち、下記の除外に当たらないもの
- README などの文書

表示の例 / Attribution example: `AF-MilitaryShogi by af (afさんのAIラボ), CC BY 4.0`

## 許諾しない：ブランド素材 / Not licensed: brand assets

複製・改変したものを公開・配布する場合は、次を差し替えてください。本家と取り違えられないようにするためです。  
If you publish or distribute a copy or a modified version, replace the following so that it is not mistaken for the original.

- 名称：「AF-MilitaryShogi」「afさんのAIラボ」「AF-AI LAB」 / Names
- ロゴ・タイトル画像：`Assets/Generated/Resources/Textures/UI/title_logo.png`、`Reference/UserProvided/Images/ロゴ タイトルとコピーのみ.png`、`Reference/UserProvided/Images/ロゴ イメージ背景込み.png`
- アプリアイコン：`Assets/Generated/Icons/`、`Reference/UserProvided/Images/アイコン画像.png`
- マスコット（チャッピー、クロさん等）/ Mascots

## 許諾しない：KSO闘龍で描いた画像 / Not licensed: images drawn with KSO Touryu

駒の文字と「総司令部」は、作者が正規に購入した昭和書体「闘龍」（KSO闘龍）で描いた画像です。フォントの使用許諾は字形の転用を認めていないため、これらの画像を第三者に許諾することはできません。流用する場合は差し替えてください。`Tools/TextureGen/generate_textures.py` を実行すると、KSO闘龍がない環境ではOSの日本語フォントで作り直せます。  
The piece names and the HQ label are images drawn with the licensed font KSO Touryu. They cannot be sublicensed. Regenerate them with `Tools/TextureGen/generate_textures.py` (it falls back to a system Japanese font).

- `Assets/Generated/Resources/Textures/Pieces/piece_*.png`（`piece_back.png`・`piece_side.png` を除く）
- `Assets/Generated/Resources/Textures/Board/label_hq.png`
- `Generated/Previews/piece_contact_sheet.png`
- フォント本体はリポジトリにも配布物にも含まれていません。使用許諾：https://designpocket.jp/font/detail/23984

## 第三者 / Third party

- Unity のエンジン・ランタイム・パッケージ：Unity の利用規約と各パッケージのライセンスに従います。Windows版・Android版のビルドに含まれる Unity のファイルは、このリポジトリのライセンスの対象ではありません。
- 生成スクリプトが使う Python・Pillow・NumPy は同梱していません。

## 補足 / Notes

- 上記は作者の方針を示したもので、法的助言ではありません。
- 過去に公開した配布ZIP（`Distribution/AF-MilitaryShogi-1.3.0-Windows.zip`、`1.5.1`）も、2026-09-25以降は同じ条件で扱います。ZIPに同梱のREADMEにはライセンスの記載がありません（同じ版数のZIPは上書きしない規約のため、次の公開版から記載します）。
