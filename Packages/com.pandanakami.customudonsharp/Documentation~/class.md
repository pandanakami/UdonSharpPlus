# 汎用クラス（実験的）

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

インスタンスは `object[]`（先頭に型名、次に型番号、続けてフィールド）になり、コンストラクタやメソッドは、そのクラスを使う UdonSharpBehaviour ごとにコピーされます。状態は `object[]` の中にあるので、インスタンスを別の UdonSharpBehaviour に渡しても、同じインスタンスとして扱えます。

汎用クラスは、どのファイルに何個書いても構いません。UdonSharpBehaviour と同じファイルにも書けます。ファイルの分け方に決まりがあるのは UdonSharpBehaviour だけで、UdonSharp と同じく、1 つのファイルに 1 つ、ファイル名と同じクラス名で書きます（インタフェースも汎用クラスと同じく、どこに書いても構いません）。

対応している書き方：

- インスタンスのフィールド（初期化子を含む）、コンストラクタ（オーバーロード、`this(...)` を含む）、インスタンスメソッド
- プロパティ（自動実装と、本体付きの get/set）
- static メソッド、拡張メソッド、static クラス、const。static メソッドは、メソッド名だけで List のメソッドに渡せます（`list.Find(IsOdd)`。[List / Dictionary](collections.md)）
- クラスの配列、`foreach`
- `is`、`as`、キャスト、`nameof`
- 再帰（`[RecursiveMethod]` を付けたメソッド）
- [インタフェース](interface.md)の実装。インタフェース経由の呼び出しは、型タグを見てそのクラスのメソッドのコピーを呼びます。インタフェースを使う UdonSharpBehaviour には、それを実装する全クラスのメソッドのコピーが入ります
  - 実装する汎用クラスが 4 個以上なら、インスタンスの型番号で `switch` して振り分けます。UdonSharp はこの `switch` をジャンプテーブルにするので、実装クラスがいくつあっても、どのクラスでも同じ速さで呼べます。型番号は同じインタフェースを実装するクラスの間で重ならないように振るので、ほとんどの場合 0〜256（ジャンプテーブルにできる範囲）に収まります。収まらないときと、実装クラスが 3 個以下のときは、型タグを順に比べます

未対応の書き方（そのクラスを UdonSharpBehaviour から使うとコンパイルエラーになります）：

- static フィールド（UdonSharp と同じく非対応）
- 継承、abstract クラス、入れ子のクラス。ScriptableObject・MonoBehaviour・Exception などを継承したクラスも、UdonSharpBehaviour から使うと「`'Settings' : 汎用クラスの継承は未対応です（ScriptableObject を継承しています）`」のエラーになります（UdonSharp も対応しておらず、そのままだと内部の例外になるため）
- ユーザー定義の構造体（struct）。UdonSharp も対応していません。UdonSharpBehaviour から使うと「`'Point' : ユーザー定義の構造体は未対応です`」のエラーになります（UdonSharp のままだと内部の例外になり、原因が分かりにくいため）
- 演算子のオーバーロード、インデクサー、イベント、`ToString` などの override
- インスタンスのメソッドを、メソッド名だけで渡すこと（`list.Find(b.IsHit)`）。ラムダで書いてください（`list.Find(x => b.IsHit(x))`）
- オブジェクト初期化子（`new Bullet { damage = 1 }`）、`?.`、型パターン（`x is Bullet b`）、`typeof(Bullet)`
- 汎用クラス型のフィールドのシリアライズと同期（`[SerializeField]`、`[UdonSynced]`）。public フィールドもシリアライズされません
- フィールド・プロパティへの代入の値を式として使うこと（`x = b.damage = 1`）。複合代入とインクリメントの受け側に、メソッド呼び出しを書くこと（`Get().damage += 1`）

注意：

- 値型のフィールドのメソッドで、そのフィールド自体を書き換えるもの（`b.pos.Normalize()` など）は、コピーに対して呼ばれるため反映されません。`b.pos = b.pos.normalized;` のように代入してください
- 汎用クラスのメソッドの中で出たコンパイルエラーは、元のクラスのソースの位置（文の先頭）に表示され、末尾にどのクラスのどのメンバーかが付きます。メッセージの中の式は書き換え後のもの（フィールド `path` が `((string)(__this)[2])`、型名が `global::` 付きの完全修飾になるなど）になります。同じクラスを複数の UdonSharpBehaviour で使っていても、同じエラーは 1 つにまとめます

  ```
  Saver.cs(10,13): Method is not exposed to Udon: 'global::System.IO.File.Exists(((string)(__this)[2]))'（汎用クラス Saver のメソッド Exists の中）
  ```

- 汎用クラスのメソッドの中で起きた実行時例外（`Udon runtime exception detected!`）も、元のクラスのソースの位置（文の単位）に表示されます。表示の仕組みは UdonSharp のものなので、クライアントで確かめるときは、UdonSharp と同じく `--enable-udon-debug-logging` を付けて起動してください
- UdonSharpBehaviour から使われていないクラスは書き換えず、検査もしません
