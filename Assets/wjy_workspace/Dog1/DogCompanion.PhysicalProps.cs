using UnityEngine;

namespace Wjy.DogTraining
{
    public sealed partial class DogCompanion
    {
        private Transform presentedToy;
        private float presentedToyActionAt = float.NegativeInfinity;
        private Vector2 presentedToyAim;
        private DogToyBody physicalBall;

        internal bool CanUsePhysicalBall => ball != null && (physicalBall == null || physicalBall.CanFetch);

        internal void ConfigurePhysicalFetchProps(out DogToyBody toyBall, out DogToyBody plush)
        {
            toyBall = ball.GetComponent<DogToyBody>();
            if (toyBall == null)
            {
                toyBall = ball.gameObject.AddComponent<DogToyBody>();
                ball.GetComponent<Renderer>().sharedMaterial = toyBall.ColorMaterial(new Color32(65, 122, 120, 255));
                var seam = new Color32(230, 206, 152, 255);
                for (int i = 0; i < 10; i++)
                {
                    float angle = i * 36f * Mathf.Deg2Rad;
                    Vector3 direction = new Vector3(Mathf.Sin(angle), .12f, Mathf.Cos(angle)).normalized;
                    Part(toyBall, "Ball grip", PrimitiveType.Sphere, direction * .48f,
                        new Vector3(.135f, .08f, .07f), Quaternion.LookRotation(direction), seam);
                }
                var sphere = ball.GetComponent<SphereCollider>();
                sphere.enabled = true;
                toyBall.Configure("玩具球", sphere, .22f);
            }
            physicalBall = toyBall;
            plush = lollipop.GetComponent<DogToyBody>();
            if (plush == null)
            {
                plush = lollipop.gameObject.AddComponent<DogToyBody>();
                for (int i = 0; i < lollipop.childCount; i++) lollipop.GetChild(i).gameObject.SetActive(false);
                var berry = new Color32(213, 128, 132, 255);
                var thread = new Color32(251, 222, 182, 255);
                Part(plush, "Padded candy-shaped cloth", PrimitiveType.Sphere, new Vector3(.135f, 0, 0),
                    new Vector3(.14f, .048f, .13f), Quaternion.identity, berry);
                Part(plush, "Folded fabric handle", PrimitiveType.Capsule, new Vector3(.042f, 0, 0),
                    new Vector3(.034f, .067f, .025f), Quaternion.Euler(0, 0, 90f), thread);
                for (int i = 0; i < 12; i++)
                {
                    float angle = i * 30f;
                    float radians = angle * Mathf.Deg2Rad;
                    Vector3 point = new Vector3(.135f + .053f * Mathf.Cos(radians), .016f, .05f * Mathf.Sin(radians));
                    Part(plush, "Topstitch", PrimitiveType.Sphere, point, new Vector3(.015f, .004f, .006f),
                        Quaternion.Euler(0, -angle + 70f, 0), thread);
                }
                var box = lollipop.gameObject.AddComponent<BoxCollider>();
                box.center = new Vector3(.095f, 0, 0);
                box.size = new Vector3(.21f, .047f, .128f);
                plush.Configure("棒棒糖毛绒玩具", box, .12f);
            }
        }

        internal DogToyBody CreateNeighborhoodToy(DogLocation location, Vector3 center)
        {
            var obj = new GameObject("Neighborhood toy");
            obj.transform.SetParent(trainingProps, false);
            obj.transform.position = center;
            var toy = obj.AddComponent<DogToyBody>();
            switch (location)
            {
                case DogLocation.Park:
                    var mint = new Color32(122, 171, 149, 255);
                    var honey = new Color32(240, 186, 103, 255);
                    Part(toy, "Padded disc", PrimitiveType.Cylinder, Vector3.zero, new Vector3(.21f, .018f, .21f),
                        Quaternion.identity, honey);
                    Part(toy, "Disc centerpiece", PrimitiveType.Cylinder, Vector3.up * .021f, new Vector3(.14f, .006f, .14f),
                        Quaternion.identity, mint);
                    var discBox = obj.AddComponent<BoxCollider>();
                    discBox.size = new Vector3(.18f, .038f, .18f);
                    toy.Configure("软飞盘", discBox, .16f);
                    break;
                case DogLocation.Terrace:
                    var sky = new Color32(160, 178, 211, 255);
                    var mist = new Color32(207, 216, 234, 255);
                    Part(toy, "Cloud body", PrimitiveType.Sphere, Vector3.zero, new Vector3(.21f, .057f, .13f),
                        Quaternion.identity, sky);
                    for (int i = -1; i <= 1; i++)
                        Part(toy, "Cloud lobe", PrimitiveType.Sphere, new Vector3(i * .059f, 0f, .032f),
                            new Vector3(.095f, .061f, i == 0 ? .16f : .125f), Quaternion.identity, mist);
                    var cloud = obj.AddComponent<BoxCollider>();
                    cloud.size = new Vector3(.21f, .057f, .168f);
                    cloud.center = Vector3.forward * .018f;
                    toy.Configure("云朵布偶", cloud, .12f);
                    break;
                default:
                    var peach = new Color32(214, 149, 111, 255);
                    Part(toy, "Stitched bone center", PrimitiveType.Capsule, Vector3.zero,
                        new Vector3(.062f, .069f, .061f), Quaternion.Euler(0, 0, 90f), peach);
                    for (int x = -1; x <= 1; x += 2)
                        for (int z = -1; z <= 1; z += 2)
                            Part(toy,"Cloth bone corner", PrimitiveType.Sphere,
                                new Vector3(x * .07f, 0, z * .031f), new Vector3(.075f, .055f, .070f), Quaternion.identity, peach);
                    var bone = obj.AddComponent<BoxCollider>();
                    bone.size = new Vector3(.20f, .055f, .13f);
                    toy.Configure("小布骨", bone, .12f);
                    break;
            }
            return toy;
        }

        private static void Part(DogToyBody toy, string label, PrimitiveType shape, Vector3 position,
            Vector3 scale, Quaternion rotation, Color32 color)
        {
            MakeProp(shape, toy.transform, label, position, scale, toy.ColorMaterial(color)).localRotation = rotation;
        }

        internal bool NoticePresentedToy(Transform item)
        {
            if (item == null || head == null || !TryLookAngles(item, 1.45f, 76f, out Vector2 aim) ||
                !TryPlayMicroAction("head_turn")) return false;
            presentedToy = item;
            presentedToyActionAt = microActionAt;
            presentedToyAim = aim;
            PlayTailWag();
            return true;
        }

        private bool ReadPresentedToyAngles(out Vector2 angles)
        {
            angles = default;
            if (microActionAt != presentedToyActionAt) return false;
            var held = presentedToy != null ? presentedToy.GetComponent<DogToyBody>() : null;
            if (held != null && held.Holder != DogToyHolder.Free && TryLookAngles(presentedToy, 1.65f, 82f, out Vector2 current))
                presentedToyAim = current;
            angles = presentedToyAim;
            return true;
        }

        private static void ResetFetchProp(Transform item, Vector3 target)
        {
            if (item == null) return;
            var physical = item.GetComponent<DogToyBody>();
            if (physical == null) item.SetPositionAndRotation(target, Quaternion.identity);
            else physical.SetHome(target, Quaternion.identity);
        }
    }
}
