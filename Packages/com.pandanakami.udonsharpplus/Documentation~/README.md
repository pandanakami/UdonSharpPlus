# UdonSharp Plus ドキュメント

UdonSharp で書ける C# を増やすパッケージ、UdonSharp Plus の詳しい説明です。導入方法と概要は、[リポジトリの README](https://github.com/pandanakami/UdonSharpPlus/blob/main/README.ja.md) を見てください。

> **実験段階です。** UdonSharp の内部 API に依存しているため、SDK のバージョンが変わると動かなくなる可能性があります。動作対象は VRChat Worlds SDK 3.10.x です。使うことによって生じたいかなる損害についても、作者は一切の責任を負いません（[免責事項](https://github.com/pandanakami/UdonSharpPlus/blob/main/README.ja.md#免責事項)）。

## 機能

| 機能 | 内容 |
|---|---|
| [インタフェース](interface.md) | UdonSharpBehaviour と普通のクラスで、ユーザー定義のインタフェースを実装・利用する |
| [普通のクラス](class.md) | UdonSharpBehaviour 以外の普通のクラスを、UdonSharpBehaviour の中で使う |
| [ジェネリクス](generics.md) | ジェネリッククラス・ジェネリックメソッドを宣言して使う |
| [List / Dictionary・ラムダ・LINQ](collections.md) | `List<T>`・`Dictionary<TKey, TValue>`、それらのメソッドに渡すラムダ、LINQ |

それぞれのページに、対応している書き方と、未対応の書き方（コンパイルエラーになるもの）を書いています。

## 全体に共通すること

- **書き換えはエディタの中だけ**：UdonSharp がコンパイルする前に、C# のソースを UdonSharp が扱える形に書き換えます。出力されるのは通常の Udon プログラムなので、VRChat のクライアント側には何も要りません。
- **エラーの位置**：書き換えで生成したコードの中で出たコンパイルエラーと実行時例外は、元のソースの位置に表示されます。必要に応じて、どのクラス・メンバーのコピーの中かの補足が付きます（[普通のクラス](class.md)の「注意」）。
- **書き換え後のソース**：デバッグ用に、プロジェクトの `Library/UdonSharpPlus/` に出力されます。思ったとおりに動かないときは、ここを見ると何に書き換わったか分かります。
- **対応していない SDK**：UdonSharp の内部 API が見つからないときは自動で無効になり、コンソールに `[UdonSharpPlus] この VRChat SDK（…）では UdonSharp の内部が変わっているため、無効にしました` のエラーが出ます。そのときは素の UdonSharp と同じ動きになるので、UdonSharp Plus を更新してください。
- **タプル**：タプル（`(int, string)`、`(1, "a")`、`var (x, y) = ...`、`foreach (var (k, v) in dict)` など）は未対応です。UdonSharp も対応しておらず、そのままだと内部の例外になって原因が分かりにくいので、UdonSharpBehaviour とそこから使われる普通のクラスでは「`'(int, string)' : タプルは未対応です（UdonSharp が対応していないため）。クラスにするか、別々の変数にしてください`」のエラーにします。
- **文言**：本パッケージが出すエラー・警告の文言は日本語です。

## 仕組み

[仕組み](how-it-works.md)に、UdonSharp のどこにパッチを当てて、何に書き換えているかの概要を書いています。
