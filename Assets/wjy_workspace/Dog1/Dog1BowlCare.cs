using System.Collections;
using UnityEngine;

namespace Wjy.DogTraining
{
    public sealed partial class Dog1Training
    {
        private bool lastBowlCareSucceeded;

        private string BowlAccessIssue(DogCareAction action)
        {
            DogToyBody bowl = world.CareBowl(action);
            if (bowl == null || !bowl.gameObject.activeInHierarchy) return "麦粒找不到对应的碗。";
            if (!bowl.IsSettled) return "先把" + bowl.Label + "放稳，麦粒才会过去。";
            Vector3 position = bowl.transform.position;
            if (Mathf.Abs(position.y - dog.transform.position.y) > .32f ||
                Vector3.Distance(position, world.ConstrainDogPosition(position)) > .25f)
                return bowl.Label + "放在了麦粒够不到的位置。";
            return "";
        }

        private string BowlCommandIssue(DogCareAction action)
        {
            string need = life.GetBlockReason(action);
            if (!string.IsNullOrEmpty(need)) return need;
            string access = BowlAccessIssue(action);
            if (!string.IsNullOrEmpty(access)) return access;
            return world.HasServing(action) ? "" : action == DogCareAction.Feed
                ? "饭碗里还没有狗粮，请先添一份。" : "水碗里还没有水，请先添上清水。";
        }

        private IEnumerator ConsumeFromBowl(DogCareAction action)
        {
            lastBowlCareSucceeded = false;
            string issue = BowlCommandIssue(action);
            if (!string.IsNullOrEmpty(issue))
            {
                reply = issue;
                yield break;
            }
            DogToyBody bowl = world.CareBowl(action);
            Vector3 station = bowl.transform.position;
            yield return dog.VisitStation(station, action == DogCareAction.Feed ? "去吃饭" : "去喝水", true);
            lastBowlCareSucceeded = dog.LastActivitySucceeded && bowl != null && bowl.IsSettled &&
                Vector3.Distance(station, bowl.transform.position) < .12f &&
                Vector3.ProjectOnPlane(station - dog.transform.position, Vector3.up).sqrMagnitude <= .49f &&
                world.HasServing(action) && string.IsNullOrEmpty(BowlAccessIssue(action));
            if (!lastBowlCareSucceeded)
            {
                reply = "碗的位置改变了，麦粒这次没有吃喝完成。请放稳后再试一次。";
                yield break;
            }
            world.EmptyBowl(action);
            FinishCare(action, true);
        }
    }
}
