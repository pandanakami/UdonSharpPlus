using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Persistence;
using VRC.SDKBase;

namespace UdonSharpPlusTests.CollectionForeignEnumerable
{
    // 外部の関数が返す IEnumerable は、List・LINQ と同じファイルでも、今の U# のまま使える（#62）
    public class User : UdonSharpBehaviour
    {
        public ToggleGroup group;
        private List<string> _names = new List<string> { "a", "b" };
        private IEnumerable<string> _keys;

        private void Start()
        {
            var on = group.ActiveToggles();
            IEnumerable<Toggle> active = group.ActiveToggles();
            _keys = PlayerData.GetKeys(Networking.LocalPlayer);
            Show(_keys);
            Debug.Log(string.Join(",", Keys()));
            Debug.Log(string.Concat(_keys));
            Debug.Log(on);
            Debug.Log(active);

            // ラムダの中で使う（元の型のまま渡す）
            IEnumerable<string> keys = _keys;
            bool any = _names.Any(n => string.Join(",", keys).Contains(n));
            var upper = _names.Select(n => n.ToUpper());
            Debug.Log(any + string.Join(",", upper.ToArray()));
        }

        private IEnumerable<string> Keys() { return PlayerData.GetKeys(Networking.LocalPlayer); }

        private void Show(IEnumerable<string> keys) { Debug.Log(string.Join(",", keys)); }
    }
}
