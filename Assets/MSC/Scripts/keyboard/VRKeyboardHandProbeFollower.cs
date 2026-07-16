using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.XR.Hands;
using System.Collections.Generic;

namespace VRTyping.Keyboard
{
    // HandTouch 输入的核心脚本：
    // 1. 从手模型骨骼、XR Hands 或 Input System 读取手指位置。
    // 2. 把不可见/可见的小球探针移动到指尖，让键盘按键通过碰撞触发。
    // 3. 在手部模式下隐藏控制器模型和提示，并限制指尖/手模型不要穿透键盘。
    public class VRKeyboardHandProbeFollower : MonoBehaviour
    {
        [Header("Probe Setup")]
        // 左右食指探针；HandTouch 模式只使用这两个，HandTouch10 模式会额外创建其他手指探针。
        [SerializeField] Transform m_LeftProbe;
        [SerializeField] Transform m_RightProbe;
        // 探针半径同时影响 SphereCollider 和调试小球大小。
        [SerializeField, Min(0.001f)] float m_ProbeRadius = 0.012f;
        // 关闭后探针仍然参与碰撞输入，只是不显示小球模型。
        [SerializeField] bool m_ShowProbeVisuals = true;

        [Header("Input")]
        // 可分别禁用左手或右手，便于单手测试或只允许一只手输入。
        [SerializeField] bool m_UseLeftHand = true;
        [SerializeField] bool m_UseRightHand = true;
        // XR Hands 没有 pokePosition 时，允许回退到 pinchPosition/devicePosition。
        [SerializeField] bool m_UseFallbackHandPositions = true;
        // 定时重新寻找设备和场景中的手骨骼，避免运行时切换设备后引用丢失。
        [SerializeField, Min(0.05f)] float m_DeviceRefreshInterval = 1f;
        [SerializeField] bool m_ShowDebugStatus = true;

        [Header("Pose Space")]
        // 优先跟随场景里的手模型骨骼，这样小球能贴到你实际看见的手指上。
        [SerializeField] bool m_PreferHandSkeletonTargets = true;
        // false = 左右食指输入；true = 十个手指都生成输入探针。
        [SerializeField] bool m_UseAllFingerTips;
        // 这些 Transform 指向手模型的各个指尖骨骼；为空时会按常见命名自动查找。
        [SerializeField] Transform m_LeftThumbTipTarget;
        [SerializeField] Transform m_LeftIndexTipTarget;
        [SerializeField] Transform m_LeftMiddleTipTarget;
        [SerializeField] Transform m_LeftRingTipTarget;
        [SerializeField] Transform m_LeftLittleTipTarget;
        [SerializeField] Transform m_RightThumbTipTarget;
        [SerializeField] Transform m_RightIndexTipTarget;
        [SerializeField] Transform m_RightMiddleTipTarget;
        [SerializeField] Transform m_RightRingTipTarget;
        [SerializeField] Transform m_RightLittleTipTarget;
        // XR Hands 返回的 pose 可能是手部可视化根节点空间，设置此根节点后会转换到世界坐标。
        [SerializeField] Transform m_HandPoseSpaceRoot;
        // 用于微调探针相对指尖的位置，例如让小球略微贴近指腹。
        [SerializeField] Vector3 m_ProbePoseOffset;

        [Header("Keyboard Surface Clamp")]
        // 限制探针最大下压深度，防止指尖小球穿过键盘。
        [SerializeField] bool m_EnableKeyboardSurfaceClamp = true;
        // 开启后不只限制小球，也会把手模型整体沿修正量拉回键盘表面。
        [SerializeField] bool m_ClampHandVisualsToKeyboard = true;
        // 指尖允许压入按键表面的最大深度；超过后会停在这个深度。
        [SerializeField, Min(0f)] float m_MaxSurfacePressDepth = 0.025f;
        // 在指尖附近搜索键盘按键的范围，太小可能找不到按键，太大可能匹配到旁边按键。
        [SerializeField, Min(0.001f)] float m_SurfaceClampSearchRadius = 0.08f;
        // 允许指尖在按键边缘外一点点仍然参与防穿透判断。
        [SerializeField, Min(0f)] float m_SurfaceClampLateralPadding = 0.01f;
        // 手模型根节点；用于对整只手施加防穿透偏移。
        [SerializeField] Transform m_LeftHandVisualRoot;
        [SerializeField] Transform m_RightHandVisualRoot;

        [Header("Hand Visuals")]
        // 检测到手部追踪时隐藏控制器模型，避免手和手柄同时显示。
        [SerializeField] bool m_HideControllerVisualsWhenHandTracked = true;
        [SerializeField] Transform m_LeftControllerVisualRoot;
        [SerializeField] Transform m_RightControllerVisualRoot;
        // 手部模式下隐藏控制器按键提示/射线提示。
        [SerializeField] bool m_HideControllerHintsInHandMode = true;
        [SerializeField] Transform m_LeftControllerHintRoot;
        [SerializeField] Transform m_RightControllerHintRoot;
        // 控制器模型和提示可能由 XR Interaction Toolkit 运行时生成，所以也需要定时刷新。
        [SerializeField, Min(0.05f)] float m_ControllerVisualRefreshInterval = 1f;

        // 缓存当前可用的 XRHandSubsystem，避免每帧分配列表。
        readonly List<XRHandSubsystem> m_HandSubsystems = new List<XRHandSubsystem>();
        // m_Left/m_Right 代表食指探针；其余字段是 HandTouch10 的额外手指探针。
        HandProbe m_Left;
        HandProbe m_Right;
        HandProbe m_LeftThumb;
        HandProbe m_LeftMiddle;
        HandProbe m_LeftRing;
        HandProbe m_LeftLittle;
        HandProbe m_RightThumb;
        HandProbe m_RightMiddle;
        HandProbe m_RightRing;
        HandProbe m_RightLittle;
        Transform m_LeftThumbProbe;
        Transform m_LeftMiddleProbe;
        Transform m_LeftRingProbe;
        Transform m_LeftLittleProbe;
        Transform m_RightThumbProbe;
        Transform m_RightMiddleProbe;
        Transform m_RightRingProbe;
        Transform m_RightLittleProbe;
        XRHandSubsystem m_HandSubsystem;
        XRHandSubsystem m_SubscribedHandSubsystem;
        float m_NextDeviceRefreshTime;
        float m_NextControllerVisualRefreshTime;
        XRHandSubsystem.UpdateSuccessFlags m_LastUpdateFlags;
        XRHandSubsystem.UpdateType m_LastUpdateType;
        string m_LastSource = "none";
        // 记录控制器 Renderer/提示物体初始状态，退出手部模式时按原状态恢复。
        RendererState[] m_LeftControllerRenderers;
        RendererState[] m_RightControllerRenderers;
        GameObjectState m_LeftControllerHintState;
        GameObjectState m_RightControllerHintState;
        // 防穿透检测复用数组，避免 Physics 查询时产生 GC。
        readonly Collider[] m_SurfaceClampHits = new Collider[32];
        // 记录手模型根节点当前被防穿透逻辑移动了多少，后续可以抵消/恢复。
        VisualRootState m_LeftHandVisualState;
        VisualRootState m_RightHandVisualState;

