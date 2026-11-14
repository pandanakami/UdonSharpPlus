# デリゲート（実験的・対応中）

自分で宣言したデリゲートと、.NET の `Action`・`Func`・`Predicate`・`Comparison` を、UdonSharpBehaviour と普通のクラスの中で使えます。

```csharp
public delegate int Calc(int x);

public class Turret : UdonSharpBehaviour
{
    private Calc _damage;

    private static int Double(int x) => x * 2;
    private int AddBonus(int x) => x + bonus;

    private void Start()
    {
        _damage = Double;          // static メソッド
        _damage = AddBonus;        // この UdonSharpBehaviour のメソッド
        int k = 3;
        _damage = x => x * k;      // ラムダ（外のローカル変数・引数も使えます）
        int d = _damage(10);       // 呼び出し（_damage.Invoke(10) も同じ）
        other.SetFilter(_damage);  // 引数に渡す（別の UdonSharpBehaviour でも呼べます）

        Func<int, bool> isBig = x => x > 10;   // Action・Func・Predicate・Comparison も同じように使えます
        int n = _scores.Where(isBig).Count();  // LINQ・List のメソッドに、デリゲートの変数を渡せます
    }
}
```

対応している書き方：

- `delegate` の宣言。名前空間の直下・UdonSharpBehaviour の中・普通のクラスの中のどこに書いても使えます
- .NET の `Action`（引数なしも）・`Func`・`Predicate`・`Comparison`。自分で宣言したデリゲートと同じように使えます（型引数ごとに別の型として扱います）
- LINQ・List のメソッドに、デリゲートを渡すこと（`list.Where(isBig)`、`list.ForEach(log)`、`list.Sort(compare)`、`list.Exists(MakeMatch())`）。C# と同じく、渡す式は 1 回だけ評価し、呼ばれている途中で元の変数を書き換えても、渡したときのデリゲートを最後まで呼びます。今までどおりラムダ・メソッド名を直接書くこともできます。普通のクラスのインスタンスのメソッド（`list.Select(box.Get)` は書けません）も、いったん変数に入れれば渡せます（`Func<int, int> get = box.Get; list.Select(get)`）
- デリゲート型の変数・フィールド・引数・戻り値・配列、普通のクラスのフィールド
- 入れられるもの：static メソッド（どのクラスのものでも）、その UdonSharpBehaviour の（`this` の）メソッド・`base` のメソッド、ほかの UdonSharpBehaviour のインスタンスのメソッド（`enemy.Hit`）、普通のクラス・インタフェースのインスタンスのメソッド（`item.IsHit`、`shape.Area`）、ラムダ、匿名メソッド（`delegate (int x) { ... }`、引数を書かない `delegate { ... }`）、ローカル関数、`null`、`new Calc(F)`
- 普通のクラスの中でも作れます（メソッド名・`this` のメソッド（private のものも）・ラムダ・匿名メソッド・ローカル関数）。ラムダは `this` のフィールドと外の変数を使えます
- null のインスタンスのメソッドから作ると（`item` が null のときの `d = item.IsHit;`）、C# と同じく作るときに実行時例外で止まります。止まる位置はデリゲートの宣言の行です
- ラムダ：式の本体（`x => x * 2`）・ブロックの本体（`x => { ... }`）・捨てる引数（`(_, _) => ...`）が使えます。フィールド・プロパティ・メソッドも使えます。ラムダの中でエラー・実行時例外が起きると、元のラムダの行に表示されます。デリゲートを呼ぶラムダは、再帰しても壊れないよう `[RecursiveMethod]` と同じ扱いにします（ラムダには属性を書けないため）
- 外のローカル変数・引数を使うラムダ（クロージャ）：C# と同じく、ラムダと元のメソッドは同じ変数を読み書きします。ループの中で作ったラムダは 1 回ごとの変数を使い、メソッドを抜けたあとに呼んでも、その変数を持ち続けます（`Calc MakeAdder(int k) => x => x + k;`）。使えるのは、ブロックの中で宣言したローカル変数・`foreach` の変数・メソッドとラムダの引数です
- 呼び出し：`d(x)`、`d.Invoke(x)`。null のデリゲートを呼ぶと、C# と同じく実行時例外で止まります。止まる位置は、変数・フィールド・プロパティを呼んだときは呼び出した行、配列の要素（`ds[0](x)`）・メソッドの戻り値（`Get()(x)`）を呼んだときはデリゲートの宣言の行です
- つなぐ・外す：`d += F;`、`d -= F;`、`d1 + d2`、`d1 - d2`。つないだデリゲートは、つないだ順に呼ばれ、戻り値は最後のものです。`-=` は、C# と同じく最後に現れるものを外し、全部外れると `null` になります。ラムダ・匿名メソッド・ローカル関数は、同じ値（変数に入れたもの）か、同じ場所で同じ範囲の中で作ったもの（`d += F; d -= F;` など）なら外れ、別の場所に書いた同じ式や、ループの別の回で作ったもの（使う外の変数が違うもの）は外れません（C# と同じ）
- 呼んだメソッドの中で起きた実行時例外は、そのメソッドの行に表示されます
- 引数・戻り値の型は、値型（`Vector3` など）・配列・`GameObject`・UdonSharpBehaviour・普通のクラス・インタフェース・ジェネリッククラス・`List` / `Dictionary`・ほかのデリゲートのどれでも使えます
- ほかの機能の中で使えます：普通のクラス・ジェネリッククラスのメソッドの中で呼ぶ、インタフェースのメソッドの引数にする、ラムダ・LINQ の中で呼ぶ（`list.ForEach(x => d(x))`）、`[RecursiveMethod]` のメソッドの中で呼ぶ・デリゲートで再帰する
- virtual のメソッドを入れると、C# と同じく override したメソッドが呼ばれます
- public のデリゲート型のフィールドは、null で始まります（シリアライズしません）
- `null` との比較（`d == null`、`d != null`）
- 別の UdonSharpBehaviour に渡して、向こうで呼ぶこと。UdonSharpBehaviour のメソッド・その中で作ったラムダなどは、そのメソッドの UdonSharpBehaviour の上で動きます（`enemy.Hit` なら `enemy`）

