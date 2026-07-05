using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.XR.Hands;
using System.Collections.Generic;

namespace VRTyping.Keyboard
{
    // Moves small press probes to tracked hand poke points exposed by OpenXR/XR Hands.
    public class VRKeyboardHandProbeFollower : MonoBehaviour
    {
        [Header("Probe Setup")]
        [SerializeField] Transform m_LeftProbe;
        [SerializeField] Transform m_RightProbe;
        [SerializeField, Min(0.001f)] float m_ProbeRadius = 0.012f;
        [SerializeField] bool m_ShowProbeVisuals = true;

        [Header("Input")]
        [SerializeField] bool m_UseLeftHand = true;
        [SerializeField] bool m_UseRightHand = true;
        [SerializeField] bool m_UseFallbackHandPositions = true;
        [SerializeField, Min(0.05f)] float m_DeviceRefreshInterval = 1f;
        [SerializeField] bool m_ShowDebugStatus = true;

        [Header("Pose Space")]
        [SerializeField] bool m_PreferHandSkeletonTargets = true;
        [SerializeField] Transform m_LeftIndexTipTarget;
        [SerializeField] Transform m_RightIndexTipTarget;
        [SerializeField] Transform m_HandPoseSpaceRoot;
        [SerializeField] Vector3 m_ProbePoseOffset;

        [Header("Hand Visuals")]
        [SerializeField] bool m_HideControllerVisualsWhenHandTracked = true;
        [SerializeField] Transform m_LeftControllerVisualRoot;
        [SerializeField] Transform m_RightControllerVisualRoot;
        [SerializeField] bool m_HideControllerHintsInHandMode = true;
        [SerializeField] Transform m_LeftControllerHintRoot;
        [SerializeField] Transform m_RightControllerHintRoot;
        [SerializeField, Min(0.05f)] float m_ControllerVisualRefreshInterval = 1f;

        readonly List<XRHandSubsystem> m_HandSubsystems = new List<XRHandSubsystem>();
        HandProbe m_Left;
        HandProbe m_Right;
        XRHandSubsystem m_HandSubsystem;
        XRHandSubsystem m_SubscribedHandSubsystem;
        float m_NextDeviceRefreshTime;
        float m_NextControllerVisualRefreshTime;
        XRHandSubsystem.UpdateSuccessFlags m_LastUpdateFlags;
        XRHandSubsystem.UpdateType m_LastUpdateType;
        string m_LastSource = "none";
        RendererState[] m_LeftControllerRenderers;
        RendererState[] m_RightControllerRenderers;
        GameObjectState m_LeftControllerHintState;
        GameObjectState m_RightControllerHintState;

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
            public InputDevice device;
            public Vector3Control position;
            public QuaternionControl rotation;
            public ButtonControl isTracked;
        }

        class HandProbe
        {
            public Transform root;
            public SphereCollider collider;
            public Renderer[] renderers;
            public HandControls controls;
            public bool active;
            public bool tracked;
            public bool poseValid;
            public string lastSource;
        }

        struct RendererState
        {
            public Renderer renderer;
            public bool initiallyEnabled;
        }

        struct GameObjectState
        {
            public GameObject gameObject;
            public bool initiallyActive;
        }

        void Reset()
        {
            EnsureProbeObjects();
        }

        void Awake()
        {
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
            UnsubscribeFromHandSubsystem();
            SetProbeActive(m_Left, false);
            SetProbeActive(m_Right, false);
            SetControllerVisualsVisible(m_LeftControllerRenderers, true);
            SetControllerVisualsVisible(m_RightControllerRenderers, true);
            SetGameObjectVisible(m_LeftControllerHintState, true);
            SetGameObjectVisible(m_RightControllerHintState, true);
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
        }

        void LateUpdate()
        {
            if (Time.unscaledTime >= m_NextDeviceRefreshTime)
            {
                RefreshHandDevices();
                RefreshFingerTargets();
                RefreshHandPoseSpace();
                m_NextDeviceRefreshTime = Time.unscaledTime + m_DeviceRefreshInterval;
            }

            if (Time.unscaledTime >= m_NextControllerVisualRefreshTime)
            {
                RefreshControllerVisuals();
                m_NextControllerVisualRefreshTime = Time.unscaledTime + m_ControllerVisualRefreshInterval;
            }

            UpdateProbe(m_Left, m_UseLeftHand);
            UpdateProbe(m_Right, m_UseRightHand);
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
            EnsureProbeObjects();
            RefreshHandSubsystem();
            m_Left.controls = FindHandControls(CommonUsages.LeftHand);
            m_Right.controls = FindHandControls(CommonUsages.RightHand);
        }