        static readonly string[] k_LeftControllerHintNames =
        {
            "Affordance Callouts Left",
            "Left Affordance Callouts",
            "Left Controller Hints",
            "Left Controller Tips",
        };

        static readonly string[] k_RightControllerHintNames =
        {
            "Affordance Callouts Right",
            "Right Affordance Callouts",
            "Right Controller Hints",
            "Right Controller Tips",
        };

        class HandControls
        {
            // Input System 的回退输入通道，主要用于没有完整 XR Hands 关节数据的运行时。
            public InputDevice device;
            public Vector3Control position;
            public QuaternionControl rotation;
            public ButtonControl isTracked;
        }

        class HandProbe
        {
            // 一个 HandProbe 就是一个能触发 VRKeyboardKey 的球形触碰点。
            public Transform root;
            public SphereCollider collider;
            public VRKeyboardPressProbe pressProbe;
            public Renderer[] renderers;
            public HandControls controls;
            public bool active;
            public bool tracked;
            public bool poseValid;
            public string lastSource;
        }

        struct RendererState
        {
            // 缓存 Renderer 原本是否开启，用于手部模式退出时恢复。
            public Renderer renderer;
            public bool initiallyEnabled;
        }

        struct GameObjectState
        {
            // 缓存提示物体原本是否激活，用于手部模式退出时恢复。
            public GameObject gameObject;
            public bool initiallyActive;
        }

        struct VisualRootState
        {
            // 手模型根节点及当前防穿透偏移量。
            public Transform root;
            public Vector3 worldOffset;
        }

        void Reset()
        {
            EnsureProbeObjects();
        }

        void Awake()
        {
            // 初始化时创建探针、绑定设备变更事件，并缓存当前场景中能找到的手部目标。
            EnsureProbeObjects();
            InputSystem.onDeviceChange += OnDeviceChange;
            RefreshHandDevices();
            RefreshFingerTargets();
            RefreshHandPoseSpace();
            RefreshControllerVisuals();
            SetProbeActive(m_Left, false);
            SetProbeActive(m_Right, false);
        }

        void OnEnable()
        {
            RefreshHandDevices();
            RefreshFingerTargets();
            RefreshHandPoseSpace();
            RefreshControllerVisuals();
            SubscribeToHandSubsystem();
        }

        void OnDisable()
        {
            // 关闭脚本时还原可视状态和手模型偏移，避免切换模式后残留隐藏/偏移。
            UnsubscribeFromHandSubsystem();
            SetProbeActive(m_Left, false);
            SetProbeActive(m_Right, false);
            SetExtraFingerProbesActive(false);
            SetControllerVisualsVisible(m_LeftControllerRenderers, true);
            SetControllerVisualsVisible(m_RightControllerRenderers, true);
            SetGameObjectVisible(m_LeftControllerHintState, true);
            SetGameObjectVisible(m_RightControllerHintState, true);
            ApplyHandVisualOffset(ref m_LeftHandVisualState, Vector3.zero);
            ApplyHandVisualOffset(ref m_RightHandVisualState, Vector3.zero);
        }

        void OnDestroy()
        {
            InputSystem.onDeviceChange -= OnDeviceChange;
        }

        void OnValidate()
        {
            if (m_ProbeRadius < 0.001f)
                m_ProbeRadius = 0.001f;

            ApplyProbeRadius(m_LeftProbe);
            ApplyProbeRadius(m_RightProbe);
            ApplyProbeRadius(m_LeftThumbProbe);
            ApplyProbeRadius(m_LeftMiddleProbe);
            ApplyProbeRadius(m_LeftRingProbe);
            ApplyProbeRadius(m_LeftLittleProbe);
            ApplyProbeRadius(m_RightThumbProbe);
            ApplyProbeRadius(m_RightMiddleProbe);
            ApplyProbeRadius(m_RightRingProbe);
            ApplyProbeRadius(m_RightLittleProbe);
        }

        public void SetUseAllFingerTips(bool useAllFingerTips)
        {
            // 输入模式切换时调用：HandTouch=false，HandTouch10=true。
            if (m_UseAllFingerTips == useAllFingerTips)
                return;

            m_UseAllFingerTips = useAllFingerTips;
            if (Application.isPlaying)
                EnsureProbeObjects();

            if (!m_UseAllFingerTips)
            {
                SetExtraFingerProbesActive(false);
                ApplyHandVisualOffset(ref m_LeftHandVisualState, Vector3.zero);
                ApplyHandVisualOffset(ref m_RightHandVisualState, Vector3.zero);
            }
        }

        public void RefreshProbesNow()
        {
            EnsureProbeObjects();
            RefreshHandDevices();
            RefreshFingerTargets();
            RefreshHandPoseSpace();
            RefreshControllerVisuals();
            UpdateHandProbes(true, m_UseLeftHand);
            UpdateHandProbes(false, m_UseRightHand);
            UpdateControllerVisuals();
        }

        void LateUpdate()
        {
            // 手部设备、手骨骼和 XR 组件在运行时可能会重建，所以按间隔刷新引用。
            if (Time.unscaledTime >= m_NextDeviceRefreshTime)
            {
                RefreshHandDevices();
                RefreshFingerTargets();
                RefreshHandVisualRoots();
                RefreshHandPoseSpace();
                m_NextDeviceRefreshTime = Time.unscaledTime + m_DeviceRefreshInterval;
            }

            if (Time.unscaledTime >= m_NextControllerVisualRefreshTime)
            {
                // 控制器模型/提示也可能延迟生成，定时刷新后才能正确隐藏。
                RefreshControllerVisuals();
                m_NextControllerVisualRefreshTime = Time.unscaledTime + m_ControllerVisualRefreshInterval;
            }

            // 每帧把左右手对应的探针移动到当前指尖位置。
            UpdateHandProbes(true, m_UseLeftHand);
            UpdateHandProbes(false, m_UseRightHand);
            UpdateControllerVisuals();
        }

        void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (change == InputDeviceChange.Added ||
                change == InputDeviceChange.Removed ||
                change == InputDeviceChange.Reconnected ||
                change == InputDeviceChange.Disconnected ||
                change == InputDeviceChange.ConfigurationChanged)
            {
                RefreshHandDevices();
            }
        }

        void RefreshHandDevices()
        {
            // 这里同时刷新三类来源：XR Hands 子系统、场景手模型根节点、Input System 回退设备。
            EnsureProbeObjects();
            RefreshHandSubsystem();
            RefreshHandVisualRoots();
            m_Left.controls = FindHandControls(CommonUsages.LeftHand);
            m_Right.controls = FindHandControls(CommonUsages.RightHand);
        }

