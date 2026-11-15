# コンパイル時の注意

コンパイルエラーにはならないが、C# と動きが違うもの・気付きにくいものを使っているとき、UdonSharp のコンパイルのたびに、コンソールに Info のログを出します。

```
[UdonSharpPlus] Assets/Scripts/Enemy.cs(42,21): USP001: LINQ の結果を変数に入れています。……（詳しくは collections.md の「LINQ」。Project Settings > Udon Sharp Plus で、出さないようにできます）
```

- 注意ごとに ID（`USP001` など）が付いています
- 同じ ID の注意は、コンパイルごとに 1 回だけ、最初に使った位置（ファイルのパスの順、ファイルの中の位置の順）に出します
- 位置（`Enemy.cs(42,21)`）をクリックすると、そのファイルのその行を開きます
- コンパイルエラーがあるときは出しません。パッケージ（`Packages/` の下）のスクリプトと、UdonSharpBehaviour から使われていない普通のクラスの中のものも出しません

## 設定

**Project Settings > Udon Sharp Plus** で、注意ごとに出す・出さないを選べます。「注意を出す」を外すと、すべて出しません。設定は `ProjectSettings/UdonSharpPlusSettings.json` に保存されるので、リポジトリに入れればチームで揃えられます（既定から変えたものだけを記録します）。

## 注意の一覧

既定で出すのは、気付かずに踏みやすい 4 つ（USP001・USP003・USP005・USP007）です。ほかは、設定で出すようにできます。

| ID | 既定 | 出すとき | 内容 | 詳しく |
|---|---|---|---|---|
| USP001 | 出す | LINQ の結果（`Where`・`Select` など）を変数・フィールドに入れたとき | 遅延評価にしないので、あとで元の List を変えても結果は変わらず、2 回使っても同じ結果 | [LINQ](collections.md#linq) |
| USP002 | － | `Concat`・`Append`・`Prepend` のあとを途中で止める（`First`・`Any` など）、その前にラムダを取る段がある LINQ | 結果は同じだが、前の段のラムダは全部の要素で呼ばれる | [LINQ](collections.md#linq) |
| USP003 | 出す | `foreach` で回している List / Dictionary を、その中で書き換えたとき（`Add`・`Remove`・`Clear`・Dictionary の添字への代入など） | C# の `InvalidOperationException` にならない。Add した要素は回らず、Remove・Clear は範囲外の読み出しで止まる | [List / Dictionary](collections.md#list--dictionary) |
| USP004 | － | Dictionary の `foreach`・LINQ | 順番は DataDictionary の順で、C# と同じとは限らない | [List / Dictionary](collections.md#list--dictionary) |
| USP005 | 出す | インタフェース型の値と `null` を比べたとき（`==`・`!=`・`is null`） | 参照の比較なので、Destroy した UdonSharpBehaviour は null にならない | [インタフェース](interface.md) |
| USP006 | － | 普通のクラスの値型のフィールドで、戻り値のないメソッドを呼んだとき（`b.pos.Normalize()`） | フィールドのコピーに対して呼ばれるので、自分を書き換えるメソッドなら反映されない | [普通のクラス](class.md) |
| USP007 | 出す | UdonSharpBehaviour のメソッド、またはその中で作ったラムダなどをデリゲートにしたとき（List・LINQ のメソッドの引数に直接書いたものは除く） | 相手の UdonSharpBehaviour を Destroy したあとに呼ぶと、実行時例外で止まる | [デリゲート](delegates.md) |
| USP008 | － | UdonSharpBehaviour の中で、static メソッドのデリゲートを `-=` したとき | 別の UdonSharpBehaviour で作ったものは外れない | [デリゲート](delegates.md) |
| USP009 | － | 値がタプルの Dictionary の `TryGetValue` | 見つからなかったときの `out` の値が null になる | [タプル](tuples.md) |
| USP010 | － | `==` を宣言した型の List の `Contains`・`IndexOf`・`LastIndexOf`・`Remove` | C# と同じく `Equals`（参照の比較）で比べ、`==` は使われない | [演算子のオーバーロード](operators.md) |
| USP011 | － | `First`・`Last`・`Single`・`Min`・`Max`・`Average`・`Aggregate`（最初の値なし）・`ElementAt`・`ToDictionary`・`Enumerable.Range`・`Repeat` | C# で例外になる場合、例外の代わりにエラーのログを出して、Udon の実行時例外で止まる（try / catch はできない） | [LINQ](collections.md#linq) |
| USP012 | － | インタフェース型の値でメソッドを呼んだとき | null のときの実行時例外の位置が、クラスの閉じ括弧の行になる | [インタフェース](interface.md) |
| USP013 | － | メソッドの呼び出しの結果で、普通のクラスのメソッドを呼んだとき（`Get().M()`） | null のときの実行時例外の位置が、クラスの閉じ括弧の行になることがある | [普通のクラス](class.md) |
| USP014 | － | .NET のデリゲート（`Action`・`Func`・`Predicate`・`Comparison`）を作ったとき | 宣言の行で止まる実行時例外の位置が、その型を最初に使ったファイルの最後の行になる | [デリゲート](delegates.md) |
| USP015 | － | UdonSharp Plus が書き換えたファイル（最初に書き換えた行に出す） | 書き換えた行の、コンパイルエラー・実行時例外の列の位置がずれることがある（行は合う） | [全体に共通すること](README.md#全体に共通すること) |
| USP016 | － | UdonSharpBehaviour のデリゲート（USP007 と同じ）と、外の変数を使うラムダ | デリゲートの呼び出しは、普通のメソッドの呼び出しより遅い | [デリゲート](delegates.md) |

出すときの判定は、書き方から分かる範囲で行います。たとえば USP006 は、メソッドが本当にフィールドを書き換えるかどうかは見ないので、書き換えないメソッドでも出ます。
