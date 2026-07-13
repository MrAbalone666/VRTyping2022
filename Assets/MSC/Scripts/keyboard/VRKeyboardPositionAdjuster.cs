using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using XRCommonUsages = UnityEngine.XR.CommonUsages;
using XRInputDevice = UnityEngine.XR.InputDevice;

namespace VRTyping.Keyboard
{
    public class VRKeyboardPositionAdjuster : MonoBehaviour
    {
        enum MoveReference
        {
            HeadYaw,
            LocalAxes,
            WorldAxes,
        }

        enum VerticalMode
        {
            Disabled,
            HoldButton,
            Always,
        }

        [Header("Target")]
        public Transform m_TargetRoot;
        public Transform m_ViewTransform;
        [SerializeField] MoveReference m_MoveReference = MoveReference.HeadYaw;

        [Header("Input")]
        public InputActionReference m_LeftMoveAction;
        public InputActionReference m_AdjustButtonAction;
        public InputActionReference m_VerticalButtonAction;
        public bool m_RequireAdjustButton = true;
        public bool m_FallbackToXRDeviceInput = true;
        public bool m_UseGripAsFallbackAdjustButton = true;
        public bool m_UseThumbstickClickAsFallbackVerticalButton = true;

        [Header("Movement")]
        public float m_MoveSpeed = 0.6f;
        public float m_VerticalMoveSpeed = 0.45f;
        public float m_Deadzone = 0.18f;
        [Tooltip("Multiplier applied to the left/right joystick axis. Use -1 if the direction is reversed.")]
        public float m_HorizontalMultiplier = 1f;
        [Tooltip("Multiplier applied to the forward/back joystick axis. Use -1 if the direction is reversed.")]
        public float m_ForwardMultiplier = 1f;
        [Tooltip("Print the axis and movement delta read by this component. Enable only while checking controller direction.")]
        public bool m_DebugLogMovement;
        
        [SerializeField] VerticalMode m_VerticalMode = VerticalMode.HoldButton;
        public bool m_ClampDistanceFromView = true;
        public float m_MinDistanceFromView = 0.35f;
        public float m_MaxDistanceFromView = 3f;
        public bool m_ClampHeight = true;
        public float m_MinHeight = 0.35f;
        public float m_MaxHeight = 2.2f;

        readonly List<XRInputDevice> m_LeftControllers = new List<XRInputDevice>(2);
        bool m_EnabledMoveAction;
        bool m_EnabledAdjustAction;
        bool m_EnabledVerticalAction;
        float m_NextDebugLogTime;

        void Reset()
        {
            m_TargetRoot = transform;
            CacheViewTransform();
        }

        void Awake()
        {
            if (m_TargetRoot == null)
                m_TargetRoot = transform;

            CacheViewTransform();
        }

        void OnEnable()
        {
            EnableAction(m_LeftMoveAction, ref m_EnabledMoveAction);
            EnableAction(m_AdjustButtonAction, ref m_EnabledAdjustAction);
            EnableAction(m_VerticalButtonAction, ref m_EnabledVerticalAction);
        }

        void OnDisable()
        {
            DisableAction(m_LeftMoveAction, ref m_EnabledMoveAction);
            DisableAction(m_AdjustButtonAction, ref m_EnabledAdjustAction);
            DisableAction(m_VerticalButtonAction, ref m_EnabledVerticalAction);
        }

        void Update()
        {
            if (m_TargetRoot == null)
                return;

            var stick = ReadMoveAxis();
            if (stick.sqrMagnitude < m_Deadzone * m_Deadzone)
                return;

            stick = ApplyDeadzone(stick);

            var adjustHeld = IsAdjustHeld();
            if (m_RequireAdjustButton && !adjustHeld)
                return;

            var delta = ComputeMoveDelta(stick);
            if (delta.sqrMagnitude <= Mathf.Epsilon)
                return;

            m_TargetRoot.position = ClampPosition(m_TargetRoot.position + delta * Time.deltaTime);

            if (m_DebugLogMovement && Time.unscaledTime >= m_NextDebugLogTime)
            {
                m_NextDebugLogTime = Time.unscaledTime + 0.25f;
                Debug.Log($"[{nameof(VRKeyboardPositionAdjuster)}] target={m_TargetRoot.name}, stick={stick}, delta={delta}, horizontalMultiplier={m_HorizontalMultiplier}, forwardMultiplier={m_ForwardMultiplier}", this);
            }
        }

