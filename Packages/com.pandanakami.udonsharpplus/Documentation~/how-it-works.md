# 仕組み

![仕組みの図](images/how-it-works.ja.svg)

図のオレンジが、本パッケージの部分です。

1. `CompilationContext.LoadSyntaxTreesAndCreateModules` にPostfixを当て、UdonSharp に渡す SyntaxTree を書き換えます。
   1. ジェネリクス：使われる型引数ごとに、ジェネリッククラス・メソッドのコピーを作り、使う側をコピーの名前にします。後の段はジェネリクスのないコードとして扱います。
   2. インタフェース：インタフェース型を `object` に置き換えます。呼び出しは、普通のクラスならそのクラスのメソッドの呼び出し（実装クラスが 4 個以上なら型番号の `switch`）に、UdonSharpBehaviour なら `SetProgramVariable`／`SendCustomEvent`／`GetProgramVariable` 経由に振り分けます。`is`／`as` は型タグと型IDで判定する形にします。
   3. 普通のクラス：インスタンスを `object[]` にし、コンストラクタ・メソッドのコピーを、使う UdonSharpBehaviour に生成します。
   4. List / Dictionary：`DataList` / `DataDictionary` に置き換え、要素の出し入れを `DataToken` との変換にします。`foreach` は添字で回すループにします。ラムダを受け取るメソッドは、ループとラムダの本体を入れたメソッドを生成し、その呼び出しにします。外のローカル変数・引数は、読むだけなら値で、書き込むなら `ref` で渡します（書き込むかどうかは Roslyn のデータフロー解析で判定します）。LINQ は、つないだ呼び出しを 1 つのループの生成メソッドにまとめます。
2. `CompilationContext.MethodExportLayout` のコンストラクタにPrefixを当て、インタフェース実装メソッドの Udon 上の名前を、どの実装クラスでも揃うものにします（メソッドは元の名前、引数は `Damage__int__string__p0`、戻り値は `Damage__int__string__ret`）。
3. `CompilationContext.AddDiagnostic` と `AssemblyDebugInfo`（実行時例外の位置を引くデバッグ情報）にパッチを当て、普通のクラスのコピーの中の位置を、元のクラスのソースの位置に戻します。
4. `Serializer.Create`（エディタ上の C# のコンポーネントと Udon の変数の間で値を写す方法を決める処理）にPrefixを当て、型を書き換えたフィールド（List・Dictionary・普通のクラス・インタフェースと、それらの配列）は写さないようにします。C# 側と Udon 側で型が違うため、そのままだと Play モードに入るたびに `not compatible with serializer` のエラーが出ます。
5. `UdonSharpUpgrader.UpgradeScripts`（UdonSharp の自動アップグレード。プログラムアセットを新しく作ったときなどに、フィールドに `[OdinSerialize]` を足してスクリプトのファイルに書き戻す処理）にパッチを当て、その中でのソースの読み込みでは 1 の書き換えをしないようにします。書き換えると、書き換え後のコードがユーザーのスクリプトに書き込まれてしまうためです。アップグレードの最後に走る UdonSharp のコンパイルでは、普通に書き換えます。

書き換え後のソースは、デバッグ用に `Library/UdonSharpPlus/` に出力されます。
