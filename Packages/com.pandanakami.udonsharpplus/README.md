# UdonSharp Plus

UdonSharp で書ける C# を増やす、エディタ専用のパッケージです（インタフェース、普通のクラス、ジェネリクス、List / Dictionary、LINQ など）。UdonSharp のコンパイラに Harmony でパッチを当て、ソースを UdonSharp が扱える形に書き換えます。出力されるのは通常の Udon プログラムなので、VRChat のクライアント側には何も要りません。

> **実験段階です。** 動作対象は VRChat Worlds SDK 3.10.x です。使うことによって生じたいかなる損害についても、作者は一切の責任を負いません（[免責事項](https://github.com/pandanakami/UdonSharpPlus/blob/main/README.ja.md#免責事項)）。

- 導入方法と概要：[リポジトリの README](https://github.com/pandanakami/UdonSharpPlus/blob/main/README.ja.md)（[English](https://github.com/pandanakami/UdonSharpPlus/blob/main/README.md)）
- 詳しい説明：このフォルダの [Documentation~/README.md](Documentation~/README.md)

## ライセンス

[MIT License](LICENSE.md)（Copyright (c) 2026 pandanakami）

このパッケージには、VRChat SDK・UdonSharp・Harmony・Roslyn は含まれていません。これらは VRChat SDK に同梱されたものを参照しているだけで、それぞれのライセンスに従います（VRChat SDK：https://hello.vrchat.com/legal/sdk ）。