        void UpdateHandProbes(bool leftHand, bool handEnabled)
        {
            var indexProbe = leftHand ? m_Left : m_Right;
            if (!m_UseAllFingerTips)
            {
                // 普通 HandTouch：只更新当前手的食指探针，关闭同一只手的其他手指探针。
                UpdateProbe(indexProbe, handEnabled);
                SetHandExtraFingerProbesActive(leftHand, false);
                return;
            }

            if (!handEnabled)
            {
                SetHandFingerProbesActive(leftHand, false);
                ApplyHandVisualOffset(leftHand, Vector3.zero);
                return;
            }

            if (!IsHandCurrentlyTracked(leftHand))
            {
                // 手丢失追踪时关闭整只手的探针，并把手模型偏移恢复，防止残留在键盘表面。
                SetHandFingerProbesActive(leftHand, false);
                ApplyHandVisualOffset(leftHand, Vector3.zero);
                return;
            }

            // 十指模式下先根据五个指尖共同计算手模型偏移，再更新每个手指探针。
            var currentOffset = leftHand ? m_LeftHandVisualState.worldOffset : m_RightHandVisualState.worldOffset;
            var visualOffset = ComputeHandVisualClampOffset(leftHand, currentOffset);
            ApplyHandVisualOffset(leftHand, visualOffset);

            if (leftHand)
            {
                UpdateProbeFromFingerTarget(m_LeftThumb, m_LeftThumbTipTarget);
                UpdateProbeFromFingerTarget(m_Left, m_LeftIndexTipTarget);
                UpdateProbeFromFingerTarget(m_LeftMiddle, m_LeftMiddleTipTarget);
                UpdateProbeFromFingerTarget(m_LeftRing, m_LeftRingTipTarget);
                UpdateProbeFromFingerTarget(m_LeftLittle, m_LeftLittleTipTarget);
            }
            else
            {
                UpdateProbeFromFingerTarget(m_RightThumb, m_RightThumbTipTarget);
                UpdateProbeFromFingerTarget(m_Right, m_RightIndexTipTarget);
                UpdateProbeFromFingerTarget(m_RightMiddle, m_RightMiddleTipTarget);
                UpdateProbeFromFingerTarget(m_RightRing, m_RightRingTipTarget);
                UpdateProbeFromFingerTarget(m_RightLittle, m_RightLittleTipTarget);
            }
        }

        bool IsHandCurrentlyTracked(bool leftHand)
        {
            RefreshHandSubsystem();
            if (m_HandSubsystem == null || !m_HandSubsystem.running)
                // 没有 XRHandSubsystem 时不强行判定失败，允许 Input System 或场景骨骼回退继续工作。
                return true;

            return leftHand ? m_HandSubsystem.leftHand.isTracked : m_HandSubsystem.rightHand.isTracked;
        }

        Vector3 ComputeHandVisualClampOffset(bool leftHand, Vector3 currentOffset)
        {
            if (!m_ClampHandVisualsToKeyboard)
                return Vector3.zero;

            // 选择“需要修正最多”的那个指尖作为整只手的偏移量，保证最深穿透的手指被顶回表面。
            var bestCorrection = Vector3.zero;
            if (leftHand)
            {
                AccumulateFingerClampOffset(m_LeftThumbTipTarget, currentOffset, ref bestCorrection);
                AccumulateFingerClampOffset(m_LeftIndexTipTarget, currentOffset, ref bestCorrection);
                AccumulateFingerClampOffset(m_LeftMiddleTipTarget, currentOffset, ref bestCorrection);
                AccumulateFingerClampOffset(m_LeftRingTipTarget, currentOffset, ref bestCorrection);
                AccumulateFingerClampOffset(m_LeftLittleTipTarget, currentOffset, ref bestCorrection);
            }
            else
            {
                AccumulateFingerClampOffset(m_RightThumbTipTarget, currentOffset, ref bestCorrection);
                AccumulateFingerClampOffset(m_RightIndexTipTarget, currentOffset, ref bestCorrection);
                AccumulateFingerClampOffset(m_RightMiddleTipTarget, currentOffset, ref bestCorrection);
                AccumulateFingerClampOffset(m_RightRingTipTarget, currentOffset, ref bestCorrection);
                AccumulateFingerClampOffset(m_RightLittleTipTarget, currentOffset, ref bestCorrection);
            }

            return bestCorrection;
        }

        void AccumulateFingerClampOffset(Transform target, Vector3 currentOffset, ref Vector3 bestCorrection)
        {
            if (target == null || !target.gameObject.activeInHierarchy)
                return;

            // target.position 已经包含当前手模型偏移；先减掉旧偏移，得到真实追踪目标，再测试是否穿透。
            var desiredPosition = target.position - currentOffset + target.rotation * m_ProbePoseOffset;
            if (!TryClampToKeyboardSurface(desiredPosition, out _, out var correction))
                return;

            // 多指同时碰到键盘时，使用幅度最大的修正量移动整只手。
            if (correction.sqrMagnitude > bestCorrection.sqrMagnitude)
                bestCorrection = correction;
        }

        void UpdateProbeFromFingerTarget(HandProbe probe, Transform target)
        {
            if (probe == null || probe.root == null)
                return;

            if (target == null || !target.gameObject.activeInHierarchy)
            {
                MarkProbeNotTracked(probe, target != null ? $"{target.name} inactive" : "finger target missing");
                SetProbeActive(probe, false);
                return;
            }

            // 十指模式直接跟随每个可见手模型的指尖骨骼；手模型已经在前面做过整体防穿透偏移。
            probe.root.SetPositionAndRotation(
                target.position + target.rotation * m_ProbePoseOffset,
                target.rotation);
            probe.tracked = true;
            probe.poseValid = true;
            probe.lastSource = target.name;
            m_LastSource = target.name;
            SetProbeActive(probe, true);
        }

        void RefreshHandSubsystem()
        {
            if (m_HandSubsystem != null && m_HandSubsystem.running)
            {
                // 已经有正在运行的子系统时只确保事件订阅存在。
                SubscribeToHandSubsystem();
                return;
            }

            m_HandSubsystem = null;
            m_HandSubsystems.Clear();
            SubsystemManager.GetSubsystems(m_HandSubsystems);

            for (var i = 0; i < m_HandSubsystems.Count; i++)
            {
                var subsystem = m_HandSubsystems[i];
                if (subsystem != null && subsystem.running)
                {
                    // 优先使用正在运行的 XRHandSubsystem。
                    m_HandSubsystem = subsystem;
                    SubscribeToHandSubsystem();
                    return;
                }
            }

            if (m_HandSubsystems.Count > 0)
                m_HandSubsystem = m_HandSubsystems[0];
        }

        void SubscribeToHandSubsystem()
        {
            if (m_HandSubsystem == null || !m_HandSubsystem.running)
                return;

            if (m_SubscribedHandSubsystem == m_HandSubsystem)
                return;

            UnsubscribeFromHandSubsystem();
            m_SubscribedHandSubsystem = m_HandSubsystem;
            m_SubscribedHandSubsystem.updatedHands += OnUpdatedHands;
        }

        void UnsubscribeFromHandSubsystem()
        {
            if (m_SubscribedHandSubsystem != null)
                m_SubscribedHandSubsystem.updatedHands -= OnUpdatedHands;

            m_SubscribedHandSubsystem = null;
        }

        void OnUpdatedHands(XRHandSubsystem subsystem, XRHandSubsystem.UpdateSuccessFlags updateFlags, XRHandSubsystem.UpdateType updateType)
        {
            // XR Hands 有更新事件时立即刷新探针，比只等 LateUpdate 更及时。
            m_LastUpdateFlags = updateFlags;
            m_LastUpdateType = updateType;

            if (!isActiveAndEnabled || subsystem == null)
                return;

            m_HandSubsystem = subsystem;

            UpdateHandProbes(true, m_UseLeftHand);
            UpdateHandProbes(false, m_UseRightHand);

            UpdateControllerVisuals();
        }

