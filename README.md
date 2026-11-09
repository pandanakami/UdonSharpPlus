# UdonSharp Plus

English | [日本語](README.ja.md)

An editor extension for VRChat world creators that lets you write more of C# in UdonSharp. It is an unofficial extension and is not affiliated with UdonSharp or VRChat.

It patches the UdonSharp compiler with Harmony and rewrites your source into a form UdonSharp can handle, right before UdonSharp compiles it. The output is a regular Udon program, so nothing extra is needed on the VRChat client.

> **Experimental.** This package depends on UdonSharp internals, so it may stop working when the VRChat SDK changes. On an unsupported SDK it disables itself, and UdonSharp behaves as usual.

## Features

| Feature | Examples |
|---|---|
| Interfaces | `class Enemy : UdonSharpBehaviour, IDamageable`, `((IDamageable)target).Damage(10)`, `is` / `as`, implementing `[NetworkCallable]` methods |
| Plain classes<br>(not UdonSharpBehaviour) | `new Bullet(pos)`, `b.Move(d)`, properties, static methods, arrays of classes |
| Generics | `class Pool<T>`, `static T Max<T>(List<T> list) where T : IComparable<T>` |
| List / Dictionary | `List<int>`, `Dictionary<string, Bullet>`, `foreach`, `TryGetValue` |
| Lambdas and LINQ | `list.Find(b => b.damage > 0)`, `list.Where(...).Select(...).ToList()`, `OrderBy`, `Sum` |

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

Anything a feature does not support is reported as a compile error. For exactly what you can write, see the [documentation](Packages/com.pandanakami.udonsharpplus/Documentation~/README.md) (in Japanese).

## How it works

![Diagram: the U# compiler parses the U# source code, a rewrite step that UdonSharp Plus inserts with Harmony rewrites it, and the compiler then generates Udon Assembly, which the Udon VM on the VRChat client runs](Packages/com.pandanakami.udonsharpplus/Documentation~/images/how-it-works.en.svg)

The UdonSharp (U#) compiler parses your C# source and generates Udon Assembly. UdonSharp Plus uses Harmony to insert a "rewrite" step (orange in the diagram) in between. That step rewrites interfaces, plain classes, List and so on into code U# can handle (`object`, `object[]`, `DataList`, and so on).

The rewrite happens only in the editor. What the Udon VM runs on the VRChat client is a regular Udon program. For details, see [How it works](Packages/com.pandanakami.udonsharpplus/Documentation~/how-it-works.md) (in Japanese).

## Requirements

- Unity 2022.3
- VRChat Worlds SDK 3.10.x (includes UdonSharp)

## Installation

1. Open the [listing page](https://pandanakami.github.io/UdonSharpPlus/) and click **Add to VCC**. VCC (VRChat Creator Companion) opens and asks you to add the repository.

   Or, in VCC, open **Settings** → **Packages** → **Add Repository** and add this URL:

   ```
   https://pandanakami.github.io/UdonSharpPlus/index.json
   ```

2. In your world project, open **Manage Project** and add **UdonSharp Plus**.

That's all. There is nothing to configure.

## Good to know

- All features are experimental. If something does not work as expected, the rewritten source in your project's `Library/UdonSharpPlus/` shows what your code was turned into.
- On an unsupported VRChat SDK, the console shows an error starting with `[UdonSharpPlus]` saying the package was disabled because UdonSharp internals changed. Update UdonSharp Plus when you see it.
- The detailed documentation and the package's own error and warning messages are in Japanese for now.

## Disclaimer

The author accepts no liability whatsoever for any damage resulting from the use of UdonSharp Plus. Use it at your own risk.

- UdonSharp Plus is experimental software provided as is, without warranty of any kind (MIT License). Damage includes, for example, broken projects or worlds, lost data, and publishing a world that does not work correctly.
- UdonSharp Plus is an unofficial extension made by an individual. It is not affiliated with or endorsed by VRChat Inc. or the authors and team of UdonSharp. VRChat, UdonSharp, Unity, Harmony, and other names and trademarks belong to their respective owners.
- UdonSharp Plus changes how the UdonSharp compiler works inside the Unity Editor. VRChat SDK updates, or other editor extensions that modify UdonSharp, may cause it to stop working or to produce incorrect programs. Back up your project before installing it, and always test your world before publishing.
- It makes no changes to the VRChat client. The output is a regular Udon program.
- Please report problems you run into while using UdonSharp Plus to this repository's Issues, not to VRChat or UdonSharp.

## License

[MIT License](LICENSE) (Copyright (c) 2026 pandanakami)

This package does not include the VRChat SDK, UdonSharp, Harmony, or Roslyn. It only references the copies that ship with the VRChat SDK; each is covered by its own license (VRChat SDK: https://hello.vrchat.com/legal/sdk).
