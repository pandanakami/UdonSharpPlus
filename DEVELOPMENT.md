# 開発メモ

このリポジトリで CustomUdonSharp を保守する人向けのメモです。利用者向けの説明は、パッケージの [README](Packages/com.pandanakami.customudonsharp/README.md) にあります。

## UdonSharp の内部 API への依存

CustomUdonSharp は、Harmony で UdonSharp（VRChat Worlds SDK に同梱）の内部処理にパッチを当てています。使っている内部 API は [UdonSharpInternals.cs](Packages/com.pandanakami.customudonsharp/Editor/UdonSharpInternals.cs) にまとめています。

- 起動時にまとめて探し、1 つでも見つからないときは、どのパッチも当てません。パッチを当てる途中で失敗したとき（引数名が変わったときなど）も、当てた分を外します。
- 無効になると、コンソールに `[CustomUdonSharp] この VRChat SDK（x.y.z）では UdonSharp の内部が変わっているため、無効にしました（…）` のエラーが 1 つ出て、素の UdonSharp と同じ動きになります。
- 対応する SDK の範囲は、[package.json](Packages/com.pandanakami.customudonsharp/package.json) の `vpmDependencies` です（今は `~3.10.6`。確かめたマイナーバージョンだけにしています）。VCC は、この範囲に合う SDK とだけ組み合わせます。

## VRChat SDK のバージョンを上げるとき

1. このプロジェクトの SDK を上げる。VCC で `com.vrchat.worlds`（と `com.vrchat.base`）を更新すると、`Packages/vpm-manifest.json` が変わります。CI はこのファイルから SDK を復元します。
2. Unity を開き、コンソールに `[CustomUdonSharp] … 無効にしました` のエラーが出ていないか見る。
3. EditMode テスト（`CustomUdonSharp.Editor.Tests`）を実行する。
   - まず `UdonSharpInternalsTests` を見る。落ちていたら、メッセージにある内部 API が変わっています。UdonSharp のソース（`Packages/com.vrchat.worlds/Integrations/UdonSharp/`）を見て、`UdonSharpInternals.cs` と、それを使うパッチを直します。
   - ゴールデンテスト（`GoldenTests`）が落ちたら、UdonSharp が出す Udon Assembly が変わっていないか、期待値ファイルとの差分を見る。変わったのが UdonSharp 側の都合だけなら、`Tools > CustomUdonSharp > Tests > Update Expected Files` で期待値を作り直す。
4. `Assets/FeatureTests/FeatureTests.unity` を ClientSim で実行し、全パネルが `RESULT: OK` になることを見る。#32（ネットワーク）は 2 クライアント、#53（実行時エラー）は手で操作して確かめる。
5. VRChat のクライアントでも、同じパネルを確かめる。
6. `package.json` の `vpmDependencies` の範囲を広げ（例：`>=3.10.6 <3.12`）、README の「動作対象」も直して、パッケージのバージョンを上げてリリースする。
