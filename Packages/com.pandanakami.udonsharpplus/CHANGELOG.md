# Changelog

Changes in each version of UdonSharp Plus. Each section is used as the body of the GitHub release.
UdonSharp Plus の各バージョンの変更点です。各節は GitHub のリリースの本文になります。

## [0.1.7]

### English

New

- UniTask (`async` / `await`) can be used in an `UdonSharpBehaviour`. UdonSharp itself does not support it. `async UniTask`, `async UniTaskVoid` and `async void` methods are rewritten to coroutines, so waiting, stopping and the frame it resumes in are the same as coroutines. You can `await UniTask.Yield()`, `NextFrame()`, `Delay(...)` (milliseconds or `TimeSpan.FromSeconds(x)`, with `ignoreTimeScale` / `DelayType`), `DelayFrame(n)`, `WaitUntil(...)`, `WaitWhile(...)`, and a call to another async method (it waits for the child). `Foo();` and `Foo().Forget();` start it in parallel. `Start`, `Update` and `override` events (`Interact`, `OnPlayerJoined`, ...) can be `async`. `UniTask<T>`, `WhenAll` / `WhenAny`, `CancellationToken` (other than `default`), keeping a `UniTask` in a variable, and `await` inside `try` are compile errors. UniTask comes with the VRChat SDK; UdonSharp Plus does not depend on it, so scripts that do not use UniTask are not affected. See Documentation~/unitask.md.

### 日本語

新しく使えるもの

- UniTask の `async` / `await` を、`UdonSharpBehaviour` の中で使えるようになりました（UdonSharp 自体は対応していません）。`async UniTask`・`async UniTaskVoid`・`async void` のメソッドをコルーチンに書き換えるので、待ち方・止まる条件・再開するフレームはコルーチンと同じです。`await` できるのは、`UniTask.Yield()`・`NextFrame()`・`Delay(...)`（ミリ秒か `TimeSpan.FromSeconds(x)`。`ignoreTimeScale`・`DelayType` も使えます）・`DelayFrame(n)`・`WaitUntil(...)`・`WaitWhile(...)`・ほかの async メソッドの呼び出し（子を待ちます）です。`Foo();`・`Foo().Forget();` で並行して始められます。`Start`・`Update`・`override` したイベント（`Interact`・`OnPlayerJoined` など）も `async` にできます。`UniTask<T>`・`WhenAll` / `WhenAny`・`CancellationToken`（`default` 以外）・変数に入れた `UniTask`・`try` の中の `await` は、コンパイルエラーです。UniTask は VRChat SDK に同梱されています。UdonSharp Plus は UniTask に依存しないので、UniTask を使わないスクリプトは何も変わりません。詳しくは Documentation~/unitask.md を見てください。

## [0.1.6]

### English

New