        HandControls FindHandControls(InternedString handUsage)
        {
            // OpenXR Hand Interaction Profile 通常会提供 pokePosition/pokeRotation。
            foreach (var device in InputSystem.devices)
            {
                if (device == null || !device.enabled || !device.usages.Contains(handUsage))
                    continue;

                var position = device.TryGetChildControl<Vector3Control>("pokePosition");
                if (position == null && m_UseFallbackHandPositions)
                    // 不同运行时暴露的字段不同，缺少 pokePosition 时用 pinch/device 位置兜底。
                    position = device.TryGetChildControl<Vector3Control>("pinchPosition") ??
                               device.TryGetChildControl<Vector3Control>("devicePosition");

                if (position == null)
                    continue;

                return new HandControls
                {
                    device = device,
                    position = position,
                    rotation = device.TryGetChildControl<QuaternionControl>("pokeRotation") ??
                               device.TryGetChildControl<QuaternionControl>("deviceRotation"),
                    isTracked = device.TryGetChildControl<ButtonControl>("isTracked"),
                };
            }

            return null;
        }

        void UpdateProbe(HandProbe probe, bool handEnabled)
        {
            if (probe == null || probe.root == null)
                return;

            var controls = probe.controls;
            if (!handEnabled)
            {
                // 对应手被禁用时关闭探针，不参与按键碰撞。
                SetProbeActive(probe, false);
                return;
            }

            var indexTipTarget = probe == m_Left ? m_LeftIndexTipTarget : m_RightIndexTipTarget;
            if (TryUpdateProbeFromSkeletonTarget(probe, indexTipTarget) || TryUpdateProbeFromXRHands(probe))
                // 优先级：手模型骨骼 > XR Hands 原始关节 > Input System pokePosition 回退。
                return;

            if (controls == null || controls.device == null || !controls.device.enabled || controls.position == null)
            {
                SetProbeActive(probe, false);
                return;
            }

            if (controls.isTracked != null && controls.isTracked.ReadValue() < 0.5f)
            {
                SetProbeActive(probe, false);
                return;
            }

            var position = controls.position.ReadValue();
            var rotation = controls.rotation != null ? controls.rotation.ReadValue() : probe.root.rotation;
            SetProbeWorldPose(probe.root, position, rotation);
            probe.tracked = true;
            probe.poseValid = true;
            probe.lastSource = controls.position.path;
            m_LastSource = controls.position.path;
            SetProbeActive(probe, true);
        }

        bool TryUpdateProbeFromSkeletonTarget(HandProbe probe, Transform indexTipTarget)
        {
            if (!m_PreferHandSkeletonTargets || probe == null || probe.root == null || indexTipTarget == null)
                return false;

            if (!indexTipTarget.gameObject.activeInHierarchy)
            {
                // 手模型指尖节点不可用时不能继续使用旧位置，否则探针会停在空中误触。
                MarkProbeNotTracked(probe, $"{indexTipTarget.name} inactive");
                ApplyHandVisualOffset(probe, Vector3.zero);
                return false;
            }

            RefreshHandSubsystem();
            if (m_HandSubsystem != null && m_HandSubsystem.running)
            {
                // 如果 XR Hands 明确告诉我们这只手未追踪，就关闭探针并恢复手模型偏移。
                var hand = probe == m_Left ? m_HandSubsystem.leftHand : m_HandSubsystem.rightHand;
                if (!hand.isTracked)
                {
                    MarkProbeNotTracked(probe, "hand not tracked");
                    ApplyHandVisualOffset(probe, Vector3.zero);
                    return false;
                }
            }

            var currentVisualOffset = GetHandVisualOffset(probe);
            var desiredPosition = indexTipTarget.position - currentVisualOffset +
                indexTipTarget.rotation * m_ProbePoseOffset;
            var clampedPosition = desiredPosition;
            var visualOffset = Vector3.zero;
            if (TryClampToKeyboardSurface(desiredPosition, out clampedPosition, out var clampCorrection))
                // 单食指模式下，探针被限制到最大下压深度，同时手模型根节点也移动同样修正量。
                visualOffset = clampCorrection;

            ApplyHandVisualOffset(probe, visualOffset);
            probe.root.SetPositionAndRotation(clampedPosition, indexTipTarget.rotation);
            probe.tracked = true;
            probe.poseValid = true;
            probe.lastSource = indexTipTarget.name;
            m_LastSource = indexTipTarget.name;
            SetProbeActive(probe, true);
            return true;
        }

        bool TryUpdateProbeFromXRHands(HandProbe probe, XRHandSubsystem.UpdateSuccessFlags requiredFlags = XRHandSubsystem.UpdateSuccessFlags.None)
        {
            // 直接从 XR Hands 的 IndexTip 关节读取 pose；主要作为没有手模型骨骼目标时的通用 OpenXR 路径。
            RefreshHandSubsystem();
            if (m_HandSubsystem == null)
            {
                MarkProbeNotTracked(probe, "no XRHandSubsystem");
                return false;
            }

            if (!m_HandSubsystem.running)
            {
                MarkProbeNotTracked(probe, "XRHandSubsystem not running");
                return false;
            }

            if (requiredFlags != XRHandSubsystem.UpdateSuccessFlags.None &&
                (m_HandSubsystem.updateSuccessFlags & requiredFlags) == XRHandSubsystem.UpdateSuccessFlags.None)
            {
                MarkProbeNotTracked(probe, "no joint update");
                return false;
            }

            var hand = probe == m_Left ? m_HandSubsystem.leftHand : m_HandSubsystem.rightHand;
            if (!hand.isTracked)
            {
                MarkProbeNotTracked(probe, "hand not tracked");
                return false;
            }

            var indexTip = hand.GetJoint(XRHandJointID.IndexTip);
            if (!indexTip.TryGetPose(out var pose))
            {
                // 有追踪不代表每个关节 pose 都有效，关节无效时不要移动探针。
                MarkProbeNotTracked(probe, "IndexTip pose invalid");
                return false;
            }

            SetProbePoseFromXRHandPose(probe.root, pose);
            probe.tracked = true;
            probe.poseValid = true;
            probe.lastSource = "XRHands IndexTip";
            m_LastSource = probe.lastSource;
            SetProbeActive(probe, true);
            return true;
        }

        void SetProbePoseFromXRHandPose(Transform probeRoot, Pose pose)
        {
            if (probeRoot == null)
                return;

            if (m_HandPoseSpaceRoot == null)
                RefreshHandPoseSpace();

            var localPosition = pose.position + pose.rotation * m_ProbePoseOffset;
            if (m_HandPoseSpaceRoot != null)
            {
                // 部分运行时/示例会把手关节 pose 放在 Hand Visualizer/Camera Offset 空间下，这里统一转到世界空间。
                SetProbeWorldPose(
                    probeRoot,
                    m_HandPoseSpaceRoot.TransformPoint(localPosition),
                    m_HandPoseSpaceRoot.rotation * pose.rotation);
                return;
            }

            SetProbeWorldPose(probeRoot, localPosition, pose.rotation);
        }

