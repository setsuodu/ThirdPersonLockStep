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

        /// <summary>
        /// position/yawDegrees 已经是 GameNetwork 在"上一tick"和"这一tick"之间
        /// 按渲染时间插值算好的结果——这里不再自己做平滑/插值，只是原样应用。
        /// 之所以插值放在 GameNetwork 而不是这里，是因为要让同一个 alpha 同时
        /// 驱动位置和朝向，两者步调一致，看起来才不会显得脱节；如果各自在这里
        /// 用不同的平滑参数分别处理，位置和朝向的"跟手感"会不一致。
        /// </summary>
        public void SetRenderState(Vector3 position, float yawDegrees)
        {
            transform.position = position;
            transform.rotation = Quaternion.Euler(0f, yawDegrees, 0f);
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