- Switch expressions (`var s = n switch { < 0 => "neg", 0 => "zero", _ => "pos" };`) can be used. UdonSharp itself does not support them. The patterns are the same as for `switch` statements (constants, `<` `>` ..., `and` / `or` / `not`, `var`, `when`). When no arm matches, an error is logged and the behaviour stops (C# throws an exception). When the tested value is not a variable, parameter, field or constant, the switch expression must be the whole value of a `return`, a variable declaration or an assignment, or the body of a `=>` method or property.
- Coroutines. In an `UdonSharpBehaviour`, you can write `yield return` in methods returning `IEnumerator` and run them with `StartCoroutine`. UdonSharp itself does not support them. Supported waits are `null`, `WaitForSeconds`, `WaitForSecondsRealtime`, `WaitUntil` / `WaitWhile`, and child coroutines (`yield return StartCoroutine(Child())`). You can stop them with `StopCoroutine` and `StopAllCoroutines`, and `yield return` works inside `if`, loops, `foreach` and `switch`. The frame in which coroutines resume follows Unity, including when the GameObject is deactivated, and so does the order within one behaviour. To reduce allocations, coroutines waiting for the next frame are resumed together per behaviour, so the order across behaviours can differ from Unity. `StartCoroutine("Foo")` / `StartCoroutine("Foo", value)` / `StopCoroutine("Foo")` with a constant name also work, and local functions can be coroutines. `WaitForEndOfFrame`, `WaitForFixedUpdate`, coroutines of other behaviours, and starting a coroutine again while it is running (outside `yield return`) are compile errors. See Documentation~/coroutines.md.

Fixes

- A `switch` statement with pattern labels (`case "b" when n > 0:`, `case > 5:`, `case 1 or 2:`, `case var x:`, ...) now works as in C#. UdonSharp silently ignored these labels, so a case could be entered without checking its condition. Patterns work on numbers, char, bool, string and enum; other patterns are compile errors.
- A `using` alias of a `List` / `Dictionary` (`using IntList = List<int>;`) used as a type in an `UdonSharpBehaviour` (`IntList xs = new IntList();`, fields, parameters, return values) no longer breaks the compile.
- Calling a delegate allocates less memory: about 2 allocations per call instead of 17 (the arguments and the return value), so it puts less pressure on the garbage collector when called every frame. A delegate made inside a plain class no longer allocates when called (3 allocations before), and a delegate combined with `+=` takes about 5 allocations per call however many methods it holds (about 12 per method before).
- `List.Sort` with a lambda (`list.Sort((a, b) => a - b)`) no longer allocates on every call inside an `UdonSharpBehaviour`: it reuses its work arrays (2 allocations per call before). Inside plain class methods it still allocates them on every call.
- `GroupBy` allocates less when the groups stay inside the LINQ chain (for example `list.GroupBy(x => x.team).Select(g => new { g.Key, N = g.Count() })`, or `.Count()`): the group containers are reused, so 3 groups take 3 allocations (boxing the keys) instead of 30. When a group leaves the chain (`ToList()`, `First()`, `foreach`, or stored or passed to a method inside a lambda), and inside plain class methods, the groups are still created on every call.
- U# compiles are faster. In 0.1.5, the rewriting by UdonSharp Plus took about 2.3 times as long as UdonSharp's own compile; it now takes about 1.5 times as long, including the coroutine and `switch` rewriting added in this version (in a test project with about 10,000 lines: about 32 seconds for the rewriting and about 21 seconds for UdonSharp). See the "コンパイルにかかる時間" (compile time) section in Documentation~/how-it-works.md.

### 日本語

新しく使えるもの

- `switch` 式（`var s = n switch { < 0 => "neg", 0 => "zero", _ => "pos" };`）が使えるようになりました。UdonSharp 自体は対応していません。パターンは `switch` 文と同じ範囲です（定数・`<` `>` など・`and` / `or` / `not`・`var`・`when`）。どの腕にも合わないときは、エラーのログを出して止まります（C# では例外）。調べる値が変数・引数・フィールド・定数でないときは、`return`・変数の宣言・代入の式全体か、`=>` のメソッド・プロパティの本体に書いてください。
- コルーチンに対応しました。`UdonSharpBehaviour` で、`IEnumerator` を返すメソッドに `yield return` を書き、`StartCoroutine` で動かせます。UdonSharp 自体は対応していません。待ち方は `null`・`WaitForSeconds`・`WaitForSecondsRealtime`・`WaitUntil`／`WaitWhile`・子のコルーチン（`yield return StartCoroutine(Child())`）に対応し、`StopCoroutine`・`StopAllCoroutines` で止められます。`if`・ループ・`foreach`・`switch` の中にも `yield return` を書けます。再開するフレームと、同じビヘイビアの中の順番は、GameObject を非アクティブにしたときも含めて、Unity に合わせています。確保を減らすため、次のフレームを待つものはビヘイビアごとにまとめて再開するので、ビヘイビアをまたいだ順番は Unity と違うことがあります。定数の名前で指定する `StartCoroutine("Foo")`・`StartCoroutine("Foo", value)`・`StopCoroutine("Foo")`、ローカル関数のコルーチンも使えます。`WaitForEndOfFrame`・`WaitForFixedUpdate`・ほかのビヘイビアのコルーチン・動いている途中のコルーチンに `yield return` の外でまた入ることは、コンパイルエラーです。詳しくは Documentation~/coroutines.md を見てください。

直したこと

- パターンのラベル（`case "b" when n > 0:`・`case > 5:`・`case 1 or 2:`・`case var x:` など）を含む `switch` 文が、C# と同じに動くようにしました。UdonSharp がこうしたラベルを黙って読み飛ばすため、条件を見ずに `case` に入ることがありました。数・文字・`bool`・文字列・enum の `switch` で使えます。それ以外のパターンはコンパイルエラーです。
- `List`／`Dictionary` の `using` の別名（`using IntList = List<int>;`）を、`UdonSharpBehaviour` の中で型に使うと（`IntList xs = new IntList();`・フィールド・引数・戻り値）、コンパイルが壊れていたのを直しました。
- デリゲートの呼び出しで確保するメモリを減らしました。1 回あたり 17 回だった確保が、約 2 回（引数と戻り値の受け渡しの分）になり、毎フレーム呼んでも GC の負担が小さくなります。普通のクラスの中で作ったデリゲートは、呼んでも確保しなくなりました（前は 3 回）。`+=` でつないだデリゲートは、つないだメソッドの数によらず、1 回の呼び出しで約 5 回になりました（前はメソッド 1 つにつき約 12 回）。
- ラムダを渡す `List.Sort`（`list.Sort((a, b) => a - b)`）が、`UdonSharpBehaviour` の中では呼ぶたびに確保しなくなりました。並べ替えの作業用の配列を使い回します（前は呼ぶたびに 2 回）。普通のクラスのメソッドの中では、前と同じく呼ぶたびに作ります。
- `GroupBy` で、グループが LINQ のつなぎの外に出ないとき（`list.GroupBy(x => x.team).Select(g => new { g.Key, N = g.Count() })` や `.Count()` など）の確保を減らしました。グループの入れ物を使い回すので、グループが 3 つなら 30 回だった確保が 3 回（キーのボックス化）になります。グループを外に出すとき（`ToList()`・`First()`・`foreach`、ラムダの中で変数に入れる・メソッドに渡す）と、普通のクラスのメソッドの中では、前と同じく呼ぶたびに作ります。
- U# のコンパイルを速くしました。UdonSharp Plus の書き換えにかかる時間は、0.1.5 では UdonSharp 自体のコンパイルの 約 2.3 倍でしたが、この版で足したコルーチン・`switch` の書き換えを含めて 約 1.5 倍になりました（約 1 万行のテスト用のプロジェクトで、書き換えが 約 32 秒、UdonSharp が 約 21 秒）。Documentation~/how-it-works.md の「コンパイルにかかる時間」も見てください。

## [0.1.5]

### English

New

- Anonymous types (`new { Name = "a", Score = 1 }`) can be used in `UdonSharpBehaviour` and plain classes. UdonSharp itself does not support them. You can read members, omit member names (`new { x.Name }`), nest them, return them from lambdas (`list.Select(x => new { x.Name, x.Hp })`), and keep them in arrays and `var` lists. `ToString()`, string concatenation and interpolation give the same text as C# (`{ Name = a, Score = 1 }`), and `Equals` compares by value. Lists of anonymous types work with the usual `List` / LINQ methods; `Contains` / `IndexOf` / `LastIndexOf` / `Remove` / `Distinct` compare by value, as in C#. Using an anonymous type as a `ToDictionary` key is a compile error (it would compare by reference). Using one as `object` (`Debug.Log(p)`), `GetHashCode()` and `GetType()` are compile errors. See Documentation~/anonymous-types.md.
- More LINQ. New methods: `SelectMany`, `Reverse`, `Concat`, `Append`, `Prepend`, `TakeWhile`, `SkipWhile`, `ElementAt` / `ElementAtOrDefault`, `Single` / `SingleOrDefault`, and `Aggregate`. Lambdas with an index (`Select((x, i) => ...)`, `Where((x, i) => ...)`) can be used. LINQ can start from `Enumerable.Range`, `Enumerable.Repeat`, and `Enumerable.Empty`. A `List` or a LINQ result can be passed directly to `string.Join` / `string.Concat` (`string.Join(",", list.Select(x => x.name))`). Anonymous methods (`delegate (int x) { ... }`) can be written directly as arguments of `List` / LINQ methods. Before, these were compile errors. See Documentation~/collections.md.

- Tuples (`(int, string)`, `(1, "a")`) can be used in `UdonSharpBehaviour` and plain classes. UdonSharp itself does not support them. You can read elements (`t.Item1`, `t.min`), deconstruct them (`var (x, y) = t;`, `(a, b) = (b, a);`, `foreach (var (k, v) in list)`), compare them with `==` / `Equals`, and turn them into strings (`(1, a)`, as in C#). They can be used for fields, parameters, return values, arrays, lists, and LINQ. Assigning to an element (`t.Item1 = 5`) is a compile error: tuples are value types in C#, but here they are references, so build a new tuple instead (`t = (5, t.Item2)`). Using a tuple as `object` (`Debug.Log(t)`) and tuple keys of a `Dictionary` are also compile errors. Before, all tuples were compile errors. See Documentation~/tuples.md.
- Operator overloading. You can declare operators (`operator +`, `==`, `++`, ...) and conversions (`implicit operator` / `explicit operator`) in plain classes and `UdonSharpBehaviour`, and use them in expressions, compound assignments (`a += b`), `++` / `--`, lambdas and LINQ. UdonSharp itself does not support them; declaring one in an `UdonSharpBehaviour` used to stop the compile with an internal error. `true` / `false` operators, `==` / `!=` on an `UdonSharpBehaviour`, and using the value of a postfix `x++` are compile errors. See Documentation~/operators.md.
- Compile-time notices. When your code uses something that compiles but behaves differently from C# (for example, keeping a LINQ result in a variable, or comparing an interface value with `null`), each U# compile writes an Info log with an ID (`USP001`, ...) and a link to the line. Each notice appears once per compile. You can turn notices on or off one by one in Project Settings > Udon Sharp Plus; four of them are on by default. Each notice in the settings page has a "?" link that opens its description on GitHub. See Documentation~/notices.md.
- `GroupBy` in LINQ: `GroupBy(x => key)` and `GroupBy(x => key, x => element)`. A group supports `g.Key`, LINQ on the group (`g.Count()`, `g.Sum(...)`, `g.Select(...)`), `foreach`, and passing it to `string.Join`. Groups and the elements in each group keep the order they first appear in, as in C#. Combined with anonymous types, you can write `list.GroupBy(x => x.team).Select(g => new { g.Key, N = g.Count() })`. Keys can be numbers, string, bool, char, enum, or class references; anonymous-type, tuple and struct keys are compile errors. See Documentation~/collections.md.
- Messages in English. Errors, warnings and notices from UdonSharp Plus, and the runtime logs in the rewritten code, are now in English by default. You can switch them to Japanese with Language in Project Settings > Udon Sharp Plus. The detailed documentation is still in Japanese.
- A guide to the cost of each feature (Documentation~/cost.md): how many `SendCustomEvent` / `GetProgramVariable` / `SetProgramVariable` calls and allocations each operation adds, which forms are already light (lambdas written directly in LINQ and `List` methods are inlined), and which forms are heavy (passing a `Func` variable to LINQ calls the delegate for every element).

Fixes

- A constructor or method parameter of a plain class named with a C# keyword (`@class`, `@event`) no longer breaks the compile.
- A plain class field whose type is written with `global::` (`public global::IFoo f;`) no longer stops the compile with an internal error.
- A plain class field with a short array initializer (`public int[] n = { 1, 2 };`) no longer breaks the compile.
- When a call to a plain class method spans several lines, an error or runtime exception in a later argument is now shown on that argument's line.
- `dict.TryGetValue(key, out var v);` written as a statement (without using its result) no longer breaks the compile, and `out _` can be used with it.
- Two interfaces with a method of the same name and parameters but a different return type (`int Size()` and `string Size()`) no longer break the compile.
- Compiling the same code twice now gives the same result. Before, the program assets (`.asset`) could change on every compile even when the code had not changed (generated names such as `__Anon_0`, and the field order of `partial` classes, depended on the order UdonSharp read the files in).
- U# compiles are faster: in a test project with about 10,000 lines, the rewriting by UdonSharp Plus went from about 68 to 51 seconds.

### 日本語

新しく使えるもの

- 匿名型（`new { Name = "a", Score = 1 }`）を、`UdonSharpBehaviour` と普通のクラスの中で使えるようになりました（UdonSharp 自体は対応していません）。メンバーを読む・メンバー名を省略する（`new { x.Name }`）・入れ子にする・ラムダの戻り値にする（`list.Select(x => new { x.Name, x.Hp })`）・配列や `var` で受けたリストに入れることができます。`ToString()`・文字列の連結・補間は C# と同じ文字列（`{ Name = a, Score = 1 }`）になり、`Equals` は値で比べます。匿名型のリストは、普通の `List` / LINQ のメソッドが使えます。`Contains`・`IndexOf`・`LastIndexOf`・`Remove`・`Distinct` は、C# と同じく値で比べます。匿名型を `ToDictionary` のキーにするのは、コンパイルエラーです（参照の比較になってしまうため）。`object` として使うこと（`Debug.Log(p)`）・`GetHashCode()`・`GetType()` は、コンパイルエラーです。詳しくは Documentation~/anonymous-types.md を見てください。
- LINQ で使えるものが増えました。メソッド：`SelectMany`・`Reverse`・`Concat`・`Append`・`Prepend`・`TakeWhile`・`SkipWhile`・`ElementAt`／`ElementAtOrDefault`・`Single`／`SingleOrDefault`・`Aggregate`。添字付きのラムダ（`Select((x, i) => ...)`・`Where((x, i) => ...)`）が使えます。`Enumerable.Range`・`Enumerable.Repeat`・`Enumerable.Empty` から LINQ を始められます。`List` や LINQ の結果を、`string.Join`／`string.Concat` にそのまま渡せます（`string.Join(",", list.Select(x => x.name))`）。匿名メソッド（`delegate (int x) { ... }`）を、`List`／LINQ のメソッドの引数に直接書けます。これまでは、どれもコンパイルエラーでした。詳しくは Documentation~/collections.md を見てください。

- タプル（`(int, string)`、`(1, "a")`）を、`UdonSharpBehaviour` と普通のクラスの中で使えるようになりました（UdonSharp 自体は対応していません）。要素を読む（`t.Item1`、`t.min`）・分解する（`var (x, y) = t;`、`(a, b) = (b, a);`、`foreach (var (k, v) in list)`）・`==`／`Equals` で比べる・文字列にする（C# と同じ `(1, a)`）ことができ、フィールド・引数・戻り値・配列・リスト・LINQ に使えます。要素への代入（`t.Item1 = 5`）はコンパイルエラーです（C# ではタプルは値型ですが、ここでは参照になるため。`t = (5, t.Item2)` のように作り直してください）。`object` として使うこと（`Debug.Log(t)`）・タプルを `Dictionary` のキーにすることも、コンパイルエラーです。これまでは、タプルはすべてコンパイルエラーでした。詳しくは Documentation~/tuples.md を見てください。
- 演算子のオーバーロードに対応しました。普通のクラスと `UdonSharpBehaviour` に、演算子（`operator +`・`==`・`++` など）と変換（`implicit operator`／`explicit operator`）を宣言して、式・複合代入（`a += b`）・`++`／`--`・ラムダ・LINQ の中で使えます。UdonSharp 自体は対応しておらず、`UdonSharpBehaviour` に宣言すると、内部のエラーでコンパイルが止まっていました。`true`／`false` 演算子・`UdonSharpBehaviour` の `==`／`!=`・後置の `x++` の値を使うことは、コンパイルエラーです。詳しくは Documentation~/operators.md を見てください。
- コンパイル時の注意を出すようにしました。コンパイルエラーにはならないが C# と動きが違うもの（LINQ の結果を変数に入れる、インタフェース型の値と `null` を比べる、など）を使っていると、U# のコンパイルのたびに、ID（`USP001` など）と、その行へのリンクの付いた Info のログを出します。同じ注意はコンパイルごとに 1 回です。Project Settings > Udon Sharp Plus で、注意ごとに出す・出さないを選べます（既定で出すのは 4 つ）。設定ページの各注意の「?」から、GitHub の説明を開けます。詳しくは Documentation~/notices.md を見てください。
- LINQ の `GroupBy` に対応しました（`GroupBy(x => キー)`・`GroupBy(x => キー, x => 要素)`）。グループの `g.Key`、グループを元にする LINQ（`g.Count()`・`g.Sum(...)`・`g.Select(...)`）、`foreach`、`string.Join` に渡すことが使えます。グループの順番とグループの中の順番は、C# と同じく最初に出た順です。匿名型と組み合わせて、`list.GroupBy(x => x.team).Select(g => new { g.Key, N = g.Count() })` のように集計できます。キーは、数値・string・bool・char・enum・クラスの参照が使えます。匿名型・タプル・構造体のキーはコンパイルエラーです。詳しくは Documentation~/collections.md を見てください。
- 文言を英語にしました。UdonSharp Plus が出すエラー・警告・注意と、書き換えたコードの中の実行時のログは、既定で英語になります。Project Settings > Udon Sharp Plus の「Language / 言語」で日本語に戻せます。詳しいドキュメントは日本語のままです。
- 機能ごとの負荷の目安（Documentation~/cost.md）を足しました。操作ごとに `SendCustomEvent`・`GetProgramVariable`・`SetProgramVariable` と動的なメモリ確保をいくつ使うか、軽くしている書き方（LINQ・`List` のメソッドに直接書いたラムダは、ループに埋め込みます）、重くなる書き方（`Func` の変数を LINQ に渡すと、要素ごとにデリゲートを呼びます）をまとめています。

直したこと

- 普通のクラスのコンストラクタ・メソッドの引数に、C# のキーワードと同じ名前（`@class`・`@event`）を使うと、コンパイルが壊れていたのを直しました。
- 普通のクラスのフィールドの型を `global::` つきで書くと（`public global::IFoo f;`）、内部のエラーでコンパイルが止まっていたのを直しました。
- 普通のクラスのフィールドに、配列の初期化子の省略形（`public int[] n = { 1, 2 };`）を書くと、コンパイルが壊れていたのを直しました。
- 普通のクラスのメソッドの呼び出しを複数行に分けて書いたとき、2 行目以降の引数の中のエラー・実行時例外が、その引数の行に出るようにしました。
- `dict.TryGetValue(key, out var v);` を、戻り値を使わない文として書くと、コンパイルが壊れていたのを直しました。`out _` も使えるようにしました。
- 別々のインタフェースに、名前と引数が同じで戻り値の型が違うメソッド（`int Size()` と `string Size()`）があると、コンパイルが壊れていたのを直しました。
- 同じコードを 2 回コンパイルすると、同じ結果になるようにしました。これまでは、コードを変えていなくても、コンパイルのたびにプログラムのアセット（`.asset`）が書き変わることがありました（生成する名前（`__Anon_0` など）と、`partial` のクラスのフィールドの並びが、UdonSharp がファイルを読んだ順で変わっていたため）。
- U# のコンパイルを速くしました。約 1 万行のテスト用のプロジェクトで、UdonSharp Plus の書き換えが 約 68 秒から 約 51 秒になりました。

## [0.1.4]

### English

New

- Delegates you declare yourself (`delegate int Calc(int x);`) can be used in variables, fields, parameters, return values, and arrays, and called with `d(x)` / `d.Invoke(x)`. A delegate can hold a static method, a method of the same or another `UdonSharpBehaviour` (`enemy.Hit`), a method of a plain class or interface instance (`item.IsHit`), or a lambda, and can be passed to and called from another `UdonSharpBehaviour`. Delegates can also be created inside plain classes. Lambdas can use local variables and parameters from outside them, with the same behavior as C# (a lambda made in a loop uses the variable of that iteration, and keeps it after the method returns). Delegates can be combined and removed with `+=` / `-=` and `+` / `-`, with the same behavior as C# (they are called in order and the last return value is used). Anonymous methods (`delegate (int x) { ... }`) and local functions can also be put in a delegate. `Action`, `Func`, `Predicate`, and `Comparison` can be used in the same way, and a variable of these types can be passed to LINQ and `List` methods (`list.Where(isBig)`, `list.Sort(compare)`). See Documentation~/delegates.md.
- Local functions (methods written inside a method) can be used in `UdonSharpBehaviour` and plain classes, including ones that use outer local variables, recursion, and `static` local functions. UdonSharp itself does not support them. Before, they were an error.

Fixes

- A public field of a plain class type in a `UdonSharpBehaviour` now starts as `null` even after UdonSharp's automatic upgrade (for example, when you create the U# program asset). Before, it started as an empty array, so `item == null` was false.
- UdonSharp's automatic upgrade no longer adds `[OdinSerialize]` to your script for public fields of plain class, interface, and delegate types. These fields are not serialized.
- In a `partial` `UdonSharpBehaviour`, when only one of the files used `is` / `as` on a plain class (including an interface call on a plain class that implements the interface), UdonSharp compilation sometimes failed with error CS0103 ("The name '__cls_..._is' does not exist"). Whether it failed depended on the order in which the files were compiled, so it could change from one compile to the next.

Clearer errors

- A `partial` method in a plain class (`partial void OnHit();`) now reports "partial methods are not supported" (UdonSharp does not support them either). Before, it caused errors CS0111 and CS0121, or, when only the declaration was written, compiled as an empty method. `partial` classes can still be used.

### 日本語

新しく使えるもの

- 自分で宣言したデリゲート（`delegate int Calc(int x);`）を、変数・フィールド・引数・戻り値・配列に入れ、`d(x)`・`d.Invoke(x)` で呼べるようになりました。入れられるのは static メソッド・その `UdonSharpBehaviour` やほかの `UdonSharpBehaviour` のメソッド（`enemy.Hit`）・普通のクラスとインタフェースのインスタンスのメソッド（`item.IsHit`）・ラムダです。普通のクラスの中でも作れます。別の `UdonSharpBehaviour` に渡して、向こうで呼ぶこともできます。ラムダでは、外のローカル変数・引数も C# と同じく使えます（ループの中で作ったラムダはその回の変数を使い、メソッドを抜けたあとも持ち続けます）。`+=` / `-=`・`+` / `-` で、C# と同じくつないだり外したりできます（つないだ順に呼ばれ、戻り値は最後のもの）。匿名メソッド（`delegate (int x) { ... }`）・ローカル関数も入れられます。`Action`・`Func`・`Predicate`・`Comparison` も同じように使え、その変数を LINQ・`List` のメソッドに渡せます（`list.Where(isBig)`、`list.Sort(compare)`）。詳しくは Documentation~/delegates.md を見てください。
- ローカル関数（メソッドの中に書いたメソッド）を、`UdonSharpBehaviour` と普通のクラスの中で使えるようになりました。外のローカル変数を使うもの・再帰するもの・`static` のローカル関数も使えます（UdonSharp 自体は対応していません）。これまではエラーでした。

不具合の修正

- `UdonSharpBehaviour` の普通のクラス型の public フィールドが、UdonSharp の自動アップグレード（U# のプログラムアセットを作ったときなど）のあとでも、`null` で始まるようにしました。これまでは空の配列で始まり、`item == null` が false になっていました。
- UdonSharp の自動アップグレードが、普通のクラス型・インタフェース型・デリゲート型の public フィールドに `[OdinSerialize]` を足して、スクリプトを書き換えないようにしました（これらのフィールドはシリアライズしません）。
- `partial` の `UdonSharpBehaviour` で、片方のファイルだけが普通のクラスの `is` / `as`（普通のクラスが実装するインタフェースの呼び出しも）を使うと、UdonSharp のコンパイルが、ときどきエラー CS0103（「The name '__cls_..._is' does not exist」）になっていたのを直しました。ファイルを処理する順番によって起きたため、コンパイルのたびに起きたり起きなかったりしていました。

分かりやすいエラーにしたもの

- 普通のクラスの `partial` メソッド（`partial void OnHit();`）は「partial メソッドは未対応です」のエラーになります（UdonSharp も対応していません）。これまでは、エラー CS0111・CS0121 になるか、宣言だけのときは中身が空のメソッドとして通っていました。`partial` クラスは、これまでどおり使えます。

## [0.1.3]

### English

Fixes

- Using the same interface call, plain class, or lambda in both a base `UdonSharpBehaviour` and a class derived from it no longer causes the UdonSharp error "U# does not yet support hiding base methods".
- Using the same interface call, plain class, or lambda in both files of a `partial` `UdonSharpBehaviour` no longer causes errors CS0111 and CS0121.
- UdonSharp compilation no longer fails at random with "get_allowUnsafeCode can only be called from the main thread". This happened most often in the first compile after a script change.
- If UdonSharp Plus hits an unexpected error while deciding how to copy a field between the C# component and Udon, it now falls back to UdonSharp's own behavior with a warning, instead of failing the whole compilation.

### 日本語

不具合の修正

- 基底の `UdonSharpBehaviour` と、その派生クラスの両方で、同じインタフェースの呼び出し・普通のクラス・ラムダを使うと、UdonSharp のエラー「U# does not yet support hiding base methods」になっていたのを直しました。
- `partial` の `UdonSharpBehaviour` の 2 つのファイルの両方で、同じインタフェースの呼び出し・普通のクラス・ラムダを使うと、エラー CS0111・CS0121 になっていたのを直しました。
- UdonSharp のコンパイルが、ときどき「get_allowUnsafeCode can only be called from the main thread」で失敗していたのを直しました。スクリプトを変えたあとの最初のコンパイルで起きやすいものでした。
- C# のコンポーネントと Udon の間でフィールドを写す方法を決めるところで、UdonSharp Plus の中で思わぬ例外が起きたときは、コンパイル全体を失敗させず、警告を出して UdonSharp の元の動きに戻すようにしました。

## [0.1.2]

### English

Fixes

- Creating a U# program asset no longer overwrites your script with UdonSharp Plus's rewritten code. This happened when the script had a public or `[SerializeField]` field of an interface type, because UdonSharp's automatic upgrade wrote the rewritten source back to the file.
- A `using` alias for an interface or a plain class (`using IDmg = Game.IDamageable;`) no longer makes the whole rewrite fail, which broke the build of the entire project.
- Writing `null` directly into a `List` / `Dictionary` of plain classes or interfaces (`list.Add(null)`, `new List<Item> { null }`) no longer causes an internal UdonSharp exception.
- Assignments in expression-bodied members of plain classes (`public Item(int v) => this.v = v;`, `set => _v = value;`) no longer report a false error.
- Errors inside copies of generic classes now point to the correct column.
- A preprocessor directive right before the closing brace of a `UdonSharpBehaviour` (`#endif` of an `#if UNITY_EDITOR` block, `#endregion` of a `#region` around the whole class, `#pragma`) no longer causes error CS1040 when the class uses interfaces, plain classes, or `List` / `Dictionary`.
- Preprocessor directives (`#if`, `#region`, `#pragma`, ...) inside methods of plain classes, inside generic classes, and inside lambdas are no longer an error. Only the active branch of `#if` is used, with the same symbols as UdonSharp (`COMPILER_UDONSHARP`, and `UNITY_EDITOR` in the editor).
- The rewritten source in `Library/UdonSharpPlus/` now keeps the folder structure of your scripts and is rebuilt on every compile. Scripts with the same name in different folders no longer overwrite each other, and outputs of deleted scripts no longer remain.
- UdonSharp errors after a multi-line `List` / `Dictionary` initializer, LINQ chain, or lambda now point to the correct line. They used to point a few lines too early.
- Calling a method or property of a plain class on `null` now always stops with a runtime exception, as in C#. Before, a member that does not use `this` (`int Twice(int x) => x * 2;`) ran without error, and a member that did other work before using `this` stopped only after that work. For these members, the exception now points to the line of the call.

Clearer errors

- Tuples (`(int, string)`, `var (x, y) = ...`, `foreach (var (k, v) in dict)`) now report "tuples are not supported" instead of an internal UdonSharp exception.
- `[SerializeField]` / `[UdonSynced]` on interface-typed fields is now an error. These fields were never serialized or synced. To set one in the Inspector, use a `UdonSharpBehaviour` field and cast it.
- Local functions in plain classes now report "local functions are not supported" (UdonSharp does not support them either).
- A warning is shown at startup when the VRChat SDK version is outside the tested range (`vpmDependencies` in package.json). UdonSharp Plus keeps working; it is still disabled automatically if UdonSharp internals it needs are missing.

### 日本語

不具合の修正

- U# のプログラムアセットを作ったときに、スクリプトが UdonSharp Plus の書き換え後のコードで上書きされることがあったのを直しました。インタフェース型の public / `[SerializeField]` のフィールドがあるスクリプトで、UdonSharp の自動アップグレードが書き換え後のソースをファイルに書き戻していました。
- インタフェースや普通のクラスを指す `using` エイリアス（`using IDmg = Game.IDamageable;`）があると、書き換え全体が失敗し、プロジェクト全体がコンパイルエラーになっていたのを直しました。
- 普通のクラス・インタフェースの `List` / `Dictionary` に `null` を直接書くと（`list.Add(null)`、`new List<Item> { null }`）、UdonSharp の内部の例外になっていたのを直しました。
- 普通のクラスの式形式のメンバーでの代入（`public Item(int v) => this.v = v;`、`set => _v = value;`）が、誤ってエラーになっていたのを直しました。
- ジェネリッククラスのコピーの中のエラーが、正しい列に出るようになりました。
- インタフェース・普通のクラス・`List` / `Dictionary` を使う `UdonSharpBehaviour` で、クラスの閉じ括弧の直前にプリプロセッサ指令（`#if UNITY_EDITOR` のブロックの `#endif`、クラス全体を囲む `#region` の `#endregion`、`#pragma`）があると、エラー CS1040 になっていたのを直しました。
- 普通のクラスのメソッドの中・ジェネリッククラスの中・ラムダの中のプリプロセッサ指令（`#if`・`#region`・`#pragma` など）が、エラーにならなくなりました。`#if` は、UdonSharp と同じシンボル（`COMPILER_UDONSHARP`、エディタでは `UNITY_EDITOR` も）で有効な側だけを使います。
- `Library/UdonSharpPlus/` の書き換え後のソースを、元のスクリプトと同じフォルダの形で出し、コンパイルのたびに作り直すようにしました。別のフォルダの同じ名前のスクリプトが上書きし合ったり、消したスクリプトの出力が残ったりしなくなりました。
- 複数行の `List` / `Dictionary` の初期化子・LINQ・ラムダのうしろの行で出た UdonSharp のエラーが、数行前の行に表示されていたのを直しました。
- null の普通のクラスのメソッド・プロパティを呼ぶと、C# と同じく必ず実行時例外で止まるようにしました。これまでは、`this` を使わないメンバー（`int Twice(int x) => x * 2;`）はそのまま動き、`this` を使う前にほかの処理をするメンバーは、その処理をしてから止まっていました。こうしたメンバーでは、例外の位置は呼び出した行になります。

分かりやすいエラーにしたもの

- タプル（`(int, string)`、`var (x, y) = ...`、`foreach (var (k, v) in dict)`）は、UdonSharp の内部の例外ではなく「タプルは未対応です」のエラーになります。
- インタフェース型のフィールドに `[SerializeField]` / `[UdonSynced]` を付けるとエラーになります（もともとシリアライズも同期もされていませんでした）。Inspector で設定するときは、`UdonSharpBehaviour` 型のフィールドで受けてキャストしてください。
- 普通のクラスの中のローカル関数は「ローカル関数は未対応です」のエラーになります（UdonSharp も対応していません）。
- VRChat SDK のバージョンが、動作を確かめた範囲（package.json の `vpmDependencies`）の外のときは、起動時に警告を出します。動きは変わりません（必要な UdonSharp の内部が見つからないときは、これまでどおり自動で無効になります）。