        void RefreshHandSubsystem()
        {
            if (m_HandSubsystem != null && m_HandSubsystem.running)
            {
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
            m_LastUpdateFlags = updateFlags;
            m_LastUpdateType = updateType;

            if (!isActiveAndEnabled || subsystem == null)
                return;

            m_HandSubsystem = subsystem;

            if (m_UseLeftHand && !TryUpdateProbeFromSkeletonTarget(m_Left, m_LeftIndexTipTarget))
                TryUpdateProbeFromXRHands(m_Left, XRHandSubsystem.UpdateSuccessFlags.LeftHandJoints);

            if (m_UseRightHand && !TryUpdateProbeFromSkeletonTarget(m_Right, m_RightIndexTipTarget))
                TryUpdateProbeFromXRHands(m_Right, XRHandSubsystem.UpdateSuccessFlags.RightHandJoints);

            UpdateControllerVisuals();
        }

        HandControls FindHandControls(InternedString handUsage)
        {
            foreach (var device in InputSystem.devices)
            {
                if (device == null || !device.enabled || !device.usages.Contains(handUsage))
                    continue;

                var position = device.TryGetChildControl<Vector3Control>("pokePosition");
                if (position == null && m_UseFallbackHandPositions)
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
                SetProbeActive(probe, false);
                return;
            }

            var indexTipTarget = probe == m_Left ? m_LeftIndexTipTarget : m_RightIndexTipTarget;
            if (TryUpdateProbeFromSkeletonTarget(probe, indexTipTarget) || TryUpdateProbeFromXRHands(probe))
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
            probe.root.SetPositionAndRotation(position, rotation);
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
                MarkProbeNotTracked(probe, $"{indexTipTarget.name} inactive");
                return false;
            }

            RefreshHandSubsystem();
            if (m_HandSubsystem != null && m_HandSubsystem.running)
            {
                var hand = probe == m_Left ? m_HandSubsystem.leftHand : m_HandSubsystem.rightHand;
                if (!hand.isTracked)
                {
                    MarkProbeNotTracked(probe, "hand not tracked");
                    return false;
                }
            }

            probe.root.SetPositionAndRotation(
                indexTipTarget.position + indexTipTarget.rotation * m_ProbePoseOffset,
                indexTipTarget.rotation);
            probe.tracked = true;
            probe.poseValid = true;
            probe.lastSource = indexTipTarget.name;
            m_LastSource = indexTipTarget.name;
            SetProbeActive(probe, true);
            return true;
        }

        bool TryUpdateProbeFromXRHands(HandProbe probe, XRHandSubsystem.UpdateSuccessFlags requiredFlags = XRHandSubsystem.UpdateSuccessFlags.None)
        {
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
                probeRoot.SetPositionAndRotation(
                    m_HandPoseSpaceRoot.TransformPoint(localPosition),
                    m_HandPoseSpaceRoot.rotation * pose.rotation);
                return;
            }

