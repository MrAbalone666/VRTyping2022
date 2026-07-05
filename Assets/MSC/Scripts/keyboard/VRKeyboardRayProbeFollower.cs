using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

namespace VRTyping.Keyboard
{

    // 让一个虚拟按压探针跟随 XR 射线末端，用射线输入模拟手指按键、凝视输入或滑动输入。
    public class VRKeyboardRayProbeFollower : MonoBehaviour
    {
        [Header("References")]

        [Tooltip("The Near-Far Interactor that owns the visible ray.")]
        // 提供射线曲线终点和命中法线的 XR 交互器。
        public NearFarInteractor m_NearFarInteractor;


        [Tooltip("Optional visual child used to show the probe position in play mode.")]
        // 运行时用来显示探针位置的可选可视物体。
        public Transform m_Visual;

        [Header("Follow Settings")]

        [Tooltip("Keeps the probe slightly inside the hit surface even when not pressing.")]
        // 普通按压/滑动模式下，让探针略微进入表面，确保能稳定接触到按键。
        public float m_BaseSurfaceOffset = 0.001f;


        [Tooltip("Input action that provides the trigger or press value in the 0..1 range.")]
        // 通常绑定到扳机键或按压输入，值范围是 0 到 1。
        public InputActionReference m_PressValueAction;


        [Min(0f)]
        [Tooltip("Maximum extra distance the probe moves into the surface while the trigger is held.")]
        // 扳机完全按下时，探针额外向命中表面内部移动的最大距离。
        public float m_MaxPressDepth = 0.012f;


        [Min(0f)]
        [Tooltip("How quickly the virtual poke depth follows the trigger value.")]
        // 按压深度跟随输入值的速度，用于让虚拟按压更平滑。
        float m_PressDepthSpeed = 0.05f;


        [Tooltip("When there is no 3D hit, place the probe at the curve end instead of disabling it.")]
        // 没有命中物体时，是否仍让探针停在射线末端/备用距离处。
        public bool m_FollowRayEndWhenNoHit = true;


        [Min(0.01f)]
        [Tooltip("Fallback distance used when no valid curve endpoint is available.")]
        // 射线没有有效终点时，从射线原点向前放置探针的距离。
        public float m_NoHitDistance = 5f;


        [Range(0f, 1f)]
        [Tooltip("Minimum press input required before the current key is locked for pressing.")]
        // 扳机值超过该阈值后，当前悬停按键会被锁定为正在按压的键。
        float m_PressActivationThreshold = 0.1f;


        [Tooltip("Extra depth used to keep the probe overlapping keys while swipe mode is active.")]
        // 滑动输入时使用的固定接触深度，让探针持续与按键触发器重叠。
        float m_SwipeContactDepth = 0.003f;

        [Header("Dwell Settings")]

        [Range(0f, 1f)]
        [Tooltip("Low-intensity highlight applied to the currently hovered key in ray press mode.")]
        // 射线按压模式下，仅悬停但未真正按下时的低强度高亮。
        public float m_PressHoverHighlight = 0.22f;


        [Min(0f)]
        [Tooltip("Surface offset used only while dwell mode is active.")]
        // 凝视模式下使用更大的表面偏移，避免探针过深导致误触。
        float m_DwellSurfaceOffset = 0.008f;


        [Min(0.05f)]
        [Tooltip("How long the ray must remain on the same key before it is automatically entered.")]
        // 凝视停留多久后自动完成一次按键输入。
        float m_DwellDuration = 0.6f;

        SphereCollider m_SphereCollider;
        Rigidbody m_Rigidbody;
        Renderer[] m_SelfRenderers;
        // NearFarInteractor 实现了该接口，用来读取射线曲线的终点和法线。
        ICurveInteractionDataProvider m_CurveProvider;
        // 当前平滑后的虚拟按压深度。
        float m_CurrentPressDepth;
        // 记录是否由本脚本启用了 InputAction，禁用时只关闭自己打开过的 action。
        bool m_EnabledPressAction;
        bool m_LastVisualActive;
        bool m_LastColliderActive;
        // 当前被按压/凝视锁定的按键。
        VRKeyboardKey m_CurrentPressedKey;
        // 当前只做高亮反馈的按键。
        VRKeyboardKey m_CurrentHighlightedKey;
        float m_DwellStartTime;
        bool m_DwellTriggered;
        // 复用的碰撞检测缓存，避免每帧分配数组。
        readonly Collider[] m_KeyOverlapBuffer = new Collider[16];
        VRKeyboardInputMode m_InputMode = VRKeyboardInputMode.Press;

