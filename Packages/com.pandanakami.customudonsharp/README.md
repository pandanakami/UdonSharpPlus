# Custom UdonSharp

UdonSharp のコンパイラをエディタ上で拡張するパッケージです。
Harmony（VRChat SDK 同梱）でコンパイラの内部処理にパッチを当てます。出力されるのは通常の Udon プログラムなので、VRChat クライアント側には何も追加しなくて済みます。

> **実験段階です。** UdonSharp の内部 API に依存しているため、SDK のバージョンが変わると動かなくなる可能性があります。動作対象は VRChat Worlds SDK 3.10.x です。

## 機能

### インタフェース（実験的）

UdonSharpBehaviour で、ユーザー定義のインタフェースを実装・利用できます。

```csharp
public interface IDamageable
{
    int Damage(int amount, string attacker);
}

public class Enemy : UdonSharpBehaviour, IDamageable
{
    public int Damage(int amount, string attacker) { ... }
}

// 呼び出し側
if (target is IDamageable)
{
    int remain = ((IDamageable)target).Damage(10, name);
}
```

対応している書き方：

- インタフェース型の引数、ローカル変数、配列、`foreach`
- メソッドの呼び出し（戻り値ありとなしの両方）
- `is`、`as`、キャスト
- `[NetworkCallable]` の実装メソッド（ローカル呼び出しと、`SendCustomNetworkEvent` でのネットワーク越しの呼び出しの両方）
- 汎用クラス（後述）による実装。UdonSharpBehaviour による実装と混ぜて、同じインタフェース型の変数や配列に入れられます

インタフェース型の変数は、`object` として扱われます（中身は UdonSharpBehaviour か、汎用クラスのインスタンス）。そのため `x != null` は、普通の C# のインタフェースと同じく参照の比較になり、Unity の比較（破棄済みのオブジェクトを null とみなす）にはなりません。

#### メソッド名とオーバーロード

インタフェース実装メソッドは、Udon 上でも元の名前（`Receive`）のままです。`SendCustomEvent` や `SendCustomNetworkEvent` には、`nameof(...)`、文字列リテラル、変数のどれで名前を渡しても届きます。受け手の型が `UdonSharpBehaviour` や `IUdonEventReceiver` でも同じです。

```csharp
// インタフェース型から呼ぶときは UdonSharpBehaviour にキャストする
((UdonSharpBehaviour)receiver).SendCustomNetworkEvent(NetworkEventTarget.All, nameof(INetworkReceiver.Receive), 1, "a");
enemy.SendCustomNetworkEvent(NetworkEventTarget.All, "Receive", 1, "a");
((IUdonEventReceiver)enemy).SendCustomNetworkEvent(NetworkEventTarget.All, "Receive", 1, "a");
```

その代わり、UdonSharp が `[NetworkCallable]` に課しているのと同じく、インタフェース実装メソッドはオーバーロードできません。次の書き方はコンパイルエラーになります。

- インタフェース実装メソッドと同じ名前のメソッドを、同じクラス（基底の UdonSharpBehaviour を含む）に置く。引数のないメソッドも含みます
- インタフェースの中でメソッドをオーバーロードする（`void Fire(int)` と `void Fire(int, float)`）
- 同じ名前で引数の違うメソッドを持つ 2 つのインタフェースを、1 つのクラスで実装する。引数まで同じなら、1 つのメソッドで両方を実装できます

`[NetworkCallable]` のそのほかの制約（戻り値なし、など）も、インタフェース実装メソッドにそのまま掛かります。

<details>
<summary>背景：UdonSharp のメソッド名の扱い</summary>

