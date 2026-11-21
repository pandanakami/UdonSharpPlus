# コルーチン（実験的）

UdonSharpBehaviour で、`IEnumerator` を返すメソッドに `yield return` を書き、`StartCoroutine` で動かせます（UdonSharp 自体は対応していません）。動く順番・再開するフレームは、普通の Unity のコルーチンに合わせています（ビヘイビアをまたいだ再開の順番だけは違うことがあります。下の「注意」）。

```csharp
public class Door : UdonSharpBehaviour
{
    public Transform panel;
    private Coroutine _moving;
    private bool _locked;

    public override void Interact()
    {
        if (_moving != null)
            StopCoroutine(_moving);
        _moving = StartCoroutine(Open(2f));
    }

    private IEnumerator Open(float seconds)
    {
        yield return new WaitUntil(() => !_locked);
        float t = 0f;
        while (t < seconds)
        {
            t += Time.deltaTime;
            panel.localPosition = Vector3.up * (t / seconds);
            yield return null;
        }
        yield return new WaitForSeconds(5f);
        yield return StartCoroutine(Close());
        _moving = null;
    }

    private IEnumerator Close() { /* ... */ yield break; }
}
```

対応している書き方：

- **書ける場所**：UdonSharpBehaviour の、`IEnumerator` を返すインスタンスメソッド。基底クラスに書いたもの・`virtual`／`override`・ジェネリックメソッドも使えます
- **待ち方**（`yield return` の値）：
  - `null`：次のフレーム
  - `new WaitForSeconds(t)`：`t` 秒（`Time.time`）。`new WaitForSecondsRealtime(t)`：`t` 秒（`Time.realtimeSinceStartup`）
  - `new WaitUntil(() => 条件)`・`new WaitWhile(() => 条件)`：条件は、式のラムダかメソッドの名前（`new WaitUntil(IsReady)`）で書きます
  - 子のコルーチン：`yield return StartCoroutine(Child())`、`yield return Child()`、開始済みの `Coroutine` の値。子が終わると、同じフレームのうちに親が再開します
  - 数・`bool`・文字列（Unity と同じく、次のフレーム）
  - `yield break`
- `WaitForSeconds`・`WaitForSecondsRealtime` を変数・フィールドに作って使い回す形（`private WaitForSeconds _wait = new WaitForSeconds(0.5f);`）
- **開始・停止**：`StartCoroutine(Foo(a, b))`、戻り値の `Coroutine` を変数・フィールドに入れて `StopCoroutine(c)`、`IEnumerator e = Foo();` で作っておいて `StartCoroutine(e)`・`StopCoroutine(e)`、`StopAllCoroutines()`、`Coroutine` と `null` の比較。同じコルーチンを、終わる前に何本でも別々に開始できます
- **yield をまたげる構文**：`if`／`else`、`for`・`while`・`do`（`break`・`continue` も）、`foreach`（List・Dictionary・配列・文字列）、`switch` 文
- **yield をまたぐ値**：ローカル変数・引数・`out var` の変数・ラムダが捕まえた変数、匿名型・タプル・普通のクラス・List・LINQ の結果など
- `Coroutine`・`IEnumerator` の値を、配列・List・Dictionary・普通のクラスのフィールドに入れる（`Contains`・`IndexOf`・`Remove` も使えます）
- `Coroutine`・`IEnumerator` の `??`・`??=`（`_c = _c ?? StartCoroutine(Foo());`、`_e ??= Foo();`）、待ち方の三項演算子（`yield return fast ? null : new WaitForSeconds(1f);`）
- コルーチンを返すデリゲート（`Func<IEnumerator> f = Foo; StartCoroutine(f());`）、コルーチンの値を返すだけのメソッド（`IEnumerator Make() => Foo(3);`）
- 引数の既定値・名前付きの引数・`params`、オーバーロード、`[RecursiveMethod]`、`partial` クラスの別のファイルに書いたコルーチン
- **ビヘイビアと GameObject の状態**（Unity と同じ）：
  - ビヘイビアを無効にしても（`enabled = false`）、止まりません
  - GameObject を非アクティブにすると（親を非アクティブにしたときも）、すべて止まります。アクティブに戻しても再開しません
  - 非アクティブの GameObject では開始せず、`null` を返して、Unity と同じエラーのログを出します（`OnDisable` の中の `StartCoroutine` も同じです）
  - `Destroy` すると止まります
- **タイミング**（Unity と同じ）：最初の `yield return` までは `StartCoroutine` の中で動きます。同じビヘイビアの中で、同じフレームに再開するものは、待ち始めた順に再開します（ビヘイビアをまたいだ順は、下の「注意」）。どの待ち方も、最低 1 フレーム待ちます

未対応の書き方（コンパイルエラーになります）：

