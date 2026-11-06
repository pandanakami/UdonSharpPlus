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

未対応の書き方：

- インタフェースのプロパティとイベント
- `?.` での呼び出し
- ref/out/in/params 引数、ジェネリックメソッド
- 明示的インタフェース実装
- インタフェース型のシリアライズフィールド（Unity がシリアライズしないため。`UdonSharpBehaviour` 型で受けてキャストしてください）
- `GetComponent<IFoo>()`

## 仕組み

1. `CompilationContext.LoadSyntaxTreesAndCreateModules` にPostfixを当て、UdonSharp に渡す SyntaxTree を書き換えます。インタフェース型を UdonSharpBehaviour に置き換え、呼び出しを `SetProgramVariable`／`SendCustomEvent`／`GetProgramVariable` 経由にし、`is`／`as` を型IDで判定する形にします。
2. `CompilationContext.MethodExportLayout` のコンストラクタにPrefixを当て、インタフェース実装メソッドの Udon 上の名前をシグネチャ由来の固定名にします（例：`Damage__int__string`、引数は `__p0`、戻り値は `__ret`）。

書き換え後のソースは、デバッグ用に `Library/CustomUdonSharp/` に出力されます。
