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

        [Tooltip("表现层的转向平滑速度，纯视觉，不影响任何同步逻辑")]
        public float rotationSmoothSpeed = 12f;

        /// <summary>
        /// angleIndex 是 LogicWorld.Facings 里同步好的朝向（所有客户端一致），
        /// 这里只是把它转成 Unity 的 Y 轴角度并做平滑插值——平滑速度可以随便调，
        /// 因为朝向本身已经在同步域里确定了，这里怎么转、多快转完全是纯表现问题。
        /// </summary>
        public void SetLogicFacing(byte angleIndex)
        {
            float targetDeg = angleIndex * (360f / FpTrig.STEPS);
            var targetRot = Quaternion.Euler(0f, targetDeg, 0f);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * rotationSmoothSpeed);
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
