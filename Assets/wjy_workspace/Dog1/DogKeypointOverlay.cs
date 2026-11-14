using System.Collections.Generic;
using UnityEngine;

namespace Wjy.DogTraining
{
    public sealed class DogKeypointOverlay : MonoBehaviour
    {
        private static readonly Color AxialColor = new Color32(113, 225, 233, 230);
        private static readonly Color LeftColor = new Color32(153, 203, 255, 235);
        private static readonly Color RightColor = new Color32(255, 167, 135, 235);
        private static readonly Color PlantedColor = new Color32(180, 255, 169, 255);
        private static readonly Color SwingColor = new Color32(255, 220, 118, 255);

        private static readonly string[][] Chains =
        {
            new[] { "DEF-head", "DEF-neck.02", "DEF-neck.01", "DEF-chest", "DEF-spine.02", "DEF-spine.01", "DEF-pelvis" },
            new[] { "DEF-scapula.L", "DEF-chest", "DEF-scapula.R" },
            new[] { "DEF-hind_thigh.L", "DEF-pelvis", "DEF-hind_thigh.R" },
            new[] { "DEF-head", "DEF-ear.01.L", "DEF-ear.02.L" },
            new[] { "DEF-head", "DEF-ear.01.R", "DEF-ear.02.R" },
            new[] { "DEF-chest", "DEF-scapula.L", "DEF-front_upper.L", "DEF-front_forearm.L", "DEF-front_paw.L", "DEF-front_toes.L" },
            new[] { "DEF-chest", "DEF-scapula.R", "DEF-front_upper.R", "DEF-front_forearm.R", "DEF-front_paw.R", "DEF-front_toes.R" },
            new[] { "DEF-pelvis", "DEF-hind_thigh.L", "DEF-hind_shin.L", "DEF-hind_hock.L", "DEF-hind_paw.L" },
            new[] { "DEF-pelvis", "DEF-hind_thigh.R", "DEF-hind_shin.R", "DEF-hind_hock.R", "DEF-hind_paw.R" },
            new[] { "DEF-pelvis", "DEF-tail.01", "DEF-tail.02", "DEF-tail.03", "DEF-tail.04" }
        };

        private readonly List<(Transform[] joints, Color color)> chains = new List<(Transform[], Color)>();
        private readonly HashSet<Transform> joints = new HashSet<Transform>();
        private Camera view;
        private DogCompanion owner;
        private Transform muzzle, head;

        public void Bind(IReadOnlyDictionary<string, Transform> bones)
        {
            owner = GetComponent<DogCompanion>();
            muzzle = owner != null ? owner.MuzzleAnchor : null;
            bones.TryGetValue("DEF-head", out head);
            chains.Clear();
            joints.Clear();
            foreach (string[] names in Chains)
            {
                var chain = new Transform[names.Length];
                for (int index = 0; index < names.Length; index++)
                {
                    if (!bones.TryGetValue(names[index], out Transform joint)) continue;
                    chain[index] = joint;
                    joints.Add(joint);
                }
                string tip = names[names.Length - 1];
                bool transverse = names[0].EndsWith(".L", System.StringComparison.Ordinal) &&
                    tip.EndsWith(".R", System.StringComparison.Ordinal);
                Color color = transverse ? AxialColor : tip.EndsWith(".L", System.StringComparison.Ordinal) ? LeftColor :
                    tip.EndsWith(".R", System.StringComparison.Ordinal) ? RightColor : AxialColor;
                chains.Add((chain, color));
            }
            if (muzzle != null) joints.Add(muzzle);
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (view == null) view = Camera.main;
            if (view == null || view.stereoEnabled) return;

            Color previousColor = GUI.color;
            Matrix4x4 previousMatrix = GUI.matrix;
            int previousDepth = GUI.depth;
            GUI.matrix = Matrix4x4.identity;
            GUI.depth = 20;
            float scale = Mathf.Clamp(Screen.height / 800f, 0.7f, 2f);
            foreach (var chain in chains)
            {
                for (int index = 1; index < chain.joints.Length; index++)
                    DrawLink(chain.joints[index - 1], chain.joints[index], chain.color, scale);
            }
            DrawLink(head, muzzle, SwingColor, scale);

            foreach (Transform joint in joints)
            {
                if (!Project(joint, out Vector2 at)) continue;
                bool planted = false;
                bool foot = owner != null && owner.TryGetFootContact(joint.name, out planted);
                Color fill = joint == muzzle ? SwingColor : AxialColor;
                if (foot)
                {
                    fill = planted ? PlantedColor : SwingColor;
                }
                DrawPoint(at, fill, (foot ? 4f : 2.6f) * scale);
            }

            GUI.depth = previousDepth;
            GUI.color = previousColor;
            GUI.matrix = previousMatrix;
        }

        private void DrawLink(Transform start, Transform end, Color color, float scale)
        {
            if (!Project(start, out Vector2 from) || !Project(end, out Vector2 to)) return;
            Vector2 line = to - from;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(line.y, line.x) * Mathf.Rad2Deg, from);
            GUI.color = new Color32(24, 35, 32, 210);
            GUI.DrawTexture(new Rect(from.x, from.y - 1.8f * scale, line.magnitude, 3.6f * scale), Texture2D.whiteTexture);
            GUI.color = color;
            GUI.DrawTexture(new Rect(from.x, from.y - .9f * scale, line.magnitude, 1.8f * scale), Texture2D.whiteTexture);
            GUI.matrix = Matrix4x4.identity;
        }

        private static void DrawPoint(Vector2 at, Color fill, float radius)
        {
            GUI.color = new Color32(24, 35, 32, 240);
            GUI.DrawTexture(new Rect(at.x - radius - 1, at.y - radius - 1, 2 * radius + 2, 2 * radius + 2), Texture2D.whiteTexture);
            GUI.color = fill;
            GUI.DrawTexture(new Rect(at.x - radius, at.y - radius, 2 * radius, 2 * radius), Texture2D.whiteTexture);
        }

        private bool Project(Transform joint, out Vector2 point)
        {
            point = default;
            if (joint == null) return false;
            Vector3 screen = view.WorldToScreenPoint(joint.position);
            if (screen.z <= view.nearClipPlane) return false;
            point = new Vector2(screen.x, Screen.height - screen.y);
            return true;
        }
    }
}