        void Reset()
        {
            // 自动从父级寻找 XR 射线交互器，并尝试把第一个子物体作为可视探针。
            m_NearFarInteractor = GetComponentInParent<NearFarInteractor>();
            m_Visual = transform.childCount > 0 ? transform.GetChild(0) : null;
        }

        void Awake()
        {
            if (m_NearFarInteractor == null)
                m_NearFarInteractor = GetComponentInParent<NearFarInteractor>();

            m_CurveProvider = m_NearFarInteractor;
            m_SphereCollider = GetComponent<SphereCollider>();
            m_Rigidbody = GetComponent<Rigidbody>();
            m_SelfRenderers = GetComponentsInChildren<Renderer>(true);

            // 探针只需要通过脚本移动和参与触发检测，不需要物理模拟。
            if (m_Rigidbody != null)
            {
                m_Rigidbody.useGravity = false;
                m_Rigidbody.isKinematic = true;
            }

            if (m_SphereCollider != null)
                m_SphereCollider.enabled = false;

            m_LastColliderActive = false;
        }

        void OnEnable()
        {
            // 如果输入 action 还没启用，由本脚本临时启用，并在 OnDisable 中恢复。
            var action = m_PressValueAction != null ? m_PressValueAction.action : null;
            if (action != null && !action.enabled)
            {
                action.Enable();
                m_EnabledPressAction = true;
            }
        }

        void OnDisable()
        {
            // 禁用时释放所有对按键的外部按压/高亮控制。
            ReleaseCurrentPressedKey();
            ClearHighlightedKey();
            ResetDwellState();
            m_CurrentPressDepth = 0f;

            if (m_SphereCollider != null)
                m_SphereCollider.enabled = false;

            m_LastColliderActive = false;
            SetVisualActive(false);
            m_LastVisualActive = false;

            if (!m_EnabledPressAction)
                return;

            var action = m_PressValueAction != null ? m_PressValueAction.action : null;//如果 m_PressValueAction 不为空，就取它里面的 .action；否则返回空
            if (action != null && action.enabled)
                action.Disable();

            m_EnabledPressAction = false;
        }

        public void SetInputMode(VRKeyboardInputMode inputMode)//切换输入模式
        {
            if (m_InputMode == inputMode)
                return;

            // 切换输入模式时清掉旧模式残留的按压、深度和凝视状态。
            m_InputMode = inputMode;
            ReleaseCurrentPressedKey();
            m_CurrentPressDepth = 0f;
            ResetDwellState();
        }

        public bool IsSwipeActivationHeld()
        {
            // 滑动输入用这个判断用户是否按住了激活键。
            return GetPressValue01() >= m_PressActivationThreshold;
        }

