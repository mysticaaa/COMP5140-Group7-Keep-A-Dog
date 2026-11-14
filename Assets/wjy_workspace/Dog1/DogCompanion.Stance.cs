using UnityEngine;

namespace Wjy.DogTraining
{
    public sealed partial class DogCompanion
    {
        private const float QuietStanceDelay = .08f;
        private const float QuietStepDuration = .16f;
        private const float QuietStepDistance = .024f;
        private float quietStanceAge, quietStepAge;
        private Foot quietStep;
        private Vector3 quietStepStart, quietStepGoal;
        private float quietStepHeight;

        private void CancelQuietStance()
        {
            // The quiet arc continuously updates the gait swing data, so a resumed gait can finish it.
            quietStep = null;
            quietStanceAge = 0f;
            quietStepAge = 0f;
        }

        private void UpdateQuietStance(float dt)
        {
            if (dt <= 0f) return;
            quietStanceAge = Mathf.Min(QuietStanceDelay, quietStanceAge + dt);
            if (quietStep != null)
            {
                AdvanceQuietStep(dt);
                return;
            }

            // More than one paw may already be airborne when the root abruptly stops. Complete those
            // strikes before lifting any other paw, even when a burst was stopped by an obstacle.
            bool landing = false;
            foreach (Foot foot in feet)
            {
                if (foot.Planted) continue;
                Vector3 oldContact = foot.Contact;
                foot.Landing = KeepFootOnOwnSide(foot, foot.Landing);
                AdvanceSwing(foot, dt * 7f);
                if (foot.SwingProgress >= 1f) foot.Contact.y = transform.position.y + PawGroundClearance;
                // Finish an interrupted swing from its visible height. Avoid rising at braking.
                else foot.Contact.y = Mathf.Min(foot.Contact.y, oldContact.y + .015f);
                landing = true;
            }
            if (landing || quietStanceAge < QuietStanceDelay) return;

            // Largest correction first, in rig order on ties. Each of the other three contacts stays fixed.
            float best = QuietStepDistance * QuietStepDistance;
            foreach (Foot foot in feet)
            {
                Vector3 goal = FootGoal(foot);
                float distance = (goal - foot.Contact).sqrMagnitude;
                if (distance <= best) continue;
                best = distance;
                quietStep = foot;
                quietStepGoal = goal;
            }
            if (quietStep == null) return;

            quietStepStart = quietStep.Contact;
            quietStepAge = 0f;
            quietStepHeight = Mathf.Min(.038f, .015f + FlatDistance(quietStepStart, quietStepGoal) * .24f);
            quietStep.SwingStart = quietStepStart;
            quietStep.Landing = quietStepGoal;
            quietStep.SwingHeight = quietStepHeight;
            quietStep.SwingFraction = 1f - StanceFraction;
            AdvanceQuietStep(dt);
        }

        private void AdvanceQuietStep(float dt)
        {
            quietStepAge += dt;
            float progress = Mathf.Clamp01(quietStepAge / QuietStepDuration);
            Vector3 point = Vector3.Lerp(quietStepStart, quietStepGoal, Mathf.SmoothStep(0f, 1f, progress));
            point.y += quietStepHeight * Mathf.Sin(Mathf.PI * progress);
            quietStep.Contact = KeepFootOnOwnSide(quietStep, point);
            quietStep.Planted = progress >= 1f;
            quietStep.SwingProgress = progress;
            if (quietStep.Planted) quietStep = null;
        }
    }
}
