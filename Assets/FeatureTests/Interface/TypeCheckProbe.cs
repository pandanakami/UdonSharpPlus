using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace FeatureTests.Interface
{
    // #33: 実行時に「object[] か、それ以外（UdonSharpBehaviour など）か」を見分けられるかを確かめる。
    // 結果はパネルに OK / NG で出す。Start で 1 回実行し、ボタン（Interact）でもう一度実行できる。
    public class TypeCheckProbe : UdonSharpBehaviour
    {
        // #106: 結果。FeatureTestResults（エディタ）が GetProgramVariable で読み、全パネルの結果をまとめる
        [HideInInspector] public bool resultReady;
        [HideInInspector] public bool resultOk;
        [HideInInspector] public string resultName = "";
        [HideInInspector] public string resultText = "";

        public Text display;
        public UdonSharpBehaviour behaviour;   // 任意の UdonSharpBehaviour
        public ObjectHolder holder;            // 別の Behaviour へ渡す確認用
        public GameObject someGameObject;

        private string _text;
        private int _ng;

        private void Start()
        {
            Run();
        }

        public override void Interact()
        {
            Run();
        }

        public void Run()
        {
            _text = "";
            _ng = 0;

            object[] created = new object[] { "Ns.Foo", 1 };
            object instance = created;
            object[] emptyArray = new object[0];
            object empty = emptyArray;
            object usb = behaviour;
            UdonSharpBehaviour[] behaviours = new UdonSharpBehaviour[] { behaviour };
            object usbArray = behaviours;
            object text = "str";
            object boxedInt = 5;
            object go = someGameObject;
            object nothing = null;

            Check("object[] instance", IsInstance(instance), true);
            Check("empty object[]", IsInstance(empty), true);
            Check("UdonSharpBehaviour", IsInstance(usb), false);
            Check("UdonSharpBehaviour[]", IsInstance(usbArray), false);
            Check("string", IsInstance(text), false);
            Check("boxed int", IsInstance(boxedInt), false);
            Check("GameObject", IsInstance(go), false);
            Check("null", IsInstance(nothing), false);

            object[] a = (object[])instance;
            Check("tag read (string)a[0]", (string)a[0] == "Ns.Foo", true);
            Check("field read (int)a[1]", (int)a[1] == 1, true);

            // 別の Behaviour へ渡して戻す（インタフェース経由の呼び出しで起きる受け渡し）
            holder.SetProgramVariable("value", instance);
            object back = holder.GetProgramVariable("value");
            Check("via other behaviour: is object[]", IsInstance(back), true);
            Check("via other behaviour: same instance", back == instance, true);
            ((object[])back)[1] = 2;
            Check("via other behaviour: shared state", (int)created[1] == 2, true);

            // 速さの目安
            float t0 = Time.realtimeSinceStartup;
            int hits = 0;
            for (int i = 0; i < 10000; i++)
                if (IsInstance(instance)) hits++;
            float t1 = Time.realtimeSinceStartup;
            for (int i = 0; i < 10000; i++)
                if (usb != null) hits++;
            float t2 = Time.realtimeSinceStartup;

            string header = "#33 runtime type check (object[] vs others)\n"
                            + (_ng == 0 ? "RESULT: ALL OK" : $"RESULT: {_ng} NG") + "\n\n";
            string types = $"\ntypes: object[]={instance.GetType().FullName}\n"
                           + $"       USB={usb.GetType().FullName}\n"
                           + $"       USB[]={usbArray.GetType().FullName}\n"
                           + $"10000 checks: GetType {(t1 - t0) * 1000f:F2}ms / null-check only {(t2 - t1) * 1000f:F2}ms";

            string result = header + _text + types;
            resultName = "#33 runtime type check"; resultOk = _ng == 0; resultText = result.Replace("\n", " | "); resultReady = true;
            Debug.Log("[FeatureTests] " + result.Replace("\n", " | "));
            if (display != null)
                display.text = result;
        }

        private bool IsInstance(object x)
        {
            return x != null && x.GetType() == typeof(object[]);
        }

        private void Check(string label, bool actual, bool expected)
        {
            bool ok = actual == expected;
            if (!ok)
                _ng++;
            _text += $"{(ok ? "OK" : "NG")}  {label}: {actual} (expected {expected})\n";
        }
    }
}
