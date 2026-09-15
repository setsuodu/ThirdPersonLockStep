using UnityEngine;

namespace FrameSyncDemo
{
    /// <summary>
    /// 纯表现层：每帧把逻辑（定点）坐标转换成 float 显示出来。
    /// 这是数据单向流动的终点——渲染层可以在这基础上叠加插值/平滑动画，
    /// 但绝不能把这里的结果回写进 LogicWorld。
    /// </summary>
    public class PlayerView : MonoBehaviour
    {
        public void SetLogicPosition(FpVec2 logicPos)
        {
            transform.position = new Vector3(logicPos.X.ToFloatDebugOnly(), 0f, logicPos.Y.ToFloatDebugOnly());
        }
    }
}
