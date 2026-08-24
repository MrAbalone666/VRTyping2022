using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace VRTyping.Keyboard
{
    // 真正的 Swipe 输入脚本：记录探针滑过的键和轨迹点，预览字母序列，并提交识别结果。
    public class VRKeyboardSwipeInput : MonoBehaviour
    {
        // 单个探针当前正在进行的一次滑动轨迹。
        class SwipeTrace
        {
            public readonly List<GesturePoint> gesturePoints = new List<GesturePoint>();
            // 已接受进本次滑动序列的 keyId。
            public readonly List<string> keyIds = new List<string>();
            // 投影到键盘 2D 平面后的轨迹点。
            public readonly List<Vector2> points = new List<Vector2>();
            // 当前正在接触的 keyId，用于判断是否刚进入新键。
            public string activeKeyId;
            // 进入当前键的时间，用于停留接受和重复字母判断。
            public float activeKeyEnteredTime;
            // 当前键是否已经因为长停留而接受过一次重复字母。
            public bool repeatedKeyAccepted;
            // 离开所有键后，延迟到这个时间再提交本次 swipe。
            public float releaseTime = float.PositiveInfinity;
        }


        [SerializeField]
        // Swipe 最终要输入文字的目标输入框。
        TMP_InputField m_OutputField;

        [SerializeField]
        // 滑动过程中显示临时字母序列的预览文本。
        TMP_Text m_SwipePreviewLabel;


        [SerializeField]
        [Min(0.01f)]
  
        // 探针离开所有按键后等待多久提交，给快速经过缝隙留一点缓冲。
        float m_EndSwipeDelay = 0.08f;



        // 调试用：提交时打印滑过序列和识别结果。
        bool m_LogSwipeSequence;

        [SerializeField]
        [Range(0.1f, 1f)]

        // 探针足够靠近键中心时，立即接受该键进入滑动序列。
        float m_KeyCenterAcceptanceRadius = 0.55f;

        [SerializeField]
        [Min(0f)]

        // 如果没有靠近中心，至少停留这么久才接受该键，减少擦边误触。
        float m_MinSwipeKeyDwellTime = 0.04f;

        [SerializeField]
        // 为 true 时，只有按住射线扳机才记录 swipe，松开时提交。
        bool m_RequireTriggerForSwipe = true;

        [SerializeField]
        [Min(0.05f)]

        // 在同一个字母键上停留足够久，允许输入双写字母。
        float m_RepeatedLetterDwellTime = 0.22f;

        [SerializeField]
        // 新识别器使用的普通词库文本；模板会根据当前键盘坐标在运行时自动生成。
        TextAsset m_TemplateWordList;

        [SerializeField]
        [Range(1, 10)]
        // 识别时保留几个候选词。
        int m_CandidateCount = 5;

        [SerializeField]
        [Min(0.001f)]
        // 相邻轨迹点距离小于该值时不记录，减少无意义密集采样。
        float m_MinTrajectorySampleDistance = 0.01f;

        [SerializeField]
        [Min(32)]

        int m_MaxTrajectoryPoints = 256;

        [SerializeField]
        // 成功识别成单词后是否自动追加空格。
        bool m_AppendSpaceAfterSwipeWord = true;

        [SerializeField]

        SwipeTypingRecognizer m_SwipeRecognizer;


        [SerializeField]

        InputActionReference m_CandidateMoveAction;

        [SerializeField]

        InputActionReference m_CandidateConfirmAction;

        [SerializeField]

        bool m_ShowCandidatesAfterSwipe = true;

        [SerializeField]
        [Range(0.1f, 1f)]
        float m_CandidateMoveThreshold = 0.55f;

        [SerializeField]
        [Min(0.05f)]
        float m_CandidateMoveRepeatDelay = 0.25f;

        [SerializeField]
        [Range(0.1f, 1f)]
        float m_CandidateConfirmThreshold = 0.5f;

        [SerializeField]
        Color m_CandidateNormalColor = Color.white;

        [SerializeField]
        Color m_CandidateSelectedColor = new Color(0.2f, 0.75f, 1f, 1f);


        [SerializeField]
        // CapsLock 当前是否开启，会影响提交字母/单词的大小写。
        bool m_CapsLockEnabled;

        [SerializeField]
        // Shift 当前是否开启；提交一次可打印内容后会自动关闭。
        bool m_ShiftEnabled;

        [SerializeField]

        // 单键 Tab 输入时，是否使用真实制表符。
        bool m_UseTabCharacter;

        [SerializeField]
        [Min(1)]

        // 不使用真实 Tab 时，Tab 会转换成几个空格。
        int m_TabSpaces = 4;

        // 当前键盘上的所有按键。
        readonly List<VRKeyboardKey> m_Keys = new List<VRKeyboardKey>();
        // 场景里所有 VRKeyboardPressProbe 的碰撞体，每个碰撞体都可能产生独立 swipe。
        readonly List<Collider> m_ProbeColliders = new List<Collider>();
        // 正在进行中的 swipe，按探针碰撞体区分。
        readonly Dictionary<Collider, SwipeTrace> m_ActiveTraces = new Dictionary<Collider, SwipeTrace>();

        readonly List<SwipeCandidate> m_PendingSwipeCandidates = new List<SwipeCandidate>(5);
        readonly List<UnityEngine.XR.InputDevice> m_ControllerDevices = new List<UnityEngine.XR.InputDevice>(4);
        readonly Collider[] m_KeyOverlapBuffer = new Collider[32];
        int m_SelectedCandidateIndex;
        float m_NextCandidateMoveTime;
        float m_NextProbeRefreshTime;
        bool m_CandidateActionConfirmWasHeld;
        bool m_RightCandidateConfirmWasHeld;
        bool m_LeftCandidateConfirmWasHeld;
        bool m_EnabledCandidateMoveAction;
        bool m_EnabledCandidateConfirmAction;
        bool m_RecognizerConfigured;

        const float ProbeRefreshInterval = 0.25f;

        SwipeKeyboardLayout m_KeyboardLayout;
        public string currentText => VRKeyboardTextComposer.GetText(m_OutputField);
        public bool hasPendingSwipeCandidates => m_PendingSwipeCandidates.Count > 0;

        void OnEnable()
        {
            // 启用时刷新键盘/探针引用，并配置运行时 swipe 识别器。
            RefreshReferences();
            EnableCandidateInputActions();
            ClearPreview();
            RefreshKeyLabels();
        }

        void OnDisable()
        {
            // 禁用时丢弃未完成的 swipe，避免下次启用后提交旧轨迹。
            m_ActiveTraces.Clear();
            DisableCandidateInputActions();
            ClearPreview();
        }

        void Update()
        {
            if (m_Keys.Count == 0)
                RefreshKeyboardReferences();

            if (m_ProbeColliders.Count == 0 && Time.unscaledTime >= m_NextProbeRefreshTime)
            {
                RefreshProbeReferences();
                m_NextProbeRefreshTime = Time.unscaledTime + ProbeRefreshInterval;
            }

            // 每个探针独立更新自己的滑动轨迹。
            if (m_PendingSwipeCandidates.Count > 0)
            {
                UpdateCandidateControllerSelection();
                return;
            }

            for (var i = m_ProbeColliders.Count - 1; i >= 0; i--)
            {
                var probeCollider = m_ProbeColliders[i];
                if (probeCollider == null || !probeCollider.gameObject.activeInHierarchy || !IsProbeActive(probeCollider))
                {
                    if (probeCollider != null)
                        m_ActiveTraces.Remove(probeCollider);
                    m_ProbeColliders.RemoveAt(i);
                    continue;
                }


                if (!probeCollider.enabled)
                {
                    m_ActiveTraces.Remove(probeCollider);
                    continue;
                }

                UpdateTraceForProbe(probeCollider);
            }

            FinalizeExpiredTraces();
        }

        public void ClearText()
        {
            VRKeyboardTextComposer.ClearText(m_OutputField);
        }

        bool IsProbeActive(Collider probeCollider)
        {
            if (probeCollider == null)
                return false;

            var probe = probeCollider.GetComponentInParent<VRKeyboardPressProbe>();
            return probe != null && probe.isActiveAndEnabled;
        }

        void EnableCandidateInputActions()
        {
            var moveAction = m_CandidateMoveAction != null ? m_CandidateMoveAction.action : null;
            if (moveAction != null && !moveAction.enabled)
            {
                moveAction.Enable();
                m_EnabledCandidateMoveAction = true;
            }

            var confirmAction = m_CandidateConfirmAction != null ? m_CandidateConfirmAction.action : null;
            if (confirmAction != null && !confirmAction.enabled)
            {
                confirmAction.Enable();
                m_EnabledCandidateConfirmAction = true;
            }
        }

        void DisableCandidateInputActions()
        {
            var moveAction = m_CandidateMoveAction != null ? m_CandidateMoveAction.action : null;
            if (m_EnabledCandidateMoveAction && moveAction != null && moveAction.enabled)
                moveAction.Disable();

            var confirmAction = m_CandidateConfirmAction != null ? m_CandidateConfirmAction.action : null;
            if (m_EnabledCandidateConfirmAction && confirmAction != null && confirmAction.enabled)
                confirmAction.Disable();

            m_EnabledCandidateMoveAction = false;
            m_EnabledCandidateConfirmAction = false;
        }

        void UpdateCandidateControllerSelection()
        {
            var axis = ReadCandidateMoveAxis();
            var navigationValue = Mathf.Abs(axis.x) >= Mathf.Abs(axis.y) ? axis.x : axis.y;
            if (Mathf.Abs(navigationValue) >= m_CandidateMoveThreshold && Time.time >= m_NextCandidateMoveTime)
            {
                MoveSelectedCandidate(navigationValue > 0f ? 1 : -1);
                m_NextCandidateMoveTime = Time.time + m_CandidateMoveRepeatDelay;
            }
            else if (Mathf.Abs(navigationValue) < m_CandidateMoveThreshold * 0.5f)
            {
                m_NextCandidateMoveTime = 0f;
            }

            ReadCandidateConfirmSources(out var actionHeld, out var rightHeld, out var leftHeld);
            var confirmPressed =
                (actionHeld && !m_CandidateActionConfirmWasHeld) ||
                (rightHeld && !m_RightCandidateConfirmWasHeld) ||
                (leftHeld && !m_LeftCandidateConfirmWasHeld);

            if (confirmPressed)
                CommitPendingCandidate(m_SelectedCandidateIndex, true);

            m_CandidateActionConfirmWasHeld = actionHeld;
            m_RightCandidateConfirmWasHeld = rightHeld;
            m_LeftCandidateConfirmWasHeld = leftHeld;
        }

        void MoveSelectedCandidate(int delta)
        {
            if (m_PendingSwipeCandidates.Count == 0)
                return;

            m_SelectedCandidateIndex = Mathf.Clamp(
                m_SelectedCandidateIndex + delta,
                0,
                m_PendingSwipeCandidates.Count - 1);
            RefreshCandidatePreview();
        }

        Vector2 ReadCandidateMoveAxis()
        {
            var axisValue = Vector2.zero;
            var action = m_CandidateMoveAction != null ? m_CandidateMoveAction.action : null;
            if (action != null)
            {
                try
                {
                    axisValue = action.ReadValue<Vector2>();
                }
                catch
                {
                }
            }

            if (TryReadControllerMoveAxis(UnityEngine.XR.InputDeviceCharacteristics.Right, out var rightAxis) &&
                rightAxis.sqrMagnitude > axisValue.sqrMagnitude)
            {
                axisValue = rightAxis;
            }

            if (TryReadControllerMoveAxis(UnityEngine.XR.InputDeviceCharacteristics.Left, out var leftAxis) &&
                leftAxis.sqrMagnitude > axisValue.sqrMagnitude)
            {
                axisValue = leftAxis;
            }

            return axisValue;
        }

        void ReadCandidateConfirmSources(out bool actionHeld, out bool rightHeld, out bool leftHeld)
        {
            var action = m_CandidateConfirmAction != null ? m_CandidateConfirmAction.action : null;
            actionHeld = false;
            if (action != null)
            {
                try
                {
                    actionHeld = action.ReadValue<float>() >= m_CandidateConfirmThreshold;
                }
                catch
                {
                    actionHeld = action.IsPressed();
                }
            }

            rightHeld = ReadControllerConfirmHeld(UnityEngine.XR.InputDeviceCharacteristics.Right);
            leftHeld = ReadControllerConfirmHeld(UnityEngine.XR.InputDeviceCharacteristics.Left);
        }

        bool TryReadControllerMoveAxis(UnityEngine.XR.InputDeviceCharacteristics hand, out Vector2 axis)
        {
            axis = Vector2.zero;
            var device = GetControllerDevice(hand);
            return device.isValid &&
                   device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out axis);
        }

        bool ReadControllerConfirmHeld(UnityEngine.XR.InputDeviceCharacteristics hand)
        {
            var device = GetControllerDevice(hand);
            if (!device.isValid)
                return false;

            if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out var triggerButton) &&
                triggerButton)
            {
                return true;
            }

            return device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trigger, out float trigger) &&
                   trigger >= m_CandidateConfirmThreshold;
        }

        UnityEngine.XR.InputDevice GetControllerDevice(UnityEngine.XR.InputDeviceCharacteristics hand)
        {
            if (TryGetControllerDevice(
                    hand | UnityEngine.XR.InputDeviceCharacteristics.Controller,
                    out var device))
            {
                return device;
            }

            if (TryGetControllerDevice(
                    hand | UnityEngine.XR.InputDeviceCharacteristics.HeldInHand,
                    out device))
            {
                return device;
            }

            return TryGetControllerDevice(hand, out device)
                ? device
                : default(UnityEngine.XR.InputDevice);
        }

        bool TryGetControllerDevice(
            UnityEngine.XR.InputDeviceCharacteristics characteristics,
            out UnityEngine.XR.InputDevice device)
        {
            m_ControllerDevices.Clear();
            UnityEngine.XR.InputDevices.GetDevicesWithCharacteristics(
                characteristics,
                m_ControllerDevices);

            for (var i = 0; i < m_ControllerDevices.Count; i++)
            {
                if (m_ControllerDevices[i].isValid)
                {
                    device = m_ControllerDevices[i];
                    return true;
                }
            }

            device = default(UnityEngine.XR.InputDevice);
            return false;
        }

        public bool TryHandleCandidateSelection(string keyId)
        {
            if (string.IsNullOrEmpty(keyId) || m_PendingSwipeCandidates.Count == 0)
                return false;

            if (keyId.Length == 1 && char.IsDigit(keyId[0]))
            {
                var index = keyId[0] - '1';
                if (index >= 0 && index < m_PendingSwipeCandidates.Count)
                {
                    CommitPendingCandidate(index, false);
                    return true;
                }
            }

            if (keyId == "Space" || keyId == "Enter")
            {
                CommitPendingCandidate(0, false);
                return true;
            }

            if (keyId == "Back" || keyId == "ESC")
            {
                ClearPreview();
                return true;
            }

            return false;
        }

        public void RefreshReferences()
        {
            RefreshKeyboardReferences();
            RefreshProbeReferences();
            ConfigureSwipeRecognizer();
        }

        void RefreshKeyboardReferences()
        {
            m_Keys.Clear();

            var keys = GetComponentsInChildren<VRKeyboardKey>(true);
            for (var i = 0; i < keys.Length; i++)
            {
                if (keys[i] != null)
                    m_Keys.Add(keys[i]);
            }

            SwipeKeyboardLayout.TryCreate(transform, m_Keys, out m_KeyboardLayout);
        }

        void RefreshProbeReferences()
        {
            m_ProbeColliders.Clear();

            var probes = FindObjectsOfType<VRKeyboardPressProbe>(true);
            for (var i = 0; i < probes.Length; i++)
            {
                if (probes[i] == null || !probes[i].isActiveAndEnabled)
                    continue;

                var colliders = probes[i].GetComponentsInChildren<Collider>(true);
                for (var j = 0; j < colliders.Length; j++)
                {
                    var collider = colliders[j];
                    // Keep known XR colliders cached while temporarily disabled.
                    if (collider != null && collider.gameObject.activeInHierarchy && !m_ProbeColliders.Contains(collider))
                        m_ProbeColliders.Add(collider);
                }
            }
        }

        public bool TryGetKeyboardLayout(out SwipeKeyboardLayout layout)
        {
            RefreshReferences();
            layout = m_KeyboardLayout;
            return layout != null;
        }

        void ConfigureSwipeRecognizer()
        {
            if (m_SwipeRecognizer == null)
                m_SwipeRecognizer = GetComponent<SwipeTypingRecognizer>();

            if (m_SwipeRecognizer == null)
                m_SwipeRecognizer = gameObject.AddComponent<SwipeTypingRecognizer>();

            if (m_RecognizerConfigured)
                return;

            if (m_KeyboardLayout != null)
                m_SwipeRecognizer.SetKeyboardLayout(m_KeyboardLayout);

            if (m_TemplateWordList != null)
            {
                var entries = m_TemplateWordList.text.Split(
                    new[] { '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries);
                m_SwipeRecognizer.SetVocabulary(entries);
            }

            m_RecognizerConfigured = true;
        }

        void UpdateTraceForProbe(Collider probeCollider)
        {
            // 如果配置为必须按住扳机，松开时立即提交当前探针的轨迹。
            if (m_RequireTriggerForSwipe && !IsSwipeTriggerHeld(probeCollider))
            {
                if (m_ActiveTraces.ContainsKey(probeCollider))
                    FinalizeTrace(probeCollider);
                return;
            }

            var touchedKey = FindTouchedKey(probeCollider);
            if (!m_ActiveTraces.TryGetValue(probeCollider, out var trace))
            {
                // 只有真正碰到第一个键时才开始一条新 swipe。
                if (touchedKey == null)
                    return;

                trace = new SwipeTrace();
                m_ActiveTraces.Add(probeCollider, trace);


                VRKeyboardInputTelemetry.NotifyInputStarted();
            }


            AddTrajectoryPoint(trace, probeCollider.bounds.center);

            if (touchedKey != null)
            {
                var keyId = VRKeyboardKeyUtility.GetKeyId(touchedKey);

                trace.releaseTime = float.PositiveInfinity;

                if (trace.activeKeyId != keyId)
                {
                    // 刚进入新按键，重置该键的停留计时。
                    trace.activeKeyId = keyId;
                    trace.activeKeyEnteredTime = Time.time;
                    trace.repeatedKeyAccepted = false;
                }

                TryAcceptTouchedKey(trace, touchedKey, probeCollider);

                return;
            }

            // 暂时离开所有键时，不马上提交，等待 m_EndSwipeDelay 以容忍短暂空隙。
            trace.activeKeyId = null;
            if (float.IsPositiveInfinity(trace.releaseTime))
                trace.releaseTime = Time.time + m_EndSwipeDelay;
        }

        bool IsSwipeTriggerHeld(Collider probeCollider)
        {
            if (probeCollider == null)
                return false;

            // 目前 Swipe 的激活状态来自射线探针上的扳机输入。
            var rayProbe = probeCollider.GetComponentInParent<VRKeyboardRayProbeFollower>();
            return rayProbe != null && rayProbe.IsSwipeActivationHeld();
        }

        void AddTrajectoryPoint(SwipeTrace trace, Vector3 worldPoint)
        {
            if (trace == null || m_KeyboardLayout == null)
                return;

            // 把世界坐标投影到键盘归一化平面后记录。
            var point = m_KeyboardLayout.ProjectWorldPoint(worldPoint);
            if (trace.points.Count > 0 &&
                Vector2.Distance(trace.points[trace.points.Count - 1], point) < m_MinTrajectorySampleDistance)
            {
                return;
            }

            trace.points.Add(point);
            trace.gesturePoints.Add(new GesturePoint(point, Time.time));

            if (trace.points.Count >= Mathf.Max(32, m_MaxTrajectoryPoints))
                CompactTrajectory(trace);
        }

        static void CompactTrajectory(SwipeTrace trace)
        {

            for (var i = trace.points.Count - 2; i > 0; i -= 2)
            {
                trace.points.RemoveAt(i);
                trace.gesturePoints.RemoveAt(i);
            }
        }

        void TryAcceptTouchedKey(SwipeTrace trace, VRKeyboardKey touchedKey, Collider probeCollider)
        {
            if (trace == null || touchedKey == null)
                return;

            var keyId = VRKeyboardKeyUtility.GetKeyId(touchedKey);
            if (string.IsNullOrEmpty(keyId))
                return;

            if (trace.keyIds.Count > 0 && trace.keyIds[trace.keyIds.Count - 1] == keyId)
            {
                // 同一个字母键长时间停留时，允许接受一次重复字母，例如 "letter" 里的 tt。
                var canRepeat = keyId.Length == 1 &&
                                char.IsLetter(keyId[0]) &&
                                !trace.repeatedKeyAccepted &&
                                Time.time - trace.activeKeyEnteredTime >= m_RepeatedLetterDwellTime;
                if (canRepeat)
                {
                    trace.keyIds.Add(keyId);
                    trace.repeatedKeyAccepted = true;
                    UpdatePreview(trace);
                }
                return;
            }

            var isFirstKey = trace.keyIds.Count == 0;
            var isNearCenter = IsProbeNearKeyCenter(probeCollider, touchedKey);
            var hasDwelled = Time.time - trace.activeKeyEnteredTime >= m_MinSwipeKeyDwellTime;

            // 第一个键总是接受；后续键需要靠近中心或停留足够久，减少擦边误判。
            if (!isFirstKey && !isNearCenter && !hasDwelled)
                return;

            trace.keyIds.Add(keyId);
            UpdatePreview(trace);
        }

        void FinalizeExpiredTraces()
        {
            if (m_ActiveTraces.Count == 0)
                return;

            // 不能在遍历 Dictionary 时直接删除，所以先收集要提交的探针。
            var completed = new List<Collider>();
            foreach (var pair in m_ActiveTraces)
            {
                if (Time.time >= pair.Value.releaseTime)
                    completed.Add(pair.Key);
            }

            for (var i = 0; i < completed.Count; i++)
                FinalizeTrace(completed[i]);
        }

        void FinalizeTrace(Collider probeCollider)
        {
            if (!m_ActiveTraces.TryGetValue(probeCollider, out var trace))
                return;

            // 移除进行中轨迹，然后提交文字并清空预览。
            m_ActiveTraces.Remove(probeCollider);
            var keepPreview = CommitTrace(trace);
            if (!keepPreview)
                ClearPreview();
        }

        bool CommitTrace(SwipeTrace trace)
        {
            var compactKeys = new List<string>(trace.keyIds);
            if (compactKeys.Count == 0)
                return false;

            // 单词轨迹算一次 Swipe；单独触发 Back/Shift/CapsLock 等功能键时保留键类型统计。
            if (compactKeys.Count == 1 &&
                !(compactKeys[0].Length == 1 && char.IsLetter(compactKeys[0][0])))
            {
                VRKeyboardInputTelemetry.RecordKeyAction(compactKeys[0]);
            }
            else
            {
                VRKeyboardInputTelemetry.RecordSwipeAction();
            }

            if (compactKeys.Count == 1 && TryHandleCandidateSelection(compactKeys[0]))
                return false;

            // 先把滑过的 keyId 转成字母序列，作为预览和无法识别时的兜底输入。
            var sequence = BuildSwipeSequence(compactKeys);
            if (sequence.Length > 0)
            {
                var committedText = sequence;
                if (sequence.Length > 1 && TryRecognizeWord(trace, out var swipeCandidates))
                {
                    var best = swipeCandidates[0];
                    if (m_ShowCandidatesAfterSwipe || best.confidence < m_SwipeRecognizer.minAutoCommitConfidence)
                    {
                        ShowCandidatePreview(swipeCandidates);
                        if (m_LogSwipeSequence)
                            Debug.Log("Swipe sequence " + sequence + " held for candidates. Best=" + best.word + " confidence=" + best.confidence.ToString("F2"), this);
                        return true;
                    }

                    committedText = VRKeyboardTextComposer.ApplySwipeWordCase(
                        best.word,
                        m_CapsLockEnabled,
                        m_ShiftEnabled);

                    if (m_LogSwipeSequence)
                        Debug.Log("Swipe sequence " + sequence + " committed as " + committedText + " confidence=" + best.confidence.ToString("F2"), this);

                    if (m_AppendSpaceAfterSwipeWord)
                        committedText += " ";
                    VRKeyboardTextComposer.AppendText(m_OutputField, committedText);

                    ReleaseShiftAfterCommit();

                    return false;
                }

                if (m_LogSwipeSequence)
                    Debug.Log("Swipe sequence " + sequence + " committed as " + committedText);

                VRKeyboardTextComposer.AppendText(m_OutputField, committedText);

                // Shift 作为一次性状态，提交一次内容后自动关闭。
                ReleaseShiftAfterCommit();

                return false;
            }

            // 如果最终只有一个键，并且它不是普通字母序列，则按普通键处理，例如 Back/Space。
            if (compactKeys.Count == 1)
            {
                var labelsWereUppercase = m_CapsLockEnabled ^ m_ShiftEnabled;
                VRKeyboardTextComposer.HandleKey(
                    compactKeys[0],
                    m_OutputField,
                    ref m_CapsLockEnabled,
                    ref m_ShiftEnabled,
                    m_UseTabCharacter,
                    m_TabSpaces);

                if (labelsWereUppercase != (m_CapsLockEnabled ^ m_ShiftEnabled))
                    RefreshKeyLabels();
            }

            return false;
        }

        bool TryRecognizeWord(SwipeTrace trace, out List<SwipeCandidate> candidates)
        {
            candidates = null;
            if (m_SwipeRecognizer == null || trace == null || trace.gesturePoints.Count < 2)
                return false;

            candidates = m_SwipeRecognizer.Recognize(trace.gesturePoints, m_CandidateCount);
            return candidates != null && candidates.Count > 0;
        }

        void ShowCandidatePreview(List<SwipeCandidate> candidates)
        {
            if (candidates == null || candidates.Count == 0)
                return;

            m_PendingSwipeCandidates.Clear();
            m_SelectedCandidateIndex = 0;
            m_NextCandidateMoveTime = 0f;
            m_CandidateActionConfirmWasHeld = false;
            m_RightCandidateConfirmWasHeld = false;
            m_LeftCandidateConfirmWasHeld = false;
            for (var i = 0; i < candidates.Count; i++)
                m_PendingSwipeCandidates.Add(candidates[i]);

            m_SelectedCandidateIndex = 0;
            m_NextCandidateMoveTime = 0f;
            ReadCandidateConfirmSources(
                out m_CandidateActionConfirmWasHeld,
                out m_RightCandidateConfirmWasHeld,
                out m_LeftCandidateConfirmWasHeld);
            RefreshCandidatePreview();
        }

        void RefreshCandidatePreview()
        {
            if (m_SwipePreviewLabel == null)
                return;

            var words = new List<string>(m_PendingSwipeCandidates.Count);
            var selectedColor = ColorUtility.ToHtmlStringRGBA(m_CandidateSelectedColor);
            var normalColor = ColorUtility.ToHtmlStringRGBA(m_CandidateNormalColor);
            for (var i = 0; i < m_PendingSwipeCandidates.Count; i++)
            {
                var color = i == m_SelectedCandidateIndex ? selectedColor : normalColor;
                var displayWord = VRKeyboardTextComposer.ApplySwipeWordCase(
                    m_PendingSwipeCandidates[i].word,
                    m_CapsLockEnabled,
                    m_ShiftEnabled);
                words.Add("<color=#" + color + ">" + (i + 1).ToString() + ":" + displayWord + "</color>");
            }

            m_SwipePreviewLabel.richText = true;
            m_SwipePreviewLabel.text = string.Join("  ", words);
        }

        void CommitPendingCandidate(int index, bool recordPhysicalAction)
        {
            if (index < 0 || index >= m_PendingSwipeCandidates.Count)
                return;

            // 控制器确认键不经过普通键盘控制器，需要在这里单独记录。
            // 触摸数字/Enter 选择候选时，外层按键或 Swipe 已记录，避免重复累计。
            if (recordPhysicalAction)
                VRKeyboardInputTelemetry.RecordCandidateSelectionAction();

            var committedText = VRKeyboardTextComposer.ApplySwipeWordCase(
                m_PendingSwipeCandidates[index].word,
                m_CapsLockEnabled,
                m_ShiftEnabled);

            if (m_AppendSpaceAfterSwipeWord)
                committedText += " ";

            VRKeyboardTextComposer.AppendText(m_OutputField, committedText);

            ReleaseShiftAfterCommit();

            ClearPreview();
        }

        string BuildSwipeSequence(List<string> compactKeys)
        {
            // 只从 keyId 中提取字母，并按 CapsLock/Shift 应用大小写。
            return VRKeyboardTextComposer.BuildLetterSequence(compactKeys, m_CapsLockEnabled, m_ShiftEnabled);
        }

        void ReleaseShiftAfterCommit()
        {
            if (!m_ShiftEnabled)
                return;

            m_ShiftEnabled = false;
            RefreshKeyLabels();
        }

        void RefreshKeyLabels()
        {
            VRKeyboardKeyLabel.RefreshLabels(transform, m_CapsLockEnabled, m_ShiftEnabled);
        }

        VRKeyboardKey FindTouchedKey(Collider probeCollider)
        {
            // 找出当前探针真正穿透/重叠的最近按键。
            VRKeyboardKey bestKey = null;
            var bestDistance = float.PositiveInfinity;
            var probePosition = probeCollider.bounds.center;

            var probeBounds = probeCollider.bounds;
            var hitCount = Physics.OverlapBoxNonAlloc(
                probeBounds.center,
                probeBounds.extents,
                m_KeyOverlapBuffer,
                Quaternion.identity,
                ~0,
                QueryTriggerInteraction.Collide);

            for (var i = 0; i < hitCount; i++)
            {
                var hitCollider = m_KeyOverlapBuffer[i];
                if (hitCollider == null || hitCollider == probeCollider)
                    continue;

                var key = hitCollider.GetComponent<VRKeyboardKey>() ??
                          hitCollider.GetComponentInParent<VRKeyboardKey>();
                if (key == null || !key.gameObject.activeInHierarchy)
                    continue;

                var keyCollider = key.pressCollider != null ? key.pressCollider : hitCollider;
                if (keyCollider == null || !keyCollider.enabled)
                    continue;

                // ComputePenetration 比简单 bounds 相交更准确，适合判断实际触碰。
                if (!Physics.ComputePenetration(
                    probeCollider,
                    probeCollider.transform.position,
                    probeCollider.transform.rotation,
                    keyCollider,
                    keyCollider.transform.position,
                    keyCollider.transform.rotation,
                    out _,
                    out _))
                {
                    continue;
                }

                var closestPoint = keyCollider.ClosestPoint(probePosition);
                var distance = (probePosition - closestPoint).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestKey = key;
                }
            }

            return bestKey;
        }

        bool IsProbeNearKeyCenter(Collider probeCollider, VRKeyboardKey key)
        {
            if (probeCollider == null || key == null)
                return false;

            var keyCollider = key.pressCollider != null ? key.pressCollider : key.GetComponent<BoxCollider>();
            if (keyCollider == null)
                return false;

            var localPoint = keyCollider.transform.InverseTransformPoint(probeCollider.bounds.center) - keyCollider.center;
            var halfSize = keyCollider.size * 0.5f;
            // 忽略最薄的轴，只在按键表面平面内判断是否靠近中心。
            var ignoredAxis = GetSmallestAxis(halfSize);
            var maxNormalizedOffset = 0f;

            for (var axis = 0; axis < 3; axis++)
            {
                if (axis == ignoredAxis)
                    continue;

                var extent = GetAxisValue(halfSize, axis);
                if (extent <= 0.0001f)
                    continue;

                var offset = Mathf.Abs(GetAxisValue(localPoint, axis)) / extent;
                if (offset > maxNormalizedOffset)
                    maxNormalizedOffset = offset;
            }

            return maxNormalizedOffset <= m_KeyCenterAcceptanceRadius;
        }

        int GetSmallestAxis(Vector3 value)
        {
            // 找到 BoxCollider 半尺寸中最小的轴，通常就是按键厚度方向。
            if (value.x <= value.y && value.x <= value.z)
                return 0;

            if (value.y <= value.x && value.y <= value.z)
                return 1;

            return 2;
        }

        float GetAxisValue(Vector3 value, int axis)
        {
            // 用数字轴索引读取 Vector3 分量。
            switch (axis)
            {
                case 0:
                    return value.x;
                case 1:
                    return value.y;
                default:
                    return value.z;
            }
        }

        void UpdatePreview(SwipeTrace trace)
        {
            if (m_SwipePreviewLabel == null)
                return;

            // 预览当前已接受的字母序列，不等于最终识别出的单词。
            m_SwipePreviewLabel.text = BuildSwipeSequence(trace.keyIds);
        }

        void ClearPreview()
        {
            m_PendingSwipeCandidates.Clear();

            // 没有进行中的 swipe 时清空预览。
            m_SelectedCandidateIndex = 0;
            m_NextCandidateMoveTime = 0f;
            m_CandidateActionConfirmWasHeld = false;
            m_RightCandidateConfirmWasHeld = false;
            m_LeftCandidateConfirmWasHeld = false;

            if (m_SwipePreviewLabel != null)
                m_SwipePreviewLabel.text = string.Empty;
        }

    }
}