        void LateUpdate()
        {
            if (m_CurveProvider == null)
                return;

            // 读取 XR 射线曲线终点：可能是真实 3D 命中、UI 命中、空命中或无终点。
            var endPointType = m_CurveProvider.TryGetCurveEndPoint(out var endPoint);//读取射线终点
            if (endPointType == EndPointType.None)//如果没有终点：
            {
                // 没有射线终点时，释放当前按键
                ReleaseCurrentPressedKey();
                ClearHighlightedKey();
                ResetDwellState();
                m_CurrentPressDepth = 0f;
                if (m_FollowRayEndWhenNoHit && TryGetFallbackPosition(out var fallbackPosition))
                    MoveProbe(fallbackPosition, true, false);
                else
                    MoveProbe(transform.position, true, false);
                return;
            }

            if (!m_FollowRayEndWhenNoHit && endPointType == EndPointType.EmptyCastHit)
            {
                // 配置为无命中时不跟随，则隐藏/停用探针的碰撞参与。
                ReleaseCurrentPressedKey();
                ClearHighlightedKey();
                ResetDwellState();
                m_CurrentPressDepth = 0f;
                MoveProbe(transform.position, true, false);
                return;
            }

            var pressValue = GetPressValue01(); //读取扳机值：
            var targetPressDepth = 0f;

            //press
            if (m_InputMode == VRKeyboardInputMode.Press)
            {
                // Press 模式：根据扳机值计算按压深度。找当前射线探针悬停在哪个键上。如果扳机超过阈值，就锁定这个键。调用这个键的外部按压方法：
                ResetDwellState();
                targetPressDepth = pressValue * m_MaxPressDepth;//按压深度 = 扳机值 * 最大按压深度
                var hoveredKey = FindKeyAtProbePosition();//找到悬停在哪个键上

                if (pressValue >= m_PressActivationThreshold)
                {
                    // 达到阈值后锁定第一次悬停到的按键，避免按压过程中滑到旁边键。
                    if (m_CurrentPressedKey == null)
                        m_CurrentPressedKey = hoveredKey;

                    if (m_CurrentPressedKey != null)
                    {
                        m_CurrentPressedKey.SetExternalPress01(pressValue);//外部按压当前按压的键
                        UpdateHighlightedKey(m_CurrentPressedKey, Mathf.Max(m_PressHoverHighlight, pressValue));//高亮显示当前按压的键
                    }
                    else
                    {
                        ClearHighlightedKey();
                    }
                }
                else
                {
                    // 未按下时只显示悬停高亮，不真正压键。
                    ReleaseCurrentPressedKey();
                    if (hoveredKey != null)
                        UpdateHighlightedKey(hoveredKey, m_PressHoverHighlight);
                    else
                        ClearHighlightedKey();
                }
            }
            //dwell
            else if (m_InputMode == VRKeyboardInputMode.Dwell)
            {
                // Dwell 模式：不依赖扳机，停留在同一个按键上足够久后自动输入。
                targetPressDepth = 0f;
                UpdateDwellPress();
            }
            //swipe
            else
            {
                // Swipe 模式：不锁定单个按键，只保持探针和键盘有轻微重叠，供滑动识别使用。
                ReleaseCurrentPressedKey();
                ClearHighlightedKey();
                ResetDwellState();
                targetPressDepth = m_SwipeContactDepth;
            }

            // 平滑当前按压深度，避免探针随输入值瞬间跳动。
            var maxStep = m_PressDepthSpeed <= 0f ? Mathf.Max(m_MaxPressDepth, m_SwipeContactDepth) : m_PressDepthSpeed * Time.deltaTime;
            m_CurrentPressDepth = Mathf.MoveTowards(m_CurrentPressDepth, targetPressDepth, maxStep);

            var targetPosition = endPoint;
            var normalType = m_CurveProvider.TryGetCurveEndNormal(out var endNormal);
            var surfaceOffset = m_InputMode == VRKeyboardInputMode.Dwell
                ? m_DwellSurfaceOffset
                : m_BaseSurfaceOffset;

            // 对 3D 命中点，沿命中法线反方向推进一点，使探针进入表面并能碰到按键触发器。
            if (normalType != EndPointType.None && endPointType != EndPointType.UI)
                targetPosition = endPoint - endNormal.normalized * (surfaceOffset + m_CurrentPressDepth);

            // 只有 Swipe 模式需要启用探针碰撞体参与连续重叠检测。
            var colliderActive = m_InputMode == VRKeyboardInputMode.Swipe;
            MoveProbe(targetPosition, true, colliderActive);

            if (Time.frameCount % 60 == 0)
                Debug.Log($"probe mode={m_InputMode}, endpoint type={endPointType}, pressValue={pressValue:F3}, pressDepth={m_CurrentPressDepth:F4}, pos={transform.position}");
        }

        void UpdateDwellPress()
        {
            var hoveredKey = FindKeyAtProbePosition();

            if (hoveredKey == null)
            {
                // 凝视离开按键后，取消当前进度。
                ReleaseCurrentPressedKey();
                ClearHighlightedKey();
                ResetDwellState();
                return;
            }

            if (hoveredKey != m_CurrentPressedKey)
            {
                // 凝视切换到新按键时重新计时。
                ReleaseCurrentPressedKey();
                m_CurrentPressedKey = hoveredKey;
                m_DwellStartTime = Time.time;
                m_DwellTriggered = false;
            }

            if (m_CurrentPressedKey == null)
                return;

            var elapsed = Time.time - m_DwellStartTime;
            var normalizedPress = Mathf.Clamp01(elapsed / Mathf.Max(0.05f, m_DwellDuration));//计算凝视进度，0~1
            UpdateHighlightedKey(m_CurrentPressedKey, normalizedPress);

            if (m_DwellTriggered)
            {
                // 已触发后保持满进度，直到射线离开或切换到其他按键。
                m_CurrentPressedKey.SetDwellProgress01(1f);
                m_CurrentPressedKey.SetExternalPress01(1f);
                return;
            }

            // 凝视进度同时驱动高亮和外部按压，到 1 时由 VRKeyboardKey 触发 onPressed。
            m_CurrentPressedKey.SetDwellProgress01(normalizedPress);
            m_CurrentPressedKey.SetExternalPress01(normalizedPress);

            if (normalizedPress >= 1f)
                m_DwellTriggered = true;
        }