        void SetProbeWorldPose(Transform probeRoot, Vector3 worldPosition, Quaternion worldRotation)
        {
            if (probeRoot == null)
                return;

            if (TryClampToKeyboardSurface(worldPosition, out var clampedPosition, out _))
                // 回退输入没有手模型可移动，只限制探针本身不穿透键盘。
                worldPosition = clampedPosition;

            probeRoot.SetPositionAndRotation(worldPosition, worldRotation);
        }

        bool TryClampToKeyboardSurface(Vector3 desiredWorldPosition, out Vector3 clampedWorldPosition, out Vector3 clampCorrection)
        {
            // 防穿透核心：如果指尖进入按键太深，就把世界坐标夹到允许的最大下压深度。
            clampedWorldPosition = desiredWorldPosition;
            clampCorrection = Vector3.zero;
            if (!m_EnableKeyboardSurfaceClamp || m_MaxSurfacePressDepth <= 0f)
                return false;

            // 只在指尖附近查找键盘按键，避免整场景扫描。
            var hitCount = Physics.OverlapSphereNonAlloc(
                desiredWorldPosition,
                m_SurfaceClampSearchRadius,
                m_SurfaceClampHits,
                ~0,
                QueryTriggerInteraction.Collide);

            VRKeyboardKey bestKey = null;
            Vector3 bestLocalPoint = default;
            var bestDepth = float.NegativeInfinity;
            var bestLateralDistance = float.PositiveInfinity;

            for (var i = 0; i < hitCount; i++)
            {
                var hit = m_SurfaceClampHits[i];
                if (hit == null)
                    continue;

                var key = hit.GetComponent<VRKeyboardKey>() ?? hit.GetComponentInParent<VRKeyboardKey>();
                if (key == null || key.pressCollider == null)
                    continue;

                var localPoint = key.transform.InverseTransformPoint(desiredWorldPosition);
                if (!TryGetPressDepthAndLateralDistance(key, localPoint, out var depth, out var lateralDistance))
                    continue;

                if (depth <= m_MaxSurfacePressDepth)
                    // 没超过最大允许下压深度时不需要修正。
                    continue;

                if (lateralDistance < bestLateralDistance ||
                    Mathf.Approximately(lateralDistance, bestLateralDistance) && depth > bestDepth)
                {
                    // 多个按键候选时优先选择横向距离最近的；距离相同时选按得更深的。
                    bestKey = key;
                    bestLocalPoint = localPoint;
                    bestDepth = depth;
                    bestLateralDistance = lateralDistance;
                }
            }

            if (bestKey == null)
                return false;

            // 同时受全局最大深度和单个按键 maxPressDistance 限制。
            var maxDepth = Mathf.Min(m_MaxSurfacePressDepth, Mathf.Max(0.001f, bestKey.maxPressDistance));
            clampedWorldPosition = ClampLocalPointToPressDepth(bestKey, bestLocalPoint, maxDepth);
            clampCorrection = clampedWorldPosition - desiredWorldPosition;
            return true;
        }

        bool TryGetPressDepthAndLateralDistance(VRKeyboardKey key, Vector3 localPoint, out float depth, out float lateralDistance)
        {
            // 把“世界中的指尖点”转为“按键本地空间中的按压深度”和“离按键面的横向距离”。
            depth = 0f;
            lateralDistance = float.PositiveInfinity;
            var collider = key.pressCollider;
            var halfSize = collider.size * 0.5f;
            var center = collider.center;

            switch (key.pressAxis)
            {
                case VRKeyboardPressAxis.NegativeX:
                    depth = center.x + halfSize.x - localPoint.x;
                    lateralDistance = GetLateralDistance(localPoint.y, localPoint.z, center.y, center.z, halfSize.y, halfSize.z);
                    break;
                case VRKeyboardPressAxis.PositiveX:
                    depth = localPoint.x - (center.x - halfSize.x);
                    lateralDistance = GetLateralDistance(localPoint.y, localPoint.z, center.y, center.z, halfSize.y, halfSize.z);
                    break;
                case VRKeyboardPressAxis.NegativeY:
                    depth = center.y + halfSize.y - localPoint.y;
                    lateralDistance = GetLateralDistance(localPoint.x, localPoint.z, center.x, center.z, halfSize.x, halfSize.z);
                    break;
                case VRKeyboardPressAxis.PositiveY:
                    depth = localPoint.y - (center.y - halfSize.y);
                    lateralDistance = GetLateralDistance(localPoint.x, localPoint.z, center.x, center.z, halfSize.x, halfSize.z);
                    break;
                case VRKeyboardPressAxis.NegativeZ:
                    depth = center.z + halfSize.z - localPoint.z;
                    lateralDistance = GetLateralDistance(localPoint.x, localPoint.y, center.x, center.y, halfSize.x, halfSize.y);
                    break;
                case VRKeyboardPressAxis.PositiveZ:
                    depth = localPoint.z - (center.z - halfSize.z);
                    lateralDistance = GetLateralDistance(localPoint.x, localPoint.y, center.x, center.y, halfSize.x, halfSize.y);
                    break;
            }

            return depth > 0f && lateralDistance <= m_SurfaceClampLateralPadding;
        }

        float GetLateralDistance(float a, float b, float centerA, float centerB, float halfA, float halfB)
        {
            // 在垂直按压轴的平面上计算离按键矩形区域的距离；落在矩形内部时距离为 0。
            var deltaA = Mathf.Max(0f, Mathf.Abs(a - centerA) - halfA);
            var deltaB = Mathf.Max(0f, Mathf.Abs(b - centerB) - halfB);
            return Mathf.Sqrt(deltaA * deltaA + deltaB * deltaB);
        }

        Vector3 ClampLocalPointToPressDepth(VRKeyboardKey key, Vector3 localPoint, float maxDepth)
        {
            // 根据按键自己的按压轴，把本地点夹到“表面向内 maxDepth”的位置。
            var collider = key.pressCollider;
            var halfSize = collider.size * 0.5f;
            var center = collider.center;

            switch (key.pressAxis)
            {
                case VRKeyboardPressAxis.NegativeX:
                    localPoint.x = center.x + halfSize.x - maxDepth;
                    break;
                case VRKeyboardPressAxis.PositiveX:
                    localPoint.x = center.x - halfSize.x + maxDepth;
                    break;
                case VRKeyboardPressAxis.NegativeY:
                    localPoint.y = center.y + halfSize.y - maxDepth;
                    break;
                case VRKeyboardPressAxis.PositiveY:
                    localPoint.y = center.y - halfSize.y + maxDepth;
                    break;
                case VRKeyboardPressAxis.NegativeZ:
                    localPoint.z = center.z + halfSize.z - maxDepth;
                    break;
                case VRKeyboardPressAxis.PositiveZ:
                    localPoint.z = center.z - halfSize.z + maxDepth;
                    break;
            }

            return key.transform.TransformPoint(localPoint);
        }