- UdonSharp は、引数のあるメソッドを、オーバーロードがなくても `__0_Receive` のような名前に改名します。数字はクラスの中で同じ名前が宣言された順に振られるので、メソッドを足したり並べ替えたりすると変わります。引数も `__0_value__param` のように改名されます
- 改名されないのは、引数のないメソッドと `[NetworkCallable]` のメソッドだけです
- UdonSharp 同士の普通の呼び出し `obj.Receive(1, "a")` は、コンパイル時に相手の型がわかるので、`obj.SetProgramVariable("__0_value__param", 1)` … `obj.SendCustomEvent("__0_Receive")` に置き換えられます。そのため、`SendCustomEvent("Receive")` を直接呼んでも、引数のあるメソッドには届きません。`SendCustomEvent` で直接呼べるのは、引数のない public メソッドです
- `[NetworkCallable]` のメソッドは改名されず、`SendCustomNetworkEvent(…, "Receive", p0, p1)` のように元の名前と引数で呼べます。名前を 1 つに決めるため、同じ名前のメソッドとの共存は禁止されています（`Duplicate network callable method name 'Receive'. Overloading of any kind is not supported.`）
- インタフェース経由の呼び出しでは、相手のクラスがコンパイル時にわからないため、どの実装クラスでも同じ名前である必要があります。本パッケージは `[NetworkCallable]` と同じ考え方で、インタフェース実装メソッドの名前を元の名前に揃え、オーバーロードを禁止しています。引数と戻り値の変数名は、実装側で引数名が違っても揃うよう、位置で決めた名前（`Receive__int__string__p0`、`Receive__int__string__ret`）にします

</details>

未対応の書き方を使うと、コンパイルエラーになります（インタフェースのプロパティとイベントだけは警告を出して無視します）。

未対応の書き方：

- インタフェースのプロパティとイベント
- `?.` での呼び出し
- ref/out/in/params 引数、ジェネリックメソッド
- 明示的インタフェース実装
- インタフェース型のシリアライズフィールド（Unity がシリアライズしないため。`UdonSharpBehaviour` 型で受けてキャストしてください）
- `GetComponent<IFoo>()`

### 汎用クラス（実験的）

UdonSharpBehaviour 以外の普通のクラスを、UdonSharpBehaviour の中で使えます。

```csharp
public class Bullet
{
    public Vector3 pos;
    public int damage = 10;
    public string Name { get; set; } = "bullet";

    public Bullet(Vector3 p) { pos = p; }

    public bool IsAlive => damage > 0;

    public void Move(Vector3 d) { pos += d; }
}

// UdonSharpBehaviour の中
Bullet b = new Bullet(Vector3.zero);
b.Move(Vector3.up);
Bullet[] pool = new Bullet[8];
```

インスタンスは `object[]`（先頭に型名、続けてフィールド）になり、コンストラクタやメソッドは、そのクラスを使う UdonSharpBehaviour ごとにコピーされます。状態は `object[]` の中にあるので、インスタンスを別の UdonSharpBehaviour に渡しても、同じインスタンスとして扱えます。

対応している書き方：

- インスタンスのフィールド（初期化子を含む）、コンストラクタ（オーバーロード、`this(...)` を含む）、インスタンスメソッド
- プロパティ（自動実装と、本体付きの get/set）
- static メソッド、拡張メソッド、static クラス、const。static メソッドは、メソッド名だけで List のメソッドに渡せます（`list.Find(IsOdd)`）
- クラスの配列、`foreach`
- `is`、`as`、キャスト、`nameof`
- 再帰（`[RecursiveMethod]` を付けたメソッド）
- インタフェースの実装。インタフェース経由の呼び出しは、型タグを見てそのクラスのメソッドのコピーを呼びます。インタフェースを使う UdonSharpBehaviour には、それを実装する全クラスのメソッドのコピーが入ります

未対応の書き方（そのクラスを UdonSharpBehaviour から使うとコンパイルエラーになります）：

- static フィールド（UdonSharp と同じく非対応）
- 継承、abstract クラス、ジェネリッククラス・メソッド、入れ子のクラス。ScriptableObject・MonoBehaviour・Exception などを継承したクラスも、UdonSharpBehaviour から使うと「`'Settings' : 汎用クラスの継承は未対応です（ScriptableObject を継承しています）`」のエラーになります（UdonSharp も対応しておらず、そのままだと内部の例外になるため）
- ユーザー定義の構造体（struct）。UdonSharp も対応していません。UdonSharpBehaviour から使うと「`'Point' : ユーザー定義の構造体は未対応です`」のエラーになります（UdonSharp のままだと内部の例外になり、原因が分かりにくいため）
- 演算子のオーバーロード、インデクサー、イベント、`ToString` などの override
- インスタンスのメソッドを、メソッド名だけで渡すこと（`list.Find(b.IsHit)`）。ラムダで書いてください（`list.Find(x => b.IsHit(x))`）
- オブジェクト初期化子（`new Bullet { damage = 1 }`）、`?.`、型パターン（`x is Bullet b`）、`typeof(Bullet)`
- 汎用クラス型のフィールドのシリアライズと同期（`[SerializeField]`、`[UdonSynced]`）。public フィールドもシリアライズされません
- フィールド・プロパティへの代入の値を式として使うこと（`x = b.damage = 1`）。複合代入とインクリメントの受け側に、メソッド呼び出しを書くこと（`Get().damage += 1`）

