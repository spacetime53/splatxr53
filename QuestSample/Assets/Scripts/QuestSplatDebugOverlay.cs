// Copyright (c) 2026 PSI Lab. MIT License. See LICENSE in the repository root.
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

namespace SplatXR.QuestSample
{
    // World-space panel parented to the headset camera, plus a matching log line for adb logcat -s Unity.
    public class QuestSplatDebugOverlay : MonoBehaviour
    {
        public Transform head;
        Text label;
        float nextLogTime;
        float smoothedFps = 72f;
        bool warnedMissingSplat;

        public static QuestSplatDebugOverlay Create(Transform headCamera)
        {
            var root = new GameObject("SplatDebugOverlay");
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = headCamera.GetComponent<Camera>();
            root.AddComponent<CanvasScaler>();

            var rect = root.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(920f, 560f);
            rect.localScale = Vector3.one * 0.00115f;
            if (headCamera != null)
            {
                root.transform.SetParent(headCamera, false);
                root.transform.localPosition = new Vector3(0f, -0.28f, 1.35f);
                root.transform.localRotation = Quaternion.identity;
            }

            var panelObject = new GameObject("Panel");
            panelObject.transform.SetParent(root.transform, false);
            var panelRect = panelObject.AddComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            var image = panelObject.AddComponent<Image>();
            image.sprite = OnePixelSprite();
            image.color = new Color(0f, 0f, 0f, 0.72f);
            image.raycastTarget = false;

            var textObject = new GameObject("Label");
            textObject.transform.SetParent(panelObject.transform, false);
            var textRect = textObject.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(24f, 16f);
            textRect.offsetMax = new Vector2(-24f, -16f);
            var text = textObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (text.font == null)
                text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = 28;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.color = new Color(0.75f, 1f, 0.85f, 1f);
            text.raycastTarget = false;
            text.text = "SplatXR Quest sample starting...";

            var overlay = root.AddComponent<QuestSplatDebugOverlay>();
            overlay.head = headCamera;
            overlay.label = text;
            return overlay;
        }

        void Update()
        {
            float fps = Time.unscaledDeltaTime > 0.0001f ? 1f / Time.unscaledDeltaTime : 0f;
            smoothedFps = Mathf.Lerp(smoothedFps, fps, 0.05f);
            string status = BuildStatus(smoothedFps);
            if (label != null)
                label.text = status;

            if (Time.unscaledTime >= nextLogTime)
            {
                nextLogTime = Time.unscaledTime + 5f;
                Debug.Log("[SplatXR Quest] " + status.Replace("\n", " | "));
            }
        }

        string BuildStatus(float fps)
        {
            var builder = new StringBuilder(512);
            builder.Append("SplatXR Quest 3 sample\n");
            builder.Append("FPS ").Append(fps.ToString("0"));
            builder.Append("   gfx ").Append(SystemInfo.graphicsDeviceType);
            builder.Append("\nXR ").Append(DescribeXr());
            builder.Append("\nCamera ").Append(Camera.main != null ? Camera.main.name : "NONE");
            if (Camera.main != null && !Camera.main.CompareTag("MainCamera"))
                builder.Append(" (not tagged MainCamera)");

            int loaded = 0;
            int submitted = 0;
            int renderers = 0;
            float sortRate = -1f;
            string shaderLine = "no renderer";

#if UNITY_6000_5_OR_NEWER
            var bases = FindObjectsByType<global::SplatXR.BaseRendererSplatXR>(FindObjectsInactive.Exclude);
#else
            var bases = FindObjectsByType<global::SplatXR.BaseRendererSplatXR>(FindObjectsSortMode.None);
#endif
            foreach (var renderer in bases)
            {
                renderers++;
                loaded += renderer.LoadedSplatCount;
                var meshFilter = renderer.GetComponent<MeshFilter>();
                if (meshFilter != null && meshFilter.sharedMesh != null)
                    submitted += meshFilter.sharedMesh.vertexCount / 4;
                sortRate = renderer.sortingRate;
                shaderLine = DescribeShader(renderer.GetComponent<MeshRenderer>());
            }

#if UNITY_6000_5_OR_NEWER
            var lods = FindObjectsByType<global::SplatXR.LoDRendererSplatXR>(FindObjectsInactive.Exclude);
#else
            var lods = FindObjectsByType<global::SplatXR.LoDRendererSplatXR>(FindObjectsSortMode.None);
#endif
            foreach (var renderer in lods)
            {
                renderers++;
                var meshFilter = renderer.GetComponent<MeshFilter>();
                if (meshFilter != null && meshFilter.sharedMesh != null)
                    submitted += meshFilter.sharedMesh.vertexCount / 4;
                sortRate = renderer.sortInterval;
                shaderLine = DescribeShader(renderer.GetComponent<MeshRenderer>());
            }

            builder.Append("\nSplat renderers ").Append(renderers);
            builder.Append("\nLoaded splats ").Append(loaded.ToString("N0"));
            builder.Append("\nIn mesh ").Append(submitted.ToString("N0"));
            if (sortRate >= 0f)
                builder.Append("\nSort every ").Append(sortRate.ToString("0.0")).Append(" s");
            builder.Append("\n").Append(shaderLine);

            bool left = QuestLocomotion.TryReadStick(XRNode.LeftHand, out _);
            bool right = QuestLocomotion.TryReadStick(XRNode.RightHand, out _);
            builder.Append("\nSticks L ").Append(left ? "yes" : "no");
            builder.Append("  R ").Append(right ? "yes" : "no");
            builder.Append("\nLeft stick move, right stick snap turn.");

            if (renderers == 0)
            {
                builder.Append("\n\nNo splat yet. Building Blocks panel,\nSplat Renderer, Add to Scene.");
                if (!warnedMissingSplat && Time.unscaledTime > 3f)
                {
                    warnedMissingSplat = true;
                    Debug.LogWarning("[SplatXR Quest] No BaseRendererSplatXR or LoDRendererSplatXR in the scene. The skybox, floor, and cyan marker are all you should see until a PLY is added.");
                }
            }
            else if (loaded > 0 && submitted == 0 && Time.unscaledTime > 5f)
            {
                builder.Append("\n\nSplat loaded but mesh is empty.\nSee adb logcat -s Unity.");
            }
            else if (loaded == 0 && Time.unscaledTime > 5f)
            {
                builder.Append("\n\nRenderer present, 0 splats loaded.");
            }

            return builder.ToString();
        }

        static string DescribeXr()
        {
            var general = UnityEngine.XR.Management.XRGeneralSettings.Instance;
            var loader = general != null && general.Manager != null ? general.Manager.activeLoader : null;
            if (loader == null)
                return "not running";
            return loader.GetType().Name;
        }

        static Sprite onePixel;

        static Sprite OnePixelSprite()
        {
            if (onePixel != null)
                return onePixel;
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            onePixel = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f));
            return onePixel;
        }

        static string DescribeShader(MeshRenderer meshRenderer)
        {
            if (meshRenderer == null || meshRenderer.sharedMaterial == null || meshRenderer.sharedMaterial.shader == null)
                return "Shader MISSING";
            var shader = meshRenderer.sharedMaterial.shader;
            return shader.name + (shader.isSupported ? " supported" : " NOT supported");
        }
    }
}
