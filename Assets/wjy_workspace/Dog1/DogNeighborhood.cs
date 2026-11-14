using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wjy.DogTraining
{
    public sealed class DogNeighborhood : IDisposable
    {
        private readonly Transform parent;
        private readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        private readonly List<Rect> obstacles = new List<Rect>();
        private readonly List<Rect> dogPlayObstacles = new List<Rect>();
        private Transform root;
        private Rect playerSpace;
        private Rect dogSpace;
        private Color sky;

        public Vector3 PlayerStart => new Vector3(0f, 1.65f, -4.8f);
        public Vector3 DogStart => new Vector3(1.55f, 0f, -1.2f);
        public Vector3 FetchPosition => new Vector3(3.15f, 0.11f, -0.45f);
        public Vector3 WalkDestination { get; private set; }
        public Vector3 WastePosition { get; private set; }
        public Vector3 ToyPosition { get; private set; }
        public Vector3 FoodPosition { get; private set; }
        public Vector3 WaterPosition { get; private set; }
        public float PlayerBoundary => playerSpace.xMax;

        public DogNeighborhood(Transform parent) => this.parent = parent;

        public void Load(int location)
        {
            if (location < 0 || location > 2) throw new ArgumentOutOfRangeException(nameof(location));
            Dispose();
            root = new GameObject(location == 0 ? "City Apartment" : location == 1 ? "Neighborhood Park" : "Roof Terrace").transform;
            root.SetParent(parent, true);
            switch (location)
            {
                case 0: BuildApartment(); break;
                case 1: BuildPark(); break;
                case 2: BuildTerrace(); break;
            }
            BuildCareStation();
        }

        public void ConfigureCamera(Camera camera)
        {
            if (camera == null) return;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = sky;
            camera.fieldOfView = 63f;
            camera.farClipPlane = 145f;
        }

        public Vector3 ConstrainPlayerMotion(Vector3 prior, Vector3 proposed)
        {
            proposed = ClampTo(playerSpace, proposed);
            Vector3 slideX = new Vector3(proposed.x, proposed.y, prior.z);
            if (HitsObstacle(slideX, 0.21f)) slideX.x = prior.x;
            proposed.x = slideX.x;
            if (HitsObstacle(proposed, 0.21f)) proposed.z = prior.z;
            return ClampTo(playerSpace, proposed);
        }

        public Vector3 ConstrainDogPosition(Vector3 proposed) => ClampTo(dogSpace, proposed);

        public bool HasDogPlayClearance(Vector3 position, float radius)
        {
            if (root == null || !(radius >= 0f) || float.IsInfinity(radius) ||
                float.IsNaN(position.x) || float.IsNaN(position.z))
                return false;
            if (position.x - radius < dogSpace.xMin || position.x + radius > dogSpace.xMax ||
                position.z - radius < dogSpace.yMin || position.z + radius > dogSpace.yMax)
                return false;
            return !HitsObstacle(position, radius, obstacles) && !HitsObstacle(position, radius, dogPlayObstacles);
        }

        private static Vector3 ClampTo(Rect bounds, Vector3 position)
        {
            position.x = Mathf.Clamp(position.x, bounds.xMin, bounds.xMax);
            position.z = Mathf.Clamp(position.z, bounds.yMin, bounds.yMax);
            return position;
        }

        private bool HitsObstacle(Vector3 point, float radius) => HitsObstacle(point, radius, obstacles);

        private static bool HitsObstacle(Vector3 point, float radius, List<Rect> footprints)
        {
            foreach (Rect obstacle in footprints)
            {
                float dx = point.x - Mathf.Clamp(point.x, obstacle.xMin, obstacle.xMax);
                float dz = point.z - Mathf.Clamp(point.z, obstacle.yMin, obstacle.yMax);
                if (dx * dx + dz * dz <= radius * radius) return true;
            }
            return false;
        }

        private void AddDogPlayObstacle(Vector3 center, float width, float depth)
        {
            dogPlayObstacles.Add(new Rect(center.x - width * 0.5f, center.z - depth * 0.5f, width, depth));
        }

        private void BuildApartment()
        {
            playerSpace = Rect.MinMaxRect(-4.3f, -5.74f, 4.74f, 4.12f);
            dogSpace = Rect.MinMaxRect(-2.87f, -5.25f, 3.73f, 3.12f);
            FoodPosition = At(-2.1f, 0f, 0.4f);
            WaterPosition = At(-2.55f, 0f, 0.4f);
            ToyPosition = At(2.5f, 0f, 1.25f);
            WastePosition = At(-1.75f, 0f, 2.5f);
            WalkDestination = At(2.6f, 0f, 2.6f);
            Lighting("DAA981", "F6C69A", "A7BED0", 1.16f);
            Color wood = ColorOf("9D6950");
            Color cream = ColorOf("E6CDB1");
            Color trim = ColorOf("42352C");

            Box("Oak slab", At(0, -0.105f, -0.65f), At(10.1f, 0.16f, 11.1f), wood, solid: true);
            for (int i = 0; i < 32; i++)
            {
                Color board = wood * (0.92f + (i * 17 % 13) * 0.013f);
                float z = -5.98f + i * 0.345f;
                float seam = -2.6f + (i * 11 % 19) * 0.272f;
                Box("Oak board", At((-5 + seam) * 0.5f, -0.035f, z), At(seam + 4.987f, 0.028f, 0.337f), board);
                Box("Oak board", At((5 + seam) * 0.5f, -0.035f, z), At(4.987f - seam, 0.028f, 0.337f), board * 0.98f);
            }
            Box("Left plaster wall", At(-5.05f, 1.7f, -0.65f), At(0.21f, 3.4f, 11.3f), cream, solid: true);
            Box("Right plaster wall", At(5.05f, 1.7f, -0.65f), At(0.21f, 3.4f, 11.3f), ColorOf("B99479"), solid: true);
            Box("Window wall base", At(0, 0.51f, 4.85f), At(10.1f, 1.02f, 0.27f), cream, solid: true);
            Box("Window wall header", At(0, 3.26f, 4.85f), At(10.1f, 0.3f, 0.27f), cream, solid: true);
            Box("Window wall left", At(-3.93f, 2.04f, 4.85f), At(2.24f, 2.05f, 0.27f), cream, solid: true);
            Box("Window wall right", At(4.12f, 2.04f, 4.85f), At(1.86f, 2.05f, 0.27f), cream, solid: true);
            Box("Baseboard", At(0, 0.08f, 4.7f), At(10.06f, 0.16f, 0.1f), ColorOf("A4785A"));
            Box("Baseboard", At(-4.92f, 0.08f, -0.65f), At(0.08f, 0.16f, 11.0f), ColorOf("A4785A"));
            Box("Window ledge", At(0.18f, 1.025f, 4.78f), At(6.25f, 0.12f, 0.52f), ColorOf("B1805B"));
            for (int i = 0; i < 5; i++)
                Box("Window mullion", At(-2.81f + 1.5f * i, 2.06f, 4.82f), At(0.067f, 2.13f, 0.14f), trim);
            Box("Window glazing bar", At(0.2f, 2.16f, 4.82f), At(6.06f, 0.045f, 0.13f), trim);

            Skyline(23f, -15f, 31, true);
            int pleats = 9;
            for (int i = 0; i < pleats; i++)
            {
                Color drape = ColorOf("B5795E") * (i % 2 == 0 ? 1.04f : 0.91f);
                Box("Linen curtain pleat", At(-2.94f + 0.069f * i, 1.62f, 4.66f - (i % 2) * 0.024f), At(0.11f, 2.87f, 0.13f), drape);
                Box("Linen curtain pleat", At(2.74f + 0.069f * i, 1.62f, 4.66f - (i % 2) * 0.024f), At(0.11f, 2.87f, 0.13f), drape);
            }
            Box("Curtain rod", At(0.2f, 3.04f, 4.59f), At(6.50f, 0.045f, 0.045f), trim);

            Rug(At(0.45f, -0.014f, 0.35f), At(5.25f, 0.03f, 4.44f), ColorOf("6C7F71"), ColorOf("B8AF8E"));
            Sofa(-3.86f, 1.95f);
            DogBed(At(-3.68f, 0f, -0.68f), ColorOf("BE9578"));
            Kitchenette();
            EntryDoor();
            Bookshelf(4.38f, 3.61f);
            PottedPlant(At(4.38f, 0f, 1.83f), 1.02f, ColorOf("B77759"));
            PottedPlant(At(-3.9f, 0f, 3.62f), 0.74f, ColorOf("879685"));
            PottedPlant(At(1.7f, 1.09f, 4.63f), 0.35f, ColorOf("976C58"));
            Pendant(At(2.33f, 2.72f, 2.03f));
            Picture(At(4.91f, 2.22f, 0.82f), 0.82f, ColorOf("D8A782"), true);
            Picture(At(4.91f, 2.16f, -0.28f), 0.68f, ColorOf("9C9F7D"), true);
            Picture(At(-3.83f, 1.98f, 4.69f), 0.60f, ColorOf("8D9A83"), false);
        }

        private void BuildPark()
        {
            playerSpace = Rect.MinMaxRect(-7.65f, -7.25f, 7.65f, 7.75f);
            dogSpace = Rect.MinMaxRect(-6.1f, -6.65f, 6.1f, 6.9f);
            FoodPosition = At(-2.68f, 0f, 0.14f);
            WaterPosition = At(-3.13f, 0f, 0.14f);
            ToyPosition = At(2.14f, 0f, 1.64f);
            WastePosition = At(-2.95f, 0f, 3.62f);
            WalkDestination = At(3.85f, 0f, 5.83f);
            Lighting("A8C9C0", "FFF0CD", "B5D8DD", 1.26f);
            Box("Park ground", At(0, -0.18f, 5.3f), At(45f, 0.285f, 43f), ColorOf("789370"), solid: true);
            GrassPatch(At(3.7f, -0.032f, -2.7f), At(7.8f, 0.04f, 8.2f), ColorOf("8BA27A"));
            GrassPatch(At(-4.3f, -0.027f, 1.2f), At(8.4f, 0.04f, 10f), ColorOf("708C63"));
            GrassPatch(At(3.1f, -0.021f, 5.3f), At(8.6f, 0.035f, 8.9f), ColorOf("879A71"));
            for (int i = 0; i < 16; i++)
            {
                float z = -6.8f + i * 1.08f;
                float x = Mathf.Sin(i * 0.24f) * 1.54f - 0.4f;
                Box("Slate walkway", At(x, -0.025f, z), At(2.19f, 0.045f, 1.07f),
                    ColorOf("BDAD90") * (0.95f + (i % 4) * 0.03f), At(0f, Mathf.Cos(i * 0.24f) * 13f, 0f));
            }
            Skyline(32f, -21f, 39, false);
            for (int i = 0; i < 7; i++)
            {
                Tree(At(-8.2f + i * 2.72f, 0f, 9.45f + (i % 2) * 1.65f), 0.97f + (i % 3) * 0.13f);
                Box("Fence post", At(-8.25f + i * 2.75f, 0.64f, 8.8f), At(0.14f, 1.28f, 0.14f), ColorOf("526F62"), solid: true);
            }
            for (int i = 0; i < 2; i++)
                Box("Fence rail", At(0f, 0.43f + i * 0.43f, 8.8f), At(16.6f, 0.075f, 0.10f), ColorOf("5D7868"), solid: true);
            Tree(At(-7.27f, 0f, 2.43f), 0.90f);
            Tree(At(7.48f, 0f, 3.62f), 1.13f);
            Tree(At(7.98f, 0f, -0.68f), 0.89f);
            Bench(At(5.85f, 0f, 7.41f), ColorOf("93694E"));
            Bench(At(-5.6f, 0f, 6.95f), ColorOf("B08660"));
            Lamp(At(3.76f, 0f, 7.16f));
            Lamp(At(-3.84f, 0f, 6.45f));
            DogBed(At(-5.82f, 0f, -0.9f), ColorOf("BCA485"));
            for (int i = 0; i < 6; i++)
            {
                Flowers(At(6.86f + 0.42f * Mathf.Sin(i), 0f, 0.6f + i * 1.13f));
                Flowers(At(-6.69f + 0.37f * Mathf.Cos(i), 0f, 0.66f + i * 1.27f));
            }
        }

        private void BuildTerrace()
        {
            playerSpace = Rect.MinMaxRect(-6.1f, -5.75f, 6.1f, 4.94f);
            dogSpace = Rect.MinMaxRect(-4.75f, -5.25f, 4.6f, 4.0f);
            FoodPosition = At(-2.9f, 0f, 0.31f);
            WaterPosition = At(-3.35f, 0f, 0.31f);
            ToyPosition = At(2.01f, 0f, 1.63f);
            WastePosition = At(-2.79f, 0f, 3.08f);
            WalkDestination = At(3.56f, 0f, 3.48f);
            Lighting("DFB5B3", "FBC697", "BEB3DE", 1.26f);
            Box("Roof slab", At(0, -0.22f, 0.45f), At(13.1f, 0.39f, 13.2f), ColorOf("776760"), solid: true);
            for (int i = 0; i < 27; i++)
                Box("Decking", At(0, -0.035f, -5.98f + i * 0.48f), At(12.97f, 0.04f, 0.468f),
                    ColorOf("A98269") * (0.95f + (i % 5) * 0.025f));

            Skyline(24f, -19f, 37, true);
            Skyline(47f, -24f, 39, false, true);
            Color concrete = ColorOf("CCC1AF");
            Color dark = ColorOf("625E62");
            Box("Terrace parapet", At(0, 0.32f, 6.32f), At(13.10f, 0.64f, 0.3f), concrete, solid: true);
            Box("Terrace parapet", At(-6.39f, 0.32f, 0.48f), At(0.3f, 0.64f, 11.75f), concrete, solid: true);
            Box("Terrace parapet", At(6.39f, 0.32f, 0.48f), At(0.3f, 0.64f, 11.75f), concrete, solid: true);
            Box("Terrace rail", At(0, 1.02f, 6.32f), At(13.10f, 0.07f, 0.08f), dark, solid: true);
            for (int i = 0; i < 12; i++)
                Box("Terrace railing post", At(-6.39f + i * 1.16f, 0.79f, 6.32f), At(0.052f, 0.48f, 0.052f), dark);
            for (int side = -1; side <= 1; side += 2)
            {
                Box("Side terrace rail", At(side * 6.39f, 1.02f, 0.48f), At(0.07f, 0.07f, 11.75f), dark, solid: true);
                for (int i = 0; i < 11; i++)
                    Box("Side railing post", At(side * 6.39f, 0.79f, -5.22f + i * 1.15f), At(0.052f, 0.48f, 0.052f), dark);
                for (int i = 0; i < 3; i++)
                    Planter(At(side * 5.79f, 0f, 0.72f + i * 1.54f), ColorOf(i % 2 == 0 ? "8B9A80" : "B17A66"));
            }
            Rug(At(0.86f, -0.016f, 0.75f), At(6.54f, 0.04f, 4.96f), ColorOf("838E77"), ColorOf("BAAF96"));
            Bench(At(3.75f, 0f, 4.91f), ColorOf("AF8065"));
            DogBed(At(-4.79f, 0f, 4.49f), ColorOf("A4A281"));
            PottedPlant(At(1.82f, 0f, 5.26f), 0.93f, ColorOf("B17A66"));
            PottedPlant(At(-2.23f, 0f, 5.14f), 1.02f, ColorOf("917C67"));
            StringLights(At(-5.8f, 2.75f, 3.78f), At(5.8f, 2.75f, 5.44f));
        }

        private void Lighting(string background, string sunColor, string bounce, float intensity)
        {
            sky = ColorOf(background);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = ColorOf(bounce) * 0.77f;
            RenderSettings.ambientEquatorColor = ColorOf("A69786") * 0.81f;
            RenderSettings.ambientGroundColor = ColorOf("655449");
            RenderSettings.fog = false;
            Light sun = MakeLight("Neighborhood sunlight", At(0, 4f, 4f), LightType.Directional,
                ColorOf(sunColor), intensity, 0f);
            sun.transform.rotation = Quaternion.Euler(45f, 213f, 0f);
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.62f;
            Light fill = MakeLight("Reflected light", At(0, 3f, -2f), LightType.Directional,
                ColorOf(bounce), 0.40f, 0f);
            fill.transform.rotation = Quaternion.Euler(38f, -25f, 0f);
        }

        private void BuildCareStation()
        {
            Vector3 bowlCenter = (FoodPosition + WaterPosition) * 0.5f;
            Box("Washable care mat", bowlCenter + At(0, -0.014f, 0), At(1.16f, 0.035f, 0.65f), ColorOf("D5AA79"));
            Bowl(FoodPosition, ColorOf("A65A43"), ColorOf("866449"), "Feeding bowl");
            Bowl(WaterPosition, ColorOf("4C7D85"), ColorOf("86BAC1"), "Water bowl");
            AddDogPlayObstacle(FetchPosition + At(0.10f, 0f, 0f), 0.55f, 0.55f);
            Vector3 leash = WalkDestination + At(0.74f, 0.62f, 0.42f);
            Box("Leash peg", leash, At(0.075f, 1.23f, 0.075f), ColorOf("9D765A"));
            AddDogPlayObstacle(leash, 0.12f, 0.12f);
            Vector3 loop = WalkDestination + At(0.74f, 0.92f, 0.36f);
            Color teal = ColorOf("5F9D98");
            for (int i = 0; i < 8; i++)
            {
                float start = i / 8f * Mathf.PI * 2f;
                float end = (i + 1) / 8f * Mathf.PI * 2f;
                Vector3 from = loop + At(Mathf.Cos(start) * 0.09f, Mathf.Sin(start) * 0.16f, 0);
                Vector3 to = loop + At(Mathf.Cos(end) * 0.09f, Mathf.Sin(end) * 0.16f, 0);
                Vector3 span = to - from;
                GameObject tie = Box("Looped turquoise leash", (from + to) * 0.5f, At(.025f, .021f, span.magnitude + .013f), teal);
                tie.transform.rotation = Quaternion.LookRotation(span, Vector3.forward);
            }
            Box("Leash clip", loop + At(0.05f, -0.20f, 0.0f), At(0.027f, 0.09f, 0.039f), ColorOf("C9A56B"));
        }

        private void Bowl(Vector3 center, Color color, Color interior, string name)
        {
            AddDogPlayObstacle(center, 0.39f, 0.39f);
            Cylinder(name + " body", center + At(0, 0.052f, 0), At(0.33f, 0.047f, 0.33f), color);
            Cylinder(name + " rim", center + At(0, 0.106f, 0), At(0.39f, 0.018f, 0.39f), color * 1.10f);
            Cylinder(name + " fill", center + At(0, 0.12f, 0), At(0.312f, 0.007f, 0.312f), interior);
        }

        private void Rug(Vector3 center, Vector3 size, Color color, Color border)
        {
            Box("Woven rug", center, size, color);
            for (int side = -1; side <= 1; side += 2)
            {
                Box("Rug selvedge", center + At(side * (size.x * 0.5f - 0.07f), size.y * 0.51f, 0),
                    At(0.032f, 0.004f, size.z * 0.97f), border);
                for (int i = 0; i < 3; i++)
                    Box("Rug edge stripe", center + At(0, size.y * 0.51f, side * (size.z * 0.5f - 0.082f - i * 0.07f)),
                        At(size.x * 0.96f, 0.004f, i == 1 ? 0.019f : 0.036f), border * (i == 1 ? 0.85f : 1.0f));
            }
        }

        private void Sofa(float x, float z)
        {
            Color fabric = ColorOf("868F7B");
            Color frame = ColorOf("596956");
            Box("Sofa main frame", At(x, 0.33f, z), At(1.36f, 0.42f, 2.61f), frame, solid: true, block: true);
            Box("Sofa backrest", At(x - 0.52f, 0.78f, z), At(0.34f, 0.92f, 2.6f), fabric, At(0, 0, 6), solid: true);
            for (int i = -1; i <= 1; i++)
            {
                Box("Sofa seat cushion", At(x + 0.15f, 0.58f, z + i * 0.76f), At(1.04f, 0.22f, 0.735f), fabric * 1.06f);
                Box("Sofa back cushion", At(x - 0.31f, 0.93f, z + i * 0.76f), At(0.24f, 0.55f, 0.724f), fabric, At(0, 0, 7));
            }
            for (int side = -1; side <= 1; side += 2)
            {
                Box("Sofa armrest", At(x + 0.07f, 0.69f, z + side * 1.19f), At(1.21f, 0.32f, 0.24f), frame);
                Box("Loose sofa cushion", At(x - 0.03f, 0.94f, z + side * 0.78f), At(0.22f, 0.47f, 0.47f),
                    ColorOf(side < 0 ? "C67957" : "C5AB78"), At(side * 12f, 6f, 18f));
                for (int foot = -1; foot <= 1; foot += 2)
                    Box("Sofa oak foot", At(x + foot * 0.51f, 0.12f, z + side * 1.12f), At(0.12f, 0.23f, 0.12f), ColorOf("795942"));
            }
        }

        private void DogBed(Vector3 position, Color color)
        {
            AddDogPlayObstacle(position, 1.50f, 0.98f);
            Cylinder("Dog bed base", position + At(0, 0.08f, 0), At(1.50f, 0.075f, 0.98f), color * 0.83f);
            Cylinder("Dog bed cushion", position + At(0, 0.155f, 0), At(1.25f, 0.031f, 0.80f), color);
            Shape(PrimitiveType.Capsule, "Dog bed bolsters", position + At(0, 0.175f, 0.33f),
                At(0.18f, 0.62f, 0.18f), color * 1.06f, At(0, 0, 90f));
        }

        private void Bookshelf(float x, float z)
        {
            Color wood = ColorOf("89604A");
            Box("Low credenza", At(x, 0.51f, z), At(1.03f, 1.02f, 1.64f), wood, solid: true, block: true);
            Box("Credenza cap", At(x, 1.046f, z), At(1.12f, 0.07f, 1.72f), wood * 1.18f);
            for (int i = -1; i <= 1; i++)
            {
                Box("Credenza door", At(x - 0.525f, 0.53f, z + i * 0.51f), At(0.041f, 0.83f, 0.47f), wood * 0.90f);
                Box("Brass pull", At(x - 0.555f, 0.81f, z + i * 0.51f), At(0.038f, 0.04f, 0.12f), ColorOf("C59B67"));
            }
            for (int i = 0; i < 6; i++)
                Box("Book", At(x + 0.09f, 1.19f + (i % 3) * 0.02f, z - 0.48f + i * 0.086f),
                    At(0.28f, 0.24f + (i % 3) * 0.04f, 0.073f), ColorOf(i % 2 == 0 ? "ACA57C" : "8C9E8B"));
            PottedPlant(At(x, 1.09f, z + 0.47f), 0.34f, ColorOf("B77C5F"));
        }

        private void Kitchenette()
        {
            Color cabinet = ColorOf("A2A18A");
            Color oak = ColorOf("AA7251");
            Color tile = ColorOf("D0B09B");
            Box("Kitchen base", At(-4.59f, .49f, -2.48f), At(.76f, .98f, 2.36f), cabinet, solid: true, block: true);
            Box("Kitchen worktop", At(-4.57f, 1.00f, -2.48f), At(.82f, .085f, 2.44f), oak);
            for (int i = 0; i < 4; i++)
            {
                float z = -3.34f + i * .57f;
                Box("Cabinet panel", At(-4.19f, .50f, z), At(.045f, .80f, .49f), cabinet * 1.06f);
                Box("Cabinet brass pull", At(-4.15f, .79f, z), At(.045f, .035f, .15f), ColorOf("B18A61"));
                Box("Upper cabinet door", At(-4.47f, 2.4f, z), At(.40f, .64f, .54f), cabinet * .88f);
                Box("Upper cabinet brass pull", At(-4.24f, 2.19f, z), At(.045f, .04f, .15f), ColorOf("B18A61"));
                for (int row = 0; row < 3; row++)
                    Box("Backsplash terracotta tile", At(-4.938f, 1.20f + .286f * row, z),
                        At(.027f, .272f, .552f), tile * (0.93f + ((i + row) % 5) * .026f));
            }
            Cylinder("Dog-food storage jar", At(-4.64f, 1.17f, -1.52f), At(.27f, .135f, .27f), ColorOf("D4AF83"));
            Cylinder("Storage jar oak lid", At(-4.64f, 1.32f, -1.52f), At(.29f, .022f, .29f), oak);
            PottedPlant(At(-4.56f, 1.05f, -2.04f), .28f, ColorOf("8C6C5B"));
        }

        private void EntryDoor()
        {
            Color frame = ColorOf("8F745F");
            Color door = ColorOf("C6AD97");
            Box("Entry lintel and frame", At(-4.01f, 1.27f, 4.69f), At(1.23f, 2.54f, .104f), frame);
            Box("Entry door", At(-4.01f, 1.23f, 4.62f), At(1.083f, 2.45f, .07f), door);
            Box("Upper recessed panel", At(-4.01f, 1.63f, 4.58f), At(.86f, 1.05f, .028f), door * .93f);
            Box("Lower recessed panel", At(-4.01f, .57f, 4.58f), At(.86f, .69f, .028f), door * .92f);
            Box("Door handle", At(-3.60f, 1.09f, 4.54f), At(.14f, .052f, .076f), ColorOf("AF875A"));
            Box("Welcome mat", At(-4.11f, -.011f, 4.26f), At(1.11f, .032f, .53f), ColorOf("A18160"));
        }

        private void Picture(Vector3 position, float scale, Color accent, bool sideWall)
        {
            Vector3 rotation = sideWall ? At(0, 90, 0) : Vector3.zero;
            Vector3 normal = sideWall ? Vector3.left : Vector3.back;
            Box("Framed print", position, At(scale, scale * 1.25f, 0.045f), ColorOf("6E5548"), rotation);
            Box("Mat board", position + normal * 0.024f, At(scale * 0.89f, scale * 1.11f, 0.01f), ColorOf("E7D7BD"), rotation);
            Box("Print color field", position + normal * 0.032f, At(scale * 0.68f, scale * 0.87f, 0.006f), accent, rotation);
            Shape(PrimitiveType.Sphere, "Print sun", position + normal * 0.039f + At(0, 0.12f, 0),
                At(scale * 0.31f, scale * 0.31f, 0.008f), ColorOf("E9BD82"), rotation);
        }

        private void Pendant(Vector3 center)
        {
            Cylinder("Pendant cable", center + At(0, 0.30f, 0), At(0.019f, 0.27f, 0.019f), ColorOf("6F5643"));
            Shape(PrimitiveType.Sphere, "Rattan pendant shade", center, At(0.53f, 0.28f, 0.53f), ColorOf("D6A26B"));
            Cylinder("Pendant diffuser", center + At(0, -0.11f, 0), At(0.40f, 0.021f, 0.40f), ColorOf("FFDDAD"), emission: true);
            MakeLight("Warm pendant", center + At(0, -0.25f, 0), LightType.Point, ColorOf("FFC78C"), 0.87f, 7.1f);
        }

        private void GrassPatch(Vector3 center, Vector3 size, Color color)
        {
            Shape(PrimitiveType.Sphere, "Grass variation", center, size, color);
        }

        private void Tree(Vector3 position, float scale)
        {
            AddDogPlayObstacle(position, 0.24f * scale, 0.24f * scale);
            Color bark = ColorOf("74624B");
            Cylinder("Tree trunk", position + At(0, 1.05f, 0) * scale, At(0.20f, 1.05f, 0.20f) * scale, bark, solid: true);
            Cylinder("Tree branch", position + At(0.30f, 1.93f, 0) * scale, At(0.10f, 0.65f, 0.10f) * scale,
                bark, At(0, 0, -30f));
            for (int i = 0; i < 6; i++)
            {
                float angle = i * 2.4f;
                float width = 1.31f + i % 3 * 0.21f;
                Vector3 crown = position + At(Mathf.Cos(angle) * 0.69f, 2.6f + (i % 3) * 0.46f, Mathf.Sin(angle) * 0.59f) * scale;
                Shape(PrimitiveType.Sphere, "Canopy", crown, At(width, width * 0.87f, width) * scale,
                    ColorOf(i % 3 == 0 ? "667F57" : i % 3 == 1 ? "7E9864" : "91A477"));
            }
        }

        private void Flowers(Vector3 position)
        {
            for (int i = 0; i < 4; i++)
            {
                Vector3 stem = position + At(0.12f * i, 0.08f + (i % 3) * 0.045f, Mathf.Sin(i) * 0.14f);
                Cylinder("Flower stem", stem, At(0.025f, 0.10f, 0.025f), ColorOf("687F52"));
                Shape(PrimitiveType.Sphere, "Flower head", stem + At(0, 0.095f, 0),
                    At(0.107f, 0.061f, 0.11f), ColorOf(i % 2 == 0 ? "D49F70" : "E1C497"));
            }
        }

        private void Bench(Vector3 center, Color wood)
        {
            Color metal = ColorOf("4D5D53");
            for (int side = -1; side <= 1; side += 2)
            {
                Box("Iron bench support", center + At(side * 0.82f, 0.37f, 0.05f), At(0.115f, 0.74f, 0.48f), metal, solid: true);
            }
            Box("Bench seat", center + At(0, 0.47f, 0), At(1.97f, 0.10f, 0.61f), wood, solid: true, block: true);
            for (int i = 0; i < 4; i++)
                Box("Bench back slat", center + At(0, 0.67f + i * 0.10f, 0.28f + i * 0.012f),
                    At(1.93f, 0.079f, 0.078f), wood * 1.09f);
        }

        private void Lamp(Vector3 position)
        {
            AddDogPlayObstacle(position, 0.10f, 0.10f);
            Color metal = ColorOf("506154");
            Cylinder("Lamp pole", position + At(0, 1.17f, 0), At(0.075f, 1.17f, 0.075f), metal, solid: true);
            Cylinder("Lamp shade", position + At(0, 2.30f, 0), At(0.34f, 0.055f, 0.34f), metal);
            Cylinder("Lamp diffuser", position + At(0, 2.23f, 0), At(0.23f, 0.061f, 0.23f), ColorOf("E6C589"), emission: true);
        }

        private void PottedPlant(Vector3 position, float scale, Color pot)
        {
            AddDogPlayObstacle(position, 0.48f * scale, 0.48f * scale);
            Cylinder("Terracotta pot", position + At(0, 0.21f, 0) * scale, At(0.42f, 0.21f, 0.42f) * scale, pot);
            Cylinder("Pot rim", position + At(0, 0.40f, 0) * scale, At(0.48f, 0.06f, 0.48f) * scale, pot * 1.08f);
            for (int i = 0; i < 6; i++)
            {
                float angle = i * 2.4f;
                float radius = Mathf.Sin(angle + 0.2f);
                Shape(PrimitiveType.Sphere, "Plant leaf", position + At(Mathf.Cos(angle) * 0.18f, 0.62f + i * 0.067f, radius * 0.14f) * scale,
                    At(0.25f, 0.52f, 0.105f) * scale, ColorOf(i % 2 == 0 ? "627C5C" : "809172"),
                    At(35f * radius, i * 38f, Mathf.Cos(angle) * 47f));
            }
        }

        private void Planter(Vector3 position, Color color)
        {
            Box("Planter box", position + At(0, 0.28f, 0), At(0.78f, 0.56f, 1.17f), color, solid: true, block: true);
            Box("Planter soil", position + At(0, 0.564f, 0), At(0.70f, 0.025f, 1.09f), ColorOf("605347"));
            for (int i = 0; i < 4; i++)
                Shape(PrimitiveType.Sphere, "Terrace shrub", position + At(Mathf.Sin(i) * 0.12f, 0.69f, -0.42f + i * 0.28f),
                    At(0.46f, 0.36f, 0.49f), ColorOf(i % 2 == 0 ? "849170" : "677D68"));
        }

        private void StringLights(Vector3 start, Vector3 end)
        {
            Color cable = ColorOf("594A46");
            for (int i = 0; i <= 1; i++)
            {
                Vector3 point = i == 0 ? start : end;
                AddDogPlayObstacle(point, 0.11f, 0.11f);
                Cylinder("Bistro light post", At(point.x, point.y * 0.5f, point.z),
                    At(0.085f, point.y * 0.5f, 0.085f), cable, solid: true);
            }
            Vector3 previous = start;
            for (int i = 1; i <= 16; i++)
            {
                float t = i / 16f;
                Vector3 point = Vector3.Lerp(start, end, t) + Vector3.down * (0.43f * Mathf.Sin(t * Mathf.PI));
                Vector3 span = point - previous;
                GameObject line = Box("Bistro light cord", (point + previous) * 0.5f, At(0.018f, 0.018f, span.magnitude + 0.03f), cable);
                line.transform.rotation = Quaternion.LookRotation(span);
                Shape(PrimitiveType.Sphere, "Bistro light bulb", point + At(0, -0.062f, 0), At(0.092f, 0.131f, 0.092f),
                    ColorOf("FFC88F"), emission: true);
                previous = point;
            }
        }

        private void Skyline(float z, float xStart, int spread, bool dusk, bool distant = false)
        {
            for (int i = 0; i < 19; i++)
            {
                float slot = i * spread / 18f;
                float x = xStart + slot;
                float height = 4.2f + i * 13 % 11 * 0.57f;
                float width = 1.32f + i * 7 % 4 * 0.38f;
                float depth = z + i % 3 * 2.7f;
                float baseline = distant ? -7.8f : -3.1f;
                Color wall = ColorOf(dusk ? "797886" : "8B9BA0") * (0.83f + (i % 4) * 0.071f);
                Box("Skyline building", At(x, baseline + height * 0.5f, depth), At(width, height, 2.60f), wall);
                Box("Skyline cornice", At(x, baseline + height + 0.08f, depth), At(width + 0.12f, 0.16f, 2.66f), wall * 1.11f);
                if (distant || i % 3 == 0) continue;
                for (int floor = 0; floor < Mathf.Min(7, Mathf.FloorToInt(height / 0.7f)); floor++)
                {
                    for (int window = 0; window < 3; window++)
                    {
                        if ((i + floor * 7 + window * 3) % 5 > 2) continue;
                        Box("City window", At(x + (window - 1) * width * 0.27f, baseline + 0.55f + floor * 0.67f, depth - 1.312f),
                            At(width * 0.157f, 0.26f, 0.024f), ColorOf(dusk ? "DBB88A" : "B6C6BF"), emission: dusk);
                    }
                }
            }
        }

        private Light MakeLight(string name, Vector3 position, LightType type, Color tint, float intensity, float range)
        {
            var light = new GameObject(name).AddComponent<Light>();
            light.transform.SetParent(root, true);
            light.transform.position = position;
            light.type = type;
            light.color = tint;
            light.intensity = intensity;
            light.range = range;
            return light;
        }

        private GameObject Box(string name, Vector3 position, Vector3 scale, Color color,
            Vector3 rotation = default, bool solid = false, bool block = false, bool emission = false)
        {
            if (block) obstacles.Add(new Rect(position.x - scale.x * 0.5f, position.z - scale.z * 0.5f, scale.x, scale.z));
            return Shape(PrimitiveType.Cube, name, position, scale, color, rotation, solid, emission);
        }

        private void Cylinder(string name, Vector3 position, Vector3 scale, Color color,
            Vector3 rotation = default, bool solid = false, bool emission = false)
        {
            Shape(PrimitiveType.Cylinder, name, position, scale, color, rotation, solid, emission);
        }

        private GameObject Shape(PrimitiveType type, string name, Vector3 position, Vector3 scale, Color color,
            Vector3 rotation = default, bool solid = false, bool emission = false)
        {
            GameObject piece = GameObject.CreatePrimitive(type);
            piece.name = name;
            piece.transform.SetParent(root, true);
            piece.transform.SetPositionAndRotation(position, Quaternion.Euler(rotation));
            piece.transform.localScale = scale;
            Collider collider = piece.GetComponent<Collider>();
            if (!solid && collider != null) collider.enabled = false;
            var renderer = piece.GetComponent<Renderer>();
            renderer.sharedMaterial = MaterialFor(color, emission);
            return piece;
        }

        private Material MaterialFor(Color color, bool emission)
        {
            string key = ColorUtility.ToHtmlStringRGB(color) + (emission ? "-lit" : string.Empty);
            if (materials.TryGetValue(key, out Material existing)) return existing;
            var material = new Material(Shader.Find("Standard"))
            {
                name = "Neighborhood " + key,
                color = color
            };
            material.SetFloat("_Glossiness", 0.07f);
            if (emission)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * 0.50f);
            }
            materials.Add(key, material);
            return material;
        }

        private static Color ColorOf(string rgb)
        {
            ColorUtility.TryParseHtmlString("#" + rgb, out Color color);
            return color;
        }

        private static Vector3 At(float x, float y, float z) => new Vector3(x, y, z);

        public void Dispose()
        {
            if (root != null)
            {
                root.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(root.gameObject);
            }
            foreach (Material material in materials.Values)
                UnityEngine.Object.Destroy(material);
            materials.Clear();
            obstacles.Clear();
            dogPlayObstacles.Clear();
            root = null;
        }
    }
}
