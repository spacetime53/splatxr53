// Copyright (c) 2026 PSI Lab. MIT License. See LICENSE in the repository root.
using UnityEngine;
using UnityEngine.XR;

namespace SplatXR.QuestSample
{
    // Left thumbstick: smooth move on the floor, relative to head yaw.
    // Right thumbstick: snap turn. Reads the XR devices directly so it does not
    // depend on an Input Actions asset.
    public class QuestLocomotion : MonoBehaviour
    {
        public Transform rig;
        public Transform head;
        public float moveSpeed = 1.5f;
        public float snapTurnDegrees = 45f;
        public float moveDeadzone = 0.15f;
        public float snapThreshold = 0.7f;
        public float snapRelease = 0.3f;

        bool snapHeld;

        void Update()
        {
            if (rig == null)
                return;

            Vector2 move = ReadStick(XRNode.LeftHand);
            if (move.magnitude >= moveDeadzone)
            {
                float yaw = head != null ? head.eulerAngles.y : rig.eulerAngles.y;
                Vector3 world = Quaternion.Euler(0f, yaw, 0f) * new Vector3(move.x, 0f, move.y);
                rig.position += world * (moveSpeed * Time.deltaTime);
            }

            Vector2 turn = ReadStick(XRNode.RightHand);
            if (!snapHeld && Mathf.Abs(turn.x) >= snapThreshold)
            {
                rig.Rotate(0f, Mathf.Sign(turn.x) * snapTurnDegrees, 0f, Space.World);
                snapHeld = true;
            }
            else if (Mathf.Abs(turn.x) <= snapRelease)
            {
                snapHeld = false;
            }
        }

        public static bool TryReadStick(XRNode node, out Vector2 value)
        {
            InputDevice device = InputDevices.GetDeviceAtXRNode(node);
            if (device.isValid && device.TryGetFeatureValue(CommonUsages.primary2DAxis, out value))
                return true;
            value = Vector2.zero;
            return false;
        }

        static Vector2 ReadStick(XRNode node)
        {
            TryReadStick(node, out Vector2 value);
            return value;
        }
    }
}