        void RefreshHandPoseSpace()
        {
            if (m_HandPoseSpaceRoot != null)
                return;

            // 自动兼容 XR Hands 示例中的 Hand Visualizer，以及 XR Origin 下常见的 Camera Offset。
            m_HandPoseSpaceRoot = FindSceneTransformByName("Hand Visualizer") ??
                FindSceneTransformByName("Camera Offset");
        }

        void RefreshFingerTargets()
        {
            // 自动绑定手模型指尖骨骼，兼容 L_IndexTip / LeftIndexTip / 包含 IndexTip 的常见命名。
            if (m_LeftThumbTipTarget == null)
                m_LeftThumbTipTarget = FindFingerTipTarget("Left Hand Tracking", "L_ThumbTip", "LeftThumbTip", "ThumbTip");

            if (m_LeftIndexTipTarget == null)
                m_LeftIndexTipTarget = FindFingerTipTarget("Left Hand Tracking", "L_IndexTip", "LeftIndexTip", "IndexTip");

            if (m_LeftMiddleTipTarget == null)
                m_LeftMiddleTipTarget = FindFingerTipTarget("Left Hand Tracking", "L_MiddleTip", "LeftMiddleTip", "MiddleTip");

            if (m_LeftRingTipTarget == null)
                m_LeftRingTipTarget = FindFingerTipTarget("Left Hand Tracking", "L_RingTip", "LeftRingTip", "RingTip");

            if (m_LeftLittleTipTarget == null)
                m_LeftLittleTipTarget = FindFingerTipTarget("Left Hand Tracking", "L_LittleTip", "LeftLittleTip", "LittleTip");

            if (m_RightThumbTipTarget == null)
                m_RightThumbTipTarget = FindFingerTipTarget("Right Hand Tracking", "R_ThumbTip", "RightThumbTip", "ThumbTip");

            if (m_RightIndexTipTarget == null)
                m_RightIndexTipTarget = FindFingerTipTarget("Right Hand Tracking", "R_IndexTip", "RightIndexTip", "IndexTip");

            if (m_RightMiddleTipTarget == null)
                m_RightMiddleTipTarget = FindFingerTipTarget("Right Hand Tracking", "R_MiddleTip", "RightMiddleTip", "MiddleTip");

            if (m_RightRingTipTarget == null)
                m_RightRingTipTarget = FindFingerTipTarget("Right Hand Tracking", "R_RingTip", "RightRingTip", "RingTip");

            if (m_RightLittleTipTarget == null)
                m_RightLittleTipTarget = FindFingerTipTarget("Right Hand Tracking", "R_LittleTip", "RightLittleTip", "LittleTip");
        }

        void RefreshHandVisualRoots()
        {
            // 自动寻找 XR Hands/Hand Visualizer 生成的左右手根节点，用于整体防穿透偏移。
            if (m_LeftHandVisualRoot == null)
                m_LeftHandVisualRoot = FindSceneTransformByName("Left Hand Tracking");

            if (m_RightHandVisualRoot == null)
                m_RightHandVisualRoot = FindSceneTransformByName("Right Hand Tracking");

            if (m_LeftHandVisualState.root != m_LeftHandVisualRoot)
            {
                // 根节点替换前先清掉旧根节点上的偏移，避免手模型被永久移位。
                ApplyHandVisualOffset(ref m_LeftHandVisualState, Vector3.zero);
                m_LeftHandVisualState.root = m_LeftHandVisualRoot;
            }

            if (m_RightHandVisualState.root != m_RightHandVisualRoot)
            {
                ApplyHandVisualOffset(ref m_RightHandVisualState, Vector3.zero);
                m_RightHandVisualState.root = m_RightHandVisualRoot;
            }
        }

        Vector3 GetHandVisualOffset(HandProbe probe)
        {
            if (probe == m_Left)
                return m_LeftHandVisualState.worldOffset;

            if (probe == m_Right)
                return m_RightHandVisualState.worldOffset;

            return Vector3.zero;
        }

        void ApplyHandVisualOffset(HandProbe probe, Vector3 worldOffset)
        {
            // 单食指模式通过当前探针判断应该移动左手还是右手模型。
            if (!m_ClampHandVisualsToKeyboard)
                worldOffset = Vector3.zero;

            if (probe == m_Left)
            {
                ApplyHandVisualOffset(ref m_LeftHandVisualState, worldOffset);
                return;
            }

            if (probe == m_Right)
                ApplyHandVisualOffset(ref m_RightHandVisualState, worldOffset);
        }

        void ApplyHandVisualOffset(bool leftHand, Vector3 worldOffset)
        {
            // 十指模式已经知道是哪只手，直接对对应手模型根节点施加偏移。
            if (!m_ClampHandVisualsToKeyboard)
                worldOffset = Vector3.zero;

            if (leftHand)
                ApplyHandVisualOffset(ref m_LeftHandVisualState, worldOffset);
            else
                ApplyHandVisualOffset(ref m_RightHandVisualState, worldOffset);
        }

        void ApplyHandVisualOffset(ref VisualRootState state, Vector3 worldOffset)
        {
            if (state.root == null)
            {
                state.worldOffset = Vector3.zero;
                return;
            }

            var delta = worldOffset - state.worldOffset;
            if (delta.sqrMagnitude > 0f)
                // 只应用“新旧偏移差值”，避免每帧重复叠加同一份修正量。
                state.root.position += delta;

            state.worldOffset = worldOffset;
        }

        void MarkProbeNotTracked(HandProbe probe, string source)
        {
            // 记录失败原因，方便 OnGUI 调试窗口直接显示当前卡在哪个输入来源。
            if (probe == null)
                return;

            probe.tracked = false;
            probe.poseValid = false;
            probe.lastSource = source;
        }

        void EnsureProbeObjects()
        {
            // 确保所有可能用到的手指探针都存在；HandTouch 不使用的额外探针会在下面关闭。
            m_Left = EnsureProbe(m_Left, ref m_LeftProbe, "Left HandTouch Probe");
            m_Right = EnsureProbe(m_Right, ref m_RightProbe, "Right HandTouch Probe");
            m_LeftThumb = EnsureProbe(m_LeftThumb, ref m_LeftThumbProbe, "Left Thumb HandTouch Probe");
            m_LeftMiddle = EnsureProbe(m_LeftMiddle, ref m_LeftMiddleProbe, "Left Middle HandTouch Probe");
            m_LeftRing = EnsureProbe(m_LeftRing, ref m_LeftRingProbe, "Left Ring HandTouch Probe");
            m_LeftLittle = EnsureProbe(m_LeftLittle, ref m_LeftLittleProbe, "Left Little HandTouch Probe");
            m_RightThumb = EnsureProbe(m_RightThumb, ref m_RightThumbProbe, "Right Thumb HandTouch Probe");
            m_RightMiddle = EnsureProbe(m_RightMiddle, ref m_RightMiddleProbe, "Right Middle HandTouch Probe");
            m_RightRing = EnsureProbe(m_RightRing, ref m_RightRingProbe, "Right Ring HandTouch Probe");
            m_RightLittle = EnsureProbe(m_RightLittle, ref m_RightLittleProbe, "Right Little HandTouch Probe");

            if (!m_UseAllFingerTips)
                SetExtraFingerProbesActive(false);
        }