注意：

- 値型のフィールドのメソッドで、そのフィールド自体を書き換えるもの（`b.pos.Normalize()` など）は、コピーに対して呼ばれるため反映されません。`b.pos = b.pos.normalized;` のように代入してください
- 汎用クラスのメソッドの中で出たコンパイルエラーは、元のクラスのソースの位置（文の先頭）に表示され、末尾にどのクラスのどのメンバーかが付きます。メッセージの中の式は書き換え後のもの（フィールド `path` が `((string)(__this)[1])`、型名が `global::` 付きの完全修飾になるなど）になります。同じクラスを複数の UdonSharpBehaviour で使っていても、同じエラーは 1 つにまとめます

  ```
  Saver.cs(10,13): Method is not exposed to Udon: 'global::System.IO.File.Exists(((string)(__this)[1]))'（汎用クラス Saver のメソッド Exists の中）
  ```

- 汎用クラスのメソッドの中で起きた実行時例外（`Udon runtime exception detected!`）も、元のクラスのソースの位置（文の単位）に表示されます。表示の仕組みは UdonSharp のものなので、クライアントで確かめるときは、UdonSharp と同じく `--enable-udon-debug-logging` を付けて起動してください
- UdonSharpBehaviour から使われていないクラスは書き換えず、検査もしません

### List / Dictionary（実験的）

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

- 要素の型：数値型、`bool`、`string`、enum、`Vector3` などの値型、UnityEngine.Object、汎用クラス、インタフェース、入れ子の List / Dictionary
- 作成：`new List<T>()`、コレクション初期化子（`{ 1, 2 }`、`{ { "a", 1 } }`、`{ ["a"] = 1 }`）、List・配列からのコピー
- List：添字（読み書き、`+=`、`++`）、`Count`、`Add`、`Insert`、`Remove`、`RemoveAt`、`RemoveRange`、`Contains`、`IndexOf`、`LastIndexOf`、`Clear`、`Sort()`、`Reverse()`、`AddRange`、`InsertRange`、`GetRange`、`ToArray`
- Dictionary：添字（読み書き、`+=`、`++`）、`Count`、`Add`、`Remove`、`ContainsKey`、`ContainsValue`、`TryGetValue`、`Clear`
- `foreach`：List、Dictionary（`kv.Key`／`kv.Value`）、`dict.Keys`、`dict.Values`
- フィールドの初期化子、メソッドの引数・戻り値（インタフェースのメソッドを含む）
- ラムダを受け取る List のメソッド：`Find`、`FindLast`、`FindIndex`、`FindLastIndex`（どちらも開始・個数の指定を含む）、`FindAll`、`ConvertAll`、`Exists`、`TrueForAll`、`RemoveAll`、`ForEach`、`Sort(Comparison)`（下の「ラムダ」）

未対応の書き方（コンパイルエラーになります）：

- List / Dictionary 型のフィールドのシリアライズと同期（public、`[SerializeField]`、`[UdonSynced]`）。private にしてください
- LINQ（`Where`、`Select` など）
- `IEnumerable<T>` などとして渡すこと（`string.Join(",", list)` など）、`?.`
- `foreach (var (k, v) in dict)` の分解、`kv` を `.Key`／`.Value` 以外で使うこと、`Keys`／`Values` を `foreach` と `.Count` 以外で使うこと
- 要素への代入の値を式として使うこと（`x = list[0] = 1`）。複合代入とインクリメントの受け側・添字に、メソッド呼び出しを書くこと（`Get()[0] += 1`）
- `TryGetValue` をループの条件の中で使うこと

注意：

- `DataList` / `DataDictionary` を直接書いた場合は、UdonSharp のままです（`foreach` は使えません）
- `foreach` の途中で List / Dictionary を書き換えないでください（C# では `InvalidOperationException` になる書き方です）。Udon では検出されず、回る回数は最初の要素数のままです。List に Add した要素は回らず、Remove / Clear すると範囲外を読んで Udon の実行時例外になります
- 存在しないキーを添字で読んだときは、C# の `KeyNotFoundException` ではなく、Udon の実行時例外になります
- `Dictionary` の `foreach` の順番は、`DataDictionary` の `GetKeys()` の順番です（C# の `Dictionary` と同じとは限りません）