未対応の書き方（コンパイルエラーになります）：

| 書き方 | 予定 |
|---|---|
| ラムダで、`for` の変数・`switch` の中・`out var`・パターン・`catch` で宣言した変数、List・LINQ のメソッドに渡すラムダの引数、プロパティの `value` を使うこと | ブロックの中で別のローカル変数に入れてから使ってください |
| ラムダで使う変数を `ref` / `out` / `in` で渡すこと、構造体の変数のフィールドに代入すること（`pos.y = 0;`） | 変数ごと代入してください（`pos = new Vector3(...)`） |
| ジェネリックのローカル関数、`async` のローカル関数、`switch` のラベルの直下に書いたローカル関数 | メソッドにするか、`{ }` で囲んでください |
| 外の変数を使うローカル関数を、呼ぶ・デリゲートに入れるほかの形で使うこと（List・LINQ のメソッドに直接渡すなど） | ラムダで包むか、デリゲートの変数に入れてください（`list.ForEach(x => F(x))`） |
| `arr[Next()] += F` のように、受け側を 2 回評価すると結果が変わる `+=` / `-=` | いったん変数に入れてください |
| UdonSharp・Unity・.NET のクラスのインスタンスのメソッド（`d = new System.Random().Next;`） | ラムダで包んでください（`d = x => r.Next(x);`） |
| 普通のクラスの中で、UdonSharpBehaviour の static メソッドを入れること | 普通のクラスの static メソッドにするか、UdonSharpBehaviour の中で作ってください |
| UdonSharpBehaviour の static メソッド・static のローカル関数の中、フィールドの初期化子の中でデリゲートを作ること | インスタンスのメソッドの中で作ってください |
| ほかの .NET のデリゲート（`Converter`・`EventHandler`・`UnityAction` など） | `Func`・`Action` か、自分で宣言したデリゲートを使ってください |
| 型の違うデリゲートへの変換（`Func<string>` を `Func<object>` に入れるなど） | ラムダで包んでください（`Func<object> o = () => s();`） |
| ラムダ・メソッド名を、LINQ・List のほかの .NET のメソッドに渡すこと（`Array.Exists(arr, x => x > 0)`） | — |
| `List<Calc>`・`List<Action>` など、型引数に使うこと | 配列を使ってください |
| `d?.Invoke(x)` | `if (d != null) d(x);` と書いてください |
| デリゲートどうしの `==` / `!=`、`Method`・`Target`・`GetInvocationList`・`DynamicInvoke` などのメンバー、`event` | — |
| ジェネリックのデリゲート（`delegate T Map<T>(T x)`）、`ref` / `out` / `in`・`params`・既定値のある引数を持つデリゲート | — |
| デリゲート型のフィールドのシリアライズ・同期（`[SerializeField]`、`[UdonSynced]`）。public のフィールドもシリアライズされません | — |
| `[NetworkCallable]` のメソッドの引数にすること（ほかのクライアントには送れません） | — |
| 型としての `typeof`・`nameof`・`is`・`as` | — |

注意：