        bool TryGetFallbackPosition(out Vector3 fallbackPosition)
        {
            fallbackPosition = Vector3.zero;

            // 没有有效射线终点时，用射线原点向前的固定距离作为备用探针位置。
            var origin = m_CurveProvider != null ? m_CurveProvider.curveOrigin : null;
            if (origin == null)
                return false;

            fallbackPosition = origin.position + origin.forward * m_NoHitDistance;
            return true;
        }

        float GetPressValue01()
        {
            var action = m_PressValueAction != null ? m_PressValueAction.action : null;
            if (action == null)
                return 0f;

            float value;
            try
            {
                // 优先按连续数值读取，适合扳机这种 0..1 输入。
                value = action.ReadValue<float>();
            }
            catch
            {
                // 某些 action 可能是按钮类型，读取 float 失败时退回到 IsPressed。
                return action.IsPressed() ? 1f : 0f;
            }

            return Mathf.Clamp01(value);
        }

        VRKeyboardKey FindKeyAtProbePosition()
        {
            if (m_SphereCollider == null)
                return null;

            // 根据探针球体碰撞器的世界半径查找附近所有触发器。
            var lossyScale = transform.lossyScale;
            var maxScale = Mathf.Max(Mathf.Abs(lossyScale.x), Mathf.Abs(lossyScale.y), Mathf.Abs(lossyScale.z));
            var worldRadius = Mathf.Max(0.0005f, m_SphereCollider.radius * maxScale);

            var hitCount = Physics.OverlapSphereNonAlloc(
                transform.position,
                worldRadius,
                m_KeyOverlapBuffer,
                ~0,
                QueryTriggerInteraction.Collide);

            VRKeyboardKey bestKey = null;
            var bestDistance = float.PositiveInfinity;

            // 如果同时重叠多个按键，选择离探针最近的那个。
            for (var i = 0; i < hitCount; i++)
            {
                var hitCollider = m_KeyOverlapBuffer[i];
                if (hitCollider == null)
                    continue;

                var key = hitCollider.GetComponent<VRKeyboardKey>() ?? hitCollider.GetComponentInParent<VRKeyboardKey>();
                if (key == null || key.pressCollider == null)
                    continue;

                var closestPoint = key.pressCollider.ClosestPoint(transform.position);
                var distance = (closestPoint - transform.position).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestKey = key;
                }
            }

            return bestKey;
        }

        void ReleaseCurrentPressedKey()
        {
            if (m_CurrentPressedKey == null)
                return;

            // 清掉对当前键的外部按压和凝视进度，让键自己回弹。
            m_CurrentPressedKey.ClearDwellProgress();
            m_CurrentPressedKey.ClearExternalPress();
            m_CurrentPressedKey = null;
        }

        void ResetDwellState()
        {
            //重设状态
            m_DwellStartTime = 0f;
            m_DwellTriggered = false;
        }

        void UpdateHighlightedKey(VRKeyboardKey key, float progress01)
        {
            if (key == null)
            {
                ClearHighlightedKey();
                return;
            }

            if (m_CurrentHighlightedKey != key)
            {
                // 同一时间只让一个按键保留外部高亮。
                ClearHighlightedKey();
                m_CurrentHighlightedKey = key;
            }

            m_CurrentHighlightedKey.SetHighlightProgress01(progress01);
        }

        void ClearHighlightedKey()
        {
            //清除当前高亮按键的高亮
            if (m_CurrentHighlightedKey == null)
                return;

            m_CurrentHighlightedKey.ClearHighlightProgress();
            m_CurrentHighlightedKey = null;
        }

        void MoveProbe(Vector3 worldPosition, bool visualActive, bool colliderActive)
        {
            transform.position = worldPosition;

            // 只有状态变化时才切换组件，避免每帧重复设置。
            if (m_SphereCollider != null && m_LastColliderActive != colliderActive)
            {
                m_SphereCollider.enabled = colliderActive;
                m_LastColliderActive = colliderActive;
            }

            if (m_LastVisualActive != visualActive)
            {
                SetVisualActive(visualActive);
                m_LastVisualActive = visualActive;
            }
        }

        void SetVisualActive(bool active)
        {
            if (m_Visual == null || m_Visual == transform)
            {
                // 没有单独 visual 子物体时，直接开关自身和子物体的 Renderer。
                SetRendererState(m_SelfRenderers, active);
                return;
            }

            m_Visual.gameObject.SetActive(active);
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
    }
}
