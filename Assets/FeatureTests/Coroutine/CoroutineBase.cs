using System.Collections;
using UdonSharp;
using UnityEngine;

namespace FeatureTests.Coroutines
{
    // #107：コルーチンを持つ基底のビヘイビア（CoroutineDerived が継承する）。基底と派生の両方にコルーチンがある・virtual のコルーチンを派生で override する
    public class CoroutineBase : UdonSharpBehaviour
    {
        [HideInInspector] public string log = "";

        public void Begin()
        {
            StartCoroutine(Twice("b"));
            StartCoroutine(Greet());
        }

        protected IEnumerator Twice(string tag)
        {
            for (int i = 0; i < 2; i++)
            {
                log += tag + i + ";";
                yield return null;
            }
        }

        // P：派生の override が base のコルーチンを待つ（hello に書く）
        [HideInInspector] public string hello = "";

        public void BeginHello() { StartCoroutine(Hello()); }

        protected virtual IEnumerator Hello()
        {
            yield return null;
            hello += "b";
        }

        protected virtual IEnumerator Greet()
        {
            yield return null;
            log += "base;";
        }
    }
}