            probeRoot.SetPositionAndRotation(localPosition, pose.rotation);
        }

        void RefreshHandPoseSpace()
        {
            if (m_HandPoseSpaceRoot != null)
                return;

            m_HandPoseSpaceRoot = FindSceneTransformByName("Hand Visualizer") ??
                FindSceneTransformByName("Camera Offset");
        }

        void RefreshFingerTargets()
        {
            if (m_LeftIndexTipTarget == null)
                m_LeftIndexTipTarget = FindIndexTipTarget("Left Hand Tracking", "L_IndexTip", "LeftIndexTip");

            if (m_RightIndexTipTarget == null)
                m_RightIndexTipTarget = FindIndexTipTarget("Right Hand Tracking", "R_IndexTip", "RightIndexTip");
        }

        void MarkProbeNotTracked(HandProbe probe, string source)
        {
            if (probe == null)
                return;

            probe.tracked = false;
            probe.poseValid = false;
            probe.lastSource = source;
        }

        void EnsureProbeObjects()
        {
            m_Left = EnsureProbe(m_Left, ref m_LeftProbe, "Left HandTouch Probe");
            m_Right = EnsureProbe(m_Right, ref m_RightProbe, "Right HandTouch Probe");
        }

        HandProbe EnsureProbe(HandProbe probe, ref Transform probeTransform, string name)
        {
            if (probeTransform == null)
            {
                var probeObject = new GameObject(name);
                probeObject.transform.SetParent(transform, false);
                probeTransform = probeObject.transform;
            }

            if (probe == null || probe.root != probeTransform)
                probe = new HandProbe { root = probeTransform };

            var probeObjectRef = probeTransform.gameObject;
            if (probeObjectRef.GetComponent<VRKeyboardPressProbe>() == null)
                probeObjectRef.AddComponent<VRKeyboardPressProbe>();

            probe.collider = probeObjectRef.GetComponent<SphereCollider>();
            if (probe.collider == null)
                probe.collider = probeObjectRef.AddComponent<SphereCollider>();

            probe.collider.isTrigger = true;
            probe.collider.radius = m_ProbeRadius;

            var rigidbody = probeObjectRef.GetComponent<Rigidbody>();
            if (rigidbody == null)
                rigidbody = probeObjectRef.AddComponent<Rigidbody>();

            rigidbody.useGravity = false;
            rigidbody.isKinematic = true;

            EnsureVisual(probeObjectRef);
            probe.renderers = probeObjectRef.GetComponentsInChildren<Renderer>(true);
            return probe;
        }

        void EnsureVisual(GameObject probeObject)
        {
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
            if (probe == null)
                return;

            if (probe.collider != null)
                probe.collider.enabled = active;

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

        void RefreshControllerVisuals()
        {
            if (m_HideControllerVisualsWhenHandTracked)
            {
                if (m_LeftControllerVisualRoot == null)
                    m_LeftControllerVisualRoot = FindControllerVisualRoot("Left Controller");

                if (m_RightControllerVisualRoot == null)
                    m_RightControllerVisualRoot = FindControllerVisualRoot("Right Controller");

                m_LeftControllerRenderers = CacheRendererStates(m_LeftControllerVisualRoot);
                m_RightControllerRenderers = CacheRendererStates(m_RightControllerVisualRoot);
            }

            if (m_HideControllerHintsInHandMode)
            {
                if (m_LeftControllerHintRoot == null)
                    m_LeftControllerHintRoot = FindControllerHintRoot(k_LeftControllerHintNames, "Left Controller");

                if (m_RightControllerHintRoot == null)
                    m_RightControllerHintRoot = FindControllerHintRoot(k_RightControllerHintNames, "Right Controller");

                m_LeftControllerHintState = CacheGameObjectState(m_LeftControllerHintRoot);
                m_RightControllerHintState = CacheGameObjectState(m_RightControllerHintRoot);
            }
        }

        Transform FindControllerVisualRoot(string controllerName)
        {
            var controller = GameObject.Find(controllerName);
            if (controller == null)
                return null;

            return FindChildRecursive(controller.transform, "UniversalController") ??
                FindChildRecursive(controller.transform, "Controller_Base") ??
                controller.transform;
        }

        Transform FindSceneTransformByName(string objectName)
        {
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

        Transform FindIndexTipTarget(string handRootName, string primaryName, string alternateName)
        {
            var handRoot = FindSceneTransformByName(handRootName);
            if (handRoot != null)
            {
                var target = FindChildRecursive(handRoot, primaryName) ??
                    FindChildRecursive(handRoot, alternateName) ??
                    FindChildByNameFragment(handRoot, "IndexTip");
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

        RendererState[] CacheRendererStates(Transform root)
        {
            if (root == null)
                return null;

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var states = new RendererState[renderers.Length];
            for (var i = 0; i < renderers.Length; i++)
            {
                states[i] = new RendererState
                {
                    renderer = renderers[i],
                    initiallyEnabled = renderers[i] != null && renderers[i].enabled,
                };
            }

            return states;
        }

        GameObjectState CacheGameObjectState(Transform root)
        {
            return new GameObjectState
            {
                gameObject = root != null ? root.gameObject : null,
                initiallyActive = root != null && root.gameObject.activeSelf,
            };
        }

        void UpdateControllerVisuals()
        {
            if (m_HideControllerVisualsWhenHandTracked)
            {
                SetControllerVisualsVisible(m_LeftControllerRenderers, m_Left == null || !m_Left.tracked);
                SetControllerVisualsVisible(m_RightControllerRenderers, m_Right == null || !m_Right.tracked);
            }

            if (m_HideControllerHintsInHandMode)
            {
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
