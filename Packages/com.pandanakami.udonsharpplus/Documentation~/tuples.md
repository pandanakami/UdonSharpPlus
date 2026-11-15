# タプル（実験的）

タプル（`(int, string)`、`(1, "a")`）を、UdonSharpBehaviour と普通のクラスの中で使えます（UdonSharp 自体は対応していません）。

```csharp
(int min, int max) Range(List<int> l) { ... return (lo, hi); }   // 戻り値
var r = Range(_scores);
int span = r.max - r.min;                                        // 名前で読む（Item1・Item2 でも読める）
var (lo, hi) = Range(_scores);                                   // 分解
(a, b) = (b, a);                                                 // 入れ替え
var best = _players.Select(p => (p.name, p.score)).OrderByDescending(t => t.score).First();
Debug.Log(best.ToString());                                      // (bob, 90)
```

対応している書き方：

- 作る（`(1, "a")`、名前付き `(x: 1, y: 2)`、変数名からの名前 `(cnt, list.Count)`、`new ValueTuple<int, int>(1, 2)`）・型に書く（`(int, string)`、`(int x, int y)`、`ValueTuple<int, string>`）。フィールド・ローカル変数・引数・戻り値・配列・`List` の要素・デリゲート（`Func<int, (int, int)>`）の型に使えます
- 要素を読む（`t.Item1`、`t.min`）。入れ子（`t.Item2.Item1`）、要素は 7 個まで
- 分解（`var (x, y) = t;`、`(int x, var y) = t;`、`(a, b) = t;`、`(a, b) = (b, a);`、捨てる `_`、入れ子 `var (a, (b, c)) = t;`）と、`foreach (var (k, v) in list)`。右辺は 1 回だけ評価し、全部取り出してから代入します（入れ替えが C# と同じになります）
- `==`・`!=`（要素ごとに `==` で比べる）、`Equals`（要素ごとに値で比べる）、`ToString()`・文字列の連結・補間（C# と同じ `(1, a)` の形）。名前だけが違うタプル（`(int a, int b)` と `(int c, int d)`）は同じ型です
- 既定値は、C# と同じく「各要素が既定値のタプル」です（`default`、初期化子のないフィールド、`new (int, string)[n]` の要素、ジェネリッククラスの `T` 型のフィールド、LINQ の `FirstOrDefault` などで要素がないとき）。null にはなりません
- 別の UdonSharpBehaviour のメソッドに渡す・返してもらう、インタフェースのメソッドの引数・戻り値にする、`Action`・`Func`・自作のデリゲートの引数・戻り値にする
- 丸ごとの代入（`t = (t.x + 1, t.y);`）。C# と同じく、ほかの変数・List・配列に入れたタプル、別のメソッドに渡したタプルは変わりません（要素が普通のクラスなどの参照なら、その中身の変更は見えます。C# と同じ）
- `==` は要素ごとの `==`、`Equals` は要素ごとの `Equals` で、C# と同じです（`(float.NaN, 1) == (float.NaN, 1)` は false、`Equals` は true）
- `List` の `Contains`・`IndexOf`・`LastIndexOf`・`Remove`、LINQ の `Contains`・`Distinct` は、C# と同じく値で比べます
- LINQ（`Select(x => (x, x * 2))`、添字付き `Select((x, i) => (x, i))`、`OrderBy(t => t.score)` など）、匿名型のメンバーにする（`new { Pos = (1, 2) }`）・タプルの要素に匿名型を使う、ジェネリッククラス・ジェネリックメソッドの中（`(T, int)`）

未対応の書き方（コンパイルエラーになります）：

| 書き方 | 代わりの書き方 |
|---|---|
| 要素への代入（`t.Item1 = 5;`、`t.x++;`） | タプルを作り直してください（`t = (5, t.Item2);`）。C# ではタプルは値型で、代入・引数で渡すたびにコピーされますが、ここでは参照になるため、要素を書き換えると、コピーしたはずの側まで変わってしまいます |
| `object`・インタフェースとして使う（`Debug.Log(t)`、`object o = t;`） | `Debug.Log(t.ToString())` のように、`ToString()` か、要素を渡してください |
| `GetHashCode`・`CompareTo`、`List` の `Sort()`（比べ方なし） | `list.Sort((a, b) => a.Item1 - b.Item1)` のように、比べ方を渡してください |
| タプルをキーにする `Dictionary`（`Dictionary<(int, int), V>`、`ToDictionary(x => (x, x))`） | キーは、数値・string などにしてください |
| 要素の型を変える変換（`(long, long) u = t;`）、null 許容のタプル（`(int, int)?`） | 要素を取り出して、タプルを作り直してください |
| 要素が 8 個以上のタプル | クラスにしてください |
| `switch`・パターン（`t is (1, 2)`） | `if` で、要素ごとに比べてください |
| 文でない場所の分解（式の中、`for` の初期化） | 1 つの文として書いてください |
| タプル以外の分解（`foreach (var (k, v) in dict)`、`Deconstruct` メソッド） | `foreach (var kv in dict)` で、`kv.Key`／`kv.Value` を使ってください |
| `ToString`・文字列の連結に、要素が普通のクラス・インタフェース・配列・`enum`・デリゲートのタプルを使う | 要素を個別に文字列にしてください |

注意：

- `Dictionary` の値をタプルにしたとき、`TryGetValue` で見つからなかった場合の `out` の値は null になります（C# では各要素が既定値のタプル）。見つかったときだけ使ってください

仕組み：

- タプルは、要素の型の並びごとに、普通のクラスになります（`__Tuple_0` など）。`(1, "a")` は `new __Tuple_0(1, "a")` に、要素の名前（`t.min`）は `Item1` などになり、あとは[普通のクラス](class.md)と同じに扱います（インスタンスは `object[]`）。クラスは、最初に使ったファイルの最後に、1 行で置きます
- 分解は、同じ行に並べた文になります（`var (x, y) = t;` → `__Tuple_0 __td0 = t; int x = __td0.Item1; int y = __td0.Item2;`）
- ジェネリクスのコピーのあとに書き換えるので、`(T, int)` は型引数ごとに別のクラスになります
- 書き換え後のソースは、`Library/UdonSharpPlus/` で見られます
