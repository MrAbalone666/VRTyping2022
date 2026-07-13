using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
//挂载在每个按键上。负责按键下压动画、碰撞检测、触发 onPressed 事件。
namespace VRTyping.Keyboard
{
    // 按键被按下时，键帽在本地坐标系中移动的方向。
    public enum VRKeyboardPressAxis
    {
        NegativeX,
        PositiveX,
        NegativeY,
        PositiveY,
        NegativeZ,
        PositiveZ,
    }

    [RequireComponent(typeof(BoxCollider))]
    // 单个 VR 键盘按键：负责检测按压深度、移动键帽、触发按键事件，并处理音效和高亮反馈。
    public class VRKeyboardKey : MonoBehaviour
    {
        [Header("References")]
        [SerializeField]
        [Tooltip("Visual transform that moves when the key is pressed. Leave empty to move this transform directly.")]
        Transform m_PressTarget;

        [SerializeField]
        [Tooltip("Trigger collider used to measure press depth.")]
        BoxCollider m_PressCollider;

        [Header("Press Settings")]
        [SerializeField]
        [Tooltip("Local axis along which the key moves when pressed.")]
        VRKeyboardPressAxis m_PressAxis = VRKeyboardPressAxis.NegativeY;

        //[SerializeField]
        [Min(0.001f)]
        [Tooltip("Maximum local travel distance for the key visual.")]
        float m_MaxPressDistance = 0.3f;

        [SerializeField]
        [Range(0.1f, 1f)]
        [Tooltip("How far the key must be pressed, relative to max travel, before On Pressed is fired.")]
        float m_PressThreshold = 0.85f;

        [SerializeField]
        [Range(0.05f, 0.95f)]
        [Tooltip("How far the key must return, relative to max travel, before it can be pressed again.")]
        float m_ReleaseThreshold = 0.35f;

        //[SerializeField]
        [Min(0f)]
        [Tooltip("How quickly the key visual follows the target depth.")]
        float m_MoveSpeed = 10.0f;

        [Header("Highlight Feedback")]
        [SerializeField]
        [Tooltip("Create a TextMeshPro label on top of this key at runtime.")]
        bool m_ShowLabel = true;

        [SerializeField]
        [Tooltip("Tint color blended onto the key while input progress builds up.")]
        Color m_HighlightColor = new Color(1f, 0.85f, 0.2f, 1f);

        [SerializeField]
        [Range(0f, 1f)]
        [Tooltip("How strongly the highlight color is blended onto the key.")]
        float m_HighlightStrength = 0.65f;

        private AudioSource m_AudioSource;
        private AudioClip m_PressClip;
        private AudioClip m_ReleaseClip;

        [Header("Events")]
        [SerializeField]
        UnityEvent m_OnPressed;

        // 当前停留在按键触发器内、并且属于 VRKeyboardPressProbe 的碰撞体。
        readonly List<Collider> m_ActivePressColliders = new List<Collider>();

        // 记录键帽初始位置，用于根据按压深度计算偏移和复位。
        Vector3 m_InitialLocalPosition;
        float m_CurrentPressDistance;
        // 小于 0 表示不使用外部按压值，改由碰撞体自动计算。
        float m_ExternalPressDistance = -1f;
        // 用于让 OnPressed 只在越过按下阈值时触发一次，回弹到释放阈值后才能再次触发。
        bool m_IsPressed;
        float m_CurrentHighlightProgress = -1f;
        // 小于 0 表示没有外部高亮进度；常用于凝视/滑动输入把按键逐渐点亮。
        float m_ExternalHighlightProgress = -1f;
        DwellRendererData[] m_DwellRenderers;

        // 缓存 Renderer 的材质和原始颜色，后续高亮时可以从原色平滑混合过去。
        struct DwellRendererData
        {
            public Renderer renderer;
            public Material[] materials;
            public Color[] baseColors;
            public int[] colorPropertyIds;
        }

        public UnityEvent onPressed => m_OnPressed;
        public BoxCollider pressCollider => m_PressCollider;
        public Transform pressTarget => m_PressTarget;
        public VRKeyboardPressAxis pressAxis => m_PressAxis;
        // HandTouch 防穿透会读取这个值，确保指尖/手模型不会超过单个按键允许的最大下压距离。
        public float maxPressDistance => m_MaxPressDistance;

