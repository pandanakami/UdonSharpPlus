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

        // V：OnDisable の中で開始する・OnDisable の中で StopAllCoroutines する・Destroy したあとに動かない（#107 の追加テスト案の 13）
        [HideInInspector] public string note = "";
        public CoroutineRunner sink;
        private bool _restartOnDisable;
        private bool _stopOnDisable;

        public void ArmRestart() { _restartOnDisable = true; }
        public void ArmStop() { _stopOnDisable = true; }
        public void BeginExt(string tag) { StartCoroutine(Ext(tag)); }

        private IEnumerator Ext(string tag)
        {
            for (int i = 0; i < 1000; i++)
            {
                sink.ext += tag;
                yield return null;
            }
        }

        private void OnDisable()
        {
            if (_restartOnDisable)
            {
                _restartOnDisable = false;
                Coroutine c = StartCoroutine(Tick("x"));
                note += "R" + (c == null) + ";";
            }
            if (_stopOnDisable)
            {
                _stopOnDisable = false;
                StopAllCoroutines();
                note += "S;";
            }
        }

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
