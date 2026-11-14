# デリゲート（実験的・対応中）

自分で宣言したデリゲートを、UdonSharpBehaviour と普通のクラスの中で使えます。

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
    }
}
```

対応している書き方：

- `delegate` の宣言。名前空間の直下・UdonSharpBehaviour の中・普通のクラスの中のどこに書いても使えます
- デリゲート型の変数・フィールド・引数・戻り値・配列、普通のクラスのフィールド
- 入れられるもの：static メソッド（どのクラスのものでも）、その UdonSharpBehaviour の（`this` の）メソッド、ラムダ、`null`、`new Calc(F)`
- ラムダ：式の本体（`x => x * 2`）・ブロックの本体（`x => { ... }`）・捨てる引数（`(_, _) => ...`）が使えます。フィールド・プロパティ・メソッドも使えます。ラムダの中でエラー・実行時例外が起きると、元のラムダの行に表示されます。デリゲートを呼ぶラムダは、再帰しても壊れないよう `[RecursiveMethod]` と同じ扱いにします（ラムダには属性を書けないため）
- 外のローカル変数・引数を使うラムダ（クロージャ）：C# と同じく、ラムダと元のメソッドは同じ変数を読み書きします。ループの中で作ったラムダは 1 回ごとの変数を使い、メソッドを抜けたあとに呼んでも、その変数を持ち続けます（`Calc MakeAdder(int k) => x => x + k;`）。使えるのは、ブロックの中で宣言したローカル変数・`foreach` の変数・メソッドとラムダの引数です
- 呼び出し：`d(x)`、`d.Invoke(x)`。null のデリゲートを呼ぶと、C# と同じく実行時例外で止まります。止まる位置は、変数・フィールド・プロパティを呼んだときは呼び出した行、配列の要素（`ds[0](x)`）・メソッドの戻り値（`Get()(x)`）を呼んだときはデリゲートの宣言の行です
- 呼んだメソッドの中で起きた実行時例外は、そのメソッドの行に表示されます
- 引数・戻り値の型は、値型（`Vector3` など）・配列・`GameObject`・UdonSharpBehaviour・普通のクラス・インタフェース・ジェネリッククラス・`List` / `Dictionary`・ほかのデリゲートのどれでも使えます
- ほかの機能の中で使えます：普通のクラス・ジェネリッククラスのメソッドの中で呼ぶ、インタフェースのメソッドの引数にする、ラムダ・LINQ の中で呼ぶ（`list.ForEach(x => d(x))`）、`[RecursiveMethod]` のメソッドの中で呼ぶ・デリゲートで再帰する
- virtual のメソッドを入れると、C# と同じく override したメソッドが呼ばれます
- public のデリゲート型のフィールドは、null で始まります（シリアライズしません）
- `null` との比較（`d == null`、`d != null`）
- 別の UdonSharpBehaviour に渡して、向こうで呼ぶこと。呼んだメソッドは、デリゲートを作った UdonSharpBehaviour の上で動きます

未対応の書き方（コンパイルエラーになります）：

| 書き方 | 予定 |
|---|---|
| ラムダで、`for` の変数・`switch` の中・`out var`・パターン・`catch` で宣言した変数、List・LINQ のメソッドに渡すラムダの引数、プロパティの `value` を使うこと | ブロックの中で別のローカル変数に入れてから使ってください |
| ラムダで使う変数を `ref` / `out` / `in` で渡すこと、構造体の変数のフィールドに代入すること（`pos.y = 0;`） | 変数ごと代入してください（`pos = new Vector3(...)`） |
| 匿名メソッド（`delegate (int x) { ... }`）、ローカル関数 | 対応予定 |
| `+=` / `-=`、`+` / `-` | 対応予定 |
| ほかのインスタンスのメソッド（`d = enemy.Hit;`、普通のクラスのメソッド） | 対応予定 |
| 普通のクラスの中・static メソッドの中・フィールドの初期化子の中でデリゲートを作ること | 普通のクラスの中は対応予定。UdonSharpBehaviour の中で作って渡してください |
| `Action`・`Func`・`Predicate` などの、.NET のデリゲート | 対応予定 |
| `List<Calc>` など、型引数に使うこと | 配列を使ってください |
| `d?.Invoke(x)` | `if (d != null) d(x);` と書いてください |
| デリゲートどうしの `==` / `!=`、`Method`・`Target`・`GetInvocationList`・`DynamicInvoke` などのメンバー、`event` | — |
| ジェネリックのデリゲート（`delegate T Map<T>(T x)`）、`ref` / `out` / `in`・`params`・既定値のある引数を持つデリゲート | — |
| デリゲート型のフィールドのシリアライズ・同期（`[SerializeField]`、`[UdonSynced]`）。public のフィールドもシリアライズされません | — |
| `[NetworkCallable]` のメソッドの引数にすること（ほかのクライアントには送れません） | — |
| 型としての `typeof`・`nameof`・`is`・`as` | — |

注意：

- デリゲートの呼び出しは、`SetProgramVariable` / `SendCustomEvent` / `GetProgramVariable` になります（同じ UdonSharpBehaviour の中で呼ぶときも）。普通のメソッドの呼び出しより遅いので、毎フレーム何度も呼ぶところでは気をつけてください
- 呼ぶ相手の UdonSharpBehaviour を Destroy したあとにデリゲートを呼ぶと、実行時例外で止まります（C# ではメソッドがそのまま呼ばれます）。止まる位置はデリゲートの宣言の行です
- 呼んでいる最中に、呼ばれたメソッドの中から同じデリゲート型のデリゲートを呼ぶことはできます（デリゲートで再帰する、別の UdonSharpBehaviour のデリゲートを呼ぶ、など）。呼び出しの途中の値は、`[RecursiveMethod]` と同じしくみでスタックに積んで戻します（呼び出しごとに、その分の手間がかかります）。再帰するメソッド自体には、UdonSharp と同じく `[RecursiveMethod]` を付けてください
- デリゲートを入れる UdonSharpBehaviour には、`__dlg_` で始まる public メソッドと protected フィールドが足されます（Udon の上の名前です。ソースは変わりません）
- ラムダで使う外の変数は、メソッドの中でも `object[]` の要素になります。その変数の読み書きは、配列の読み書きと型変換の分だけ遅くなります（C# でも同じしくみで、クラスのフィールドになります）。ラムダで使わない変数は変わりません

## 仕組み

デリゲートの値は `object[]`（呼ぶ相手の UdonSharpBehaviour・呼ぶメソッドの名前・ラムダが使う外の変数）になります。

- `d = F;` → `new object[] { this, "__dlg_…", null }`。作った UdonSharpBehaviour に、`F` を呼ぶだけの引数なしの public メソッド（中継メソッド）を足します
- `d(x)` → 引数をデリゲート型ごとに決まった名前の変数に `SetProgramVariable` し、中継メソッドを `SendCustomEvent` し、戻り値を `GetProgramVariable` で読みます
- ラムダが使う外の変数は、宣言した範囲（ブロック・`foreach` の 1 回・メソッドやラムダの本体）に入るたびに作る `object[]` の要素にします。元のメソッドもラムダも、その要素を読み書きします。ラムダの値には、使う範囲の `object[]` の一覧を入れます
