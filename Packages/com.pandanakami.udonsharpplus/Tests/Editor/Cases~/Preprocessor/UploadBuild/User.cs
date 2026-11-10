using System.Collections.Generic;
using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.PreprocessorUploadBuild
{
    public interface IUnit { int Hp(); }

    public class Unit : IUnit
    {
        public int hp = 3;
        public int Hp() { return hp; }
    }

    // #91: アップロード用のビルド（UNITY_EDITOR なし）では #else の側が使われる。
    // UNITY_EDITOR の中だけで宣言したフィールドを #else の側で使わないこと
    public class User : UdonSharpBehaviour
    {
        private List<int> _values = new List<int>();
#if UNITY_EDITOR
        private int _editorOnly = 10;
#endif

        private void Start()
        {
            IUnit u = new Unit();
#if UNITY_EDITOR
            _values.Add(u.Hp() + _editorOnly);
#else
            _values.Add(u.Hp());
#endif
            Debug.Log(_values[0]);
        }
    }
}
