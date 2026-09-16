using UnityEngine;

namespace FrameSyncDemo
{
    /// <summary>
    /// Input → LogicCamera 这一段。
    ///
    /// 纯本地状态：只把鼠标输入累加成一个"玩家意图朝向"(yaw，弧度)。
    /// 刻意不做任何平滑/阻尼——平滑效果留给 ThirdPersonCameraRig 去做，
    /// 这里必须是"刚性、瞬间响应"的，因为 GameNetwork 采样输入时读的就是这个值，
    /// 如果这里也平滑了，会导致移动方向的响应跟着变迟钝。
    ///
    /// YawRadians 本身不需要跨客户端一致（每个人的镜头转向互不相关），
    /// 但同一台设备上，从这里读出的值到解析成 InputMsg 发送出去这条链路，
    /// 必须是可确定性重放的——所以这里只做无损的累加，不依赖帧率相关的插值状态。
    /// </summary>
    public class LogicCamera : MonoBehaviour
    {
        public float mouseSensitivity = 3f;
        public float pitchSensitivity = 2f;
        public float minPitchDegrees = -35f;
        public float maxPitchDegrees = 70f;

        // 0 弧度 = 朝向世界 +Z（约定和 FpTrig.DirFromAngle 一致），随输入增大朝 +X 转。
        public float YawRadians { get; private set; }

        // 只用于镜头俯仰角的表现（不影响移动方向解析，移动只关心水平朝向）。
        public float PitchDegrees { get; private set; }

        private bool cursorLocked;

        void Update()
        {
            if (!cursorLocked) return;

            float mouseX = Input.GetAxis("Mouse X");
            float mouseY = Input.GetAxis("Mouse Y");

            YawRadians += mouseX * mouseSensitivity * Time.deltaTime;

            PitchDegrees -= mouseY * pitchSensitivity * Time.deltaTime * 60f;
            PitchDegrees = Mathf.Clamp(PitchDegrees, minPitchDegrees, maxPitchDegrees);
        }

        public void SetCursorLocked(bool locked)
        {
            cursorLocked = locked;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
