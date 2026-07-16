using UnityEngine;
using UnityEngine.InputSystem;

namespace VRTyping.Keyboard
{
    [RequireComponent(typeof(VRKeyboardPressProbe))]
    [RequireComponent(typeof(SphereCollider))]
    [RequireComponent(typeof(Rigidbody))]
    // StickTap 输入用的探针：根据手柄/手部 anchor 的位置和 forward 方向生成一根虚拟 stick。
    public class VRKeyboardStickProbeFollower : MonoBehaviour
    {
        // 可视 stick 物体用哪个本地轴表示长度。
        public enum StickVisualAxis
        {
            X,
            Y,
            Z,
        }

        [Header("References")]
        [Tooltip("Controller or anchor transform that defines the stick origin and forward direction.")]
        // 跟随目标，通常是控制器或手部锚点；它的位置和 forward 决定 stick 的方向。
        public Transform m_FollowTarget;

        [Tooltip("Controller hand driven by this probe. Auto infers from the follow target hierarchy.")]
        public VRKeyboardControllerHand m_ControllerHand = VRKeyboardControllerHand.Auto;


        [Tooltip("Optional child used as the visible rod between the controller and the probe tip.")]
        // 可选的杆状可视物，用来显示从控制器到探针尖端的 stick。
        public Transform m_StickVisual;


        [Tooltip("Optional child used as the visible tip of the probe.")]
        // 可选的探针尖端可视物。
        public Transform m_ProbeVisual;


        [Tooltip("Optional thumbstick action used to adjust stick length at runtime.")]
        // 可选输入，例如摇杆 Y 轴，用于运行时调节 stick 长度。
        public InputActionReference m_LengthAdjustAction;

        [Header("Stick Settings")]

        [Min(0f)]
        [Tooltip("Delay in seconds before the stick visual and probe collider become active.")]
        // 启用后延迟一段时间再显示和启用碰撞，避免刚切换模式时立刻误触。
        public float m_ActivationDelay = 0f;


        [Min(0.01f)]
        [Tooltip("Fixed distance from the follow target to the probe tip.")]
        // 从跟随目标到探针尖端的当前距离。
        public float m_StickLength = 0.18f;


        [Min(0.01f)]
        [Tooltip("Shortest allowed stick length when runtime adjustment is enabled.")]
        // 运行时调节 stick 长度时允许的最短长度。
        public float m_MinStickLength = 0.08f;


        [Min(0.01f)]
        [Tooltip("Longest allowed stick length when runtime adjustment is enabled.")]
        // 运行时调节 stick 长度时允许的最长长度。
        public float m_MaxStickLength = 0.3f;


        [Min(0f)]
        [Tooltip("How fast the stick length changes per second when pushing the thumbstick vertically.")]
        // 摇杆竖直方向输入改变 stick 长度的速度。
        public float m_StickLengthAdjustSpeed = 0.2f;


        [Range(0f, 1f)]
        [Tooltip("Ignore small thumbstick movements under this absolute Y threshold.")]
        // 摇杆死区，忽略轻微抖动。
        public float m_LengthAdjustDeadzone = 0.2f;


        [Min(0f)]
        [Tooltip("Extra forward offset added before the stick begins.")]
        // stick 起点相对跟随目标向前偏移一点，避免从手柄内部开始。
        public float m_BaseForwardOffset = 0.02f;


        [Min(0.001f)]
        [Tooltip("Rendered thickness of the optional stick visual.")]
        // 杆状可视物的厚度。
        public float m_StickThickness = 0.006f;


        [Min(0.001f)]
        [Tooltip("Length represented by a scale of 1 on the stick visual's primary axis.")]
        // 可视物主轴缩放为 1 时代表的实际长度，用于把世界距离换算成本地 scale。
        public float m_StickVisualBaseLength = 1f;


        [Tooltip("Axis used by the stick visual to represent its length.")]
        // 指定 m_StickVisual 哪个本地轴沿 stick 方向拉伸。
        public StickVisualAxis m_StickVisualAxis = StickVisualAxis.Z;

