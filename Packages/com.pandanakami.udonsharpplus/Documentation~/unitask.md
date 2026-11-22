# UniTask の async / await（実験的）

UdonSharpBehaviour で、UniTask の `async` / `await` が書けます（UdonSharp 自体は対応していません）。書き換えでは、`async` のメソッドを[コルーチン](coroutines.md)にするので、待つ処理・止まる条件・動くフレームは、コルーチンと同じです。

```csharp
using Cysharp.Threading.Tasks;
using UdonSharp;
using UnityEngine;

public class Door : UdonSharpBehaviour
{
    public Transform panel;

    public override async void Interact()
    {
        await Open(2f);
        await UniTask.Delay(5000);      // 5 秒
        await Close();
    }

    private async UniTask Open(float seconds)
    {
        await UniTask.WaitUntil(() => !_locked);
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            panel.localPosition = Vector3.up * (t / seconds);
            await UniTask.NextFrame();
        }
    }

    private async UniTask Close() { /* ... */ await UniTask.Yield(); }

    private bool _locked;
}
```

## 使えるもの

UniTask は、VRChat SDK に同梱されています（`com.vrchat.base` の `Plugins/UniTask`。2.5.x）。UdonSharp Plus は UniTask のアセンブリに依存せず、型の名前（`Cysharp.Threading.Tasks.UniTask` など）で見分けます。UniTask を使わないスクリプトは、何も変わりません。

- **書ける場所**：UdonSharpBehaviour の、`async UniTask`・`async UniTaskVoid`・`async void`・値を返す `async UniTask<T>` のインスタンスメソッド。`virtual`／`override`・ジェネリックメソッド・引数のあるメソッドも使えます。`{ }` の本体で書いてください（`=>` の式の本体は未対応です）
- **イベント**：`Start`・`Update`・`OnEnable` などの Unity のメッセージや、`override` したイベント（`Interact`・`OnPlayerJoined` など）も `async` にできます。Udon が名前で呼ぶので、同じ名前の `void` のメソッドが、コルーチンを始めます
- **`await` の書き方**（文として書きます）：

  | 書き方 | 待ち方 |
  |---|---|
  | `await UniTask.Yield();`・`await UniTask.NextFrame();` | 次のフレーム |
  | `await UniTask.Delay(ms);`・`await UniTask.Delay(TimeSpan.FromSeconds(x));` | 指定の時間（`Time.time`）。`TimeSpan` は、`FromSeconds`・`FromMilliseconds`・`FromMinutes` の形だけ |
  | `await UniTask.Delay(ms, ignoreTimeScale: true);`・`DelayType.UnscaledDeltaTime`／`Realtime` | `Time.timeScale` に左右されない時間（`Time.realtimeSinceStartup`） |
  | `await UniTask.DelayFrame(n);` | `n` フレーム（`0` は `Yield` と同じで 1 フレーム） |
  | `await UniTask.WaitUntil(() => 条件);`・`WaitWhile` | 条件が成り立つ（外れる）まで。条件は、式のラムダかメソッドの名前 |
  | `await Foo();`（`Foo` も async） | 子の `Foo` が終わるまで。終わると、同じフレームのうちに再開します |

- **待たずに呼ぶ**：`Foo();`・`Foo().Forget();`（並行して動きます。最初の `await` までは、呼んだ場所で動きます）
- **値を返す async**（`UniTask<T>`）：`async UniTask<int> Add(int a, int b) { await UniTask.Yield(); return a + b; }` を、次の形で受け取れます。`T` は、数・文字列・クラスなど、ふつうの値です
  - `var x = await Add(1, 2);`・`int x = await Add(1, 2);`
  - `x = await Add(1, 2);`（左は、変数・フィールド・要素）
  - `return await Add(1, 2);`（値を返す async の中）
  - 値を使わない `await Add(1, 2);`・`Add(1, 2);`・`Add(1, 2).Forget();`
  - 式の途中の `await`（`n = 1 + await Add(1, 2);`・`if (await Check())`）は、前のものの評価の順番が変わるので、未対応です。`var` に受けてから使ってください
- **`UniTask.WhenAll`**（`async` メソッドの呼び出しを並べる形）：全部を始めて、全部が終わるまで待ちます。値を返すものは、`var (a, b) = await UniTask.WhenAll(A(), B());`（タプル）で受け取れます（`UniTask` の本物と同じ。引数が 2〜15 個のとき）
- **`return;`**、`if`・ループ・`switch`・`foreach` の中の `await`、`await` をまたぐローカル変数（[コルーチン](coroutines.md)と同じ）
- `CancellationToken` の引数は、`default`・`CancellationToken.None` のときだけ使えます（止められないので、何も変わりません）