        Vector3 ComputeMoveDelta(Vector2 stick)
        {
            var right = Vector3.right;
            var forward = Vector3.forward;

            if (m_MoveReference == MoveReference.HeadYaw)
            {
                CacheViewTransform();
                if (m_ViewTransform != null)
                {
                    right = Vector3.ProjectOnPlane(m_ViewTransform.right, Vector3.up).normalized;
                    forward = Vector3.ProjectOnPlane(m_ViewTransform.forward, Vector3.up).normalized;
                }
            }
            else if (m_MoveReference == MoveReference.LocalAxes)
            {
                right = Vector3.ProjectOnPlane(m_TargetRoot.right, Vector3.up).normalized;
                forward = Vector3.ProjectOnPlane(m_TargetRoot.forward, Vector3.up).normalized;
            }

            if (right.sqrMagnitude <= Mathf.Epsilon)
                right = Vector3.right;

            if (forward.sqrMagnitude <= Mathf.Epsilon)
                forward = Vector3.forward;

            var verticalActive = m_VerticalMode == VerticalMode.Always ||
                (m_VerticalMode == VerticalMode.HoldButton && IsVerticalHeld());

            var horizontalInput = stick.x * m_HorizontalMultiplier;
            var forwardInput = stick.y * m_ForwardMultiplier;

            if (verticalActive)
                return right * (horizontalInput * m_MoveSpeed) + Vector3.up * (forwardInput * m_VerticalMoveSpeed);

            return right * (horizontalInput * m_MoveSpeed) + forward * (forwardInput * m_MoveSpeed);
        }

        Vector3 ClampPosition(Vector3 position)
        {
            if (m_ClampHeight)
                position.y = Mathf.Clamp(position.y, m_MinHeight, m_MaxHeight);

            if (!m_ClampDistanceFromView)
                return position;

            CacheViewTransform();
            if (m_ViewTransform == null)
                return position;

            var viewPosition = m_ViewTransform.position;
            var horizontalOffset = Vector3.ProjectOnPlane(position - viewPosition, Vector3.up);
            var distance = horizontalOffset.magnitude;
            if (distance <= Mathf.Epsilon)
                return position;

            var clampedDistance = Mathf.Clamp(distance, m_MinDistanceFromView, m_MaxDistanceFromView);
            if (Mathf.Approximately(distance, clampedDistance))
                return position;

            return viewPosition + horizontalOffset.normalized * clampedDistance + Vector3.up * (position.y - viewPosition.y);
        }

        Vector2 ReadMoveAxis()
        {
            var action = m_LeftMoveAction != null ? m_LeftMoveAction.action : null;
            if (action != null)
            {
                try
                {
                    return action.ReadValue<Vector2>();
                }
                catch
                {
                    return Vector2.zero;
                }
            }

            if (!m_FallbackToXRDeviceInput)
                return Vector2.zero;

            var device = GetLeftController();
            if (device.isValid &&
                device.TryGetFeatureValue(XRCommonUsages.primary2DAxis, out Vector2 axis))
            {
                return axis;
            }

            return Vector2.zero;
        }

        bool IsAdjustHeld()
        {
            if (!m_RequireAdjustButton)
                return false;

            var action = m_AdjustButtonAction != null ? m_AdjustButtonAction.action : null;
            if (action != null)
            {
                try
                {
                    return action.ReadValue<float>() > 0.5f;
                }
                catch
                {
                    return action.IsPressed();
                }
            }

            if (!m_FallbackToXRDeviceInput || !m_UseGripAsFallbackAdjustButton)
                return !m_RequireAdjustButton;

            var device = GetLeftController();
            return device.isValid &&
                device.TryGetFeatureValue(XRCommonUsages.gripButton, out var gripButton) &&
                gripButton;
        }

        bool IsVerticalHeld()
        {
            var action = m_VerticalButtonAction != null ? m_VerticalButtonAction.action : null;
            if (action != null)
            {
                try
                {
                    return action.ReadValue<float>() > 0.5f;
                }
                catch
                {
                    return action.IsPressed();
                }
            }

            if (!m_FallbackToXRDeviceInput || !m_UseThumbstickClickAsFallbackVerticalButton)
                return false;

            var device = GetLeftController();
            return device.isValid &&
                device.TryGetFeatureValue(XRCommonUsages.primary2DAxisClick, out var click) &&
                click;
        }

        XRInputDevice GetLeftController()
        {
            m_LeftControllers.Clear();
            InputDevices.GetDevicesWithCharacteristics(
                InputDeviceCharacteristics.Left | InputDeviceCharacteristics.Controller,
                m_LeftControllers);

            for (var i = 0; i < m_LeftControllers.Count; i++)
            {
                if (m_LeftControllers[i].isValid)
                    return m_LeftControllers[i];
            }

            return default;
        }

        void CacheViewTransform()
        {
            if (m_ViewTransform != null)
                return;

            if (Camera.main != null)
                m_ViewTransform = Camera.main.transform;
        }

        static Vector2 ApplyDeadzone(Vector2 value)
        {
            var magnitude = Mathf.Clamp01(value.magnitude);
            if (magnitude <= Mathf.Epsilon)
                return Vector2.zero;

            return value.normalized * magnitude;
        }

        static void EnableAction(InputActionReference actionReference, ref bool enabledByThis)
        {
            var action = actionReference != null ? actionReference.action : null;
            if (action == null || action.enabled)
                return;

            action.Enable();
            enabledByThis = true;
        }

        static void DisableAction(InputActionReference actionReference, ref bool enabledByThis)
        {
            var action = actionReference != null ? actionReference.action : null;
            if (!enabledByThis || action == null || !action.enabled)
            {
                enabledByThis = false;
                return;
            }

            action.Disable();
            enabledByThis = false;
        }
    }
}