        HandProbe EnsureProbe(HandProbe probe, ref Transform probeTransform, string name)
        {
            // 探针对象可以手动指定；如果没有配置，就运行时在本物体下面创建。
            if (probeTransform == null)
            {
                var probeObject = new GameObject(name);
                probeObject.transform.SetParent(transform, false);
                probeTransform = probeObject.transform;
            }

            if (probe == null || probe.root != probeTransform)
                probe = new HandProbe { root = probeTransform };

            var probeObjectRef = probeTransform.gameObject;
            probe.pressProbe = probeObjectRef.GetComponent<VRKeyboardPressProbe>();
            if (probe.pressProbe == null)
                probe.pressProbe = probeObjectRef.AddComponent<VRKeyboardPressProbe>();
            if (probe.pressProbe == null)
                // VRKeyboardKey 只接受带 VRKeyboardPressProbe 标记的碰撞体。
                probeObjectRef.AddComponent<VRKeyboardPressProbe>();

            probe.pressProbe = probeObjectRef.GetComponent<VRKeyboardPressProbe>();
            probe.collider = probeObjectRef.GetComponent<SphereCollider>();
            if (probe.collider == null)
                probe.collider = probeObjectRef.AddComponent<SphereCollider>();

            probe.collider.isTrigger = true;
            probe.collider.radius = m_ProbeRadius;

            var rigidbody = probeObjectRef.GetComponent<Rigidbody>();
            if (rigidbody == null)
                rigidbody = probeObjectRef.AddComponent<Rigidbody>();

            // 键盘按键用 trigger 检测探针，探针本身不参与物理推动。
            rigidbody.useGravity = false;
            rigidbody.isKinematic = true;

            EnsureVisual(probeObjectRef);
            probe.renderers = probeObjectRef.GetComponentsInChildren<Renderer>(true);
            return probe;
        }

        void EnsureVisual(GameObject probeObject)
        {
            // 调试小球只负责可视化，碰撞由父物体上的 SphereCollider 负责。
            var visual = probeObject.transform.Find("Visual");
            if (visual == null)
            {
                var visualObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                visualObject.name = "Visual";
                visualObject.transform.SetParent(probeObject.transform, false);

                var visualCollider = visualObject.GetComponent<Collider>();
                if (Application.isPlaying)
                    Destroy(visualCollider);
                else
                    DestroyImmediate(visualCollider);

                visual = visualObject.transform;
            }

            visual.localPosition = Vector3.zero;
            visual.localRotation = Quaternion.identity;
            visual.localScale = Vector3.one * (m_ProbeRadius * 2f);
        }

        void ApplyProbeRadius(Transform probeTransform)
        {
            // Inspector 修改 Probe Radius 时，同步更新碰撞半径和小球显示大小。
            if (probeTransform == null)
                return;

            var sphereCollider = probeTransform.GetComponent<SphereCollider>();
            if (sphereCollider != null)
                sphereCollider.radius = m_ProbeRadius;

            var visual = probeTransform.Find("Visual");
            if (visual != null)
                visual.localScale = Vector3.one * (m_ProbeRadius * 2f);
        }

        void SetProbeActive(HandProbe probe, bool active)
        {
            // active=false 时关闭碰撞体和可视化，避免未追踪手指误触键盘。
            if (probe == null)
                return;

            if (probe.collider != null)
                probe.collider.enabled = active;

            if (probe.pressProbe != null)
                probe.pressProbe.enabled = active;

            if (probe.renderers != null)
            {
                for (var i = 0; i < probe.renderers.Length; i++)
                {
                    if (probe.renderers[i] != null)
                        probe.renderers[i].enabled = active && m_ShowProbeVisuals;
                }
            }

            probe.active = active;
        }

        void SetExtraFingerProbesActive(bool active)
        {
            // 只控制十指模式额外的八个手指，不包含左右食指。
            SetProbeActive(m_LeftThumb, active);
            SetProbeActive(m_LeftMiddle, active);
            SetProbeActive(m_LeftRing, active);
            SetProbeActive(m_LeftLittle, active);
            SetProbeActive(m_RightThumb, active);
            SetProbeActive(m_RightMiddle, active);
            SetProbeActive(m_RightRing, active);
            SetProbeActive(m_RightLittle, active);
        }

        void SetHandExtraFingerProbesActive(bool leftHand, bool active)
        {
            // HandTouch 模式下保留食指探针，关闭同一只手的其他四个手指探针。
            if (leftHand)
            {
                SetProbeActive(m_LeftThumb, active);
                SetProbeActive(m_LeftMiddle, active);
                SetProbeActive(m_LeftRing, active);
                SetProbeActive(m_LeftLittle, active);
                return;
            }

            SetProbeActive(m_RightThumb, active);
            SetProbeActive(m_RightMiddle, active);
            SetProbeActive(m_RightRing, active);
            SetProbeActive(m_RightLittle, active);
        }

        void SetHandFingerProbesActive(bool leftHand, bool active)
        {
            // 十指模式下整只手丢失/禁用时，一次性关闭五个手指探针。
            if (leftHand)
            {
                SetProbeActive(m_LeftThumb, active);
                SetProbeActive(m_Left, active);
                SetProbeActive(m_LeftMiddle, active);
                SetProbeActive(m_LeftRing, active);
                SetProbeActive(m_LeftLittle, active);
                return;
            }

            SetProbeActive(m_RightThumb, active);
            SetProbeActive(m_Right, active);
            SetProbeActive(m_RightMiddle, active);
            SetProbeActive(m_RightRing, active);
            SetProbeActive(m_RightLittle, active);
        }

        void RefreshControllerVisuals()
        {
            // 缓存控制器模型 Renderer 和提示根节点，后续根据是否追踪到手来隐藏/恢复。
            if (m_HideControllerVisualsWhenHandTracked)
            {
                if (m_LeftControllerVisualRoot == null)
                    m_LeftControllerVisualRoot = FindControllerVisualRoot("Left Controller");

                if (m_RightControllerVisualRoot == null)
                    m_RightControllerVisualRoot = FindControllerVisualRoot("Right Controller");

                m_LeftControllerRenderers = CacheRendererStates(m_LeftControllerVisualRoot, m_LeftControllerRenderers);
                m_RightControllerRenderers = CacheRendererStates(m_RightControllerVisualRoot, m_RightControllerRenderers);
            }

            if (m_HideControllerHintsInHandMode)
            {
                if (m_LeftControllerHintRoot == null)
                    m_LeftControllerHintRoot = FindControllerHintRoot(k_LeftControllerHintNames, "Left Controller");

                if (m_RightControllerHintRoot == null)
                    m_RightControllerHintRoot = FindControllerHintRoot(k_RightControllerHintNames, "Right Controller");

                m_LeftControllerHintState = CacheGameObjectState(m_LeftControllerHintRoot, m_LeftControllerHintState);
                m_RightControllerHintState = CacheGameObjectState(m_RightControllerHintRoot, m_RightControllerHintState);
            }
        }

        Transform FindControllerVisualRoot(string controllerName)
        {
            // XR Interaction Toolkit 的控制器模型常见名字是 UniversalController/Controller_Base。
            var controller = GameObject.Find(controllerName);
            if (controller == null)
                return null;

            return FindChildRecursive(controller.transform, "UniversalController") ??
                FindChildRecursive(controller.transform, "Controller_Base") ??
                controller.transform;
        }