## UniTask との違い

- **`UniTask.Yield()` は、次のフレームに再開します。** UniTask は、`Update` より前（`Start` など）で呼ぶと、同じフレームのうちに再開することがあります（`NextFrame()` は必ず次のフレーム）。ここでは、どちらも次のフレームです
- **`PlayerLoopTiming` は無視します。** 再開するのは、Udon の遅れのイベントが届くときです（コルーチンと同じ）
- **`WhenAll`**：子を全部始めてから、順に待ちます。同期的に終わる（`await` がない）子を待つと、次のフレームに再開します（コルーチンの「終わったものを待つ」と同じ）。UniTask は、その場で続きます
- **例外**：`await` のあとで例外が起きると、そのビヘイビアが止まります（Udon は例外でビヘイビアが止まります）。例外を投げる・`try` / `catch` で受けることはできません
- **止め方**：`async` のメソッドには、`StopCoroutine` に渡す値がありません。GameObject を非アクティブにすると、すべて止まります（コルーチンと同じ）。ビヘイビアを無効にしても止まりません
- **同時に動かせる数**：同じメソッドを、何度でも並行して始められます（コルーチンと同じ）。ただし、動いている途中の async メソッドが、自分自身を `await` なしで呼ぶ（`Foo();`）形は、コルーチンと同じく、コンパイルエラー（コンパイル時にたどれないものは、実行時に止めます）です。`await Foo();` の形なら呼べます

## 未対応の書き方（コンパイルエラー）

エラーは、`await`・メソッドの宣言の行に出ます。

- `UniTask.WhenAny`、変数・配列・List に入れた `UniTask` を渡す `WhenAll`、`Run`・`SwitchToThreadPool`・`WaitUntilValueChanged`・`UniTaskCompletionSource` など、上の表にない `await`
- `UniTask` の値を、変数・フィールド・引数・戻り値に入れる書き方（`var t = Foo();`・`UniTask _t;`）
- `await` の値を使う書き方（`x = await ...`）、式の途中の `await`
- `try`・`catch`・`finally`・`using`・`lock` の中の `await`
- `CancellationToken`（`default`・`None` 以外）。止めるときは、GameObject を非アクティブにするか、フラグで抜けてください
- `static` の async メソッド、`Task`・`ValueTask` を返す async メソッド、ref・out・in の引数がある async メソッド
- ほかのビヘイビアの async メソッドを呼ぶ（`other.Foo();`）。そのビヘイビアに、async を始める public メソッドを作って呼んでください
- UdonSharpBehaviour でないクラスの async メソッド・async のラムダ・async のローカル関数

## 負荷

`async` の呼び出し・`await` の負荷は、同じことをコルーチンで書いたときと同じです（[負荷の目安](cost.md)の「コルーチン」）。値を返す async（`UniTask<T>`）は、呼ぶたびに戻り値の置き場の `object[]`（要素 1 つ）を 1 つ作り、値の型（`int` など）の戻り値は、ボックス化で 1 回確保します。

## 仕組み

書き換えの段（`AsyncLowering`）が、コルーチンの段より前に、`async` のメソッドを `IEnumerator` を返すメソッドにします。

```csharp
async UniTask Foo(int n) { A(); await UniTask.Delay(500); B(); }
// →
IEnumerator Foo(int n) { A(); yield return new WaitForSeconds((500) / 1000f); B(); yield break; }
Foo(3);   // → StartCoroutine(Foo(3));
```

値を返す async は、戻り値の置き場を、呼ぶ側が開始ごとに作って、最後の引数で渡します（コルーチンの状態は、終わると使い回されるので、状態の中には置きません。並行して同じメソッドを動かしても、値は混ざりません）。

```csharp
async UniTask<int> Add(int a, int b) { await UniTask.Yield(); return a + b; }
var x = await Add(1, 2);
// →
IEnumerator Add(int a, int b, object[] __ar) { yield return null; { __ar[0] = (object)(a + b); yield break; } }
object[] __ar0 = new object[1]; yield return Add(1, 2, __ar: __ar0); var x = ((int)__ar0[0]);
```

行は増やしません。コルーチンの状態機械・デバッグ情報（実行時例外の位置）も、コルーチンと同じです。
