using System.Collections;
using UdonSharp;
using UnityEngine;

namespace FeatureTests.Coroutines
{
    // #107：CoroutineRunner から操作される相手。StopAllCoroutines と、GameObject を非アクティブにしたとき・ビヘイビアを無効にしたときを確かめる
    public class CoroutinePeer : UdonSharpBehaviour
    {
        [HideInInspector] public string log = "";

        public void Begin(string tag) { StartCoroutine(Tick(tag)); }

        public bool BeginReturnsNull(string tag)
        {
            Coroutine c = StartCoroutine(Tick(tag));
            return c == null;
        }

        public void StopAll() { StopAllCoroutines(); }

        private IEnumerator Tick(string tag)
        {
            for (int i = 0; i < 1000; i++)
            {
                log += tag;
                yield return null;
            }
        }
    }
}
