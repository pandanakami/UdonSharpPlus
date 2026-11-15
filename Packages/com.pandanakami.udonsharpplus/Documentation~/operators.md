# 演算子のオーバーロード（実験的）

普通のクラスと UdonSharpBehaviour に、演算子（`operator +` など）と変換（`implicit operator` / `explicit operator`）を宣言して使えます（UdonSharp 自体は対応しておらず、UdonSharpBehaviour に宣言すると、それだけで内部の例外になります）。

```csharp
public class Vec
{
    public int x, y;
    public Vec(int x, int y) { this.x = x; this.y = y; }

    public static Vec operator +(Vec a, Vec b) => new Vec(a.x + b.x, a.y + b.y);
    public static Vec operator *(Vec a, int k) => new Vec(a.x * k, a.y * k);
    public static Vec operator -(Vec a) => new Vec(-a.x, -a.y);
    public static bool operator ==(Vec a, Vec b) => (object)a == null ? (object)b == null : (object)b != null && a.x == b.x && a.y == b.y;
    public static bool operator !=(Vec a, Vec b) => !(a == b);
    public static implicit operator Vec(int v) => new Vec(v, v);
    public static explicit operator int(Vec v) => v.x + v.y;
}

// UdonSharpBehaviour の中
Vec p = new Vec(1, 2) + new Vec(3, 4);   // (4, 6)
p += 1;                                  // 暗黙の変換で (1, 1) を足す
p *= 2;
++p;                                     // operator ++ を宣言していれば
bool same = p == new Vec(11, 15);
int n = (int)p;
```

対応している書き方：

- 2 項の演算子（`+` `-` `*` `/` `%` `&` `|` `^` `<<` `>>` `==` `!=` `<` `>` `<=` `>=`）、単項の演算子（`+` `-` `!` `~` `++` `--`）、暗黙・明示の変換。左右の型が違うもの（`Vec * int` と `int * Vec`）、オーバーロードも使えます
- 使う場所：式の中、引数・戻り値・初期化子（`Vec v = 3;`、`Vec[] vs = { 1, 2 };`）、三項演算子、`foreach` の変数の型への変換（`foreach (Vec v in ints)`）、複合代入（`a += b`）、前置・後置の `++`／`--`（後置は文として書いたとき）、ラムダ・LINQ（`list.Aggregate((p, q) => p + q)`）、タプルの `==`（要素の `==` に使われます）、匿名型のメンバー
- 複合代入・`++`／`--` の受け側：ローカル変数・引数・フィールド・プロパティ（普通のクラスのものも）、配列・`List` の要素（添字が変数か定数のもの）
- ジェネリッククラスの演算子・変換（`OpBox<T>`。型引数ごとのコピーの中で使えます）
- UdonSharpBehaviour の演算子・変換。別の UdonSharpBehaviour からも使えます（`peer + 1`、`(int)peer`）。UdonSharpBehaviour は `new` できないので、戻り値は値か、すでにある参照になります

未対応の書き方（コンパイルエラーになります）：

| 書き方 | 代わりの書き方 |
|---|---|
| `true` / `false` 演算子と、それを使う `&&`／`||`（ユーザー定義の `&`／`|` と組み合わせるもの） | `bool` を返すメソッドかプロパティにしてください |
| 後置の `++`／`--` の値を使う（`var y = x++;`） | 前置にする（`var y = ++x;`）か、文にしてください |
| 複合代入・`++`／`--` の受け側にメソッドの呼び出しなどを書く（`arr[Next()] += v`、`Get().pos += v`） | 変数に入れてから使ってください |
| UdonSharpBehaviour の `==`／`!=` | 比べるメソッドにしてください（UnityEngine.Object の比較（破棄したものを null とみなす）を置き換えてしまうため） |
| UdonSharpBehaviour の型への変換（`explicit operator Peer(int v)`） | static メソッドにしてください（UdonSharpBehaviour のキャストと区別できないため） |

注意：

- `==` を宣言しても、`List` の `Contains`・`IndexOf`・`Remove` は、C# と同じく `Equals` で比べます（普通のクラスの `Equals` の override は未対応なので、参照の比較になります）
- `==` の中で `a == null` と書くと、C# と同じく、自分の `==` を呼びます。null との比較は、`(object)a == null` と書いてください
- 書き換えたメソッドの名前（`op_Addition__Vec__Vec` など）を、同じクラスのほかのメンバーに使うとエラーになります
- 演算子の中で出たコンパイルエラー・実行時例外は、元の演算子の宣言の位置に出ます。普通のクラスの演算子の中のコンパイルエラーには、「（クラス Vec の演算子 + の中）」のような補足が付きます

仕組み：

- 演算子の宣言は、名前の付いた普通の static メソッドになります（`operator +(Vec a, Vec b)` → `op_Addition__Vec__Vec`、`implicit operator Vec(int v)` → `op_Implicit__int__to_Vec`）。使う側の式（`a + b`、暗黙の変換の `3` など）は、そのメソッドの呼び出し（`Vec.op_Addition__Vec__Vec(a, b)`）に、`a += b` は `a = Vec.op_Addition__Vec__Vec(a, b)` になります
- どの演算子・変換が使われるかは、C# のコンパイラと同じ規則（Roslyn の意味解析）で決めます
- あとは、普通のクラスなら[普通のクラス](class.md)の static メソッドと同じに（使う UdonSharpBehaviour にコピーされます）、UdonSharpBehaviour なら static メソッドの呼び出しとして扱います
- 書き換え後のソースは、`Library/UdonSharpPlus/` で見られます