| 書き方 | 代わりの書き方 |
|---|---|
| `yield return new WaitForEndOfFrame()` | 次のフレームでよければ `yield return null`。そのフレームの最後に動かすなら `PostLateUpdate()` に書いてください（Udon の遅れのイベントでは、描画のあとに再開できないため） |
| `yield return new WaitForFixedUpdate()` | 物理に合わせる処理は `FixedUpdate()` に書いてください |
| ほかの値の `yield return`（`AsyncOperation`・`CustomYieldInstruction` など） | 上の待ち方にしてください |
| `WaitUntil`・`WaitWhile` を変数に入れる、条件にブロックのラムダ（`() => { ... }`）を書く | `yield return new WaitUntil(() => 条件)` の形で書いてください |
| 文字列で指定する `StartCoroutine("Foo")`・`StopCoroutine("Foo")` | `StartCoroutine(Foo())` のように、コルーチンの値で書いてください |
| ほかのビヘイビアのコルーチン（`other.StartCoroutine(...)`、`StartCoroutine(other.Foo())`） | そのビヘイビアに、コルーチンを開始する public メソッドを作って呼んでください |
| `try`／`catch`／`finally`・`using`・`lock` の中の `yield return` | `yield return` を外に出してください |
| `yield return` を含む `foreach` で、List・Dictionary・配列・文字列以外を回す（`foreach (Transform t in transform)`） | 配列などに入れてから回してください |
| `yield return` を含む `switch` の、変数を宣言する `case` | 変数の宣言を `switch` の外に出してください |
| `yield return` を含む `switch` の、`when` の付いた `case` | `case` の中で `if` を使ってください（UdonSharp が `when` を正しく扱えないため） |
| `Coroutine`・`IEnumerator` の `??`・`??=` の左に、メソッドの呼び出しなどを書く（`_cs[Next()] ??= ...`） | 変数に入れてから使ってください（左を 2 回読むため） |
| `IEnumerator<T>`・`IEnumerable`・`IEnumerable<T>` を返す `yield`（列挙のための `yield`） | List などに入れて返してください（`IEnumerator` はコルーチンにだけ使えます） |
| `IEnumerator` の `MoveNext`・`Current`・`Reset`、外部のメソッドが返す `IEnumerator`（`transform.GetEnumerator()` など） | `IEnumerator` はコルーチンにだけ使えます |
| static メソッド・UdonSharpBehaviour でないクラスのメソッド・ローカル関数・インタフェースのメソッド・プロパティのコルーチン | UdonSharpBehaviour のインスタンスメソッドにしてください |
| 動いている途中のコルーチンに、`yield return` の外の `StartCoroutine` でまた入る（下の「同じコルーチンへの再入」） | `yield return StartCoroutine(Foo())` の形で書いてください |

## 同じコルーチンへの再入

UdonSharp では、メソッドのローカル変数は、メソッドごとに 1 組だけです（`[RecursiveMethod]` を付けたものを除く）。そのため、コルーチンが動いている途中に、同じコルーチンが `StartCoroutine` で開始されると（開始すると、その場で最初の `yield return` まで動くため）、外側の引数・ローカル変数が上書きされます。

```csharp
private IEnumerator Foo(int n)
{
    yield return null;
    StartCoroutine(Foo(n + 1));   // エラー：Foo が動いている途中に Foo に入る（Foo → Foo）
    Debug.Log(n);                 // 上書きされた n を読んでしまう
}
```

- **コンパイル時**：同じクラスの中の呼び出し（`StartCoroutine(X())` の `X`、ふつうのメソッド、`virtual` なら `override` の候補も）をたどり、`yield return` を通らずに同じコルーチンの開始へ戻れたら、そこをエラーにします。エラーの文には、たどった道（`Foo → Bar → Foo`）が付きます
- **実行時**：デリゲート・`SendCustomEvent`・ほかのビヘイビアを通るものは、コンパイル時にはたどれません。実行時に見つけたら、内側の開始をしないで止め、`[UdonSharpPlus] Foo のコルーチンが動いている途中に、同じコルーチンを StartCoroutine で開始したため、止めました` のエラーをログに出します（Unity では内側も動きます）
- `yield return StartCoroutine(Foo(n + 1))`・`yield return Foo(n + 1)` は、親の変数を状態に書いてから子を開始し、戻ったらすぐ戻るので安全です。再帰のコルーチンは、この形で書けます

## 注意

- **ビヘイビアをまたいだ、再開の順番**：次のフレームを待つもの（`yield return null`・`WaitUntil`・`WaitWhile` など）は、遅れのイベントの確保を減らすため、ビヘイビアごとにまとめて再開します。同じビヘイビアの中の順は Unity と同じですが、ビヘイビアをまたぐと、Unity と順が違うことがあります。例えば、同じフレームに A の a1 → B の b1 → A の a2 の順で待ち始めると、Unity は a1 → b1 → a2、こちらは a1 → a2 → b1 です（ビヘイビアどうしは、前のフレームで、そのビヘイビアが最初に待ち始めた順）。Unity も、ビヘイビアをまたいだ順はドキュメントで約束していません
- **同じフレームの中の、再開の順番**：何フレーム目に再開するかは Unity と同じですが、同じフレームに、秒の待ちと `yield return null` の両方が再開するときは、順番が違うことがあります。Unity は、秒の待ちも `yield return null` も、期限の順に 1 つの列に並べるためです
  - `WaitForSeconds(0)`：`yield return null` と同じ順です（Unity とも同じ）
  - 負の値（`WaitForSeconds(-1)`。残りの時間を計算して待つときに、なりえます）：Unity では、そのフレームの先頭で再開します。こちらは `yield return null` と同じ順です
  - 1 フレームより短い正の値（`WaitForSeconds(0.000001f)`）：Unity では `yield return null` の後ろで再開します。こちらは前です
