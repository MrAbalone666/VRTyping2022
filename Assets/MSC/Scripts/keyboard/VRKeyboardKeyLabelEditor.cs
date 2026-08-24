#if UNITY_EDITOR
using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VRTyping.Keyboard.Editor
{
    public static class VRKeyboardKeyLabelEditor
    {
        const string k_LabelName = "KeyLabel";
        const string k_DemoScene = "Assets/Samples/XR Interaction Toolkit/3.4.1/Starter Assets/DemoScene.unity";


        static void AddOrUpdateSelectedLabels()
        {
            var keyboardRoot = FindKeyboardRoot(Selection.activeTransform);
            if (keyboardRoot == null)
            {
                EditorUtility.DisplayDialog("VR Keyboard", "Select the keyboard or one of its keys first.", "OK");
                return;
            }

            var count = AddOrUpdateLabels(keyboardRoot);
            EditorUtility.DisplayDialog("VR Keyboard", "Updated " + count + " key labels.", "OK");
        }

 
        static bool ValidateAddOrUpdateSelectedLabels()
        {
            return Selection.activeTransform != null;
        }

        public static void GenerateDemoSceneLabels()
        {
            var scene = EditorSceneManager.OpenScene(k_DemoScene);
            var keyboard = GameObject.Find("keyboard");
            if (keyboard == null)
                throw new InvalidOperationException("Could not find the keyboard object in DemoScene.");

            AddOrUpdateLabels(keyboard.transform);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        public static int AddOrUpdateLabels(Transform keyboardRoot)
        {
            var keys = keyboardRoot.GetComponentsInChildren<VRKeyboardKey>(true);
            var updated = 0;
            Undo.SetCurrentGroupName("Add VR Keyboard Key Labels");
            var undoGroup = Undo.GetCurrentGroup();

            for (var i = 0; i < keys.Length; i++)
            {
                if (AddOrUpdateLabel(keys[i]))
                    updated++;
            }

            Undo.CollapseUndoOperations(undoGroup);
            return updated;
        }

        static bool AddOrUpdateLabel(VRKeyboardKey key)
        {
            if (key == null || key.pressTarget == null)
                return false;

            var labelText = GetDisplayLabel(VRKeyboardKeyUtility.GetKeyId(key));
            if (string.IsNullOrEmpty(labelText))
                return false;

            var existing = key.pressTarget.Find(k_LabelName);
            TextMeshPro text;
            if (existing == null)
            {
                var labelObject = new GameObject(k_LabelName, typeof(RectTransform), typeof(TextMeshPro));
                Undo.RegisterCreatedObjectUndo(labelObject, "Create key label");
                labelObject.transform.SetParent(key.pressTarget, false);
                text = labelObject.GetComponent<TextMeshPro>();
            }
            else
            {
                text = existing.GetComponent<TextMeshPro>();
                if (text == null)
                    text = Undo.AddComponent<TextMeshPro>(existing.gameObject);
                Undo.RecordObject(existing, "Update key label transform");
                Undo.RecordObject(text, "Update key label text");
            }

            if (!TryGetVisualBounds(key.pressTarget, text.transform, out var bounds))
                return false;

            var normal = GetSurfaceNormal(key.pressAxis);
            var labelUp = GetLabelUp(normal);
            var labelRight = Vector3.Cross(labelUp, normal).normalized;
            var position = GetSurfacePoint(bounds, normal) + normal * 0.01f;
            var width = ProjectedSize(bounds.size, labelRight) * 0.82f;
            var height = ProjectedSize(bounds.size, labelUp) * 0.62f;

            var rect = text.rectTransform;
            rect.localPosition = position;
            rect.localRotation = Quaternion.LookRotation(-normal, labelUp);
            rect.localScale = Vector3.one;
            rect.sizeDelta = new Vector2(Mathf.Max(0.1f, width), Mathf.Max(0.1f, height));

            text.text = labelText;
            text.alignment = TextAlignmentOptions.Center;
            text.color = new Color(0.94f, 0.97f, 1f, 1f);
            text.fontStyle = FontStyles.Bold;
            text.enableAutoSizing = true;
            text.fontSizeMin = 0.08f;
            text.fontSizeMax = 1.2f;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;

            EditorUtility.SetDirty(text);
            EditorUtility.SetDirty(rect);
            return true;
        }

        static Transform FindKeyboardRoot(Transform selected)
        {
            var current = selected;
            while (current != null)
            {
                if (current.GetComponentsInChildren<VRKeyboardKey>(true).Length > 1)
                    return current;
                current = current.parent;
            }
            return null;
        }

        static bool TryGetVisualBounds(Transform target, Transform label, out Bounds bounds)
        {
            bounds = default;
            var initialized = false;
            var renderers = target.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || renderer.transform.IsChildOf(label))
                    continue;

                var worldBounds = renderer.bounds;
                var min = worldBounds.min;
                var max = worldBounds.max;
                for (var corner = 0; corner < 8; corner++)
                {
                    var worldPoint = new Vector3(
                        (corner & 1) == 0 ? min.x : max.x,
                        (corner & 2) == 0 ? min.y : max.y,
                        (corner & 4) == 0 ? min.z : max.z);
                    var localPoint = target.InverseTransformPoint(worldPoint);
                    if (!initialized)
                    {
                        bounds = new Bounds(localPoint, Vector3.zero);
                        initialized = true;
                    }
                    else
                    {
                        bounds.Encapsulate(localPoint);
                    }
                }
            }
            return initialized;
        }

        static Vector3 GetSurfaceNormal(VRKeyboardPressAxis axis)
        {
            switch (axis)
            {
                case VRKeyboardPressAxis.NegativeX: return Vector3.right;
                case VRKeyboardPressAxis.PositiveX: return Vector3.left;
                case VRKeyboardPressAxis.NegativeY: return Vector3.up;
                case VRKeyboardPressAxis.PositiveY: return Vector3.down;
                case VRKeyboardPressAxis.NegativeZ: return Vector3.forward;
                default: return Vector3.back;
            }
        }

        static Vector3 GetLabelUp(Vector3 normal)
        {
            return Mathf.Abs(Vector3.Dot(normal, Vector3.back)) < 0.95f ? Vector3.back : Vector3.up;
        }

        static Vector3 GetSurfacePoint(Bounds bounds, Vector3 normal)
        {
            return bounds.center + new Vector3(
                normal.x * bounds.extents.x,
                normal.y * bounds.extents.y,
                normal.z * bounds.extents.z);
        }

        static float ProjectedSize(Vector3 size, Vector3 axis)
        {
            return Mathf.Abs(axis.x) * size.x + Mathf.Abs(axis.y) * size.y + Mathf.Abs(axis.z) * size.z;
        }

        static string GetDisplayLabel(string keyId)
        {
            switch (keyId)
            {
                case "Back": return "Back";
                case "Cap": return "Caps";
                case "Enter": return "Enter";
                case "Shift": case "Shift_1": return "Shift";
                case "LCtrL": case "RCTRL": return "Ctrl";
                case "LAlt": case "RAlt": return "Alt";
                case "LWin": case "RWin": return "Win";
                case "minus": return "-";
                case "Plus": return "=";
                case "LKuohao": return "[";
                case "RKuohao": return "]";
                case "LMaohao": return ";";
                case "RMaohao": return "'";
                case "less": return ",";
                case "More": return ".";
                case "ques": return "/";
                case "Tab_1": return "\\";
                case "Space": return "Space";
                default: return keyId.Length == 1 ? keyId.ToUpperInvariant() : keyId;
            }
        }
    }
}
#endif
