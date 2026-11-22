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

- **書ける場所**：UdonSharpBehaviour の、`async UniTask`・`async UniTaskVoid`・`async void` のインスタンスメソッド。`virtual`／`override`・ジェネリックメソッド・引数のあるメソッドも使えます。`{ }` の本体で書いてください（`=>` の式の本体は未対応です）
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
- **`return;`**、`if`・ループ・`switch`・`foreach` の中の `await`、`await` をまたぐローカル変数（[コルーチン](coroutines.md)と同じ）
- `CancellationToken` の引数は、`default`・`CancellationToken.None` のときだけ使えます（止められないので、何も変わりません）

## UniTask との違い

- **`UniTask.Yield()` は、次のフレームに再開します。** UniTask は、`Update` より前（`Start` など）で呼ぶと、同じフレームのうちに再開することがあります（`NextFrame()` は必ず次のフレーム）。ここでは、どちらも次のフレームです
- **`PlayerLoopTiming` は無視します。** 再開するのは、Udon の遅れのイベントが届くときです（コルーチンと同じ）
- **例外**：`await` のあとで例外が起きると、そのビヘイビアが止まります（Udon は例外でビヘイビアが止まります）。例外を投げる・`try` / `catch` で受けることはできません
- **止め方**：`async` のメソッドには、`StopCoroutine` に渡す値がありません。GameObject を非アクティブにすると、すべて止まります（コルーチンと同じ）。ビヘイビアを無効にしても止まりません
- **同時に動かせる数**：同じメソッドを、何度でも並行して始められます（コルーチンと同じ）。ただし、動いている途中の async メソッドが、自分自身を `await` なしで呼ぶ（`Foo();`）形は、コルーチンと同じく、コンパイルエラー（コンパイル時にたどれないものは、実行時に止めます）です。`await Foo();` の形なら呼べます

## 未対応の書き方（コンパイルエラー）

エラーは、`await`・メソッドの宣言の行に出ます。

- 値を返す `UniTask<T>`（結果はフィールドに入れてください）
- `UniTask.WhenAll`・`WhenAny`・`Run`・`SwitchToThreadPool`・`WaitUntilValueChanged`・`UniTaskCompletionSource` など、上の表にない `await`
- `UniTask` の値を、変数・フィールド・引数・戻り値に入れる書き方（`var t = Foo();`・`UniTask _t;`）
- `await` の値を使う書き方（`x = await ...`）、式の途中の `await`
- `try`・`catch`・`finally`・`using`・`lock` の中の `await`
- `CancellationToken`（`default`・`None` 以外）。止めるときは、GameObject を非アクティブにするか、フラグで抜けてください
- `static` の async メソッド、`Task`・`ValueTask` を返す async メソッド、ref・out・in の引数がある async メソッド
- ほかのビヘイビアの async メソッドを呼ぶ（`other.Foo();`）。そのビヘイビアに、async を始める public メソッドを作って呼んでください
- UdonSharpBehaviour でないクラスの async メソッド・async のラムダ・async のローカル関数

## 負荷

`async` の呼び出し・`await` の負荷は、同じことをコルーチンで書いたときと同じです（[負荷の目安](cost.md)の「コルーチン」）。

## 仕組み

書き換えの段（`AsyncLowering`）が、コルーチンの段より前に、`async` のメソッドを `IEnumerator` を返すメソッドにします。

```csharp
async UniTask Foo(int n) { A(); await UniTask.Delay(500); B(); }
// →
IEnumerator Foo(int n) { A(); yield return new WaitForSeconds((500) / 1000f); B(); yield break; }
Foo(3);   // → StartCoroutine(Foo(3));
```

行は増やしません。コルーチンの状態機械・デバッグ情報（実行時例外の位置）も、コルーチンと同じです。