- **待ち続けるもの**（Unity と同じ）：`WaitForSeconds` の NaN（Unity と同じ警告を出します）・正の無限大、`WaitForSecondsRealtime` の正の無限大、自分自身の `Coroutine` を待つ、互いに待つ、ほかのコルーチンがもう待っているものを待つ（Unity と同じエラーを出します）は、二度と再開しません。止めるまで、状態を持ったままです。なお Unity では、NaN と負の値の `WaitForSeconds` を同じプレイで使うと、すべてのコルーチンが止まる不具合がありますが、それはまねていません
- **作ったまま開始しない `IEnumerator`**：`IEnumerator e = Foo();` で作った状態は、開始するか止めるまで表に残ります（どこからも使われなくなったかは分からないため）。`Update` などで作り続けると、表が増え続けます。なお、`StopCoroutine(Foo())`（作ったばかりのものを止める形。Unity でも何も起きません）は、状態を残しません
- **止めたものを待つ**：止めた `Coroutine` を `yield return` すると、Unity と同じく、その場で続きが動きます（終わったものなら次のフレーム）。ただし、同じ状態（下の「仕組み」）を 2 回以上止めたときは、古いほうの `Coroutine` は終わったものとして扱います（1 フレーム遅れます）
- **`Coroutine`・`IEnumerator` の値**は、そのビヘイビアの中の番号（`int`）です。ほかのビヘイビアに渡しても、意味はありません。そのフィールドは、Unity と同じくシリアライズしません（public でも Inspector に出ず、配列は `null` で始まります）。`[SerializeField]`・`[UdonSynced]` を付けるとコンパイルエラーです。`object` として扱うと番号として動くので、`ReferenceEquals(c, null)`・`Equals(c, null)` は、`c` が null でも false になり、`Debug.Log(c)`・`ToString()` は番号を出します（`c == null` を使ってください）
- **生成するイベント**：再開に使う `__co_frame`・`__co_sec` は、遅れのイベントで呼ぶため public です。`SendCustomEvent` で外から呼ばないでください（`__co_frame` は、待ち始めたフレームのうちは何もしませんが、`__co_sec` は、期限の前に再開することがあります）
- **`OnDisable`**：GameObject を非アクティブにしたときに止めるため、コルーチンを使うビヘイビアに `OnDisable` を生成します。自分で `OnDisable` を書いていれば、その先頭で止めます。継承でつながったクラスでは、書き換え後のコードの中で `OnDisable` を `public override`（または `public virtual`）にすることがあります（元のソースは変えません）
- **型パターン**（`x is Bullet b`）は、UdonSharp も UdonSharp Plus も対応していないので、コルーチンの中でも使えません
- **実行時例外の位置**：`yield return` のあと・ループの中・子のコルーチン・`WaitUntil` の条件の中で出た実行時例外も、元のソースの位置に出ます
- `__co_` で始まる名前は、生成するメンバーに使います。自分のメンバーに使わないでください

## 仕組み

- コルーチンのメソッドは、再開する位置で分けた状態機械（`while (true) { switch (__pc) { case 0: ... } }`）のメソッドになります。UdonSharp は `goto` に対応していないので、`yield return` を含むループは、状態の行き来にします。`yield return` を含まない部分は、そのまま残ります
- 開始ごとの状態は `object[]` です（見出しの `int[]`・引数・`yield return` をまたぐローカル変数）。`Coroutine`・`IEnumerator` の値は、状態の番号（`int`）になります。終わった・止めた状態は表に残し、同じメソッドの次の開始で使い回します。古い番号は、世代を進めて見つからなくします
- 待つ処理は、遅れのイベントにします：`yield return null` は `SendCustomEventDelayedFrames`（ビヘイビアごとに 1 フレーム 1 つ。届いたら、前のフレームまでに待ち始めたものを全部再開します）、`WaitForSeconds` は `SendCustomEventDelayedSeconds`。`WaitUntil`・`WaitWhile`・`WaitForSecondsRealtime` は、条件を調べて `yield return null` をくり返すループになります
- 行は増やしません（メソッドの頭はその行に、`yield return` はその行に置き換え、生成したメンバーはクラスの閉じ括弧の行に置きます）。書き換え後のソースは、`Library/UdonSharpPlus/` で見られます
