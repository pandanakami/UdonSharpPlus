using System.Collections;
using UnityEngine;

namespace FeatureTests.Coroutines
{
    // #107：CoroutineBase を継承し、自分のコルーチンを足し、基底の virtual のコルーチンを override する
    public class CoroutineDerived : CoroutineBase
    {
        public void BeginMore()
        {
            StartCoroutine(Own());
        }

        private IEnumerator Own()
        {
            yield return StartCoroutine(Twice("d"));
            log += "own;";
        }

        protected override IEnumerator Hello()
        {
            yield return base.Hello();
            hello += "d";
        }

        protected override IEnumerator Tail(string tag)
        {
            yield return null;
            tail += tag + "d";
        }

        protected override IEnumerator Greet()
        {
            yield return null;
            yield return null;
            log += "derived;";
        }

        private void OnDisable()
        {
            log += "off;";
        }
    }
}
