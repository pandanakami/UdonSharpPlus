# ジェネリクス（実験的）

ジェネリッククラスと、ジェネリックメソッドを宣言して使えます。

```csharp
public class Pool<T>
{
    private List<T> _items = new List<T>();
    public void Add(T item) { _items.Add(item); }
    public T Get(int i) => _items[i];
    public T Empty() => default(T);
}

public static class Util
{
    public static T Max<T>(List<T> list) where T : IComparable<T> { ... }
}

// UdonSharpBehaviour の中
var pool = new Pool<int>();
int max = Util.Max(scores);   // 型引数の推論も使えます
```

使われる型引数ごとにコピーを作ります。`Pool<int>` は `Pool__int` という汎用クラスに、`Max<int>` は `Max__int` というメソッドになり、コピーの中では `T` が具体的な型（`default(T)`、`new T()` の中なども）に置き換わります。そのため、`List<T>` や LINQ など、[ほかの機能](collections.md)もそのまま使えます。インスタンスは[汎用クラス](class.md)と同じく `object[]` です。

対応している書き方：

- ジェネリッククラス（型引数は複数でもよい）。中身は汎用クラスと同じ書き方ができます
- 汎用クラス・static クラスのジェネリックメソッド（拡張メソッドを含む）、ジェネリッククラスの中のジェネリックメソッド
- UdonSharpBehaviour のジェネリックメソッド（同じクラスと、継承したクラスの中から呼ぶもの）。別のクラスから見えるもの（`public`・`internal`・`protected internal`）は、別の UdonSharpBehaviour からは呼べないので、宣言に警告が出ます。`private` か `protected` にしてください
- ジェネリックが別のジェネリックを呼ぶこと
- 制約（`where T : IComparable<T>` など）。`a.CompareTo(b)` などは、具体的な型のメソッドの呼び出しになります。Udon で `CompareTo` が使えるのは数値型・`bool`・`string` で、`Equals`・`GetHashCode` はどの型でも使えます

未対応の書き方（コンパイルエラーになります）：

- 別の UdonSharpBehaviour のジェネリックメソッドを呼ぶこと（`other.Echo(1)`）。static クラスに移すか、型を決めたメソッドを用意してください
- ジェネリックな UdonSharpBehaviour、ジェネリックなインタフェース
- 型が際限なく増える再帰（`Nest<T>` の中で `Nest<List<T>>` を呼ぶなど）
- `EqualityComparer<T>.Default`・`Comparer<T>.Default`、型引数を省いた `typeof(Pool<>)`
- そのほか、汎用クラスで未対応の書き方（ジェネリッククラスの static フィールド、継承など）

注意：

- コピーの中で出たコンパイルエラーは、元のジェネリックなソースの位置に表示され、末尾にどの型引数のコピーかが付きます（「（汎用クラス Pool<int> のメソッド Get の中）」）
- どこからも使われないジェネリッククラス・メソッドは、コピーが作られず、検査もしません
