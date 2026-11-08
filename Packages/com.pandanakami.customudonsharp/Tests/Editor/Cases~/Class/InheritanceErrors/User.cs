using UdonSharp;
using UnityEngine;

namespace CustomUdonSharpTests.InheritanceErrors
{
    public class User : UdonSharpBehaviour
    {
        [Note] public int noted;
        public Settings settings;

        private void Start()
        {
            Debug.Log(settings.level);
            Debug.Log(GetComponent<Helper>().v);
            Debug.Log(new GameError().code);
            Debug.Log(new Box().depth);
            Debug.Log(new Animal().legs);
        }
    }
}
