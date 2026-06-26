using System;
using TMPro;
using UnityEngine;

namespace VRTyping.Keyboard
{
    public static class VRKeyboardKeyLabel
    {
        const string k_LabelName = "KeyLabel";

        public static void EnsureLabel(VRKeyboardKey key)
        {
            if (key == null || key.pressTarget == null || key.pressCollider == null)
                return;

            var label = GetDisplayLabel(VRKeyboardKeyUtility.GetKeyId(key));
            if (string.IsNullOrEmpty(label))
                return;

            var existing = key.pressTarget.Find(k_LabelName);
            TextMeshPro text;
            if (existing == null)
            {
                var labelObject = new GameObject(k_LabelName, typeof(RectTransform), typeof(TextMeshPro));
                labelObject.transform.SetParent(key.pressTarget, false);
                text = labelObject.GetComponent<TextMeshPro>();
            }
            else
            {
                text = existing.GetComponent<TextMeshPro>();
                if (text == null)
                    text = existing.gameObject.AddComponent<TextMeshPro>();
            }

            PositionLabel(key, text.rectTransform);
            ConfigureText(text, label);
        }

        static void PositionLabel(VRKeyboardKey key, RectTransform rect)
        {
            var collider = key.pressCollider;
            var rootNormal = GetSurfaceNormal(key.pressAxis);
            var worldNormal = key.transform.TransformDirection(rootNormal);
            var targetNormal = key.pressTarget.InverseTransformDirection(worldNormal).normalized;

            var rootUp = Mathf.Abs(Vector3.Dot(rootNormal, Vector3.back)) < 0.95f
                ? Vector3.back
                : Vector3.up;
            var worldUp = key.transform.TransformDirection(rootUp);
            var targetUp = key.pressTarget.InverseTransformDirection(worldUp).normalized;

            var rootSurfacePoint = collider.center + new Vector3(
                rootNormal.x * collider.size.x * 0.5f,
                rootNormal.y * collider.size.y * 0.5f,
                rootNormal.z * collider.size.z * 0.5f);
            var worldSurfacePoint = key.transform.TransformPoint(rootSurfacePoint);

            GetLabelSize(collider.size, key.pressAxis, out var width, out var height);
            rect.localPosition = key.pressTarget.InverseTransformPoint(worldSurfacePoint) + targetNormal * 0.01f;
            rect.localRotation = Quaternion.LookRotation(-targetNormal, targetUp);
            rect.localScale = Vector3.one;
            rect.sizeDelta = new Vector2(Mathf.Max(0.1f, width * 0.82f), Mathf.Max(0.1f, height * 0.62f));
        }

        static void ConfigureText(TextMeshPro text, string label)
        {
            text.text = label;
            text.alignment = TextAlignmentOptions.Center;
            text.color = new Color(0.94f, 0.97f, 1f, 1f);
            text.fontStyle = FontStyles.Bold;
            text.enableAutoSizing = true;
            text.fontSizeMin = 2.0f;
            text.fontSizeMax = 3.0f;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
        }

        static void GetLabelSize(Vector3 size, VRKeyboardPressAxis axis, out float width, out float height)
        {
            switch (axis)
            {
                case VRKeyboardPressAxis.NegativeX:
                case VRKeyboardPressAxis.PositiveX:
                    width = size.z;
                    height = size.y;
                    return;
                case VRKeyboardPressAxis.NegativeZ:
                case VRKeyboardPressAxis.PositiveZ:
                    width = size.x;
                    height = size.y;
                    return;
                default:
                    width = size.x;
                    height = size.z;
                    return;
            }
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