        Transform FindSceneTransformByName(string objectName)
        {
            // GameObject.Find 只能找激活对象；Resources.FindObjectsOfTypeAll 兜底查找场景中未激活对象。
            var activeObject = GameObject.Find(objectName);
            if (activeObject != null)
                return activeObject.transform;

            var transforms = Resources.FindObjectsOfTypeAll<Transform>();
            for (var i = 0; i < transforms.Length; i++)
            {
                var candidate = transforms[i];
                if (candidate == null ||
                    candidate.name != objectName ||
                    !candidate.gameObject.scene.IsValid())
                {
                    continue;
                }

                return candidate;
            }

            return null;
        }

        Transform FindFingerTipTarget(string handRootName, string primaryName, string alternateName, string fragmentName)
        {
            // 优先在指定的左右手根节点下查找，避免左右手有同名子节点时拿错。
            var handRoot = FindSceneTransformByName(handRootName);
            if (handRoot != null)
            {
                var target = FindChildRecursive(handRoot, primaryName) ??
                    FindChildRecursive(handRoot, alternateName) ??
                    FindChildByNameFragment(handRoot, fragmentName);
                if (target != null)
                    return target;
            }

            return FindSceneTransformByName(primaryName) ??
                FindSceneTransformByName(alternateName);
        }

        Transform FindChildRecursive(Transform root, string childName)
        {
            if (root == null)
                return null;

            if (root.name == childName)
                return root;

            for (var i = 0; i < root.childCount; i++)
            {
                var match = FindChildRecursive(root.GetChild(i), childName);
                if (match != null)
                    return match;
            }

            return null;
        }

        Transform FindControllerHintRoot(string[] names, string controllerName)
        {
            // 先按已知根节点名查找，找不到再从控制器层级里按名字片段兜底。
            for (var i = 0; i < names.Length; i++)
            {
                var found = GameObject.Find(names[i]);
                if (found != null)
                    return found.transform;
            }

            var controller = GameObject.Find(controllerName);
            if (controller == null)
                return null;

            return FindChildByNameFragment(controller.transform, "Affordance Callouts") ??
                FindChildByNameFragment(controller.transform, "Controller Hints") ??
                FindChildByNameFragment(controller.transform, "Controller Tips");
        }

        Transform FindChildByNameFragment(Transform root, string nameFragment)
        {
            if (root == null)
                return null;

            if (root.name.Contains(nameFragment))
                return root;

            for (var i = 0; i < root.childCount; i++)
            {
                var match = FindChildByNameFragment(root.GetChild(i), nameFragment);
                if (match != null)
                    return match;
            }

            return null;
        }

        RendererState[] CacheRendererStates(Transform root, RendererState[] existingStates)
        {
            // 隐藏控制器时只改 Renderer.enabled，不直接禁用根物体，避免影响交互组件。
            if (root == null)
                return existingStates;

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var states = new List<RendererState>((existingStates != null ? existingStates.Length : 0) + renderers.Length);
            if (existingStates != null)
            {
                for (var i = 0; i < existingStates.Length; i++)
                {
                    if (existingStates[i].renderer != null && !ContainsRenderer(states, existingStates[i].renderer))
                        states.Add(existingStates[i]);
                }
            }
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || ContainsRenderer(states, renderer))
                    continue;

                states.Add(new RendererState
                {
                    renderer = renderer,
                    initiallyEnabled = renderer.enabled,
                });
            }

            return states.Count > 0 ? states.ToArray() : null;
        }

        bool ContainsRenderer(List<RendererState> states, Renderer renderer)
        {
            for (var i = 0; i < states.Count; i++)
            {
                if (states[i].renderer == renderer)
                    return true;
            }

            return false;
        }

        GameObjectState CacheGameObjectState(Transform root, GameObjectState existingState)
        {
            if (root == null)
                return existingState;

            if (existingState.gameObject == root.gameObject)
                return existingState;

            // 控制器提示通常是纯显示对象，可以直接 SetActive，并保留初始 active 状态。
            return new GameObjectState
            {
                gameObject = root.gameObject,
                initiallyActive = root.gameObject.activeSelf,
            };
        }

        void UpdateControllerVisuals()
        {
            // 追踪到手时隐藏对应控制器模型；手丢失时恢复控制器模型，方便回退到手柄。
            if (m_HideControllerVisualsWhenHandTracked)
            {
                SetControllerVisualsVisible(m_LeftControllerRenderers, m_Left == null || !m_Left.tracked);
                SetControllerVisualsVisible(m_RightControllerRenderers, m_Right == null || !m_Right.tracked);
            }

            if (m_HideControllerHintsInHandMode)
            {
                // 只要处于手部输入脚本启用状态，就隐藏手柄操作提示。
                SetGameObjectVisible(m_LeftControllerHintState, false);
                SetGameObjectVisible(m_RightControllerHintState, false);
            }
        }

        void SetControllerVisualsVisible(RendererState[] states, bool visible)
        {
            if (states == null)
                return;

            for (var i = 0; i < states.Length; i++)
            {
                var renderer = states[i].renderer;
                if (renderer != null)
                    renderer.enabled = visible && states[i].initiallyEnabled;
            }
        }

        void SetGameObjectVisible(GameObjectState state, bool visible)
        {
            if (state.gameObject == null)
                return;

            state.gameObject.SetActive(visible && state.initiallyActive);
        }

        void OnGUI()
        {
            // 运行时调试窗口：用于确认当前数据来源、XRHandSubsystem 状态和左右手追踪情况。
            if (!Application.isPlaying || !m_ShowDebugStatus)
                return;

            var rect = new Rect(12f, 12f, 560f, 150f);
            GUI.Box(rect, string.Empty);
            GUI.Label(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, rect.height - 16f), BuildDebugStatus());
        }

        string BuildDebugStatus()
        {
            var subsystemText = m_HandSubsystem == null
                ? "none"
                : $"{m_HandSubsystem.GetType().Name}, running={m_HandSubsystem.running}";
            var leftControls = m_Left?.controls?.device == null ? "none" : m_Left.controls.device.displayName;
            var rightControls = m_Right?.controls?.device == null ? "none" : m_Right.controls.device.displayName;

            return
                $"HandTouch Debug\n" +
                $"XRHandSubsystems={m_HandSubsystems.Count}, selected={subsystemText}\n" +
                $"FingerTargets L={TargetName(m_LeftIndexTipTarget)}, R={TargetName(m_RightIndexTipTarget)}\n" +
                $"LastUpdate={m_LastUpdateType}, flags={m_LastUpdateFlags}, source={m_LastSource}\n" +
                $"Left tracked={m_Left?.tracked}, pose={m_Left?.poseValid}, active={m_Left?.active}, fallback={leftControls}, status={m_Left?.lastSource}\n" +
                $"Right tracked={m_Right?.tracked}, pose={m_Right?.poseValid}, active={m_Right?.active}, fallback={rightControls}, status={m_Right?.lastSource}";
        }

        string TargetName(Transform target)
        {
            return target != null ? target.name : "none";
        }
    }
}
