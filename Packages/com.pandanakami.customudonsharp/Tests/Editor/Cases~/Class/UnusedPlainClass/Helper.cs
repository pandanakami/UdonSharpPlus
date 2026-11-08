using System.Collections.Generic;

namespace CustomUdonSharpTests.UnusedPlainClass
{
    // UdonSharpBehaviour から使われない普通のクラス：未対応の書き方があってもエラーにしない
    public class EditorOnlyHelper
    {
        public static int count;
        public List<int> items = new List<int>();
    }
}
