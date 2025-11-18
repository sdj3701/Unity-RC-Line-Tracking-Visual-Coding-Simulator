using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MG_BlocksEngine2.Block
{
    public class BE2_OuterAreaVertical : BE2_OuterArea
    {
        public BE2_OuterAreaVertical(Transform transform) : base(transform)
        {
            (transform as RectTransform).pivot = new Vector2(0, 1);
        }

        protected override void InitializeLayoutGroup()
        {
            LayoutGroup previousLg = Transform.GetComponent<LayoutGroup>();
            if (!(previousLg is VerticalLayoutGroup))
            {
#if UNITY_EDITOR
                if (Application.isPlaying)
#endif
                    MonoBehaviour.Destroy(previousLg);
#if UNITY_EDITOR
                else
                    MonoBehaviour.DestroyImmediate(previousLg);
#endif
            }
            VerticalLayoutGroup lg = Transform.GetComponent<VerticalLayoutGroup>();
            if (!lg)
                lg = Transform.gameObject.AddComponent<VerticalLayoutGroup>();

            lg.padding.left = 0;
            lg.padding.right = 0;
            lg.padding.top = -10;
            lg.padding.bottom = 0;
            lg.spacing = -10;
            lg.childControlHeight = false;
            lg.childControlWidth = false;
            lg.childForceExpandHeight = false;
            lg.childForceExpandWidth = false;
        }
    }
}