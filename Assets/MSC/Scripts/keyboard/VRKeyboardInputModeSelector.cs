using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace VRTyping.Keyboard
{
    public enum VRKeyboardInputMode
    {
        Press,
        Swipe,
        StickTap,
        Dwell,
        HandTouch,
        HandTouch10,
    }

    public class VRKeyboardInputModeSelector : MonoBehaviour
    {
        [SerializeField] VRKeyboardInputMode m_InputMode = VRKeyboardInputMode.Press;

        [Header("Input Components")]
        [SerializeField] VRKeyboardController m_PressInput;
        [SerializeField] VRKeyboardSwipeInput m_SwipeInput;

        [Tooltip("Legacy primary ray probe. Keep this assigned for Swipe and Dwell if you do not assign a right probe object.")]
        [SerializeField] VRKeyboardRayProbeFollower m_RayProbeInput;

        [Tooltip("Legacy primary stick probe.")]
        [SerializeField] VRKeyboardStickProbeFollower m_StickTapInput;

        [SerializeField] VRKeyboardHandProbeFollower m_HandTouchInput;

        [Header("Controller Probe Objects")]
        [Tooltip("Drag the left controller follow sphere here. It can contain Ray and Stick probe follower components.")]
        public GameObject m_LeftControllerProbeObject;

        [Tooltip("Drag the right controller follow sphere here. It can contain Ray and Stick probe follower components.")]
        public GameObject m_RightControllerProbeObject;

        readonly List<VRKeyboardRayProbeFollower> m_RayProbeCache = new List<VRKeyboardRayProbeFollower>(4);
        readonly List<VRKeyboardStickProbeFollower> m_StickProbeCache = new List<VRKeyboardStickProbeFollower>(4);

        public VRKeyboardInputMode currentInputMode => m_InputMode;

        void Reset()
        {
            CacheInputComponents();
        }

        void OnEnable()
        {
            CacheInputComponents();
            ApplyMode();
        }

        void OnValidate()
        {
            CacheInputComponents();
            ApplyMode();
        }

        public void SetInputMode(VRKeyboardInputMode inputMode)
        {
            m_InputMode = inputMode;
            ApplyMode();
        }

        public void SetInputModeFromDropdown(int optionIndex)
        {
            if (optionIndex < 0 || optionIndex > (int)VRKeyboardInputMode.HandTouch10)
            {
                Debug.LogWarning("Invalid keyboard input mode dropdown index: " + optionIndex, this);
                return;
            }

            SetInputMode((VRKeyboardInputMode)optionIndex);
        }

        public void SetPressMode()
        {
            SetInputMode(VRKeyboardInputMode.Press);
        }

        public void SetSwipeMode()
        {
            SetInputMode(VRKeyboardInputMode.Swipe);
        }

        public void SetStickTapMode()
        {
            SetInputMode(VRKeyboardInputMode.StickTap);
        }

        public void SetDwellMode()
        {
            SetInputMode(VRKeyboardInputMode.Dwell);
        }

        public void SetHandTouchMode()
        {
            SetInputMode(VRKeyboardInputMode.HandTouch);
        }

        public void SetHandTouch10Mode()
        {
            SetInputMode(VRKeyboardInputMode.HandTouch10);
        }

        void CacheInputComponents()
        {
            if (m_PressInput == null)
                m_PressInput = GetComponent<VRKeyboardController>();

            if (m_SwipeInput == null)
                m_SwipeInput = GetComponent<VRKeyboardSwipeInput>();

            if (m_RayProbeInput == null)
                m_RayProbeInput = FindObjectOfType<VRKeyboardRayProbeFollower>(true);

            if (m_StickTapInput == null)
                m_StickTapInput = FindObjectOfType<VRKeyboardStickProbeFollower>(true);

            if (m_HandTouchInput == null)
                m_HandTouchInput = FindObjectOfType<VRKeyboardHandProbeFollower>(true);
        }

        void ApplyMode()
        {
            if (Application.isPlaying)
            {
                DisableAllInputSources();
                ActivateProbeGameObjects();
                ResetAllKeyInteractionState();
                Physics.SyncTransforms();
            }

            var rayProbes = CollectRayProbes();
            var stickProbes = CollectStickProbes();

            var useDiscreteKeyPress =
                m_InputMode == VRKeyboardInputMode.Press ||
                m_InputMode == VRKeyboardInputMode.StickTap ||
                m_InputMode == VRKeyboardInputMode.Dwell ||
                m_InputMode == VRKeyboardInputMode.HandTouch ||
                m_InputMode == VRKeyboardInputMode.HandTouch10;

            if (m_PressInput != null)
                m_PressInput.enabled = useDiscreteKeyPress;

            if (m_SwipeInput != null)
                m_SwipeInput.enabled = m_InputMode == VRKeyboardInputMode.Swipe;

            var primaryRayProbe = GetPrimaryRayProbe(rayProbes);
            var enableControllerRay =
                m_InputMode != VRKeyboardInputMode.StickTap &&
                m_InputMode != VRKeyboardInputMode.HandTouch &&
                m_InputMode != VRKeyboardInputMode.HandTouch10;

            for (var i = 0; i < rayProbes.Count; i++)
            {
                var rayProbe = rayProbes[i];
                if (rayProbe == null)
                    continue;

                var enableRayProbe = enableControllerRay &&
                    (m_InputMode == VRKeyboardInputMode.Press || rayProbe == primaryRayProbe);

                rayProbe.enabled = enableRayProbe;
                if (!enableRayProbe)
                    continue;

                var rayMode = m_InputMode == VRKeyboardInputMode.Swipe
                    ? VRKeyboardInputMode.Swipe
                    : m_InputMode == VRKeyboardInputMode.Dwell
                        ? VRKeyboardInputMode.Dwell
                        : VRKeyboardInputMode.Press;

                rayProbe.SetInputMode(rayMode);
                if (Application.isPlaying)
                    rayProbe.ResetProbeState(true);
            }

            for (var i = 0; i < stickProbes.Count; i++)
            {
                if (stickProbes[i] != null)
                    stickProbes[i].enabled = m_InputMode == VRKeyboardInputMode.StickTap;
            }

            if (m_HandTouchInput != null)
            {
                var enableHandTouch = m_InputMode == VRKeyboardInputMode.HandTouch ||
                    m_InputMode == VRKeyboardInputMode.HandTouch10;
                m_HandTouchInput.SetUseAllFingerTips(m_InputMode == VRKeyboardInputMode.HandTouch10);
                m_HandTouchInput.enabled = enableHandTouch;
                if (enableHandTouch && Application.isPlaying)
                    m_HandTouchInput.RefreshProbesNow();
            }
        }

        void DisableAllInputSources()
        {
            if (m_PressInput != null)
                m_PressInput.enabled = false;

            if (m_SwipeInput != null)
                m_SwipeInput.enabled = false;

            var rayProbes = CollectRayProbes();
            for (var i = 0; i < rayProbes.Count; i++)
            {
                if (rayProbes[i] != null)
                    rayProbes[i].enabled = false;
            }

            var stickProbes = CollectStickProbes();
            for (var i = 0; i < stickProbes.Count; i++)
            {
                if (stickProbes[i] != null)
                    stickProbes[i].enabled = false;
            }

            if (m_HandTouchInput != null)
                m_HandTouchInput.enabled = false;
        }

        void ActivateProbeGameObjects()
        {
            SetGameObjectActive(m_LeftControllerProbeObject, true);
            SetGameObjectActive(m_RightControllerProbeObject, true);

            var rayProbes = CollectRayProbes();
            for (var i = 0; i < rayProbes.Count; i++)
                SetComponentGameObjectActive(rayProbes[i], true);

            var stickProbes = CollectStickProbes();
            for (var i = 0; i < stickProbes.Count; i++)
                SetComponentGameObjectActive(stickProbes[i], true);

            SetComponentGameObjectActive(m_HandTouchInput, true);
        }

        List<VRKeyboardRayProbeFollower> CollectRayProbes()
        {
            m_RayProbeCache.Clear();
            AddRayProbeObject(m_RightControllerProbeObject, VRKeyboardControllerHand.Right);
            AddRayProbeObject(m_LeftControllerProbeObject, VRKeyboardControllerHand.Left);
            AddUnique(m_RayProbeCache, m_RayProbeInput);
            return m_RayProbeCache;
        }

        List<VRKeyboardStickProbeFollower> CollectStickProbes()
        {
            m_StickProbeCache.Clear();
            AddStickProbeObject(m_RightControllerProbeObject, VRKeyboardControllerHand.Right);
            AddStickProbeObject(m_LeftControllerProbeObject, VRKeyboardControllerHand.Left);
            AddUnique(m_StickProbeCache, m_StickTapInput);
            return m_StickProbeCache;
        }

        void AddRayProbeObject(GameObject probeObject, VRKeyboardControllerHand hand)
        {
            if (probeObject == null)
                return;

            var rayProbe = probeObject.GetComponent<VRKeyboardRayProbeFollower>();
            if (rayProbe == null)
                return;

            rayProbe.m_ControllerHand = hand;
            AddUnique(m_RayProbeCache, rayProbe);
        }

        void AddStickProbeObject(GameObject probeObject, VRKeyboardControllerHand hand)
        {
            if (probeObject == null)
                return;

            var stickProbe = probeObject.GetComponent<VRKeyboardStickProbeFollower>();
            if (stickProbe == null)
                return;

            stickProbe.m_ControllerHand = hand;
            AddUnique(m_StickProbeCache, stickProbe);
        }

        static VRKeyboardRayProbeFollower GetPrimaryRayProbe(List<VRKeyboardRayProbeFollower> rayProbes)
        {
            return rayProbes.Count > 0 ? rayProbes[0] : null;
        }

        static void SetComponentGameObjectActive(Component component, bool active)
        {
            if (component != null && component.gameObject.activeSelf != active)
                component.gameObject.SetActive(active);
        }

        static void SetGameObjectActive(GameObject gameObject, bool active)
        {
            if (gameObject != null && gameObject.activeSelf != active)
                gameObject.SetActive(active);
        }

        static void AddUnique<T>(List<T> list, T item) where T : Component
        {
            if (item != null && !list.Contains(item))
                list.Add(item);
        }

        void ResetAllKeyInteractionState()
        {
            var keys = GetComponentsInChildren<VRKeyboardKey>(true);
            for (var i = 0; i < keys.Length; i++)
            {
                if (keys[i] != null)
                    keys[i].ResetInteractionState();
            }
        }
    }

    public enum VRKeyboardControllerHand
    {
        Auto,
        Left,
        Right,
    }

    static class VRKeyboardControllerHandUtility
    {
        static readonly List<InputDevice> s_Devices = new List<InputDevice>(4);

        public static VRKeyboardControllerHand Resolve(
            VRKeyboardControllerHand configuredHand,
            Component owner,
            Transform preferredTransform = null)
        {
            if (configuredHand != VRKeyboardControllerHand.Auto)
                return configuredHand;

            if (ContainsHandName(preferredTransform, "Left"))
                return VRKeyboardControllerHand.Left;

            if (ContainsHandName(preferredTransform, "Right"))
                return VRKeyboardControllerHand.Right;

            if (owner != null)
            {
                if (ContainsHandName(owner.transform, "Left"))
                    return VRKeyboardControllerHand.Left;

                if (ContainsHandName(owner.transform, "Right"))
                    return VRKeyboardControllerHand.Right;
            }

            return VRKeyboardControllerHand.Right;
        }

        public static bool TryReadTrigger(VRKeyboardControllerHand hand, out float value)
        {
            value = 0f;
            var device = GetControllerDevice(hand);
            if (!device.isValid)
                return false;

            if (device.TryGetFeatureValue(CommonUsages.trigger, out value))
            {
                value = Mathf.Clamp01(value);
                return true;
            }

            if (device.TryGetFeatureValue(CommonUsages.triggerButton, out var pressed))
            {
                value = pressed ? 1f : 0f;
                return true;
            }

            return false;
        }

        public static bool TryReadThumbstick(VRKeyboardControllerHand hand, out Vector2 axis)
        {
            axis = Vector2.zero;
            var device = GetControllerDevice(hand);
            return device.isValid &&
                device.TryGetFeatureValue(CommonUsages.primary2DAxis, out axis);
        }

        static InputDevice GetControllerDevice(VRKeyboardControllerHand hand)
        {
            s_Devices.Clear();
            var characteristics = InputDeviceCharacteristics.Controller;
            characteristics |= hand == VRKeyboardControllerHand.Left
                ? InputDeviceCharacteristics.Left
                : InputDeviceCharacteristics.Right;

            InputDevices.GetDevicesWithCharacteristics(characteristics, s_Devices);
            for (var i = 0; i < s_Devices.Count; i++)
            {
                if (s_Devices[i].isValid)
                    return s_Devices[i];
            }

            return default;
        }

        static bool ContainsHandName(Transform transform, string handName)
        {
            while (transform != null)
            {
                if (transform.name.IndexOf(handName, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                transform = transform.parent;
            }

            return false;
        }
    }
}
