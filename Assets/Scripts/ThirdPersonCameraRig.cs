using UnityEngine;

namespace FrameSyncDemo
{
    /// <summary>
    /// LogicCamera → EngineCamera 这一段。
    ///
    /// 纯表现层：每渲染帧读取 LogicCamera 当前的 yaw 作为"目标朝向"，自己在这基础上
    /// 做平滑跟随/平滑转动。所有你想要的镜头手感（跟随迟滞、转向阻尼）都在这里调，
    /// 完全不影响 GameNetwork 里移动方向的解析——那边读的是 LogicCamera 的即时值，
    /// 不是这里插值出来的中间状态。
    ///
    /// 数据流向是单向的：LogicCamera 驱动这里，这里的平滑结果绝不会反过来
    /// 修改 LogicCamera.YawRadians。
    /// </summary>
    public class ThirdPersonCameraRig : MonoBehaviour
    {
        [Tooltip("本地玩家的 Transform，由 GameNetwork 在本地玩家生成时赋值")]
        public Transform target;
        public LogicCamera logicCamera;

        public float distance = 6f;
        public float height = 3f;
        public float followSmooth = 12f;
        public float rotateSmooth = 12f;

        private float currentYawRad;
        private float currentPitchDeg;
        private bool initialized;

        void LateUpdate()
        {
            if (target == null || logicCamera == null) return;

            if (!initialized)
            {
                // 第一帧直接对齐，避免镜头从场景原点飞过来的突兀感。
                currentYawRad = logicCamera.YawRadians;
                currentPitchDeg = logicCamera.PitchDegrees;
                initialized = true;
            }

            currentYawRad = Mathf.LerpAngle(currentYawRad * Mathf.Rad2Deg, logicCamera.YawRadians * Mathf.Rad2Deg, Time.deltaTime * rotateSmooth) * Mathf.Deg2Rad;
            currentPitchDeg = Mathf.LerpAngle(currentPitchDeg, logicCamera.PitchDegrees, Time.deltaTime * rotateSmooth);

            // forward 用的角度约定和 FpTrig.DirFromAngle 一致：yaw=0 朝 +Z，随 yaw 增大转向 +X。
            Vector3 forward = new Vector3(Mathf.Sin(currentYawRad), 0f, Mathf.Cos(currentYawRad));
            Vector3 pitchedOffset = Quaternion.AngleAxis(currentPitchDeg, Vector3.Cross(forward, Vector3.up)) * (-forward);

            Vector3 desiredPos = target.position + Vector3.up * height + pitchedOffset * distance;

            transform.position = Vector3.Lerp(transform.position, desiredPos, Time.deltaTime * followSmooth);
            transform.LookAt(target.position + Vector3.up * 1.2f);
        }
    }
}
