using UnityEngine;
using UnityEngine.Rendering;

namespace Wjy.DogTraining
{
    public sealed partial class DogNeighborhood
    {
        private const float ApartmentWallHeight = 2.95f;
        private const float ApartmentWallDepth = .14f;
        private const float BedroomFront = -2.30f;
        private const float BathroomFront = -3.50f;
        private const string ApartmentResources = "Dog1/Apartment/";

        internal void UseApartment(DogApartmentLayout layout, Rect playerBounds, Rect dogBounds,
            DogApartmentLayout.Door[] doors, BoxCollider[] walls, Transform[] furnitureGroups, Transform[] dogPlayMarkers,
            Transform showerSwitch, ParticleSystem streams, ParticleSystem splashes, Color background)
        {
            root = layout.transform;
            playerSpace = playerBounds;
            gardenSpace = layout.GardenBounds;
            lift = layout.Lift;
            if (lift != null) lift.ResetLift();
            dogSpace = dogBounds;
            PlayerStart = layout.PlayerStart;
            FoodPosition = layout.FoodPoint;
            WaterPosition = layout.WaterPoint;
            ToyPosition = layout.ToyPoint;
            WastePosition = layout.WastePoint;
            WalkDestination = layout.WalkPoint;
            sky = background;
            layout.ApplyLighting();

            Physics.SyncTransforms();
            foreach (BoxCollider wall in walls)
                if (wall != null && wall.enabled && wall.gameObject.activeInHierarchy)
                    AddObstacle(wall.bounds);
            if (layout.GardenObstacles != null)
                foreach (BoxCollider obstacle in layout.GardenObstacles)
                    if (obstacle != null && obstacle.enabled && obstacle.gameObject.activeInHierarchy)
                    {
                        Bounds bounds = obstacle.bounds;
                        if (lift == null) AddObstacle(bounds);
                        else groundObstacles.Add(Rect.MinMaxRect(bounds.min.x, bounds.min.z, bounds.max.x, bounds.max.z));
                    }
            foreach (Transform group in furnitureGroups)
            {
                if (group == null || !group.gameObject.activeInHierarchy) continue;
                foreach (Transform item in group)
                {
                    if (!item.gameObject.activeInHierarchy) continue;
                    MeshRenderer[] renderers = item.GetComponentsInChildren<MeshRenderer>();
                    if (renderers.Length == 0) continue;
                    Bounds bounds = renderers[0].bounds;
                    foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
                    AddObstacle(bounds);
                }
            }
            foreach (Transform marker in dogPlayMarkers)
                if (marker != null && marker.gameObject.activeInHierarchy)
                    AddDogPlayObstacle(marker.position, marker.lossyScale.x, marker.lossyScale.z);
            AddDogPlayObstacle(FoodPosition, .39f, .39f);
            AddDogPlayObstacle(WaterPosition, .39f, .39f);
            AddDogPlayObstacle(FetchPosition + At(.10f, 0f, 0f), .55f, .55f);

            foreach (DogApartmentLayout.Door door in doors)
            {
                if (door.pivot == null || !door.pivot.gameObject.activeInHierarchy) continue;
                float yaw = door.pivot.eulerAngles.y;
                Vector3 target = door.pivot.TransformPoint(At(door.width * .5f, 1.13f, 0f));
                apartmentDoors.Add(new ApartmentDoor(door.pivot, target, door.width, yaw, door.openAngle, door.label));
            }
            if (showerSwitch != null)
            {
                showerControl = showerSwitch.position;
                showerStreams = streams;
                showerSplashes = splashes;
                if (showerStreams != null) showerStreams.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                if (showerSplashes != null) showerSplashes.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private void BuildApartment()
        {
            playerSpace = Rect.MinMaxRect(-5.20f, -6.52f, 5.20f, 4.9f);
            dogSpace = Rect.MinMaxRect(-2.85f, -1.83f, 3.73f, 3.12f);
            FoodPosition = At(-2.03f, 0f, .32f);
            WaterPosition = At(-2.49f, 0f, .32f);
            ToyPosition = At(2.5f, 0f, 1.25f);
            WastePosition = At(-1.75f, 0f, 2.5f);
            WalkDestination = At(3.13f, 0f, 2.08f);

            Lighting("CEDFE5", "FFF4E2", "DCE9F2", .78f);
            root.Find("Neighborhood sunlight").rotation = Quaternion.Euler(26f, 211f, 0f);
            RenderSettings.ambientEquatorColor = ColorOf("BBAC97");
            RenderSettings.ambientGroundColor = ColorOf("948575");

            BuildApartmentShell();
            FurnishLivingRoom();
            FurnishOpenKitchen();
            FurnishBedroom();
            FurnishBathroom();

            ApartmentFurniture("coatRack", "Entry hooks", At(0, 1.48f, -6.56f), At(.72f, .31f, .13f), 180f, false);
            Rug(At(0, -.007f, -5.95f), At(1.2f, .024f, .78f), ColorOf("C5B398"), ColorOf("F0DEBD"));
            ApartmentFlushLight("Entry ceiling light", At(0, 2.86f, -5.4f), 3.8f, .85f);
        }

        private void BuildApartmentShell()
        {
            const float left = -5.5f, right = 5.5f, front = 5.2f, back = -6.8f;
            const float hallLeft = -.9f, hallRight = .9f;
            Color oak = ColorOf("D3B18A");
            Color trim = ColorOf("FAF4E9");
            Color bathWall = ColorOf("DFE5D8");

            Box("Apartment floor slab", At(0, -.10f, -.8f), At(11, .16f, 12), oak, solid: true);
            for (int row = 0; row < 34; row++)
            {
                float z = back + (row + .5f) * (12f / 34f);
                float first = left;
                for (int board = 0; board < 5; board++)
                {
                    float end = board == 4 ? right : left + (board + 1) * 2.2f + (row % 4 - 1.5f) * .53f;
                    Color shade = oak * (.94f + (board * 29 + row * 11) % 13 * .010f);
                    Box("Light oak floorboard", At((first + end) * .5f, -.021f, z),
                        At(end - first - .009f, .037f, .346f), shade);
                    first = end;
                }
            }

            Box("Ivory ceiling", At(0, 3.01f, -.8f), At(11.12f, .12f, 12.12f), trim);

            Material wallpaper = MaterialFor(Color.white, false);
            wallpaper.mainTexture = Resources.Load<Texture2D>(ApartmentResources + "IvoryBotanicalWallpaper");

            ApartmentWall("Bedroom window wall", left, back, -6.29f, true, wallpaper);
            ApartmentWall("Left exterior wall", left, -4.70f, front, true, wallpaper);
            ApartmentWall("Bedroom window sill wall", left, -6.29f, -4.70f, true, wallpaper, top: .92f);
            ApartmentWall("Bedroom window lintel", left, -6.29f, -4.70f, true, wallpaper, bottom: 2.30f, blocks: false);
            Box("Bedroom frosted window", At(left, 1.61f, -5.495f), At(.06f, 1.38f, 1.59f), ColorOf("D5E6E5"), emission: true);
            Box("Bedroom oak window sill", At(left + .04f, .92f, -5.495f), At(.28f, .06f, 1.69f), ColorOf("D5B68E"));
            Box("Bedroom window mullion", At(left + .04f, 1.61f, -5.495f), At(.10f, 1.38f, .05f), trim);
            Box("Bedroom window head rail", At(left + .04f, 2.29f, -5.495f), At(.10f, .055f, 1.63f), trim);
            ApartmentWall("Right exterior wall", right, BathroomFront, front, true, wallpaper);
            ApartmentWall("Bathroom exterior side wall", right, back, BathroomFront, true, null, bathWall);
            ApartmentWall("Bedroom rear wall", back, left, hallLeft, false, wallpaper);
            ApartmentWall("Entry rear wall", back, hallLeft, hallRight, false, wallpaper);
            ApartmentWall("Bathroom rear wall", back, hallRight, right, false, null, bathWall);
            ApartmentWall("Bedroom hall wall", hallLeft, back, BedroomFront, true, wallpaper);
            ApartmentDoorway("Bedroom", "卧室门", BedroomFront, left, hallLeft, -2.71f, -1.41f, false, wallpaper);
            ApartmentWall("Bathroom partition", BathroomFront, hallRight, right, false, wallpaper, bathWall);
            ApartmentDoorway("Bathroom", "卫浴门", hallRight, back, BathroomFront, -4.85f, -3.68f, true, wallpaper, bathWall);

            ApartmentWall("Window wall left pier", front, left, -4.77f, false, wallpaper);
            ApartmentWall("Window wall center pier", front, -.97f, .97f, false, wallpaper);
            ApartmentWall("Window wall right pier", front, 4.77f, right, false, wallpaper);
            ApartmentWindow(-2.87f, front, wallpaper);
            ApartmentWindow(2.87f, front, wallpaper);
            Skyline(23f, -15f, 31, false);

            Box("Entry door", At(0, 1.12f, -6.69f), At(.98f, 2.24f, .08f), ColorOf("DCBE96"));
            Box("Entry door inset", At(0, 1.24f, -6.645f), At(.72f, 1.63f, .024f), ColorOf("E7CDA9"));
            Box("Entry door brass lever", At(.34f, 1.04f, -6.59f), At(.14f, .034f, .085f), ColorOf("AF875A"));
        }

        private void ApartmentWall(string name, float along, float from, float to, bool sideWall, Material wallpaper,
            Color? reverseColor = null, float bottom = 0f, float top = ApartmentWallHeight, bool blocks = true)
        {
            float width = to - from;
            float spanCenter = (from + to) * .5f;
            float height = top - bottom;
            float mid = (bottom + top) * .5f;
            Vector3 center = sideWall ? At(along, mid, spanCenter) : At(spanCenter, mid, along);
            Vector3 size = sideWall ? At(ApartmentWallDepth, height, width) : At(width, height, ApartmentWallDepth);
            Box(name, center, size, reverseColor ?? ColorOf("F6EEE0"), solid: true, block: blocks);

            if (wallpaper != null)
            {
                Vector3 outwards = sideWall ? Vector3.right : Vector3.forward;
                // The reverse side of the bathroom walls has a washable finish.
                bool paperPositive = !sideWall;
                for (int side = -1; side <= 1; side += 2)
                {
                    if (reverseColor.HasValue && (side > 0) != paperPositive) continue;
                    Vector3 normal = outwards * side;
                    GameObject face = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    face.name = "Ivory botanical wallpaper";
                    face.transform.SetParent(root, true);
                    face.transform.SetPositionAndRotation(center + normal * (ApartmentWallDepth * .5f + .001f),
                        Quaternion.LookRotation(-normal));
                    face.transform.localScale = At(width, height, 1f);
                    face.GetComponent<Collider>().enabled = false;
                    var renderer = face.GetComponent<Renderer>();
                    renderer.sharedMaterial = wallpaper;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    var properties = new MaterialPropertyBlock();
                    properties.SetVector("_MainTex_ST", new Vector4(width / 1.65f, height / 1.65f, from * side / 1.65f, bottom / 1.65f));
                    renderer.SetPropertyBlock(properties);
                }
            }

            if (bottom != 0f) return;
            Vector3 skirting = sideWall ? At(.184f, .105f, width) : At(width, .105f, .184f);
            Box("Ivory skirting", At(center.x, .0525f, center.z), skirting, ColorOf("F9F3E6"));
        }

        private void ApartmentDoorway(string name, string label, float along, float from, float to, float low, float high,
            bool sideWall, Material wallpaper, Color? reverseColor = null)
        {
            const float height = 2.2f;
            ApartmentWall(name + " wall", along, from, low, sideWall, wallpaper, reverseColor);
            ApartmentWall(name + " wall", along, high, to, sideWall, wallpaper, reverseColor);
            ApartmentWall(name + " lintel", along, low, high, sideWall, wallpaper, reverseColor, height, blocks: false);

            Vector3 Position(float span, float y) => sideWall ? At(along, y, span) : At(span, y, along);
            Vector3 Size(float width, float y, float depth) => sideWall ? At(depth, y, width) : At(width, y, depth);

            Color trim = ColorOf("E0C7A6");
            float center = (low + high) * .5f;
            foreach (float jamb in new[] { low, high })
                Box(name + " door jamb", Position(jamb, height * .5f), Size(.055f, height, .19f), trim);
            Box(name + " door head", Position(center, height), Size(high - low + .055f, .065f, .19f), trim);
            Box(name + " threshold", Position(center, .006f), Size(high - low, .018f, .20f), trim);

            float hinge = sideWall ? high - .028f : low + .028f;
            AddApartmentDoor(name, label, Position(hinge, 0f), Position(center, 1.13f),
                high - low - .056f, sideWall ? 90f : 0f, sideWall ? -90f : 90f);
        }

        private void ApartmentWindow(float x, float z, Material wallpaper)
        {
            float from = x - 1.90f, to = x + 1.90f;
            ApartmentWall("Window sill wall", z, from, to, false, wallpaper, top: .68f);
            ApartmentWall("Window lintel", z, from, to, false, wallpaper, bottom: 2.53f, blocks: false);
            Color frame = ColorOf("EEE2CD");
            Box("Oak window sill", At(x, .705f, z - .05f), At(3.96f, .075f, .34f), ColorOf("D5B68E"));
            Box("Window head rail", At(x, 2.51f, z), At(3.84f, .071f, .12f), frame);
            Box("Window crossbar", At(x, 1.38f, z), At(3.84f, .048f, .10f), frame);
            for (int i = -1; i <= 1; i++)
                Box("Window mullion", At(x + i * 1.87f, 1.60f, z), At(.061f, 1.86f, .13f), frame);

            Cylinder("Curtain rod bracket", At(x, 2.72f, z - .21f), At(.042f, 1.99f, .042f),
                ColorOf("B99F7E"), At(0, 0, 90f));
            foreach (int side in new[] { -1, 1 })
                for (int pleat = 0; pleat < 7; pleat++)
                    Box("Linen curtain", At(x + side * (1.88f - pleat * .079f), 1.53f, z - .25f - .033f * (pleat % 2)),
                        At(.10f, 2.24f - .035f * (pleat % 2), .11f),
                        ColorOf("F9EDDA") * (pleat % 2 == 0 ? 1f : .965f));
        }

        private void FurnishLivingRoom()
        {
            Rug(At(-3.51f, -.006f, 1.85f), At(3.25f, .029f, 3.38f), ColorOf("DED4BD"), ColorOf("F6EDDB"));
            ApartmentFurniture("loungeSofa", "Sage living room sofa", At(-3.72f, 0, 3.89f), At(2.67f, .85f, .94f));
            ApartmentFurniture("pillow", "Clay sofa cushion", At(-4.45f, .45f, 3.99f), At(.43f, .37f, .18f), 10f, false);
            ApartmentFurniture("pillowBlue", "Cream sofa cushion", At(-3.26f, .45f, 3.99f), At(.43f, .31f, .17f), -12f, false);
            ApartmentFurniture("tableCoffee", "Oak coffee table", At(-3.78f, 0, 2.18f), At(1.40f, .49f, .76f));
            ApartmentFurniture("plantSmall2", "Coffee table succulent", At(-4.17f, .49f, 2.24f), At(.18f, .23f, .18f), 0f, false);
            ApartmentFurniture("lampRoundFloor", "Linen floor lamp", At(-5.06f, 0, 2.54f), At(.37f, 1.64f, .45f));
            ApartmentFurniture("cabinetTelevisionDoors", "Oak media sideboard", At(-4.03f, 0, BedroomFront + .41f),
                At(1.82f, .58f, .46f), 180f);
            ApartmentFurniture("televisionModern", "Living room television", At(-4.03f, .58f, BedroomFront + .41f),
                At(1.38f, .84f, .23f), 180f, false);
            ApartmentFurniture("pottedPlant", "Living room greenery", At(-5.06f, 0, -1.23f), At(.48f, 1.50f, .54f));
            DogBed(At(-4.52f, 0, -.34f), ColorOf("C4A88A"));
            Picture(At(-3.57f, 2.03f, BedroomFront + .086f), .66f, ColorOf("BAC4A4"), Vector3.forward);
            Picture(At(-4.46f, 1.96f, BedroomFront + .086f), .50f, ColorOf("DABAA0"), Vector3.forward);
            Pendant(At(-3.50f, 2.55f, 1.80f));
        }

        private void FurnishOpenKitchen()
        {
            const float counterX = 5.01f;
            ApartmentFurniture("kitchenFridge", "Cream refrigerator", At(4.97f, 0, -2.91f), At(.85f, 1.90f, .80f), 90f);
            ApartmentFurniture("kitchenCabinetDrawer", "Prep counter", At(counterX, 0, -2.01f), At(.84f, .92f, .76f), 90f);
            ApartmentFurniture("kitchenStoveElectric", "Cooktop and oven", At(counterX, 0, -1.17f), At(.84f, .92f, .76f), 90f);
            ApartmentFurniture("kitchenCabinetDrawer", "Counter drawers", At(counterX, 0, -.33f), At(.84f, .92f, .76f), 90f);
            ApartmentFurniture("kitchenSink", "Kitchen sink", At(counterX, 0, .51f), At(.84f, 1.02f, .76f), 90f);
            ApartmentFurniture("kitchenCabinetDrawer", "Coffee counter", At(counterX, 0, 1.35f), At(.84f, .92f, .76f), 90f);
            ApartmentFurniture("hoodModern", "Range hood", At(5.16f, 1.64f, -1.17f), At(.74f, .66f, .47f), 90f, false);
            ApartmentFurniture("kitchenCabinetUpperDouble", "Upper cupboards", At(5.18f, 1.65f, .52f),
                At(1.64f, .72f, .42f), 90f, false);
            ApartmentFurniture("toaster", "Cream toaster", At(4.92f, .92f, -2.04f), At(.29f, .19f, .18f), 90f, false);
            ApartmentFurniture("plantSmall2", "Counter herbs", At(4.98f, .92f, 1.44f), At(.23f, .29f, .23f), 0f, false);
            for (int col = 0; col < 10; col++)
                for (int row = 0; row < 4; row++)
                    Box("Cream backsplash tile", At(5.411f, 1.015f + row * .18f, -2.24f + col * .417f),
                        At(.021f, .172f, .408f), ColorOf((col + row) % 3 == 0 ? "ECE2CC" : "F2E9D6"));

            ApartmentFurniture("table", "Rectangular oak kitchen table", At(3.12f, 0, -2.13f),
                At(1.72f, .88f, .92f), 90f);
            ApartmentFurniture("chairModernCushion", "Kitchen table chair", At(2.30f, 0, -2.31f),
                At(.46f, 1.00f, .49f), -90f);

            ApartmentFurniture("tableRound", "Round breakfast table", At(3.89f, 0, 3.84f), At(1.15f, .76f, 1.15f));
            ApartmentFurniture("chairModernCushion", "Breakfast chair", At(3.89f, 0, 3.16f), At(.44f, .91f, .49f), 180f);
            ApartmentFurniture("chairModernCushion", "Window breakfast chair", At(3.89f, 0, 4.54f), At(.44f, .91f, .49f));
            ApartmentFurniture("plantSmall2", "Breakfast table plant", At(3.89f, .76f, 3.84f), At(.23f, .30f, .23f), 0f, false);
            Pendant(At(3.89f, 2.50f, 3.84f));
            ApartmentFlushLight("Kitchen ceiling light", At(4.09f, 2.86f, -.70f), 4.55f, 1.38f);
        }

        private void FurnishBedroom()
        {
            Rug(At(-3.65f, -.005f, -4.70f), At(2.8f, .025f, 3.12f), ColorOf("E4CBB7"), ColorOf("F5E5D1"));
            ApartmentFurniture("bedDouble", "Double bed with sage linen", At(-3.73f, 0, -5.64f), At(1.79f, .71f, 2.12f), 180f);
            ApartmentFurniture("sideTableDrawers", "Bedside table", At(-2.34f, 0, -6.32f), At(.60f, .56f, .49f), 180f);
            ApartmentFurniture("sideTableDrawers", "Second bedside table", At(-5.05f, 0, -6.32f), At(.52f, .56f, .49f), 180f);
            ApartmentFurniture("lampRoundTable", "Bedside lamp", At(-2.34f, .56f, -6.33f), At(.27f, .47f, .29f), 0f, false);
            ApartmentFurniture("plantSmall2", "Bedside plant", At(-5.07f, .56f, -6.32f), At(.20f, .26f, .20f), 0f, false);
            ApartmentFurniture("bookcaseClosedDoors", "Bedroom wardrobe", At(-4.78f, 0, BedroomFront - .43f),
                At(1.05f, 1.91f, .59f));
            ApartmentFurniture("chairModernCushion", "Bedroom reading chair", At(-3.63f, 0, -3.09f),
                At(.54f, .93f, .57f), 25f);
            ApartmentFurniture("lampRoundFloor", "Bedroom reading lamp", At(-3.12f, 0, -2.66f),
                At(.34f, 1.47f, .37f));
            Picture(At(-3.73f, 1.97f, -6.712f), .76f, ColorOf("C3C8AA"), Vector3.forward);
            ApartmentFlushLight("Bedroom ceiling light", At(-3.24f, 2.86f, -5.08f), 4.7f, 1.68f);
        }

        private void FurnishBathroom()
        {
            Color grout = ColorOf("C6CEC1");
            Box("Bathroom grout bed", At(3.20f, .0005f, -5.15f), At(4.48f, .011f, 3.16f), grout);
            for (int column = 0; column < 10; column++)
                for (int row = 0; row < 7; row++)
                    Box("Soft sage ceramic floor tile", At(1.184f + column * .448f, .009f, -6.499f + row * .45f),
                        At(.440f, .009f, .442f), ColorOf((column + row) % 3 == 0 ? "D9E2D5" : "E4E9DF"));

            ApartmentFurniture("shower", "Glazed walk-in shower", At(4.75f, .014f, -6.01f), At(1.29f, 2.17f, 1.35f), 90f);
            BuildShowerWater(At(4.93f, 1.69f, -6.23f), At(4.93f, .182f, -6.23f), At(4.07f, 1.12f, -6.01f));
            const float fixtureX = 2.30f;
            ApartmentFurniture("toilet", "Porcelain toilet", At(fixtureX + .28f, .014f, BathroomFront - .525f),
                At(.57f, .80f, .88f), 90f);
            ApartmentFurniture("bathroomSinkSquare", "Porcelain vanity basin", At(fixtureX, .014f, -6.34f), At(.86f, .91f, .57f), 180f);
            ApartmentFurniture("bathroomMirror", "Vanity mirror", At(fixtureX, 1.12f, -6.61f), At(.60f, .72f, .17f), 180f, false);
            ApartmentFurniture("washer", "Front-loading laundry machine", At(4.95f, .014f, -3.96f),
                At(.75f, .87f, .69f));
            ApartmentFurniture("plantSmall2", "Bathroom plant", At(4.94f, .884f, -3.96f),
                At(.22f, .28f, .22f), 0f, false);
            Rug(At(4.05f, .013f, -4.83f), At(1.11f, .019f, .64f), ColorOf("AABEA8"), ColorOf("DEDDC9"));
            Box("Oak towel rail", At(5.38f, 1.12f, -4.66f), At(.10f, .034f, .61f), ColorOf("C19B73"));
            Box("Cotton bath towel", At(5.33f, .91f, -4.66f), At(.055f, .42f, .44f), ColorOf("F3E6CF"));
            ApartmentFlushLight("Bathroom ceiling light", At(3.07f, 2.86f, -5.05f), 4.8f, 1.63f);
        }

        private void ApartmentFlushLight(string name, Vector3 position, float range, float intensity)
        {
            Cylinder(name + " diffuser", position, At(.38f, .045f, .38f), ColorOf("FFF0D3"), emission: true);
            MakeLight(name, position + At(0, -.22f, 0), LightType.Point, ColorOf("FFF1DB"), intensity, range);
        }

        private void ApartmentFurniture(string asset, string name, Vector3 position, Vector3 size, float yaw = 0f, bool block = true)
        {
            var model = Resources.Load<GameObject>(ApartmentResources + "Furniture/" + asset);
            if (model == null)
            {
                Debug.LogError("Missing apartment furniture model: " + asset);
                return;
            }

            GameObject piece = UnityEngine.Object.Instantiate(model, root, false);
            piece.name = name;
            Renderer[] renderers = piece.GetComponentsInChildren<Renderer>();
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers)
            {
                bounds.Encapsulate(renderer.bounds);
                Material[] original = renderer.sharedMaterials;
                for (int slot = 0; slot < original.Length; slot++)
                    original[slot] = ApartmentFurnitureMaterial(asset, original[slot]);
                renderer.sharedMaterials = original;
            }

            Vector3 scale = At(size.x / bounds.size.x, size.y / bounds.size.y, size.z / bounds.size.z);
            Quaternion rotation = Quaternion.Euler(0, yaw, 0);
            Vector3 pivot = piece.transform.InverseTransformPoint(At(bounds.center.x, bounds.min.y, bounds.center.z));
            piece.transform.localScale = Vector3.Scale(piece.transform.localScale, scale);
            piece.transform.SetPositionAndRotation(position - rotation * Vector3.Scale(pivot, piece.transform.localScale), rotation);

            if (!block) return;
            bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers)
                bounds.Encapsulate(renderer.bounds);
            AddObstacle(bounds);
        }

