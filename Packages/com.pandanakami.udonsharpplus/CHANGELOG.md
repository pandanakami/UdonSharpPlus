# Changelog

Changes in each version of UdonSharp Plus. Each section is used as the body of the GitHub release.
UdonSharp Plus の各バージョンの変更点です。各節は GitHub のリリースの本文になります。

## [0.1.2]

### English

Fixes

- Creating a U# program asset no longer overwrites your script with UdonSharp Plus's rewritten code. This happened when the script had a public or `[SerializeField]` field of an interface type, because UdonSharp's automatic upgrade wrote the rewritten source back to the file.
- A `using` alias for an interface or a plain class (`using IDmg = Game.IDamageable;`) no longer makes the whole rewrite fail, which broke the build of the entire project.
- Writing `null` directly into a `List` / `Dictionary` of plain classes or interfaces (`list.Add(null)`, `new List<Item> { null }`) no longer causes an internal UdonSharp exception.
- Assignments in expression-bodied members of plain classes (`public Item(int v) => this.v = v;`, `set => _v = value;`) no longer report a false error.
- Errors inside copies of generic classes now point to the correct column.
- A preprocessor directive right before the closing brace of a `UdonSharpBehaviour` (`#endif` of an `#if UNITY_EDITOR` block, `#endregion` of a `#region` around the whole class, `#pragma`) no longer causes error CS1040 when the class uses interfaces, plain classes, or `List` / `Dictionary`.

Clearer errors

- Tuples (`(int, string)`, `var (x, y) = ...`, `foreach (var (k, v) in dict)`) now report "tuples are not supported" instead of an internal UdonSharp exception.
- `[SerializeField]` / `[UdonSynced]` on interface-typed fields is now an error. These fields were never serialized or synced. To set one in the Inspector, use a `UdonSharpBehaviour` field and cast it.
- Local functions in plain classes now report "local functions are not supported" (UdonSharp does not support them either).

### 日本語

不具合の修正

- U# のプログラムアセットを作ったときに、スクリプトが UdonSharp Plus の書き換え後のコードで上書きされることがあったのを直しました。インタフェース型の public / `[SerializeField]` のフィールドがあるスクリプトで、UdonSharp の自動アップグレードが書き換え後のソースをファイルに書き戻していました。
- インタフェースや普通のクラスを指す `using` エイリアス（`using IDmg = Game.IDamageable;`）があると、書き換え全体が失敗し、プロジェクト全体がコンパイルエラーになっていたのを直しました。
- 普通のクラス・インタフェースの `List` / `Dictionary` に `null` を直接書くと（`list.Add(null)`、`new List<Item> { null }`）、UdonSharp の内部の例外になっていたのを直しました。
- 普通のクラスの式形式のメンバーでの代入（`public Item(int v) => this.v = v;`、`set => _v = value;`）が、誤ってエラーになっていたのを直しました。
- ジェネリッククラスのコピーの中のエラーが、正しい列に出るようになりました。
- インタフェース・普通のクラス・`List` / `Dictionary` を使う `UdonSharpBehaviour` で、クラスの閉じ括弧の直前にプリプロセッサ指令（`#if UNITY_EDITOR` のブロックの `#endif`、クラス全体を囲む `#region` の `#endregion`、`#pragma`）があると、エラー CS1040 になっていたのを直しました。

分かりやすいエラーにしたもの

- タプル（`(int, string)`、`var (x, y) = ...`、`foreach (var (k, v) in dict)`）は、UdonSharp の内部の例外ではなく「タプルは未対応です」のエラーになります。
- インタフェース型のフィールドに `[SerializeField]` / `[UdonSynced]` を付けるとエラーになります（もともとシリアライズも同期もされていませんでした）。Inspector で設定するときは、`UdonSharpBehaviour` 型のフィールドで受けてキャストしてください。
- 普通のクラスの中のローカル関数は「ローカル関数は未対応です」のエラーになります（UdonSharp も対応していません）。