        void Reset()
        {
            // 添加组件或点击 Reset 时，自动填充常用引用，减少 Inspector 手动配置。
            m_PressCollider = GetComponent<BoxCollider>();
            m_PressTarget = transform.childCount > 0 ? transform.GetChild(0) : transform;
        }

        void Awake()
        {
            if (m_PressCollider == null)
                m_PressCollider = GetComponent<BoxCollider>();

            if (m_PressCollider != null)
                m_PressCollider.isTrigger = true;

            if (m_PressTarget == null)
                m_PressTarget = transform.childCount > 0 ? transform.GetChild(0) : transform;

            // 之后所有按压偏移都以这个初始本地位置为基准。
            m_InitialLocalPosition = m_PressTarget.localPosition;
            CacheDwellRendererData();

            if (m_ShowLabel)
                VRKeyboardKeyLabel.EnsureLabel(this);

            if (m_AudioSource == null)
                m_AudioSource = GetComponentInParent<AudioSource>();

            // 从 Resources 目录加载默认按下/释放音效。
            if (m_PressClip == null)
                m_PressClip = Resources.Load<AudioClip>("click_start");

            if (m_ReleaseClip == null)
                m_ReleaseClip = Resources.Load<AudioClip>("click_end");
        }

        void OnEnable()
        {
            // 启用时恢复成“未按下、无高亮”的干净状态。
            if (m_PressTarget != null)
                m_PressTarget.localPosition = m_InitialLocalPosition;

            m_CurrentPressDistance = 0f;
            m_ExternalPressDistance = -1f;
            m_IsPressed = false;
            m_ActivePressColliders.Clear();
            ClearDwellProgress();
        }

        void OnDisable()
        {
            ResetInteractionState();
        }

        void OnValidate()
        {
            if (m_PressCollider == null)
                m_PressCollider = GetComponent<BoxCollider>();

            if (m_PressCollider != null)
                m_PressCollider.isTrigger = true;

            if (m_PressTarget == null)
                m_PressTarget = transform.childCount > 0 ? transform.GetChild(0) : transform;

            if (m_MaxPressDistance < 0.001f)
                m_MaxPressDistance = 0.001f;
        }

        void Update()
        {
            // 优先使用外部设置的按压值；没有外部输入时，根据探针碰撞体计算真实按压深度。
            var targetPressDistance = m_ExternalPressDistance >= 0f
                ? m_ExternalPressDistance
                : ComputeTargetPressDistance();

            // 让键帽以固定速度追随目标深度，避免位置瞬间跳变。
            var maxStep = m_MoveSpeed <= 0f ? m_MaxPressDistance : m_MoveSpeed * Time.deltaTime;
            m_CurrentPressDistance = Mathf.MoveTowards(m_CurrentPressDistance, targetPressDistance, maxStep);

            if (m_PressTarget != null)
                m_PressTarget.localPosition = m_InitialLocalPosition + GetPressOffset(m_CurrentPressDistance);

            var normalizedPress = m_MaxPressDistance > 0.0001f
                ? Mathf.Clamp01(m_CurrentPressDistance / m_MaxPressDistance)
                : 0f;

            // 高亮进度取“物理按压”和“外部输入进度”中较大的那个。
            var externalHighlight = m_ExternalHighlightProgress >= 0f ? m_ExternalHighlightProgress : 0f;
            UpdateHighlightProgress(Mathf.Max(normalizedPress, externalHighlight));

            var pressDistanceForEvent = m_MaxPressDistance * m_PressThreshold;
            var releaseDistanceForEvent = m_MaxPressDistance * m_ReleaseThreshold;

            // 按下阈值和释放阈值分开，形成迟滞区间，防止临界位置反复触发。
            if (!m_IsPressed && m_CurrentPressDistance >= pressDistanceForEvent)
            {
                m_IsPressed = true;

                if (m_AudioSource != null && m_PressClip != null)
                    m_AudioSource.PlayOneShot(m_PressClip);

                m_OnPressed?.Invoke();
            }
            else if (m_IsPressed && m_CurrentPressDistance <= releaseDistanceForEvent)
            {
                m_IsPressed = false;

                if (m_AudioSource != null && m_ReleaseClip != null)
                    m_AudioSource.PlayOneShot(m_ReleaseClip);
            }
        }

        public void SetExternalPress01(float normalizedPress)
        {
            // 允许射线、手柄或其他输入系统直接驱动按键下压，参数范围为 0 到 1。
            m_ExternalPressDistance = Mathf.Clamp01(normalizedPress) * m_MaxPressDistance;
        }