#### ラムダ

UdonSharp はラムダを扱えないので、ラムダは上記の List のメソッドの引数としてだけ使えます。ループとラムダの本体は、呼び出しごとに生成するメソッドの中に入ります（ラムダを変数に入れる、後で呼ぶ、などはできません）。

```csharp
int limit = 4;
Bullet hit = _bullets.Find(b => b.damage > limit);
int total = 0;
_scores.ForEach(s => total += s);
_scores.Sort((a, b) => b - a);
```

- ラムダの中から、外のローカル変数・引数を読み書きできます。書き込みも C# と同じく、呼び出しの直後から外の変数に反映されます。フィールドもそのまま読み書きできます
- 本体は式（`x => x > 0`）とブロック（`x => { ... }`）のどちらでも書けます。メソッド名を渡すこともできます（`_scores.Exists(IsOdd)`）
- `Sort(Comparison)` は安定な並べ替え（マージソート）です（C# の `List.Sort` は安定とは限りません）
- `FindIndex(start, count, match)` などで範囲外を指定したときは、C# の `ArgumentOutOfRangeException` ではなく、Udon の実行時例外か、見つからない結果（-1）になります
- ラムダの中で起きたコンパイルエラーと実行時例外は、元のラムダの位置に表示されます

未対応の書き方（コンパイルエラーになります）：

- 上記の List のメソッドの引数以外のラムダ（`Func<int, bool> f = x => x > 0;` など）、`delegate { ... }`、デリゲートの変数を渡すこと
- フィールドの初期化子の中で、ラムダを渡す List のメソッドを呼ぶこと
- `foreach (var kv in dict)` の `kv` をラムダの中で使うこと（`kv.Key`／`kv.Value` をいったん変数に入れてください）
- `[RecursiveMethod]` を付けたメソッドの中で、外のローカル変数・引数に書き込むラムダ。外の変数は `ref` 引数で生成メソッドに渡しますが、UdonSharp は `[RecursiveMethod]` の中での `ref` 引数のメソッドの呼び出しに対応していないため、UdonSharp のエラー（`U# does not yet support calling user methods with ref/out parameters from methods marked with RecursiveMethod`）になります。読むだけのラムダは使えます

## 仕組み

1. `CompilationContext.LoadSyntaxTreesAndCreateModules` にPostfixを当て、UdonSharp に渡す SyntaxTree を書き換えます。
   1. インタフェース：インタフェース型を `object` に置き換えます。呼び出しは、汎用クラスならそのクラスのメソッドの呼び出しに、UdonSharpBehaviour なら `SetProgramVariable`／`SendCustomEvent`／`GetProgramVariable` 経由に振り分けます。`is`／`as` は型タグと型IDで判定する形にします。
   2. 汎用クラス：インスタンスを `object[]` にし、コンストラクタ・メソッドのコピーを、使う UdonSharpBehaviour に生成します。
   3. List / Dictionary：`DataList` / `DataDictionary` に置き換え、要素の出し入れを `DataToken` との変換にします。`foreach` は添字で回すループにします。ラムダを受け取るメソッドは、ループとラムダの本体を入れたメソッドを生成し、その呼び出しにします。外のローカル変数・引数は、読むだけなら値で、書き込むなら `ref` で渡します（書き込むかどうかは Roslyn のデータフロー解析で判定します）。
2. `CompilationContext.MethodExportLayout` のコンストラクタにPrefixを当て、インタフェース実装メソッドの Udon 上の名前を、どの実装クラスでも揃うものにします（メソッドは元の名前、引数は `Damage__int__string__p0`、戻り値は `Damage__int__string__ret`）。
3. `CompilationContext.AddDiagnostic` と `AssemblyDebugInfo`（実行時例外の位置を引くデバッグ情報）にパッチを当て、汎用クラスのコピーの中の位置を、元のクラスのソースの位置に戻します。

書き換え後のソースは、デバッグ用に `Library/CustomUdonSharp/` に出力されます。

## ライセンス

[MIT License](LICENSE.md)（Copyright (c) 2026 pandanakami）

このパッケージには、VRChat SDK・UdonSharp・Harmony・Roslyn は含まれていません。これらは VRChat SDK に同梱されたものを参照しているだけで、それぞれのライセンスに従います（VRChat SDK：https://hello.vrchat.com/legal/sdk ）。
