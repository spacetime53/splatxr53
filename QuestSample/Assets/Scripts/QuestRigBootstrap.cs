// Copyright (c) 2026 PSI Lab. MIT License. See LICENSE in the repository root.
using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;
using UnityEngine.XR.Management;

namespace SplatXR.QuestSample
{
    // Builds an OpenXR rig at startup so the scene file does not need package script GUIDs.
    [DefaultExecutionOrder(-200)]
    public class QuestRigBootstrap : MonoBehaviour
    {
        [Tooltip("Meters per second for left-stick locomotion.")]
        public float moveSpeed = 1.5f;

        [Tooltip("Degrees per right-stick snap.")]
        public float snapTurnDegrees = 45f;

        void Awake()
        {
            Debug.Log("[SplatXR Quest] Bootstrap starting. gfx=" + SystemInfo.graphicsDeviceType +
                      " (" + SystemInfo.graphicsDeviceName + "), platform=" + Application.platform + ".");

            Transform head = EnsureRig();
            if (FindAnyObjectByType<SplatSlot>() == null)
                CreateSlot();
            CreateReferenceMarkers();
            if (head != null)
                QuestSplatDebugOverlay.Create(head);
            StartCoroutine(ReportAfterXrStarts());
        }

        Transform EnsureRig()
        {
            var existing = FindAnyObjectByType<XROrigin>();
            if (existing != null && existing.Camera != null)
            {
                if (!existing.Camera.CompareTag("MainCamera"))
                    existing.Camera.tag = "MainCamera";
                Debug.Log("[SplatXR Quest] Using XR Origin already in the scene: " + existing.name);
                EnsureLocomotion(existing.transform, existing.Camera.transform);
                return existing.Camera.transform;
            }

            var originObject = new GameObject("XR Origin");
            var origin = originObject.AddComponent<XROrigin>();
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
            origin.CameraYOffset = 1.6f;

            var offset = new GameObject("Camera Offset");
            offset.transform.SetParent(originObject.transform, false);
            origin.CameraFloorOffsetObject = offset;

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(offset.transform, false);
            cameraObject.transform.localPosition = new Vector3(0f, 1.6f, 0f);

            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.backgroundColor = new Color(0.05f, 0.08f, 0.16f, 1f);
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 500f;
            camera.allowHDR = false;
            camera.allowMSAA = true;
            cameraObject.AddComponent<AudioListener>();

            var extra = cameraObject.AddComponent<UniversalAdditionalCameraData>();
            extra.renderType = CameraRenderType.Base;
            extra.renderPostProcessing = false;
            extra.requiresDepthTexture = false;
            extra.requiresColorTexture = false;
            extra.antialiasing = AntialiasingMode.None;

            try
            {
                var driver = cameraObject.AddComponent<TrackedPoseDriver>();
                driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
                driver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
                var position = new InputAction("HeadPosition", InputActionType.Value);
                position.AddBinding("<XRHMD>/centerEyePosition");
                position.AddBinding("<XRHMD>/devicePosition");
                var rotation = new InputAction("HeadRotation", InputActionType.Value);
                rotation.AddBinding("<XRHMD>/centerEyeRotation");
                rotation.AddBinding("<XRHMD>/deviceRotation");
                driver.positionAction = position;
                driver.rotationAction = rotation;
            }
            catch (System.Exception exception)
            {
                Debug.LogError("[SplatXR Quest] Head pose driver was not created. Active Input Handling must be Input System Package, then restart Unity and rebuild. " + exception.Message);
            }

            origin.Camera = camera;
            EnsureLocomotion(originObject.transform, cameraObject.transform);
            Debug.Log("[SplatXR Quest] Created XR Origin with a MainCamera. In the editor, without a headset, the camera stays at standing height.");
            return cameraObject.transform;
        }

        void EnsureLocomotion(Transform rig, Transform head)
        {
            var locomotion = rig.GetComponent<QuestLocomotion>();
            if (locomotion == null)
                locomotion = rig.gameObject.AddComponent<QuestLocomotion>();
            locomotion.rig = rig;
            locomotion.head = head;
            locomotion.moveSpeed = moveSpeed;
            locomotion.snapTurnDegrees = snapTurnDegrees;
        }

        static void CreateSlot()
        {
            var slot = new GameObject("SplatSlot");
            slot.AddComponent<SplatSlot>();
        }

        static void CreateReferenceMarkers()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Debug.LogError("[SplatXR Quest] No unlit shader found for the reference marker. If the splat is also missing, URP is not set as the active pipeline.");
                return;
            }

            var floor = GameObject.CreatePrimitive(PrimitiveType.Quad);
            floor.name = "ReferenceFloor";
            floor.transform.position = new Vector3(0f, 0.01f, 0f);
            floor.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            floor.transform.localScale = new Vector3(6f, 6f, 1f);
            ApplyColor(floor, shader, new Color(0.12f, 0.13f, 0.15f, 1f));

            var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = "ReferenceMarker";
            marker.transform.position = new Vector3(0f, 1.3f, 2f);
            marker.transform.localScale = Vector3.one * 0.18f;
            ApplyColor(marker, shader, new Color(0.1f, 0.9f, 0.95f, 1f));
            Debug.Log("[SplatXR Quest] Reference floor and cyan marker created. If you see those but not the room, the splat did not render. If you see only the skybox, the camera or pipeline is wrong.");
        }

        static void ApplyColor(GameObject target, Shader shader, Color color)
        {
            var material = new Material(shader);
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
            var renderer = target.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        IEnumerator ReportAfterXrStarts()
        {
            yield return null;
            yield return new WaitForSeconds(1.5f);

            var loader = XRGeneralSettings.Instance != null && XRGeneralSettings.Instance.Manager != null
                ? XRGeneralSettings.Instance.Manager.activeLoader
                : null;
            if (Application.platform == RuntimePlatform.Android && loader == null)
            {
                Debug.LogError("[SplatXR Quest] XR did not start on Android, so the head will not track. Enable OpenXR and the Meta Quest feature group for the Android tab, then rebuild. adb logcat -s Unity");
            }
            else
            {
                Debug.Log("[SplatXR Quest] XR loader: " + (loader != null ? loader.GetType().Name : "none. Editor Play without a headset does this; a Quest build should say OpenXRLoader."));
            }

            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            foreach (var display in displays)
            {
                if (!display.running)
                    continue;
                if (display.TryRequestDisplayRefreshRate(72f))
                    Debug.Log("[SplatXR Quest] Requested a 72 Hz display refresh.");
                else
                    Debug.LogWarning("[SplatXR Quest] Could not request 72 Hz. The headset will keep its current refresh rate.");
            }
        }
    }
}