        public void ClearExternalPress()
        {
            // 回到基于碰撞体探针自动计算按压深度的模式。
            m_ExternalPressDistance = -1f;
        }

        public void SetDwellProgress01(float normalizedProgress)
        {
            // Dwell 进度本质上就是外部高亮进度，这里保留语义化接口。
            SetHighlightProgress01(normalizedProgress);
        }

        public void ClearDwellProgress()
        {
            ClearHighlightProgress();
        }

        public void SetHighlightProgress01(float normalizedProgress)
        {
            m_ExternalHighlightProgress = Mathf.Clamp01(normalizedProgress);
        }

        public void ClearHighlightProgress()
        {
            m_ExternalHighlightProgress = -1f;
        }

        public void ResetInteractionState()
        {
            m_ActivePressColliders.Clear();
            m_CurrentPressDistance = 0f;
            m_ExternalPressDistance = -1f;
            m_ExternalHighlightProgress = -1f;
            m_CurrentHighlightProgress = -1f;
            m_IsPressed = false;

            if (m_PressTarget != null)
                m_PressTarget.localPosition = m_InitialLocalPosition;

            UpdateHighlightProgress(0f);
        }

        void OnTriggerEnter(Collider other)
        {
            // 只接受带 VRKeyboardPressProbe 的对象，避免其他碰撞体误触键盘。
            if (!IsValidPressCollider(other))
                return;

            if (!m_ActivePressColliders.Contains(other))
                m_ActivePressColliders.Add(other);
        }

        void OnTriggerExit(Collider other)
        {
            m_ActivePressColliders.Remove(other);
        }

        bool IsValidPressCollider(Collider other)
        {
            if (other == null)
                return false;

            var probe = other.GetComponentInParent<VRKeyboardPressProbe>();
            return probe != null && probe.isActiveAndEnabled;
        }

        float ComputeTargetPressDistance()
        {
            if (m_PressCollider == null || m_ActivePressColliders.Count == 0)
                return 0f;

            // 多个探针同时接触时，使用最深的那个作为当前按压深度。
            var maxDepth = 0f;
            for (var i = m_ActivePressColliders.Count - 1; i >= 0; i--)
            {
                var other = m_ActivePressColliders[i];
                // 清理已经销毁或失活的碰撞体，避免列表长期残留无效引用。
                if (other == null || !other.enabled || !other.gameObject.activeInHierarchy || !IsValidPressCollider(other))
                {
                    m_ActivePressColliders.RemoveAt(i);
                    continue;
                }

                if (!TryGetProbeLocalPoint(other, out var localPoint))
                    continue;

                var depth = ComputeLocalPressDepth(localPoint);
                if (depth > maxDepth)
                    maxDepth = depth;
            }

            return Mathf.Clamp(maxDepth, 0f, m_MaxPressDistance);
        }

        bool TryGetProbeLocalPoint(Collider other, out Vector3 localPoint)
        {
            localPoint = default;

            if (m_PressCollider == null || other == null)
                return false;

            if (!m_PressCollider.bounds.Intersects(other.bounds))
                return false;

            // 把探针中心从世界坐标转成本按键的本地坐标，后续才能沿本地轴计算深度。
            localPoint = transform.InverseTransformPoint(other.bounds.center);

            var halfSize = m_PressCollider.size * 0.5f;
            var center = m_PressCollider.center;

            // 只检查垂直于按压轴的两个方向是否落在按键区域内。
            switch (m_PressAxis)
            {
                case VRKeyboardPressAxis.NegativeX:
                case VRKeyboardPressAxis.PositiveX:
                    return Mathf.Abs(localPoint.y - center.y) <= halfSize.y &&
                           Mathf.Abs(localPoint.z - center.z) <= halfSize.z;
                case VRKeyboardPressAxis.NegativeY:
                case VRKeyboardPressAxis.PositiveY:
                    return Mathf.Abs(localPoint.x - center.x) <= halfSize.x &&
                           Mathf.Abs(localPoint.z - center.z) <= halfSize.z;
                case VRKeyboardPressAxis.NegativeZ:
                case VRKeyboardPressAxis.PositiveZ:
                    return Mathf.Abs(localPoint.x - center.x) <= halfSize.x &&
                           Mathf.Abs(localPoint.y - center.y) <= halfSize.y;
                default:
                    return false;
            }
        }