        private void AddObstacle(Bounds bounds)
        {
            obstacles.Add(Rect.MinMaxRect(bounds.min.x, bounds.min.z, bounds.max.x, bounds.max.z));
        }

        private Material ApartmentFurnitureMaterial(string asset, Material source)
        {
            string color;
            if (source == null) return MaterialFor(ColorOf("F9F4E7"), false);
            switch (source.name)
            {
                case "wood": color = asset.StartsWith("kitchen") ? "B7C2AA" : "D5B187"; break;
                case "woodDark": color = "ACB199"; break;
                case "carpetBlue": color = asset == "pillowBlue" ? "F0DCB8" : "AAB9A1"; break;
                case "carpet": color = asset == "pillow" ? "D7A58D" : "A6B69B"; break;
                case "carpetWhite": color = "F9F4E7"; break;
                case "metal": color = asset == "televisionModern" ? "394742" : "B9A68A"; break;
                case "metalLight": color = "EAE2D3"; break;
                case "metalMedium": color = "C4BDAD"; break;
                case "metalDark": color = "686F68"; break;
                case "glass": color = "819E9C"; break;
                case "plant": color = "73946D"; break;
                case "lamp": color = "F5DEAC"; break;
                default: return source;
            }

            if (source.name == "glass" && asset == "shower")
            {
                // The thin glass panel uses the same shader as the rest of the room.
                const string key = "Apartment shower glass";
                if (!materials.TryGetValue(key, out Material glass))
                {
                    glass = new Material(Shader.Find("Standard")) { name = key, color = ColorOf("B9DAD8") };
                    glass.color = new Color(glass.color.r, glass.color.g, glass.color.b, .27f);
                    glass.SetFloat("_Mode", 2f);
                    glass.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                    glass.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                    glass.SetFloat("_ZWrite", 0f);
                    glass.EnableKeyword("_ALPHABLEND_ON");
                    glass.SetOverrideTag("RenderType", "Transparent");
                    glass.renderQueue = (int)RenderQueue.Transparent;
                    materials.Add(key, glass);
                }
                return glass;
            }

            return MaterialFor(ColorOf(color), source.name == "lamp");
        }

        private void Picture(Vector3 position, float scale, Color accent, Vector3 normal)
        {
            Quaternion facing = Quaternion.LookRotation(-normal);
            Vector3 rotation = facing.eulerAngles;
            Box("Oak picture frame", position, At(scale, scale * 1.25f, .038f), ColorOf("D1AB82"), rotation);
            Box("Ivory picture mat", position + normal * .023f, At(scale * .88f, scale * 1.11f, .012f),
                ColorOf("FAF1E2"), rotation);
            Box("Landscape print", position + normal * .032f, At(scale * .72f, scale * .91f, .008f),
                accent, rotation);
            Shape(PrimitiveType.Sphere, "Print setting sun", position + normal * .039f + At(.10f, .13f, 0),
                At(scale * .28f, scale * .28f, .008f), ColorOf("FFF0CB"), rotation);
            Shape(PrimitiveType.Sphere, "Print green hill", position + normal * .040f + At(-.042f, -.12f, 0),
                At(scale * .53f, scale * .29f, .008f), ColorOf("829878"), rotation);
        }
    }
}
