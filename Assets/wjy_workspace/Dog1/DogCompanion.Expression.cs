using UnityEngine;
using UnityEngine.Rendering;

namespace Wjy.DogTraining
{
    public sealed partial class DogCompanion
    {
        private Transform puzzleMark;
        private Material puzzleInk;
        private Transform tongue;
        private Material tongueInk;

        private void UpdatePuzzleMark()
        {
            bool visible = IsPerforming && CurrentAction == "refuse" && head != null;
            if (!visible)
            {
                if (puzzleMark != null) puzzleMark.gameObject.SetActive(false);
                return;
            }
            if (puzzleMark == null) BuildPuzzleMark();
            puzzleMark.gameObject.SetActive(true);
            float appear = Pulse(actionAge, .06f, .28f, 1.38f, 2.1f);
            puzzleMark.position = head.position + Vector3.up * (.27f + .035f * appear) * bodyScale;
            Vector3 facing = user != null ? puzzleMark.position - user.position : -transform.forward;
            if (facing.sqrMagnitude > .001f) puzzleMark.rotation = Quaternion.LookRotation(facing, Vector3.up);
            puzzleMark.localScale = Vector3.one * (Mathf.Max(.01f, appear) * bodyScale);
        }

        private void BuildPuzzleMark()
        {
            puzzleMark = new GameObject("Puzzled question mark").transform;
            puzzleMark.SetParent(transform, false);
            puzzleInk = new Material(Shader.Find("Standard")) { color = new Color32(255, 205, 118, 255) };
            puzzleInk.SetFloat("_Glossiness", .18f);
            puzzleInk.EnableKeyword("_EMISSION");
            puzzleInk.SetColor("_EmissionColor", new Color32(67, 35, 11, 255));
            Vector3[] stroke =
            {
                new Vector3(-.050f,.074f,0), new Vector3(-.039f,.100f,0),
                new Vector3(-.014f,.114f,0), new Vector3(.017f,.112f,0),
                new Vector3(.040f,.095f,0), new Vector3(.047f,.071f,0),
                new Vector3(.038f,.049f,0), new Vector3(.018f,.034f,0),
                new Vector3(.001f,.016f,0), new Vector3(-.001f,-.004f,0)
            };
            var line = puzzleMark.gameObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = stroke.Length;
            line.SetPositions(stroke);
            line.widthMultiplier = .021f;
            line.numCapVertices = 6;
            line.numCornerVertices = 6;
            line.sharedMaterial = puzzleInk;
            line.shadowCastingMode = ShadowCastingMode.Off;
            var dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dot.name = "Question mark dot";
            dot.transform.SetParent(puzzleMark, false);
            dot.transform.localPosition = new Vector3(-.001f, -.037f, 0f);
            dot.transform.localScale = Vector3.one * .026f;
            Destroy(dot.GetComponent<Collider>());
            dot.GetComponent<Renderer>().sharedMaterial = puzzleInk;
        }

        private void UpdatePawGrooming()
        {
            float phase = Time.time - microActionAt;
            Transform paw = Bone(microActionSide > 0f ? "DEF-front_toes.R" : "DEF-front_toes.L");
            if (GroomReach <= .001f || IsPerforming || MuzzleAnchor == null || head == null || paw == null)
            {
                if (tongue != null) tongue.gameObject.SetActive(false);
                return;
            }
            Vector3 contact = paw.position + Vector3.up * .024f * bodyScale;
            AimGroomingJoint(Bone("DEF-neck.01"), contact, 22f);
            AimGroomingJoint(head, contact, 19f);
            Vector3 toward = contact - MuzzleAnchor.position;
            float pulse = Mathf.Max(0f, Mathf.Sin((phase - 1.35f) * 15f));
            bool canShow = phase >= 1.35f && phase <= 2.82f &&
                toward.magnitude > .005f && toward.magnitude < .105f * bodyScale && pulse > .45f;
            if (tongue != null) tongue.gameObject.SetActive(canShow);
            if (!canShow) return;
            if (tongue == null)
            {
                tongue = GameObject.CreatePrimitive(PrimitiveType.Sphere).transform;
                tongue.name = "Grooming tongue";
                tongue.SetParent(transform, false);
                Destroy(tongue.GetComponent<Collider>());
                tongueInk = new Material(Shader.Find("Standard")) { color = new Color32(192, 99, 102, 255) };
                tongue.GetComponent<Renderer>().sharedMaterial = tongueInk;
            }
            float length = Mathf.Min(toward.magnitude, .072f * bodyScale * pulse);
            tongue.position = MuzzleAnchor.position + toward.normalized * (length * .36f);
            tongue.rotation = Quaternion.LookRotation(toward, transform.up);
            tongue.localScale = new Vector3(.026f * bodyScale, .012f * bodyScale, length);
        }

        private void AimGroomingJoint(Transform joint, Vector3 contact, float limit)
        {
            if (joint == null) return;
            Vector3 nose = MuzzleAnchor.position - joint.position;
            Vector3 goal = contact - joint.position;
            Quaternion turn = Quaternion.FromToRotation(nose, goal);
            joint.rotation = Quaternion.RotateTowards(Quaternion.identity, turn, limit * GroomReach) * joint.rotation;
        }

        private void DisposeExpressions()
        {
            if (puzzleInk != null) Destroy(puzzleInk);
            if (tongueInk != null) Destroy(tongueInk);
        }
    }
}
