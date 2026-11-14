# List / Dictionary・ラムダ・LINQ（実験的）

`List<T>`・`Dictionary<TKey, TValue>` と、それを使うラムダ・LINQ です。

## List / Dictionary

`List<T>` と `Dictionary<TKey, TValue>` を書くと、内部では Udon の `DataList` / `DataDictionary` で動きます。要素は、宣言した型で `DataToken` と変換されます。

```csharp
private List<int> _scores = new List<int> { 1, 2, 3 };
private Dictionary<string, Bullet> _bullets = new Dictionary<string, Bullet>();

_scores.Add(4);
_scores[0] += 10;
foreach (int s in _scores) { ... }

if (_bullets.TryGetValue("a", out Bullet b)) { ... }
foreach (var kv in _bullets) Debug.Log(kv.Key + kv.Value.damage);
```

対応している書き方：

- 要素の型：数値型、`bool`、`string`、enum、`Vector3` などの値型、UnityEngine.Object、普通のクラス、インタフェース、入れ子の List / Dictionary
- 作成：`new List<T>()`、コレクション初期化子（`{ 1, 2 }`、`{ { "a", 1 } }`、`{ ["a"] = 1 }`）、List・配列からのコピー、Dictionary からのコピー（`new Dictionary<K, V>(other)`）。容量の指定（`new List<int>(10)`）は書けますが、無視します。比較子の指定（`StringComparer.Ordinal` など）は未対応です
- List：添字（読み書き、`+=`、`++`）、`Count`、`Add`、`Insert`、`Remove`、`RemoveAt`、`RemoveRange`、`Contains`、`IndexOf`、`LastIndexOf`、`Clear`、`Sort()`、`Reverse()`、`AddRange`、`InsertRange`、`GetRange`、`ToArray`
- Dictionary：添字（読み書き、`+=`、`++`）、`Count`、`Add`、`Remove`、`ContainsKey`、`ContainsValue`、`TryGetValue`、`Clear`
- `foreach`：List、Dictionary（`kv.Key`／`kv.Value`）、`dict.Keys`、`dict.Values`。`kv` は変数に入れる、メソッドに渡す、ラムダの中で使う、などもできます（下の「KeyValuePair」）
- `KeyValuePair<K, V>`（変数・引数・戻り値・List の要素、`new KeyValuePair<K, V>(k, v)`、`.Key`／`.Value`）
- フィールドの初期化子、メソッドの引数・戻り値（インタフェースのメソッドを含む）
- ラムダを受け取る List のメソッド：`Find`、`FindLast`、`FindIndex`、`FindLastIndex`（どちらも開始・個数の指定を含む）、`FindAll`、`ConvertAll`、`Exists`、`TrueForAll`、`RemoveAll`、`ForEach`、`Sort(Comparison)`（下の「ラムダ」）

未対応の書き方（コンパイルエラーになります）：

