# インタフェース（実験的）

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
- [普通のクラス](class.md)による実装。UdonSharpBehaviour による実装と混ぜて、同じインタフェース型の変数や配列に入れられます

インタフェース型の変数は、`object` として扱われます（中身は UdonSharpBehaviour か、普通のクラスのインスタンス）。そのため `x != null` は、普通の C# のインタフェースと同じく参照の比較になり、Unity の比較（破棄済みのオブジェクトを null とみなす）にはなりません。

## メソッド名とオーバーロード

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
- インタフェース型（とその配列）のフィールドの、シリアライズと同期（`[SerializeField]`、`[UdonSynced]`）。「`'target' : インタフェース型のフィールドは、シリアライズ・同期できません（…）`」のエラーになります。Inspector で設定したいときは、`UdonSharpBehaviour` 型で受けてキャストしてください

  ```csharp
  [SerializeField] private UdonSharpBehaviour target;   // Inspector で設定する
  IDamageable d = (IDamageable)target;
  ```

  public フィールドはエラーにしませんが、シリアライズされず、Inspector にも出ません（コードで代入して使う分には使えます）
- `GetComponent<IFoo>()` などの GetComponent 系（`GetComponents`・`GetComponentInChildren`・`GetComponentInParent` など、GameObject から呼ぶものも）。インタフェース型は `object` に書き換わるので、そのままだと実行時に例外で止まります。[普通のクラス](class.md)の型も同じです。次のように、具体的な UdonSharpBehaviour の型で取得するか、`UdonSharpBehaviour` で受けて `is`／`as` してください

  ```csharp
  Enemy enemy = GetComponent<Enemy>();           // 具体的な型で取得する
  IDamageable d = enemy;

  foreach (UdonSharpBehaviour b in GetComponents<UdonSharpBehaviour>())
  {
      IDamageable target = b as IDamageable;     // UdonSharpBehaviour で受けて as
      if (target != null)
          target.Damage(10, name);
  }
  ```