- UdonSharpBehaviour のメソッド・その中で作ったラムダなどの呼び出しは、`SetProgramVariable` / `SendCustomEvent` / `GetProgramVariable` になります（同じ UdonSharpBehaviour の中で呼ぶときも）。普通のメソッドの呼び出しより遅いので、毎フレーム何度も呼ぶところでは気をつけてください。普通のクラスの中で作ったもの・普通のクラスとインタフェースのメソッドは、これらを使わずに直接呼ぶので、より速く呼べます
- 呼ぶ相手の UdonSharpBehaviour を Destroy したあとにデリゲートを呼ぶと、実行時例外で止まります（C# ではメソッドがそのまま呼ばれます）。止まる位置はデリゲートの宣言の行です
- 呼んでいる最中に、呼ばれたメソッドの中から同じデリゲート型のデリゲートを呼ぶことはできます（デリゲートで再帰する、別の UdonSharpBehaviour のデリゲートを呼ぶ、など）。呼び出しの途中の値は、`[RecursiveMethod]` と同じしくみでスタックに積んで戻します（呼び出しごとに、その分の手間がかかります）。再帰するメソッド自体には、UdonSharp と同じく `[RecursiveMethod]` を付けてください
- デリゲートを入れる UdonSharpBehaviour には、`__dlg_` で始まる public メソッドと protected フィールドが足されます（Udon の上の名前です。ソースは変わりません）
- UdonSharpBehaviour の中で作った static メソッドのデリゲートは、作った UdonSharpBehaviour ごとに別のものになります。別の UdonSharpBehaviour で作った同じ static メソッドのデリゲートを `-=` しても外れません（C# では外れます）。同じ UdonSharpBehaviour で作ったもの・普通のクラスの中で作ったものなら外れます
- ラムダで使う外の変数は、メソッドの中でも `object[]` の要素になります。その変数の読み書きは、配列の読み書きと型変換の分だけ遅くなります（C# でも同じしくみで、クラスのフィールドになります）。ラムダで使わない変数は変わりません
- LINQ・List のメソッドに null のデリゲートを渡すと、C# と同じく、要素がなくても渡したときに実行時例外で止まります（C# では `ArgumentNullException`）。止まる位置は、変数・フィールド・プロパティを渡したときは渡した行、そのほかの式（`list.Where(Make())`）のときはデリゲートの宣言の行です
- `EventHandler` などの対応しない .NET のデリゲートのエラーは、デリゲート（`delegate` の宣言・`Action`・`Func`・`Predicate`・`Comparison`）かローカル関数を使っているプロジェクトでだけ、UdonSharp Plus のエラーになります。そうでなければ UdonSharp のエラーになります
- .NET のデリゲート（`Func` など）には、ソースに宣言の行がありません。上で「デリゲートの宣言の行」で止まるとしているものは、その型（`Func<int, int>` など）を最初に使ったファイルの、最後の行で止まります

## ローカル関数

メソッドの中に書いたメソッド（ローカル関数）を、UdonSharpBehaviour と普通のクラスの中で使えます（UdonSharp 自体は対応していません）。デリゲートを使わなくても使えます。

```csharp
private void Start()
{
    int hits = 0;
    void Hit(int damage) { hits++; hp -= damage; }   // 外の変数・フィールドを読み書きできます
    int Fact(int n) => n <= 1 ? 1 : n * Fact(n - 1); // 再帰
    Hit(3);
    Calc c = Fact;                                    // デリゲートにも入れられます
}
```

- 宣言より前で呼ぶ、ローカル関数の中のローカル関数、`static` のローカル関数、属性（`[RecursiveMethod]` など）も使えます
- 同じクラスの private メソッドになります。外の変数は、ラムダと同じく `object[]` の要素になり、その配列を引数で受け取ります
- 自分を（ほかのローカル関数を通しても）呼ぶローカル関数と、デリゲートを呼ぶローカル関数には、`[RecursiveMethod]` を自動で付けます
- ローカル関数の中でエラー・実行時例外が起きると、元の行に表示されます

## 仕組み

デリゲートの値は `object[]`（呼ぶ相手の UdonSharpBehaviour・呼ぶメソッドの名前・ラムダが使う外の変数）になります。

- `d = F;` → `new object[] { this, "__dlg_…", null }`。作った UdonSharpBehaviour に、`F` を呼ぶだけの引数なしの public メソッド（中継メソッド）を足します
- `d(x)` → 引数をデリゲート型ごとに決まった名前の変数に `SetProgramVariable` し、中継メソッドを `SendCustomEvent` し、戻り値を `GetProgramVariable` で読みます
- `d = enemy.Hit;` → `new object[] { enemy, "__dlg_…", null }`。中継メソッドは `Hit` を宣言したクラスに置きます
- 普通のクラスの中で作ったもの・普通のクラスとインタフェースのメソッド（`d = item.IsHit;`）→ `new object[] { null, "__dlg_…", new object[] { item } }`。呼ぶ相手が null の組は、呼び出しの中の振り分け（static メソッド）が名前を見て直接呼びます。普通のクラスのメソッドは、使う UdonSharpBehaviour にコピーされるので、どの UdonSharpBehaviour の中で呼んでも同じように動きます
- ラムダが使う外の変数は、宣言した範囲（ブロック・`foreach` の 1 回・メソッドやラムダの本体）に入るたびに作る `object[]` の要素にします。元のメソッドもラムダも、その要素を読み書きします。ラムダの値には、使う範囲の `object[]` の一覧を入れます
- `Func<int, int>` などの .NET のデリゲートは、型引数ごとに、自分で宣言したデリゲートと同じ扱いにします。呼び出しの static クラスは、その型を最初に使ったファイルの末尾（名前空間の外）に置きます
- `list.Where(isBig)` → `list.Where((__dp) => __dlg_….__invoke(__dlg_….__dlgarg(isBig), __dp))`。そのラムダは、直接書いたラムダと同じく、List・LINQ の書き換えがループの中に埋め込みます。`__dlgarg(…)` のところは、生成メソッドの引数にして、呼ぶ側で 1 回だけ評価します
