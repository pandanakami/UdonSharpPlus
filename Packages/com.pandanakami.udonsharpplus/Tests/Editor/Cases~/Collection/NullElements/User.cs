using System.Collections.Generic;
using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.NullElements
{
    public class Item
    {
        public int v;
    }

    public interface IThing
    {
        int Get();
    }

    public class Thing : IThing
    {
        public int Get() { return 1; }
    }

    // #89: 普通のクラス・インタフェース・UnityEngine.Object の List / Dictionary に null の定数を書いても、U# の内部の例外にならない
    public class User : UdonSharpBehaviour
    {
        private void Start()
        {
            List<Item> a = new List<Item> { new Item(), null };
            a.Add(null);
            a.Add(default(Item));
            a.Insert(0, null);
            a[1] = null;
            Debug.Log(a.Contains(null) + " " + a.IndexOf(null) + " " + a.LastIndexOf(null) + " " + a.Remove(null));

            List<IThing> b = new List<IThing> { null, new Thing() };
            b.Add(null);
            b[0] = default(IThing);

            Dictionary<string, Item> c = new Dictionary<string, Item> { { "x", null } };
            Dictionary<string, Item> d = new Dictionary<string, Item> { ["y"] = null };
            c["z"] = null;
            c.Add("w", null);
            Debug.Log(c.ContainsValue(null) + " " + d.Count);

            List<GameObject> e = new List<GameObject> { null };
            e.Add(null);

            List<string> f = new List<string> { "s", null };
            f.Add(null);
            Debug.Log(a.Count + b.Count + c.Count + e.Count + f.Count);
        }
    }
}
