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

#### イベント名の渡し方

引数のあるインタフェース実装メソッドは、Udon 上の名前が `Damage__int__string` のような固定名になります。`SendCustomNetworkEvent` などでイベント名を渡すときは、`nameof(...)` か文字列リテラルを使ってください。コンパイル時に固定名へ書き換えます。

```csharp
// インタフェース型から呼ぶときは UdonSharpBehaviour にキャストする
((UdonSharpBehaviour)receiver).SendCustomNetworkEvent(NetworkEventTarget.All, nameof(INetworkReceiver.Receive), 1, "a");
enemy.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(Enemy.Receive), 1, "a");
enemy.SendCustomNetworkEvent(NetworkEventTarget.All, "Receive", 1, "a");
```

- 変数に入れた文字列は書き換えられません。該当しそうな場合は警告を出します。
- 書き換え対象のメソッドを指す `nameof(...)` は、どこで使っても固定名になります（`string s = nameof(Enemy.Receive);` の `s` は `"Receive__int__string"`）。
- `[NetworkCallable]` には UdonSharp の制約（戻り値なし、オーバーロード不可など）がそのまま掛かります。オーバーロードは元の名前で判定されるため、固定名が重ならなくても、同じクラスに `Receive(int)` を足すとエラーになります。

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
- static メソッド、拡張メソッド、static クラス、const
- クラスの配列、`foreach`
- `is`、`as`、キャスト、`nameof`
- 再帰（`[RecursiveMethod]` を付けたメソッド）

未対応の書き方（そのクラスを UdonSharpBehaviour から使うとコンパイルエラーになります）：

- static フィールド（UdonSharp と同じく非対応）
- 継承、abstract クラス、ジェネリッククラス・メソッド、入れ子のクラス、struct
- 演算子のオーバーロード、インデクサー、イベント、`ToString` などの override
- オブジェクト初期化子（`new Bullet { damage = 1 }`）、`?.`、型パターン（`x is Bullet b`）、`typeof(Bullet)`
- 汎用クラス型のフィールドのシリアライズと同期（`[SerializeField]`、`[UdonSynced]`）。public フィールドもシリアライズされません
- フィールド・プロパティへの代入の値を式として使うこと（`x = b.damage = 1`）。複合代入とインクリメントの受け側に、メソッド呼び出しを書くこと（`Get().damage += 1`）

注意：

- 値型のフィールドのメソッドで、そのフィールド自体を書き換えるもの（`b.pos.Normalize()` など）は、コピーに対して呼ばれるため反映されません。`b.pos = b.pos.normalized;` のように代入してください
- 汎用クラスのメソッドの中で Udon が対応していない API を使うと、エラーの位置が、そのクラスを使っている UdonSharpBehaviour の閉じ括弧の行になります
- UdonSharpBehaviour から使われていないクラスは書き換えず、検査もしません

## 仕組み

1. `CompilationContext.LoadSyntaxTreesAndCreateModules` にPostfixを当て、UdonSharp に渡す SyntaxTree を書き換えます。インタフェース型を UdonSharpBehaviour に置き換え、呼び出しを `SetProgramVariable`／`SendCustomEvent`／`GetProgramVariable` 経由にし、`is`／`as` を型IDで判定する形にします。
2. `CompilationContext.MethodExportLayout` のコンストラクタにPrefixを当て、インタフェース実装メソッドの Udon 上の名前をシグネチャ由来の固定名にします（例：`Damage__int__string`、引数は `__p0`、戻り値は `__ret`）。

書き換え後のソースは、デバッグ用に `Library/CustomUdonSharp/` に出力されます。

## ライセンス

[MIT License](LICENSE.md)（Copyright (c) 2026 pandanakami）

このパッケージには、VRChat SDK・UdonSharp・Harmony・Roslyn は含まれていません。これらは VRChat SDK に同梱されたものを参照しているだけで、それぞれのライセンスに従います（VRChat SDK：https://hello.vrchat.com/legal/sdk ）。
