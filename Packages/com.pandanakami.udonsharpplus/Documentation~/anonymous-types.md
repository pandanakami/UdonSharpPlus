# 匿名型（実験的）

匿名型（`new { Name = "a", Score = 1 }`）を、UdonSharpBehaviour と普通のクラスの中で使えます（UdonSharp 自体は対応していません）。

```csharp
var p = new { Name = "bob", Score = 3 };
int s = p.Score + 1;
var q = new { item.Name, item.Hp };                  // メンバー名の省略
var n = new { A = 1, Inner = new { B = "x" } };      // 入れ子
var top = _scores.Select(x => new { Id = x, Sq = x * x }).Where(a => a.Sq > 4).ToList();   // LINQ の射影
Debug.Log(p.ToString());                              // { Name = bob, Score = 3 }
```

対応している書き方：

- `new { ... }` を `var` で受ける・メンバーを読む・ラムダの戻り値にする・入れ子にする・メンバー名を省略する（`new { x.Name }`、フィールド・変数も）。同じ名前・型・並びなら同じ型で、`cond ? new { A = 1 } : new { A = 2 }` のように混ぜて使えます
- 匿名型の配列（`new[] { new { K = "a" }, new { K = "b" } }`）・LINQ の射影（`Select(x => new { ... })`）・匿名型の `List`（`var` で受けたもの）
- 匿名型のリスト（`var` で受けた `List`）：`foreach`・`for`、`Add`・`Insert`・`RemoveAt`・`RemoveAll`・`Sort`・`Reverse`・`GetRange`、インデクサー、`Find`・`FindIndex`・`Exists`、LINQ（`Where`・`Select`・`OrderBy`・`ThenBy`・`Skip`・`Take`・`First`・`Last`・`Any`・`Count`・`Sum`・`Max`・`ToArray`・`ToDictionary` など）が、普通のクラスのリストと同じに使えます。`ToList()` の結果は、`var` に入れて使います（フィールドや引数の型には書けません。C# も同じです）
- 匿名型のリストの `Contains`・`IndexOf`・`LastIndexOf`・`Remove`、LINQ の `Contains`・`Distinct` は、C# と同じく値（`Equals`）で比べます（null どうしは等しい）。LINQ の `Contains`・`Distinct` は、C# と同じく、必要な分だけ前のメソッドを進めます。範囲の合わない `IndexOf`・`LastIndexOf` は、C# と同じく、呼んだ行で実行時例外になります
- ラムダ・匿名メソッド・ローカル関数・クロージャの中で使う（キャプチャした変数に入れて読む）。普通のクラスの中でも作って使えます
- ジェネリックメソッドの型引数にする（`Id(new { A = 1 })`）、ジェネリッククラスの中で型引数をメンバーにする（`new { value }`。型引数ごとに別の型になります）
- `ToString()`、文字列の連結（`"x" + p`、`s += p`）・補間（`$"{p}"`）：C# と同じ `{ Name = bob, Score = 3 }` の形になります。メンバーが数値・bool・char・string・外部の構造体（`Vector3` など）・匿名型のときだけ使えます
- `Equals`：C# と同じく、メンバーごとに値で比べます（普通のクラスのメンバーは参照で比べます）。`==`・`!=` は、C# と同じ参照の比較です
- initializer は C# と同じく、左から 1 回ずつ評価します（`new { A = Next(), B = Next() }`）。作った時点の値を持ち（値型のローカル変数をあとで変えても、匿名型は変わりません）、普通のクラス・配列などの参照型のメンバーは、作ったあとの変更が見えます
- メンバーの名前に、キーワード（`@class`）・日本語（`名前`）も使えます。`ToString` には元の名前が出ます
- 型の同一性は C# と同じです。名前・型・並びのどれかが違えば別の型で、`Equals` は値が同じでも false になります
- null の匿名型（`cond ? new { A = 1 } : null`）は、メンバーの読み出し・`ToString()`・`Equals` が、C# と同じく呼んだ行で実行時例外で止まります。文字列の連結・補間では、空の文字列になります

未対応の書き方（コンパイルエラーになります）：

| 書き方 | 代わりの書き方 |
|---|---|
| `object`・インタフェースとして使う（`Debug.Log(p)`、`object o = p;`、`string.Format("{0}", p)`） | `Debug.Log(p.ToString())` のように、`ToString()` か、メンバーを渡してください |
| `GetHashCode()`・`GetType()` | — |
| 型引数が匿名型になるジェネリックメソッドの中の `list.Contains(x)`・`IndexOf`・`Remove`・`Distinct` など（参照の比較になるため）、メンバーがデリゲートの匿名型のリストの `Contains` など | 匿名型のリストに直接呼ぶか、`Exists`・`FindIndex`・`Any` などのラムダで比べてください |
| 匿名型をキーにする `ToDictionary`（`ToDictionary(e => e)`。C# はキーを値で比べるが、ここでは参照の比較になるため）。匿名型を値にするのは使えます（`ToDictionary(e => e.Id)`） | キーは、数値・string などのメンバーにしてください |
| `ToString`・文字列の連結に、メンバーが普通のクラス・インタフェース・配列・`enum`・デリゲートの匿名型を使う | メンバーを数値・string などにするか、メンバーを個別に文字列にしてください |
| `Equals` に、メンバーがデリゲートの匿名型を使う | — |
| 型引数を使う匿名型を、型引数の決まらない場所（使われないジェネリックなど）で使う | — |

仕組み：

- 匿名型は、メンバーの並び（名前と型）ごとに、普通のクラスになります（`__Anon_0` など）。`new { Name = "a", Score = 1 }` は `new __Anon_0("a", 1)` になり、あとは[普通のクラス](class.md)と同じに扱います（インスタンスは `object[]`、メソッドは使う UdonSharpBehaviour にコピーされます）。クラスは、最初に使ったファイルの最後に、1 行で置きます
- 普通のクラスは `ToString`・`Equals` を override できないので、`p.ToString()` は `p.__ToString()`、`p.Equals(q)` は `p.__Equals(q)`、`"x" + p` は `__Anon_0.__Str(p)` に書き換えます
- 匿名型のリストの `Contains` などは、生成するクラスの static メソッドの呼び出しにします（`list.Contains(x)` → `__Anon_0.__Contains(list, x)`。中で `Equals` と同じ比べ方をします）。使っているものだけを生成します。LINQ の `Contains`・`Distinct` は、LINQ のメソッドのまま、値の比較を呼ぶ形にします
- 匿名型は、ジェネリクスの前（`Id(new { A = 1 })` のため）と、ジェネリクスのコピーのあと（型引数を使うもの）の 2 回、書き換えます
- 書き換え後のソースは、`Library/UdonSharpPlus/` で見られます
