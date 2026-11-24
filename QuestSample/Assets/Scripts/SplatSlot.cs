// Copyright (c) 2026 PSI Lab. MIT License. See LICENSE in the repository root.
using UnityEngine;

namespace SplatXR.QuestSample
{
    // Marker for where a room splat should sit. The Building Blocks panel creates its own
    // object; it does not have to be parented here. The debug overlay searches the whole scene.
    public class SplatSlot : MonoBehaviour
    {
        [TextArea(3, 6)]
        public string instructions =
            "Add your PLY with SplatXR > Building Blocks Panel > Splat Renderer > Base Renderer > Add to Scene. " +
            "Parenting the result under this object is optional. Indoor scans are often several meters across and should stay near the origin.";

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.1f, 0.85f, 0.95f, 0.9f);
            Gizmos.DrawWireCube(transform.position + Vector3.up * 0.75f, new Vector3(1f, 1.5f, 1f));
        }
    }
}
