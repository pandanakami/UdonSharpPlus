# Custom UdonSharp ドキュメント

UdonSharp で書ける C# を増やすパッケージ、Custom UdonSharp の詳しい説明です。導入方法と概要は、[リポジトリの README](https://github.com/pandanakami/CustomUdonSharp/blob/main/README.ja.md) を見てください。

> **実験段階です。** UdonSharp の内部 API に依存しているため、SDK のバージョンが変わると動かなくなる可能性があります。動作対象は VRChat Worlds SDK 3.10.x です。

## 機能

| 機能 | 内容 |
|---|---|
| [インタフェース](interface.md) | UdonSharpBehaviour と汎用クラスで、ユーザー定義のインタフェースを実装・利用する |
| [汎用クラス](class.md) | UdonSharpBehaviour 以外の普通のクラスを、UdonSharpBehaviour の中で使う |
| [ジェネリクス](generics.md) | ジェネリッククラス・ジェネリックメソッドを宣言して使う |
| [List / Dictionary・ラムダ・LINQ](collections.md) | `List<T>`・`Dictionary<TKey, TValue>`、それらのメソッドに渡すラムダ、LINQ |

それぞれのページに、対応している書き方と、未対応の書き方（コンパイルエラーになるもの）を書いています。

## 全体に共通すること

- **書き換えはエディタの中だけ**：UdonSharp がコンパイルする前に、C# のソースを UdonSharp が扱える形に書き換えます。出力されるのは通常の Udon プログラムなので、VRChat のクライアント側には何も要りません。
- **エラーの位置**：書き換えで生成したコードの中で出たコンパイルエラーと実行時例外は、元のソースの位置に表示されます。必要に応じて、どのクラス・メンバーのコピーの中かの補足が付きます（[汎用クラス](class.md)の「注意」）。
- **書き換え後のソース**：デバッグ用に、プロジェクトの `Library/CustomUdonSharp/` に出力されます。思ったとおりに動かないときは、ここを見ると何に書き換わったか分かります。
- **対応していない SDK**：UdonSharp の内部 API が見つからないときは自動で無効になり、コンソールに `[CustomUdonSharp] この VRChat SDK（…）では UdonSharp の内部が変わっているため、無効にしました` のエラーが出ます。そのときは素の UdonSharp と同じ動きになるので、Custom UdonSharp を更新してください。
- **文言**：本パッケージが出すエラー・警告の文言は日本語です。

## 仕組み

[仕組み](how-it-works.md)に、UdonSharp のどこにパッチを当てて、何に書き換えているかの概要を書いています。
