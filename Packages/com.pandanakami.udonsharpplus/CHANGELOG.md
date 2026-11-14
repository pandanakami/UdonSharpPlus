# Changelog

Changes in each version of UdonSharp Plus. Each section is used as the body of the GitHub release.
UdonSharp Plus の各バージョンの変更点です。各節は GitHub のリリースの本文になります。

## [0.1.4]

### English

New

- Delegates you declare yourself (`delegate int Calc(int x);`) can be used in variables, fields, parameters, return values, and arrays, and called with `d(x)` / `d.Invoke(x)`. A delegate can hold a static method, a method of the same `UdonSharpBehaviour`, or a lambda, and can be passed to and called from another `UdonSharpBehaviour`. Lambdas can use local variables and parameters from outside them, with the same behavior as C# (a lambda made in a loop uses the variable of that iteration, and keeps it after the method returns). Delegates can be combined and removed with `+=` / `-=` and `+` / `-`, with the same behavior as C# (they are called in order and the last return value is used). Anonymous methods (`delegate (int x) { ... }`) and local functions can also be put in a delegate. `Action` / `Func` / `Predicate` are not supported yet (they are compile errors). See Documentation~/delegates.md.
- Local functions (methods written inside a method) can be used in `UdonSharpBehaviour` and plain classes, including ones that use outer local variables, recursion, and `static` local functions. UdonSharp itself does not support them. Before, they were an error.

Fixes

- A public field of a plain class type in a `UdonSharpBehaviour` now starts as `null` even after UdonSharp's automatic upgrade (for example, when you create the U# program asset). Before, it started as an empty array, so `item == null` was false.
- UdonSharp's automatic upgrade no longer adds `[OdinSerialize]` to your script for public fields of plain class, interface, and delegate types. These fields are not serialized.
- In a `partial` `UdonSharpBehaviour`, when only one of the files used `is` / `as` on a plain class (including an interface call on a plain class that implements the interface), UdonSharp compilation sometimes failed with error CS0103 ("The name '__cls_..._is' does not exist"). Whether it failed depended on the order in which the files were compiled, so it could change from one compile to the next.

Clearer errors

- A `partial` method in a plain class (`partial void OnHit();`) now reports "partial methods are not supported" (UdonSharp does not support them either). Before, it caused errors CS0111 and CS0121, or, when only the declaration was written, compiled as an empty method. `partial` classes can still be used.

### 日本語

新しく使えるもの

- 自分で宣言したデリゲート（`delegate int Calc(int x);`）を、変数・フィールド・引数・戻り値・配列に入れ、`d(x)`・`d.Invoke(x)` で呼べるようになりました。入れられるのは static メソッド・その `UdonSharpBehaviour` のメソッド・ラムダです。別の `UdonSharpBehaviour` に渡して、向こうで呼ぶこともできます。ラムダでは、外のローカル変数・引数も C# と同じく使えます（ループの中で作ったラムダはその回の変数を使い、メソッドを抜けたあとも持ち続けます）。`+=` / `-=`・`+` / `-` で、C# と同じくつないだり外したりできます（つないだ順に呼ばれ、戻り値は最後のもの）。匿名メソッド（`delegate (int x) { ... }`）・ローカル関数も入れられます。`Action` / `Func` / `Predicate` は、まだ未対応です（コンパイルエラーになります）。詳しくは Documentation~/delegates.md を見てください。
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
