using UnityEngine;

namespace FrameSyncDemo
{
    /// <summary>
    /// 纯表现层：每帧把逻辑（定点）坐标转换成 float 显示出来。
    /// 同时负责玩家颜色表现；颜色由网络层同步后设置。
    /// </summary>
    public class PlayerView : MonoBehaviour
    {
        private Renderer cachedRenderer;

        private void Awake()
        {
            cachedRenderer = GetComponentInChildren<Renderer>();
        }

        public void SetLogicPosition(FpVec2 logicPos)
        {
            transform.position = new Vector3(logicPos.X.ToFloatDebugOnly(), 0f, logicPos.Y.ToFloatDebugOnly());
        }

        public void SetColor(Color color)
        {
            if (cachedRenderer == null)
                cachedRenderer = GetComponentInChildren<Renderer>();

            if (cachedRenderer != null)
                cachedRenderer.material.color = color;
        }
    }
}
