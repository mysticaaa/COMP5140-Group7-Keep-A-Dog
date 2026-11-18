using System;
using UnityEngine;

namespace Wjy.DogTraining
{
    [DisallowMultipleComponent]
    public sealed class DogApartmentLayout : MonoBehaviour
    {
        [Serializable]
        internal struct Door
        {
            public Transform pivot;
            public float width;
            public float openAngle;
            public string label;
        }

        [SerializeField] private Rect playerBounds, dogBounds;
        [SerializeField] private Rect gardenBounds;
        [SerializeField] private BoxCollider[] gardenObstacles;
        [SerializeField] private DogApartmentLift lift;
        [SerializeField] private Transform playerStart, dogStart, fetchPoint, toyPoint, wastePoint, walkPoint;
        [SerializeField] private Transform foodPoint, waterPoint;
        [SerializeField] private BoxCollider[] walls;
        [SerializeField] private Transform[] furnitureGroups, dogPlayMarkers;
        [SerializeField] private Door[] doors;
        [SerializeField] private Transform showerControl;
        [SerializeField] private ParticleSystem showerStreams, showerSplashes;
        [SerializeField] private Color sky, ambientSky, ambientEquator, ambientGround;

        internal void Apply(DogNeighborhood world) =>
            world.UseApartment(this, playerBounds, dogBounds, doors, walls, furnitureGroups, dogPlayMarkers,
                showerControl, showerStreams, showerSplashes, sky);

        internal Vector3 PlayerStart => playerStart.position;
        internal Vector3 DogStart => dogStart.position;
        internal Vector3 FetchPoint => fetchPoint.position;
        internal Vector3 ToyPoint => toyPoint.position;
        internal Vector3 WastePoint => wastePoint.position;
        internal Vector3 WalkPoint => walkPoint.position;
        internal Vector3 FoodPoint => foodPoint.position;
        internal Vector3 WaterPoint => waterPoint.position;
        internal Rect GardenBounds => gardenBounds;
        internal BoxCollider[] GardenObstacles => gardenObstacles;
        internal DogApartmentLift Lift => lift;

        internal void ApplyLighting()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = ambientSky;
            RenderSettings.ambientEquatorColor = ambientEquator;
            RenderSettings.ambientGroundColor = ambientGround;
            RenderSettings.fog = false;
        }
    }
}