- List / Dictionary 型のフィールドのシリアライズと同期（public、`[SerializeField]`、`[UdonSynced]`）。private にしてください
- 未対応の LINQ のメソッド（下の「LINQ」）
- `IEnumerable<T>` などとして扱うこと（`string.Join(",", list)` など。下の「IEnumerable」）、`GetEnumerator()`、`?.`
- `foreach (var (k, v) in dict)` の分解（[タプル](README.md#全体に共通すること)は未対応のため。`kv.Key`／`kv.Value` を使ってください）
- `Keys`／`Values` を `foreach`、`.Count`、LINQ 以外で使うこと
- 要素への代入の値を式として使うこと（`x = list[0] = 1`）。複合代入とインクリメントの受け側・添字に、変数・フィールド・定数以外を書くこと（`Get()[0] += 1`、入れ子の `nested[0][1] += 1`）。いったん変数に入れてください（`List<int> row = nested[0]; row[1] += 1;`）。入れ子の要素への普通の代入（`nested[0][1] = 1`）は書けます
- `TryGetValue` をループの条件の中で使うこと

注意：

- `DataList` / `DataDictionary` を直接書いた場合は、UdonSharp のままです（`foreach` は使えません）
- `foreach` の途中で List / Dictionary を書き換えないでください（C# では `InvalidOperationException` になる書き方です）。Udon では検出されず、回る回数は最初の要素数のままです。List に Add した要素は回らず、Remove / Clear すると範囲外を読んで Udon の実行時例外になります
- 存在しないキーを添字で読んだときは、C# の `KeyNotFoundException` ではなく、Udon の実行時例外になります
- `Dictionary` の `foreach` と LINQ の順番は、`DataDictionary` の `GetKeys()` の順番です（C# の `Dictionary` と同じとは限りません）

### ラムダ

上記の List のメソッドと LINQ の引数に、ラムダを直接書けます。ループとラムダの本体は、呼び出しごとに生成するメソッドの中に入ります。ラムダを `Func`・`Predicate` などの変数に入れてから渡すこともできます（[デリゲート](delegates.md)）。

```csharp
int limit = 4;
Bullet hit = _bullets.Find(b => b.damage > limit);
int total = 0;
_scores.ForEach(s => total += s);
_scores.Sort((a, b) => b - a);
```

- ラムダの中から、外のローカル変数・引数を読み書きできます。書き込みも C# と同じく、呼び出しの直後から外の変数に反映されます。フィールドもそのまま読み書きできます
- 本体は式（`x => x > 0`）とブロック（`x => { ... }`）のどちらでも書けます。メソッド名を渡すこともできます（`_scores.Exists(IsOdd)`）
- `Func`・`Predicate`・`Comparison`・`Action` の変数・フィールド・引数も渡せます（`_scores.Exists(isOdd)`、`_scores.Sort(compare)`。[デリゲート](delegates.md)）
- 戻り値のないラムダ（`ForEach` など）の式の本体には、普通のクラスのフィールド・プロパティや、List / Dictionary の要素への代入・インクリメントも書けます（`_enemies.ForEach(e => e.hp -= 1)`、`_names.ForEach(n => _counts[n]++)`）
- `Sort(Comparison)` は安定な並べ替え（マージソート）です（C# の `List.Sort` は安定とは限りません）
- `FindIndex(start, count, match)` などで範囲外を指定したときは、C# の `ArgumentOutOfRangeException` ではなく、Udon の実行時例外か、見つからない結果（-1）になります
- ラムダの中で起きたコンパイルエラーと実行時例外は、元のラムダの位置に表示されます

未対応の書き方（コンパイルエラーになります）：

- 上記の List のメソッドと LINQ の引数に直接書いた `delegate { ... }`（変数に入れてから渡すことはできます）。ほかの .NET のメソッドの引数のラムダ（`Array.Exists(arr, x => x > 0)` など）
- `ConvertAll` に `Converter` の変数を渡すこと（ラムダかメソッド名を直接書いてください）
- フィールドの初期化子の中で、ラムダを渡す List のメソッドを呼ぶこと
- `[RecursiveMethod]` を付けたメソッドの中で、外のローカル変数・引数に書き込むラムダ。外の変数は `ref` 引数で生成メソッドに渡しますが、UdonSharp は `[RecursiveMethod]` の中での `ref` 引数のメソッドの呼び出しに対応していないため、UdonSharp のエラー（`U# does not yet support calling user methods with ref/out parameters from methods marked with RecursiveMethod`）になります。読むだけのラムダは使えます

### KeyValuePair

`KeyValuePair<K, V>` は、普通のクラスと同じ形の `object[]`（先頭に型の印、続けてキーと値）になります。`Key`／`Value` は読み取り専用なので、値型の `KeyValuePair` と同じように使えます。

```csharp
foreach (var kv in _scores)
{
    if (IsHigh(kv))            // KeyValuePair<string, int> を受け取るメソッドに渡す
        _picked.Add(kv);       // List<KeyValuePair<string, int>> に入れる
}
var best = _scores.OrderByDescending(kv => kv.Value).First();   // LINQ から取り出す
Debug.Log(best.Key + best.Value);
```

- `foreach (var kv in dict)` と LINQ の中では、`kv.Key`／`kv.Value` だけを使う間は `object[]` を作りません（キーと `dict[キー]` に書き換えます）。変数に入れる、メソッドに渡すなど、`kv` そのものを使うところでだけ作ります
- `kv` そのものを使うたびに新しい `object[]` を作るので、`Equals` で比べると同じインスタンスかどうかの比較になります（`.Key`／`.Value` で比べてください）

## LINQ

List・配列・Dictionary（`dict.Keys`／`dict.Values` も）に、LINQ のメソッドを使えます（`using System.Linq;`）。つないだ呼び出しは、1 つのループの生成メソッドにまとめます。

```csharp
List<string> alive = _enemies.Where(e => e.hp > 0).Select(e => e.name).ToList();
int total = _scores.Sum();
bool any = _items.Any(i => i.price > limit);
var big = _scores.Where(s => s >= 50);   // var で受けると DataList（中身は結果のリスト）
foreach (int s in big) { ... }
```

対応している書き方：

- `Where`、`Select`、`OrderBy`／`OrderByDescending`／`ThenBy`／`ThenByDescending`、`Distinct`、`Skip`、`Take`（つないで使えます）
- `ToList`、`ToArray`、`ToDictionary`、`Any`、`All`、`Count`、`First`、`FirstOrDefault`、`Last`、`LastOrDefault`、`Contains`
- `Sum`、`Min`、`Max`、`Average`（`int`、`long`、`float`、`double`）
- 条件・変換は、ラムダかメソッド名で渡せます（`Any(x => x > 0)`、`Where(IsOdd)`）。ラムダの中から外の変数を読み書きできます（上の「ラムダ」と同じ）
- 結果を `var` で受けて、`foreach` やほかの LINQ につなげること
- Dictionary の要素（`KeyValuePair`）：ラムダの中で `kv.Key`／`kv.Value` を使えます。`First()` や `ToList()` で `KeyValuePair` を取り出すこともできます（上の「KeyValuePair」）。`ToDictionary(kv => kv.Key, kv => kv.Value)` で Dictionary に戻せます
- `Enumerable.Where(list, f)` の static の形

遅延評価にはしません。呼んだ時点で結果を作ります：

- `Where` / `Select` / `Distinct` / `Skip` / `Take` は要素を 1 つずつ次に渡すので、途中のリストは作りません。`First`／`Any`／`Take` などは、C# と同じく必要な分を取った時点で止まり、ラムダを呼ぶ回数も C# と同じです
- `OrderBy` は、そこまでの値とキーをためてから並べ替えます。ためる場所（`Distinct` の見たことのある値も）は、呼び出し箇所ごとのフィールドを使い回します。普通のクラスのメソッドの中（UdonSharp は static フィールドを使えないため）では、毎回作ります
- 並べ替えは安定です（C# の `OrderBy` と同じ）。キーは要素ごとに 1 回だけ計算します。string のキーは `string.Compare`（C# の既定と同じく、カルチャに従う比較）で比べます
- 結果を `var` で受けたとき、`ToList()`、`ToArray()` は、毎回新しいリスト・配列を作ります
- C# と結果が変わるのは、クエリを変数に入れておき、後で使う書き方だけです。1 つの式で書いてすぐ使うなら違いは出ません
  - 変数に入れてから使うまでの間に元の List を変えても、変更は結果に入りません（C# では入ります）
  - 同じクエリを 2 回使ったとき、2 回目も最初の結果のままです（C# では使うたびに計算し直します）

```csharp
var big = list.Where(x => x > 1);
list.Add(10);
int n = big.Count();   // C# では 10 も数える。ここでは Where を呼んだ時点の結果を数える
```

未対応の書き方（コンパイルエラーになります）：

- `GroupBy`、`SelectMany`、`Reverse` など、上記以外のメソッド
- クエリ構文（`from x in list where x > 0 select x`）。メソッドの形（`list.Where(x => x > 0)`）で書いてください
- `Enumerable.Range`・`Enumerable.Repeat`・`Enumerable.Empty` など、List・配列・Dictionary から始まらない LINQ
- 比較子（`IComparer`／`IEqualityComparer`）を渡す形、数値・string・bool・char・enum 以外のキーでの並べ替え
- 添字付きのラムダ（`Select((x, i) => ...)`）、`Contains` の比較子の指定、数値以外の `Sum`／`Min`／`Max`／`Average`
- Dictionary の要素（`KeyValuePair`）の `Contains`（`ContainsKey` などを使ってください）
- LINQ の結果を `IEnumerable<T>` 型の変数・フィールド・引数・戻り値に入れること、そのままメソッドに渡すこと（`string.Join(",", list.Select(...))`）。`var` で受けるか、`ToList()`／`ToArray()` にしてください（下の「IEnumerable」）
- フィールドの初期化子の中の LINQ

注意：

- 要素がないときの `First`／`Last`／`Min`／`Max`／`Average` と、`ToDictionary` で同じキーがあったときは、C# の例外の代わりに、エラーのログを出して Udon の実行時例外になります
- `Distinct` は、普通のクラスなどの参照は同じインスタンスかどうかで比べます（C# の既定と同じ）。Dictionary の要素はキーが重ならないので、何もしません
- `Contains` は `==` で比べます

## IEnumerable

`IEnumerable<T>` には対応しません。Udon には `IEnumerable` の中を回す手段（`GetEnumerator`／`MoveNext`／`Current`）がなく、List・Dictionary は内部では `DataList`／`DataDictionary` になるためです。

- List・Dictionary・LINQ の結果を `IEnumerable<T>` などのインタフェース型として扱うと、コンパイルエラーになります（変数・フィールドの初期化、代入、引数、戻り値）。`var` で受けるか、`ToList()`／`ToArray()` にしてください

  ```csharp
  IEnumerable<int> a = new List<int>();          // エラー
  IEnumerable<int> b = list.Where(x => x > 0);   // エラー（var b = ... なら使える）
  string s = string.Join(",", list);             // エラー（string.Join(",", list.ToArray()) なら使える）
  ```

- List・Dictionary の `GetEnumerator()` は、コンパイルエラーになります
- 外部の関数が返す `IEnumerable`（`PlayerData.GetKeys()`、`ToggleGroup.ActiveToggles()` など）は、UdonSharp と同じく、変数・フィールド・引数・戻り値として受け渡しし、`string.Join`／`string.Concat` に渡せます。`foreach` や LINQ には使えず、コンパイルエラーになります（「外部の関数が返す IEnumerable は、Udon では中を回せません」）
