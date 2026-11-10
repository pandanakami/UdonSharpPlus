# 開発メモ

このリポジトリで UdonSharp Plus を保守する人向けのメモです。利用者向けの説明は、パッケージの [README](Packages/com.pandanakami.udonsharpplus/README.md) にあります。

## UdonSharp の内部 API への依存

UdonSharp Plus は、Harmony で UdonSharp（VRChat Worlds SDK に同梱）の内部処理にパッチを当てています。使っている内部 API は [UdonSharpInternals.cs](Packages/com.pandanakami.udonsharpplus/Editor/UdonSharpInternals.cs) にまとめています。

- 起動時にまとめて探し、1 つでも見つからないときは、どのパッチも当てません。パッチを当てる途中で失敗したとき（引数名が変わったときなど）も、当てた分を外します。
- 無効になると、コンソールに `[UdonSharpPlus] この VRChat SDK（x.y.z）では UdonSharp の内部が変わっているため、無効にしました（…）` のエラーが 1 つ出て、素の UdonSharp と同じ動きになります。
- 対応する SDK の範囲は、[package.json](Packages/com.pandanakami.udonsharpplus/package.json) の `vpmDependencies` です（今は `~3.10.6`。確かめたマイナーバージョンだけにしています）。VCC は、この範囲に合う SDK とだけ組み合わせます。

## VRChat SDK のバージョンを上げるとき

1. このプロジェクトの SDK を上げる。VCC で `com.vrchat.worlds`（と `com.vrchat.base`）を更新すると、`Packages/vpm-manifest.json` が変わります。CI はこのファイルから SDK を復元します。
2. Unity を開き、コンソールに `[UdonSharpPlus] … 無効にしました` のエラーが出ていないか見る。
3. EditMode テスト（`UdonSharpPlus.Editor.Tests`）を実行する。
   - まず `UdonSharpInternalsTests` を見る。落ちていたら、メッセージにある内部 API が変わっています。UdonSharp のソース（`Packages/com.vrchat.worlds/Integrations/UdonSharp/`）を見て、`UdonSharpInternals.cs` と、それを使うパッチを直します。
   - ゴールデンテスト（`GoldenTests`）が落ちたら、UdonSharp が出す Udon Assembly が変わっていないか、期待値ファイルとの差分を見る。変わったのが UdonSharp 側の都合だけなら、`Tools > UdonSharpPlus > Tests > Update Expected Files` で期待値を作り直す。
4. `Assets/FeatureTests/FeatureTests.unity` を ClientSim で実行し、全パネルが `RESULT: OK` になることを見る。#32（ネットワーク）は 2 クライアント、#53（実行時エラー）は手で操作して確かめる。
5. VRChat のクライアントでも、同じパネルを確かめる。
6. `package.json` の `vpmDependencies` の範囲を広げ（例：`>=3.10.6 <3.12`）、README の「動作対象」も直して、パッケージのバージョンを上げてリリースする。

## リリース

1. [CHANGELOG.md](Packages/com.pandanakami.udonsharpplus/CHANGELOG.md) に、新しいバージョンの節（`## [0.1.2]`）を足す。英語（`### English`）と日本語（`### 日本語`）の両方を書く。
2. `package.json` の `version` を同じバージョンにする。
3. コミットして push し、`gh workflow run release.yml` でリリースのワークフローを実行する。
   - ワークフローは、CHANGELOG.md のそのバージョンの節（見出しの次の行から、次の `## [` の前まで）を、GitHub のリリースの本文にする。
   - 節がない・中身が空のときは、タグを作る前に止まる（本文のないリリースを出さないため）。
   - リリースのあと、VPM のリスティング（Build Repo Listing）も作り直す。

## 仕組みの図

README とドキュメントの仕組みの図（`Packages/com.pandanakami.udonsharpplus/Documentation~/images/how-it-works.*.svg`）は、同じフォルダの `.mmd`（mermaid）から作っています。直すときは `.mmd` を直し、mermaid-cli で SVG を作り直します。GitHub で `<img>` として表示しても崩れないよう、ラベルは HTML ではなく SVG の文字にしています（`.mmd` の先頭の `htmlLabels: false`）。

```sh
npx -p @mermaid-js/mermaid-cli@11.4.2 mmdc -b white -i how-it-works.ja.mmd -o how-it-works.ja.svg
```
