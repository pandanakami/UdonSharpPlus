# Custom UdonSharp

[English](README.md) | 日本語

UdonSharp で書ける C# を増やす、VRChat ワールド制作向けのエディタ拡張です。

UdonSharp のコンパイラに Harmony でパッチを当て、UdonSharp がコンパイルする前に、ソースを UdonSharp が扱える形に書き換えます。出力されるのは通常の Udon プログラムなので、VRChat のクライアント側には何も要りません。

> **実験段階です。** UdonSharp の内部 API に依存しているため、VRChat SDK のバージョンが変わると動かなくなる可能性があります。対応していない SDK では自動で無効になり、素の UdonSharp と同じ動きになります。

## できること

| 機能 | 例 |
|---|---|
| インタフェース | `class Enemy : UdonSharpBehaviour, IDamageable`、`((IDamageable)target).Damage(10)`、`is`／`as`、`[NetworkCallable]` の実装 |
| 普通のクラス | `new Bullet(pos)`、`b.Move(d)`、プロパティ、static メソッド、クラスの配列 |
| ジェネリクス | `class Pool<T>`、`static T Max<T>(List<T> list) where T : IComparable<T>` |
| List / Dictionary | `List<int>`、`Dictionary<string, Bullet>`、`foreach`、`TryGetValue` |
| ラムダ・LINQ | `list.Find(b => b.damage > 0)`、`list.Where(...).Select(...).ToList()`、`OrderBy`、`Sum` |

```csharp
using System.Collections.Generic;
using System.Linq;
using UdonSharp;

public interface IDamageable
{
    int Damage(int amount);
}

public class Bullet
{
    public int damage = 10;
    public Bullet(int damage) { this.damage = damage; }
}

public class Attacker : UdonSharpBehaviour
{
    public UdonSharpBehaviour[] targets;
    private List<Bullet> _bullets = new List<Bullet>();

    private void Start()
    {
        _bullets.Add(new Bullet(5));
        _bullets.Add(new Bullet(20));
        int total = _bullets.Where(b => b.damage > 10).Sum(b => b.damage);

        foreach (UdonSharpBehaviour t in targets)
            if (t is IDamageable)
                ((IDamageable)t).Damage(total);
    }
}
```

どの機能も、対応していない書き方はコンパイルエラーになります。何がどこまで書けるかは、[ドキュメント](Packages/com.pandanakami.customudonsharp/Documentation~/README.md)を見てください。

## 必要なもの

- Unity 2022.3
- VRChat Worlds SDK 3.10.x（UdonSharp を含む）

## 導入方法

1. VCC（VRChat Creator Companion）の **Settings** → **Packages** → **Add Repository** で、次の URL を追加します。

   ```
   https://pandanakami.github.io/CustomUdonSharp/index.json
   ```

2. ワールドのプロジェクトの **Manage Project** で、**Custom UdonSharp** を追加します。

これだけで有効になります。設定は要りません。

## 知っておいてほしいこと

- どの機能も実験的です。思ったとおりに動かないときは、書き換え後のソース（プロジェクトの `Library/CustomUdonSharp/`）を見ると、何に書き換わったか分かります。
- 対応していない VRChat SDK では、コンソールに `[CustomUdonSharp] この VRChat SDK（…）では UdonSharp の内部が変わっているため、無効にしました` のエラーが出ます。そのときは Custom UdonSharp を更新してください。
- 詳しいドキュメントと、本パッケージが出すエラー・警告の文言は日本語です。

## ライセンス

[MIT License](LICENSE)（Copyright (c) 2026 pandanakami）

このパッケージには、VRChat SDK・UdonSharp・Harmony・Roslyn は含まれていません。これらは VRChat SDK に同梱されたものを参照しているだけで、それぞれのライセンスに従います（VRChat SDK：https://hello.vrchat.com/legal/sdk ）。