        float ComputeLocalPressDepth(Vector3 localPoint)
        {
            var halfSize = m_PressCollider.size * 0.5f;
            var center = m_PressCollider.center;

            // 根据按压轴，从触发器表面向内部计算探针压入了多少距离。
            switch (m_PressAxis)
            {
                case VRKeyboardPressAxis.NegativeX:
                    return center.x + halfSize.x - localPoint.x;
                case VRKeyboardPressAxis.PositiveX:
                    return localPoint.x - (center.x - halfSize.x);
                case VRKeyboardPressAxis.NegativeY:
                    return center.y + halfSize.y - localPoint.y;
                case VRKeyboardPressAxis.PositiveY:
                    return localPoint.y - (center.y - halfSize.y);
                case VRKeyboardPressAxis.NegativeZ:
                    return center.z + halfSize.z - localPoint.z;
                case VRKeyboardPressAxis.PositiveZ:
                    return localPoint.z - (center.z - halfSize.z);
                default:
                    return 0f;
            }
        }

        Vector3 GetPressOffset(float pressDistance)
        {
            // 把标量按压深度转换成键帽实际移动的本地偏移。
            switch (m_PressAxis)
            {
                case VRKeyboardPressAxis.NegativeX:
                    return Vector3.left * pressDistance;
                case VRKeyboardPressAxis.PositiveX:
                    return Vector3.right * pressDistance;
                case VRKeyboardPressAxis.NegativeY:
                    return Vector3.down * pressDistance;
                case VRKeyboardPressAxis.PositiveY:
                    return Vector3.up * pressDistance;
                case VRKeyboardPressAxis.NegativeZ:
                    return Vector3.back * pressDistance;
                case VRKeyboardPressAxis.PositiveZ:
                    return Vector3.forward * pressDistance;
                default:
                    return Vector3.zero;
            }
        }

        void CacheDwellRendererData()
        {
            if (m_PressTarget == null)
            {
                m_DwellRenderers = System.Array.Empty<DwellRendererData>();
                return;
            }

            var renderers = m_PressTarget.GetComponentsInChildren<Renderer>(true);
            m_DwellRenderers = new DwellRendererData[renderers.Length];

            // 兼容 URP/HDRP 常用的 _BaseColor 和内置管线常用的 _Color。
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                var materials = renderer != null ? renderer.materials : System.Array.Empty<Material>();
                var baseColors = new Color[materials.Length];
                var colorPropertyIds = new int[materials.Length];

                for (var j = 0; j < materials.Length; j++)
                {
                    var material = materials[j];
                    if (material == null)
                        continue;

                    if (material.HasProperty("_BaseColor"))
                    {
                        colorPropertyIds[j] = Shader.PropertyToID("_BaseColor");
                        baseColors[j] = material.GetColor(colorPropertyIds[j]);
                    }
                    else if (material.HasProperty("_Color"))
                    {
                        colorPropertyIds[j] = Shader.PropertyToID("_Color");
                        baseColors[j] = material.GetColor(colorPropertyIds[j]);
                    }
                }

                m_DwellRenderers[i] = new DwellRendererData
                {
                    renderer = renderer,
                    materials = materials,
                    baseColors = baseColors,
                    colorPropertyIds = colorPropertyIds,
                };
            }
        }

        void UpdateHighlightProgress(float progress01)
        {
            if (Mathf.Approximately(m_CurrentHighlightProgress, progress01))
                return;

            m_CurrentHighlightProgress = progress01;

            if (m_DwellRenderers == null)
                return;

            var blend = progress01 * m_HighlightStrength;

            // 按进度把每个材质从原始颜色混合到高亮色。
            for (var i = 0; i < m_DwellRenderers.Length; i++)
            {
                var data = m_DwellRenderers[i];
                if (data.materials == null || data.colorPropertyIds == null)
                    continue;

                for (var j = 0; j < data.materials.Length; j++)
                {
                    var material = data.materials[j];
                    var colorPropertyId = j < data.colorPropertyIds.Length ? data.colorPropertyIds[j] : 0;
                    if (material == null || colorPropertyId == 0)
                        continue;

                    var baseColor = j < data.baseColors.Length ? data.baseColors[j] : Color.white;
                    var highlightedColor = Color.Lerp(baseColor, m_HighlightColor, blend);
                    material.SetColor(colorPropertyId, highlightedColor);
                }
            }
        }
    }
}