        SphereCollider m_SphereCollider;
        VRKeyboardPressProbe m_PressProbe;
        Rigidbody m_Rigidbody;
        Renderer[] m_SelfRenderers;
        // 记录上一次显示/碰撞状态，避免每帧重复 SetActive 或 enabled。
        bool m_LastVisualActive;
        bool m_LastColliderActive;
        // 到达该时间后才真正启用探针。
        float m_ActivationTime;
        public VRKeyboardControllerHand effectiveControllerHand =>
            VRKeyboardControllerHandUtility.Resolve(m_ControllerHand, this, m_FollowTarget);

        void Reset()
        {
            // 自动补常用引用，方便把脚本挂到 prefab 后少配几个字段。
            if (m_FollowTarget == null)
                m_FollowTarget = transform.parent;

            if (m_ProbeVisual == null && transform.childCount > 0)
                m_ProbeVisual = transform.GetChild(0);

            EnsureProbeColliderSetup();
        }

        void Awake()
        {
            m_SphereCollider = GetComponent<SphereCollider>();
            m_PressProbe = GetComponent<VRKeyboardPressProbe>();
            m_Rigidbody = GetComponent<Rigidbody>();
            m_SelfRenderers = GetComponentsInChildren<Renderer>(true);

            // 探针由脚本直接移动，不需要重力和物理动力学。
            if (m_Rigidbody != null)
            {
                m_Rigidbody.useGravity = false;
                m_Rigidbody.isKinematic = true;
            }

            EnsureProbeColliderSetup();
        }

        void OnValidate()
        {
            // Inspector 修改参数时保证长度范围合法。
            if (m_MaxStickLength < m_MinStickLength)
                m_MaxStickLength = m_MinStickLength;

            m_StickLength = Mathf.Clamp(m_StickLength, m_MinStickLength, m_MaxStickLength);
            EnsureProbeColliderSetup();
        }

        void OnEnable()
        {
            SetPressProbeActive(true);

            // XR Rig 全局共享这个 action；模式切换时保持启用，只停用本探针。
            var action = m_LengthAdjustAction != null ? m_LengthAdjustAction.action : null;
            if (action != null && !action.enabled)
                action.Enable();

            m_ActivationTime = Time.time + m_ActivationDelay;
            SyncToTarget();
            ApplyActiveState();
        }

        void OnDisable()
        {
            SetPressProbeActive(false);

            // 禁用时隐藏可视物并停用碰撞，避免留在场景里继续触发按键。
            SetColliderActive(false);
            SetVisualActive(false);

        }

        void SetPressProbeActive(bool active)
        {
            if (m_PressProbe == null)
                m_PressProbe = GetComponent<VRKeyboardPressProbe>();

            if (m_PressProbe != null)
                m_PressProbe.enabled = active;
        }

        void LateUpdate()
        {
            // 每帧先处理长度调节，再根据目标 transform 更新探针位置和显示状态。
            UpdateStickLength();
            SyncToTarget();
            ApplyActiveState();
        }

        void SyncToTarget()
        {
            if (m_FollowTarget == null)
            {
                SetColliderActive(false);
                SetVisualActive(false);
                return;
            }

            var stickOrigin = m_FollowTarget.position + m_FollowTarget.forward * m_BaseForwardOffset;
            var tipPosition = stickOrigin + m_FollowTarget.forward * m_StickLength;

            // 探针本体放在 stick 尖端，旋转跟随手柄/手部 anchor。
            transform.SetPositionAndRotation(tipPosition, m_FollowTarget.rotation);

            UpdateStickVisual(stickOrigin, tipPosition);
        }

        void ApplyActiveState()
        {
            // 延迟结束后才让探针开始可见并参与触发。
            var isActive = Time.time >= m_ActivationTime;
            SetColliderActive(isActive);
            SetVisualActive(isActive);
        }

        void UpdateStickLength()
        {
            var action = m_LengthAdjustAction != null ? m_LengthAdjustAction.action : null;
            if (m_StickLengthAdjustSpeed <= 0f)
                return;

            var axisValue = Vector2.zero;
            var hasAxisValue = false;
            if (action != null)
            {
                try
                {
                    axisValue = action.ReadValue<Vector2>();
                    hasAxisValue = true;
                }
                catch
                {
                }
            }

            if (VRKeyboardControllerHandUtility.TryReadThumbstick(effectiveControllerHand, out var fallbackAxisValue) &&
                (!hasAxisValue || fallbackAxisValue.sqrMagnitude > axisValue.sqrMagnitude))
            {
                axisValue = fallbackAxisValue;
                hasAxisValue = true;
            }

            if (!hasAxisValue)
            {
                return;
            }

            var inputY = axisValue.y;
            if (Mathf.Abs(inputY) < m_LengthAdjustDeadzone)
                return;

            // 用摇杆 Y 轴增减 stick 长度，并限制在允许范围内。
            m_StickLength += inputY * m_StickLengthAdjustSpeed * Time.deltaTime;
            m_StickLength = Mathf.Clamp(m_StickLength, m_MinStickLength, m_MaxStickLength);
        }

        void UpdateStickVisual(Vector3 stickOrigin, Vector3 tipPosition)
        {
            if (m_StickVisual == null)
                return;

            var direction = tipPosition - stickOrigin;
            var distance = direction.magnitude;
            if (distance <= Mathf.Epsilon)
                return;

            // 杆状物放在起点和尖端中间，并朝向尖端。
            m_StickVisual.SetPositionAndRotation(
                stickOrigin + direction * 0.5f,
                Quaternion.LookRotation(direction.normalized, m_FollowTarget.up));

            var visualScale = m_StickVisual.localScale;
            var axialScale = Mathf.Max(0.001f, distance / Mathf.Max(0.001f, m_StickVisualBaseLength));

            // 只拉伸主轴，另外两个轴用厚度控制。
            switch (m_StickVisualAxis)
            {
                case StickVisualAxis.X:
                    visualScale.x = axialScale;
                    visualScale.y = m_StickThickness;
                    visualScale.z = m_StickThickness;
                    break;
                case StickVisualAxis.Y:
                    visualScale.x = m_StickThickness;
                    visualScale.y = axialScale;
                    visualScale.z = m_StickThickness;
                    break;
                default:
                    visualScale.x = m_StickThickness;
                    visualScale.y = m_StickThickness;
                    visualScale.z = axialScale;
                    break;
            }

            m_StickVisual.localScale = visualScale;
        }

        void SetColliderActive(bool active)
        {
            // SphereCollider 是真正参与键盘触发检测的探针碰撞体。
            if (m_SphereCollider == null || m_LastColliderActive == active)
                return;

            m_SphereCollider.enabled = active;
            m_LastColliderActive = active;
        }

        void SetVisualActive(bool active)
        {
            if (m_LastVisualActive == active)
                return;

            // 优先开关显式指定的可视子物体；没有时退回到开关自身所有 Renderer。
            if (m_ProbeVisual != null && m_ProbeVisual != transform)
                m_ProbeVisual.gameObject.SetActive(active);

            if (m_StickVisual != null && m_StickVisual != transform)
                m_StickVisual.gameObject.SetActive(active);

            if (m_ProbeVisual == null || m_ProbeVisual == transform)
                SetRendererState(m_SelfRenderers, active);

            m_LastVisualActive = active;
        }

        static void SetRendererState(Renderer[] renderers, bool enabled)
        {
            if (renderers == null)
                return;

            for (var i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                    renderers[i].enabled = enabled;
            }
        }

        void EnsureProbeColliderSetup()
        {
            if (m_SphereCollider == null)
                m_SphereCollider = GetComponent<SphereCollider>();

            // 探针只需要触发器重叠，不需要实体碰撞。
            if (m_SphereCollider != null)
                m_SphereCollider.isTrigger = true;
        }
    }
}
