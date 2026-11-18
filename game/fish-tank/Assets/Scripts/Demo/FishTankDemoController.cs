using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CyberFishTank.Demo
{
    /// <summary>First playable slice: one glass tank, three species, and simple schooling movement.</summary>
    public sealed class FishTankDemoController : MonoBehaviour
    {
        private static FishTankDemoController activeDemo;
        private const float TankWidth = 12f;
        private const float TankHeight = 6f;
        private const float TankDepth = 5f;
        private const float SubstrateBottomY = -3.25f;

        private readonly List<FishBehaviorAgent> agents = new List<FishBehaviorAgent>();
        private readonly List<FishSchoolAnchor> schools = new List<FishSchoolAnchor>();
        private readonly List<PlantSway> plantSways = new List<PlantSway>();
        private readonly List<Bounds> rockObstacleBounds = new List<Bounds>();
        private readonly List<MeshCollider> hardscapeColliders = new List<MeshCollider>();
        private readonly List<FishFoodPellet> foodPellets = new List<FishFoodPellet>();
        private readonly List<Renderer> inspectionHiddenRenderers = new List<Renderer>();
        private MeshCollider anubiasAnchorCollider;
        private Mesh waterSurfaceMesh;
        private Renderer waterSurfaceRenderer;
        private Vector3[] waterSurfaceRestVertices;
        private Vector3[] waterSurfaceVertices;
        private float waterSurfaceClock;
        private float timeOfDayHours = 9f;
        private float waterTemperature = 24f;
        private float waterPh = 7.1f;
        private float ammonia = 0.02f;
        private float nitrate = 5f;
        private float foodWasteLoad;
        private Light aquariumKeyLight;
        private Light aquariumFillLight;
        private Camera demoCamera;
        private FishBehaviorAgent selectedFish;
        private Vector3 selectedCameraOffset;
        private Vector3 cameraTargetBeforeSelection;
        private float cameraDistanceBeforeSelection;
        private bool selectionCameraSaved;
        private Vector3 cameraTarget = Vector3.zero;
        private float cameraYaw;
        private float cameraPitch = 12f;
        private float cameraYawBeforeSelection;
        private float cameraPitchBeforeSelection;
        private float cameraDistance = 16f;
        private static readonly Species[] SpeciesData =
        {
            new Species("孔雀鱼", "Guppy_Hero", new Color(0.95f, 0.43f, 0.36f), 4, 6, 12),
            new Species("霓虹灯鱼", "NeonTetra_Refined", new Color(0.2f, 0.78f, 0.88f), 6, 4, 8),
            new Species("熊猫鼠", "PandaCory_Refined", new Color(0.84f, 0.78f, 0.59f), 6, 2, 5)
        };

        private void Start()
        {
            // The vertical-slice scene can stay loaded while the play scene reloads.
            // Keep one generated tank so transparent water isn't composited multiple times.
            if (activeDemo != null && activeDemo != this)
            {
                Destroy(gameObject);
                return;
            }
            activeDemo = this;
            BuildTank();
            TuneAquariumLighting();
            BuildDecor();
            BuildSchools();
            PositionCamera();
        }

        private void Update()
        {
            for (var i = foodPellets.Count - 1; i >= 0; i--)
                if (foodPellets[i] == null) foodPellets.RemoveAt(i);
            for (var i = 0; i < schools.Count; i++)
                schools[i].Tick(Time.deltaTime);
            for (var i = 0; i < agents.Count; i++)
                agents[i].Tick(Time.deltaTime, agents);
            FollowSelectedFish(Time.deltaTime);
            for (var i = 0; i < plantSways.Count; i++)
                plantSways[i].Tick(Time.time);
            UpdateWaterAndDaylight(Time.deltaTime);
            AnimateWaterSurface(Time.deltaTime);
            HandleCameraInput();
        }

        private void BuildTank()
        {
            // Water is clear; avoid a solid cuboid whose large faces read as opaque glass.
            // The subtly tinted camera background gives a light underwater color instead.
            var glass = new Color(0.78f, 0.94f, 0.97f, 0.028f);
            var glassHeight = TankHeight * 0.5f - SubstrateBottomY;
            var glassCenterY = (TankHeight * 0.5f + SubstrateBottomY) * 0.5f;
            CreateBlock("玻璃-前", new Vector3(0f, glassCenterY, -TankDepth * 0.5f), new Vector3(TankWidth, glassHeight, 0.04f), glass, false);
            CreateBlock("玻璃-后", new Vector3(0f, glassCenterY, TankDepth * 0.5f), new Vector3(TankWidth, glassHeight, 0.04f), glass, false);
            CreateBlock("玻璃-左", new Vector3(-TankWidth * 0.5f, glassCenterY, 0f), new Vector3(0.04f, glassHeight, TankDepth), glass, false);
            CreateBlock("玻璃-右", new Vector3(TankWidth * 0.5f, glassCenterY, 0f), new Vector3(0.04f, glassHeight, TankDepth), glass, false);
            var frame = new Color(0.17f, 0.27f, 0.28f, 1f);
            CreateBlock("上框-前", new Vector3(0f, TankHeight * 0.5f, -TankDepth * 0.5f), new Vector3(TankWidth + 0.08f, 0.075f, 0.08f), frame, true);
            CreateBlock("上框-后", new Vector3(0f, TankHeight * 0.5f, TankDepth * 0.5f), new Vector3(TankWidth + 0.08f, 0.075f, 0.08f), frame, true);
            CreateBlock("上框-左", new Vector3(-TankWidth * 0.5f, TankHeight * 0.5f, 0f), new Vector3(0.08f, 0.075f, TankDepth), frame, true);
            CreateBlock("上框-右", new Vector3(TankWidth * 0.5f, TankHeight * 0.5f, 0f), new Vector3(0.08f, 0.075f, TankDepth), frame, true);
            CreateBlock("白色鱼缸底座", new Vector3(0f, SubstrateBottomY - 0.1f, 0f), new Vector3(TankWidth + 0.55f, 0.2f, TankDepth + 0.55f), new Color(0.92f, 0.93f, 0.91f), true);
            CreateSubstrate();
            CreateWaterSurface();
        }

        private void TuneAquariumLighting()
        {
            // Place the key directly above the tank so fish and plants read under
            // a consistent aquarium light instead of a side-lit studio angle.
            var lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
            var hasOverheadKey = false;
            for (var i = 0; i < lights.Length; i++)
            {
                if (lights[i].type != LightType.Directional) continue;
                if (!hasOverheadKey)
                {
                    aquariumKeyLight = lights[i];
                    lights[i].transform.position = transform.TransformPoint(new Vector3(0f, 5.4f, 0f));
                    lights[i].transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                    hasOverheadKey = true;
                }
                lights[i].shadows = LightShadows.Soft;
                lights[i].shadowStrength = 0.24f;
                lights[i].shadowBias = 0.045f;
                lights[i].shadowNormalBias = 0.35f;
            }

            if (!hasOverheadKey)
            {
                var keyObject = new GameObject("Aquarium overhead key light");
                keyObject.transform.SetParent(transform, false);
                keyObject.transform.localPosition = new Vector3(0f, 5.4f, 0f);
                keyObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                var key = keyObject.AddComponent<Light>();
                aquariumKeyLight = key;
                key.type = LightType.Directional;
                key.intensity = 1.35f;
                key.shadows = LightShadows.Soft;
                key.shadowStrength = 0.24f;
            }

            var fillObject = new GameObject("Aquarium soft blue fill");
            fillObject.transform.SetParent(transform, false);
            fillObject.transform.localPosition = new Vector3(0f, 4.7f, 0f);
            var fill = fillObject.AddComponent<Light>();
            aquariumFillLight = fill;
            fill.type = LightType.Point;
            fill.color = new Color(0.64f, 0.82f, 0.88f);
            fill.intensity = 0.48f;
            fill.range = 13f;
            fill.shadows = LightShadows.None;
        }

        internal static float SubstrateHeight(float x, float z)
        {
            var rearSlope = Mathf.InverseLerp(-2.7f, 2.7f, z) * 0.18f;
            var moundA = 0.34f * Mathf.Exp(-((x + 2.1f) * (x + 2.1f) / 4.2f + (z - 0.55f) * (z - 0.55f) / 1.5f));
            var moundB = 0.24f * Mathf.Exp(-((x - 2.5f) * (x - 2.5f) / 2.8f + (z + 0.65f) * (z + 0.65f) / 1.15f));
            var ripple = 0.035f * Mathf.Sin(x * 1.45f + z * 0.75f) * Mathf.Cos(z * 1.8f - x * 0.3f)
                + 0.012f * Mathf.Sin(x * 3.1f - z * 2.4f);
            return -TankHeight * 0.5f + rearSlope + moundA + moundB + ripple;
        }

        private void CreateSubstrate()
        {
            const int columns = 48;
            const int rows = 24;
            // Keep the sand mesh just inside the glass footprint.
            var width = TankWidth - 0.08f;
            var depth = TankDepth - 0.08f;
            var stride = columns + 1;
            var topCount = (columns + 1) * (rows + 1);
            var vertices = new List<Vector3>(topCount + (columns + rows) * 4 + 4);
            var triangles = new List<int>(columns * rows * 6 + (columns + rows) * 24);
            for (var row = 0; row <= rows; row++)
            {
                var z = -depth * 0.5f + depth * row / rows;
                for (var column = 0; column <= columns; column++)
                {
                    var x = -width * 0.5f + width * column / columns;
                    vertices.Add(new Vector3(x, SubstrateHeight(x, z), z));
                }
            }
            for (var row = 0; row < rows; row++)
            {
                for (var column = 0; column < columns; column++)
                {
                    var a = row * stride + column;
                    var b = a + 1;
                    var c = a + stride;
                    var d = c + 1;
                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                    triangles.Add(b); triangles.Add(c); triangles.Add(d);
                }
            }

            var bottom = SubstrateBottomY;
            void AddSide(List<int> edge)
            {
                var first = vertices.Count;
                for (var i = 0; i < edge.Count; i++)
                {
                    var top = vertices[edge[i]];
                    vertices.Add(new Vector3(top.x, bottom, top.z));
                }
                for (var i = 0; i < edge.Count - 1; i++)
                {
                    var a = edge[i]; var b = edge[i + 1];
                    var c = first + i; var d = first + i + 1;
                    triangles.Add(a); triangles.Add(b); triangles.Add(c);
                    triangles.Add(b); triangles.Add(d); triangles.Add(c);
                }
            }
            var front = new List<int>(); var back = new List<int>();
            var left = new List<int>(); var right = new List<int>();
            for (var c = 0; c <= columns; c++)
            {
                front.Add(c); back.Add(rows * stride + (columns - c));
            }
            for (var r = 0; r <= rows; r++)
            {
                left.Add(r * stride + columns); right.Add((rows - r) * stride);
            }
            AddSide(front); AddSide(back); AddSide(left); AddSide(right);

            var mesh = new Mesh { name = "Uneven sandy substrate" };
            mesh.vertices = vertices.ToArray();
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateNormals();
            var ground = new GameObject("沙床（缓坡与浅洼）");
            ground.transform.SetParent(transform, false);
            ground.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = ground.AddComponent<MeshRenderer>();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            renderer.material = new Material(shader);
            SetColor(renderer, new Color(0.74f, 0.68f, 0.52f));
            if (renderer.material.HasProperty("_Smoothness")) renderer.material.SetFloat("_Smoothness", 0.12f);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        private void CreateWaterSurface()
        {
            const int columns = 28;
            const int rows = 14;
            var vertices = new Vector3[(columns + 1) * (rows + 1)];
            var triangles = new int[columns * rows * 6];
            for (var row = 0; row <= rows; row++)
            {
                for (var column = 0; column <= columns; column++)
                {
                    var x = -TankWidth * 0.5f + TankWidth * column / columns;
                    var z = -TankDepth * 0.5f + TankDepth * row / rows;
                    vertices[row * (columns + 1) + column] = new Vector3(x, TankHeight * 0.5f - 0.12f, z);
                }
            }

            var triangle = 0;
            for (var row = 0; row < rows; row++)
            {
                for (var column = 0; column < columns; column++)
                {
                    var a = row * (columns + 1) + column;
                    var b = a + 1;
                    var c = a + columns + 1;
                    var d = c + 1;
                    triangles[triangle++] = a;
                    triangles[triangle++] = c;
                    triangles[triangle++] = b;
                    triangles[triangle++] = b;
                    triangles[triangle++] = c;
                    triangles[triangle++] = d;
                }
            }

            waterSurfaceMesh = new Mesh { name = "Gently rippling water surface" };
            waterSurfaceMesh.MarkDynamic();
            waterSurfaceMesh.vertices = vertices;
            waterSurfaceMesh.triangles = triangles;
            waterSurfaceMesh.RecalculateNormals();
            waterSurfaceRestVertices = waterSurfaceMesh.vertices;
            waterSurfaceVertices = new Vector3[waterSurfaceRestVertices.Length];

            var surface = new GameObject("水面（轻微波纹）");
            surface.transform.SetParent(transform, false);
            surface.AddComponent<MeshFilter>().sharedMesh = waterSurfaceMesh;
            waterSurfaceRenderer = surface.AddComponent<MeshRenderer>();
            var renderer = waterSurfaceRenderer;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            renderer.material = new Material(shader);
            // Keep the water transparent from the side, but make its plane legible from above.
            SetColor(renderer, new Color(0.48f, 0.82f, 0.88f, 0.13f), false);
            if (renderer.material.HasProperty("_Smoothness")) renderer.material.SetFloat("_Smoothness", 0.86f);
            if (renderer.material.HasProperty("_Metallic")) renderer.material.SetFloat("_Metallic", 0.035f);
            if (renderer.material.HasProperty("_EmissionColor"))
            {
                renderer.material.EnableKeyword("_EMISSION");
                renderer.material.SetColor("_EmissionColor", new Color(0.006f, 0.022f, 0.028f, 1f));
            }
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        }

        private void AnimateWaterSurface(float deltaTime)
        {
            if (waterSurfaceMesh == null) return;
            waterSurfaceClock += deltaTime;
            for (var i = 0; i < waterSurfaceRestVertices.Length; i++)
            {
                var vertex = waterSurfaceRestVertices[i];
                var waveA = Mathf.Sin(vertex.x * 0.8f + waterSurfaceClock * 0.75f);
                var waveB = Mathf.Cos(vertex.z * 1.25f - waterSurfaceClock * 0.55f);
                vertex.y += (waveA + waveB) * 0.015f;
                waterSurfaceVertices[i] = vertex;
            }
            waterSurfaceMesh.vertices = waterSurfaceVertices;
            waterSurfaceMesh.RecalculateNormals();
        }

        private void UpdateWaterAndDaylight(float deltaTime)
        {
            // A short demo day makes lighting changes visible without accelerating
            // fish, plant, or food motion. One aquarium hour passes every eight seconds.
            var gameHours = deltaTime * 0.125f;
            timeOfDayHours = Mathf.Repeat(timeOfDayHours + gameHours, 24f);
            foodWasteLoad = Mathf.Max(0f, foodWasteLoad - gameHours * 0.0015f);
            ammonia = Mathf.Clamp(ammonia + gameHours * (agents.Count * 0.0002f + foodWasteLoad * 0.012f)
                - gameHours * 0.005f, 0.01f, 1f);
            nitrate = Mathf.Clamp(nitrate + gameHours * (agents.Count * 0.0012f + foodWasteLoad * 0.035f), 0f, 100f);
            var targetPh = 7.1f - Mathf.Min(0.25f, foodWasteLoad * 0.055f);
            waterPh = Mathf.MoveTowards(waterPh, targetPh, gameHours * 0.0015f);

            var sunHeight = Mathf.Sin((timeOfDayHours - 6f) * Mathf.PI / 12f);
            var daylight = Mathf.Clamp01((sunHeight + 0.12f) / 1.12f);
            waterTemperature = 23.9f + daylight * 0.35f;
            if (aquariumKeyLight != null)
            {
                aquariumKeyLight.intensity = Mathf.Lerp(0.22f, 1.25f, daylight);
                aquariumKeyLight.color = Color.Lerp(new Color(0.55f, 0.68f, 0.84f), new Color(1f, 0.94f, 0.84f), daylight);
            }
            if (aquariumFillLight != null)
                aquariumFillLight.intensity = Mathf.Lerp(0.08f, 0.48f, daylight);
            if (demoCamera != null)
                demoCamera.backgroundColor = Color.Lerp(new Color(0.025f, 0.055f, 0.09f), new Color(0.08f, 0.17f, 0.2f), daylight);
            if (waterSurfaceRenderer != null && waterSurfaceRenderer.material.HasProperty("_EmissionColor"))
                waterSurfaceRenderer.material.SetColor("_EmissionColor", Color.Lerp(
                    new Color(0.002f, 0.008f, 0.018f, 1f), new Color(0.006f, 0.022f, 0.028f, 1f), daylight));

            var stress = Mathf.InverseLerp(0.12f, 0.42f, ammonia);
            for (var i = 0; i < agents.Count; i++) agents[i].SetWaterStress(stress);
        }

        private void BuildDecor()
        {
            CreateRockFormation(new Vector3(-1.25f, -2.98f, 0.85f));
            CreateAquascapeAccents();
            CreatePlant("AmazonSword", new Vector3(-4.45f, -2.99f, -0.85f));
            CreatePlant("Vallisneria", new Vector3(-3.8f, -2.99f, 0.75f));
            CreatePlant("Cryptocoryne", new Vector3(-4.25f, -2.99f, -1.45f));
            CreatePlant("AmazonSword", new Vector3(4.65f, -2.99f, 0.95f));
            CreatePlant("Cryptocoryne", new Vector3(4.35f, -2.99f, -0.45f));
            CreatePlant("Cryptocoryne", new Vector3(3.65f, -2.99f, -1.35f));
            CreatePlant("Vallisneria", new Vector3(-5.05f, -2.99f, 1.45f));
            CreatePlant("AmazonSword", new Vector3(-4.75f, -2.99f, -1.72f));
            CreatePlant("AmazonSword", new Vector3(5.05f, -2.99f, 1.25f));
            CreatePlant("AmazonSword", new Vector3(4.85f, -2.99f, -1.65f));
            // Layer the planting instead of leaving two isolated side clumps: tall background
            // ribbons, medium swords, and low foreground rosettes create depth and scale.
            CreatePlant("Vallisneria", new Vector3(-3.55f, -2.99f, 1.95f));
            CreatePlant("Cryptocoryne", new Vector3(3.85f, -2.99f, 1.75f));
            CreatePlant("AmazonSword", new Vector3(-2.9f, -2.99f, 1.35f));
            CreatePlant("AmazonSword", new Vector3(2.45f, -2.99f, 1.32f));
            CreatePlant("Cryptocoryne", new Vector3(-2.8f, -2.99f, -1.8f));
            CreatePlant("Cryptocoryne", new Vector3(3.1f, -2.99f, -1.92f));
            // Anubias is an epiphyte; seat it on the main rock and leave its rhizome exposed.
            CreatePlant("AnubiasNana", new Vector3(-1.65f, -1.75f, 0.28f), true);
        }

        private void BuildSchools()
        {
            var counts = new[] { 5, 7, 6 };
            for (var school = 0; school < SpeciesData.Length; school++)
            {
                var center = school == 0 ? new Vector3(-2.7f, 1.15f, -0.3f)
                    : school == 1 ? new Vector3(2.4f, 0.05f, 0.35f)
                    : new Vector3(0.2f, -2.2f, 0.2f);
                var anchor = new FishSchoolAnchor(center, school == 2, school);
                schools.Add(anchor);
                for (var i = 0; i < counts[school]; i++)
                {
                    var offset = Random.insideUnitSphere * (school == 2 ? 0.48f : school == 1 ? 0.76f : 1.15f);
                    offset.y *= school == 2 ? 0.12f : school == 1 ? 0.16f : 0.48f;
                    var position = FindSpawnPosition(center + offset, school == 2 ? 0.54f : 0.50f);
                    var fish = CreateFish(SpeciesData[school], position, school);
                    agents.Add(new FishBehaviorAgent(fish.transform, anchor, school, rockObstacleBounds, foodPellets));
                }
            }
        }

        private Vector3 FindSpawnPosition(Vector3 candidate, float radius)
        {
            for (var attempt = 0; attempt < 28; attempt++)
            {
                var clear = true;
                for (var i = 0; i < agents.Count; i++)
                {
                    var required = radius + agents[i].CollisionRadius + 0.12f;
                    if (Vector3.Distance(candidate, agents[i].Position) < required)
                    {
                        clear = false;
                        break;
                    }
                }
                if (clear) return candidate;
                candidate += Random.insideUnitSphere * 0.45f;
                candidate.y = Mathf.Clamp(candidate.y, -2.35f, 2.35f);
                candidate.x = Mathf.Clamp(candidate.x, -5.1f, 5.1f);
                candidate.z = Mathf.Clamp(candidate.z, -2f, 2f);
            }
            return candidate;
        }

        private GameObject CreateFish(Species species, Vector3 position, int school)
        {
            var model = Resources.Load<GameObject>("FishModels/" + species.AssetKey);
            var fish = model != null ? Instantiate(model) : GameObject.CreatePrimitive(PrimitiveType.Capsule);
            fish.name = species.Name;
            fish.transform.SetParent(transform, false);
            fish.transform.position = position;
            fish.transform.localScale = Vector3.one * (school == 2 ? 0.82f : 0.76f);
            fish.transform.localRotation = Quaternion.identity;
            if (model != null)
            {
                ConfigureFishMaterials(fish, school);
                TuneFishSilhouette(fish, school);
                // FBX unit metadata can differ between Blender exports. Normalize by the
                // imported renderer bounds so a valid model cannot appear as tiny specks.
                var renderers = fish.GetComponentsInChildren<Renderer>();
                var bounds = new Bounds(fish.transform.position, Vector3.zero);
                var hasBounds = false;
                for (var i = 0; i < renderers.Length; i++)
                {
                    if (!renderers[i].enabled) continue;
                    if (!hasBounds)
                    {
                        bounds = renderers[i].bounds;
                        hasBounds = true;
                    }
                    else bounds.Encapsulate(renderers[i].bounds);
                }
                var longestSide = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
                var targetLength = school == 2 ? 1.05f : 1.2f;
                if (hasBounds && longestSide > 0.0001f)
                {
                    var correction = Mathf.Clamp(targetLength / longestSide, 0.75f, 2f);
                    fish.transform.localScale *= correction;
                }
            }
            if (model == null)
            {
                fish.transform.localScale = school == 2 ? new Vector3(0.8f, 0.45f, 1.45f) : new Vector3(0.65f, 0.38f, 1.8f);
                SetColor(fish.GetComponent<Renderer>(), species.Color);
                var collider = fish.GetComponent<Collider>();
                if (collider != null) Destroy(collider);
            }
            return fish;
        }

        private void CreatePlant(string assetName, Vector3 position, bool attachedToHardscape = false)
        {
            var prefab = Resources.Load<GameObject>("AquaticPlants/" + assetName);
            if (prefab == null) return;
            if (!attachedToHardscape)
                position.y = SubstrateHeight(position.x, position.z);
            var plantRotation = Quaternion.Euler(-90f, 0f, 0f);
            if (attachedToHardscape && anubiasAnchorCollider != null)
            {
                var rockBounds = anubiasAnchorCollider.bounds;
                var samplePoint = new Vector3(
                    rockBounds.center.x - rockBounds.size.x * 0.16f,
                    rockBounds.max.y + 2f,
                    rockBounds.center.z - rockBounds.size.z * 0.12f);
                if (Physics.Raycast(samplePoint, Vector3.down, out var hit, rockBounds.size.y + 4f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    && hit.collider == anubiasAnchorCollider)
                {
                    // Seat the rhizome just inside the rock surface instead of
                    // relying on a guessed world-space height.
                    position = hit.point - hit.normal * 0.025f;
                    plantRotation = Quaternion.FromToRotation(Vector3.up, hit.normal) * plantRotation;
                }
            }
            var plant = Instantiate(prefab, position, plantRotation, transform);
            plant.name = assetName;
            ConfigureNaturalAssetMaterials(plant, 0.34f);
            plant.transform.rotation = Quaternion.AngleAxis(Random.Range(-32f, 32f), plantRotation * Vector3.up) * plantRotation;
            // Blender's exported metre assets import at centimetre scale in this project.
            var plantScale = assetName == "Vallisneria" ? 220f
                : assetName == "AmazonSword" ? 178f
                : assetName == "Cryptocoryne" ? 165f
                : 100f;
            var individualScale = Random.Range(0.84f, 1.14f);
            plant.transform.localScale = Vector3.one * (plantScale * individualScale);
            KeepPlantInsideGlass(plant);
            var rootSwayDegrees = assetName == "Vallisneria" ? 1.4f
                : assetName == "AnubiasNana" ? 0.55f
                : assetName == "Cryptocoryne" ? 0.85f
                : 0.7f;
            var flexRatio = assetName == "Vallisneria" ? 0.065f
                : assetName == "Cryptocoryne" ? 0.075f
                : assetName == "AnubiasNana" ? 0.045f
                : 0.055f;
            plantSways.Add(new PlantSway(plant.transform, rootSwayDegrees, flexRatio, Random.Range(0f, 6.28f)));
        }

        private static void KeepPlantInsideGlass(GameObject plant)
        {
            var renderers = plant.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            // Leave room for the tips to bend without crossing the glass panes.
            var shift = Vector3.zero;
            if (bounds.min.x < -TankWidth * 0.5f + 0.82f) shift.x = -TankWidth * 0.5f + 0.82f - bounds.min.x;
            else if (bounds.max.x > TankWidth * 0.5f - 0.82f) shift.x = TankWidth * 0.5f - 0.82f - bounds.max.x;
            if (bounds.min.z < -TankDepth * 0.5f + 0.55f) shift.z = -TankDepth * 0.5f + 0.55f - bounds.min.z;
            else if (bounds.max.z > TankDepth * 0.5f - 0.55f) shift.z = TankDepth * 0.5f - 0.55f - bounds.max.z;
            if (bounds.max.y > TankHeight * 0.5f - 0.4f) shift.y = TankHeight * 0.5f - 0.4f - bounds.max.y;

            plant.transform.position += shift;
        }

        private void CreateRockFormation(Vector3 position)
        {
            var prefab = Resources.Load<GameObject>("Aquascape/IwagumiRockFormation");
            if (prefab == null) return;
            position.y = SubstrateHeight(position.x, position.z);
            var rocks = Instantiate(prefab, position, Quaternion.identity, transform);
            rocks.name = "Iwagumi 假山石组";
            ConfigureNaturalAssetMaterials(rocks, 0.16f);
            rocks.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            // The exported formation is already in metre-sized Unity units. A
            // 100x correction made the mesh and its avoidance bounds hundreds
            // of metres wide, hiding the rocks and steering every school away.
            rocks.transform.localScale = Vector3.one;
            var renderers = rocks.GetComponentsInChildren<Renderer>();
            for (var i = 0; i < renderers.Length; i++)
                rockObstacleBounds.Add(renderers[i].bounds);

            // Use the main stone surface as a real planting anchor. The other
            // rocks stay collider-free and continue using their bounds for fish avoidance.
            var meshFilters = rocks.GetComponentsInChildren<MeshFilter>();
            for (var i = 0; i < meshFilters.Length; i++)
            {
                if (meshFilters[i].sharedMesh == null) continue;
                var collider = meshFilters[i].GetComponent<MeshCollider>();
                if (collider == null) collider = meshFilters[i].gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = meshFilters[i].sharedMesh;
                hardscapeColliders.Add(collider);
                if (meshFilters[i].name.Contains("Oyaishi")) anubiasAnchorCollider = collider;
            }
            Physics.SyncTransforms();
        }

        private void CreateAquascapeAccents()
        {
            var pebbleClusterPrefab = Resources.Load<GameObject>("Aquascape/MixedRiverPebbleCluster");
            var pebbleClusterSites = new[]
            {
                new Vector3(-3.55f, 0f, -0.9f),
                new Vector3(0.35f, 0f, -1.95f),
                new Vector3(3.75f, 0f, 0.72f)
            };
            if (pebbleClusterPrefab != null)
            {
                for (var i = 0; i < pebbleClusterSites.Length; i++)
                {
                    var site = pebbleClusterSites[i];
                    site.y = SubstrateHeight(site.x, site.z);
                    var cluster = Instantiate(pebbleClusterPrefab, site, Quaternion.Euler(-90f, Random.Range(0f, 360f), 0f), transform);
                    cluster.name = "混色卵石簇";
                    cluster.transform.localScale = Vector3.one * Random.Range(1.04f, 1.18f);
                    ConfigureNaturalAssetMaterials(cluster, 0.2f);
                }
            }

            var shellPrefab = Resources.Load<GameObject>("Aquascape/SmallGastropodShell");
            var shellSites = new[]
            {
                new Vector3(2.35f, 0f, -1.55f), new Vector3(2.95f, 0f, -1.32f),
                new Vector3(-3.25f, 0f, -1.35f), new Vector3(0.75f, 0f, 1.8f)
            };
            if (shellPrefab != null)
            {
                for (var i = 0; i < shellSites.Length; i++)
                {
                    var site = shellSites[i];
                    site.y = SubstrateHeight(site.x, site.z);
                    var shell = Instantiate(shellPrefab, site, Quaternion.Euler(-90f, Random.Range(0f, 360f), Random.Range(-12f, 12f)), transform);
                    shell.name = "淡水螺壳";
                    shell.transform.localScale = Vector3.one * Random.Range(0.72f, 0.92f);
                    ConfigureNaturalAssetMaterials(shell, 0.24f);
                }
            }

            CreateSandMossPatches();
            CreateHermitCrab();
        }

        private void CreateHermitCrab()
        {
            var prefab = Resources.Load<GameObject>("Aquascape/FreshwaterHermitCrab");
            if (prefab == null) return;
            var position = new Vector3(0.55f, SubstrateHeight(0.55f, -1.4f), -1.4f);
            var crab = Instantiate(prefab, position, Quaternion.Euler(-90f, 28f, 0f), transform);
            crab.name = "淡水寄居蟹";
            crab.transform.localScale = Vector3.one * 0.8f;
            ConfigureNaturalAssetMaterials(crab, 0.3f);
            crab.AddComponent<HermitCrabBehaviorAgent>();
        }

        private void CreateSandMossPatches()
        {
            var prefab = Resources.Load<GameObject>("Aquascape/FreshwaterMossCarpet");
            if (prefab == null) return;
            var sites = new[]
            {
                new Vector3(-2.25f, 0f, -1.18f), new Vector3(0.75f, 0f, -1.38f),
                new Vector3(-0.85f, 0f, 0.52f), new Vector3(2.15f, 0f, 0.68f)
            };
            for (var i = 0; i < sites.Length; i++)
            {
                var site = sites[i];
                site.y = SubstrateHeight(site.x, site.z) + 0.01f;
                var yaw = Random.Range(-13f, 13f);
                var moss = Instantiate(prefab, site, Quaternion.Euler(-90f, yaw, 0f), transform);
                moss.name = "沙地成片青苔毯";
                moss.transform.localScale = Vector3.one * Random.Range(1.02f, 1.18f);
                ConfigureNaturalAssetMaterials(moss, 0.18f);
            }
        }

        private sealed class PlantSway
        {
            private readonly Transform root;
            private readonly Quaternion restRotation;
            private readonly float rootSwayDegrees;
            private readonly float flexRatio;
            private readonly float phase;
            private readonly List<LeafMesh> leafMeshes = new List<LeafMesh>();

            public PlantSway(Transform root, float rootSwayDegrees, float flexRatio, float phase)
            {
                this.root = root;
                restRotation = root.localRotation;
                this.rootSwayDegrees = rootSwayDegrees;
                this.flexRatio = flexRatio;
                this.phase = phase;

                var filters = root.GetComponentsInChildren<MeshFilter>(true);
                for (var i = 0; i < filters.Length; i++)
                {
                    if (filters[i].sharedMesh == null || !filters[i].sharedMesh.isReadable) continue;
                    var mesh = filters[i].mesh;
                    mesh.MarkDynamic();
                    leafMeshes.Add(new LeafMesh(mesh));
                }
            }

            public void Tick(float time)
            {
                var broadCurrent = Mathf.Sin(time * 0.48f + phase);
                var smallCurrent = Mathf.Sin(time * 0.91f + phase * 1.73f) * 0.24f;
                var rootBend = (broadCurrent + smallCurrent) * rootSwayDegrees;
                root.localRotation = restRotation * Quaternion.Euler(rootBend * 0.7f, 0f, rootBend * 0.45f);

                var flowX = Mathf.Sin(time * 0.58f + phase) + Mathf.Sin(time * 1.03f + phase * 1.41f) * 0.28f;
                var flowY = Mathf.Cos(time * 0.43f + phase * 0.83f) * 0.42f;
                var leafWaveAngle = time * 0.72f + phase;
                var leafWaveSin = Mathf.Sin(leafWaveAngle);
                var leafWaveCos = Mathf.Cos(leafWaveAngle);
                for (var m = 0; m < leafMeshes.Count; m++)
                {
                    var leafMesh = leafMeshes[m];
                    for (var v = 0; v < leafMesh.OriginalVertices.Length; v++)
                    {
                        var original = leafMesh.OriginalVertices[v];
                        var localWaveSin = leafWaveSin * leafMesh.WavePhaseCos[v] + leafWaveCos * leafMesh.WavePhaseSin[v];
                        var localWaveCos = leafWaveCos * leafMesh.WavePhaseCos[v] - leafWaveSin * leafMesh.WavePhaseSin[v];
                        // The phase travels from root to tip. Adjacent sections of
                        // a blade bend in opposite directions and form a soft S.
                        var localFlowX = localWaveSin * 0.82f + flowX * 0.18f;
                        var localFlowY = localWaveCos * 0.3f + flowY * 0.16f;
                        var bendWeight = leafMesh.BendWeights[v];
                        var displacement = leafMesh.Height * flexRatio * bendWeight;
                        var deformed = original;
                        deformed.x += localFlowX * displacement;
                        deformed.y += localFlowY * displacement;
                        leafMesh.DeformedVertices[v] = deformed;
                    }

                    leafMesh.Mesh.vertices = leafMesh.DeformedVertices;
                    leafMesh.Mesh.RecalculateNormals();
                    leafMesh.Mesh.RecalculateBounds();
                }
            }

            private sealed class LeafMesh
            {
                public readonly Mesh Mesh;
                public readonly Vector3[] OriginalVertices;
                public readonly Vector3[] DeformedVertices;
                public readonly float[] BendWeights;
                public readonly float[] WavePhaseSin;
                public readonly float[] WavePhaseCos;
                public readonly float Height;

                public LeafMesh(Mesh mesh)
                {
                    Mesh = mesh;
                    OriginalVertices = mesh.vertices;
                    DeformedVertices = new Vector3[OriginalVertices.Length];
                    BendWeights = new float[OriginalVertices.Length];
                    WavePhaseSin = new float[OriginalVertices.Length];
                    WavePhaseCos = new float[OriginalVertices.Length];
                    var bounds = mesh.bounds;
                    Height = Mathf.Max(bounds.size.z, 0.0001f);
                    for (var i = 0; i < OriginalVertices.Length; i++)
                    {
                        var vertex = OriginalVertices[i];
                        var height = Mathf.Clamp01((vertex.z - bounds.min.z) / Height);
                        BendWeights[i] = Mathf.Pow(height, 1.7f);
                        var azimuth = Mathf.Atan2(vertex.y, vertex.x);
                        var wavePhase = azimuth * 0.34f + (vertex.x + vertex.y) * 18f
                            - height * Mathf.PI * 2f * 1.15f;
                        WavePhaseSin[i] = Mathf.Sin(wavePhase);
                        WavePhaseCos[i] = Mathf.Cos(wavePhase);
                    }
                }
            }
        }

        private static void ConfigureFishMaterials(GameObject fish, int speciesIndex)
        {
            ConfigureNaturalAssetMaterials(fish, 0.32f);
            var guppyScaleNormal = speciesIndex == 0
                ? Resources.Load<Texture2D>("FishModels/Guppy_Hero_Normal")
                : null;
            var renderers = fish.GetComponentsInChildren<Renderer>();
            var finColor = speciesIndex == 0 ? new Color(0.72f, 0.47f, 0.27f)
                : speciesIndex == 1 ? new Color(0.56f, 0.72f, 0.73f)
                : new Color(0.76f, 0.72f, 0.62f);
            for (var i = 0; i < renderers.Length; i++)
            {
                var partName = renderers[i].name.ToLowerInvariant();
                if (partName.Contains("body"))
                {
                    var bodyTint = speciesIndex == 0 ? new Color(0.74f, 0.65f, 0.48f)
                        : speciesIndex == 1 ? new Color(0.55f, 0.7f, 0.77f)
                        : new Color(0.76f, 0.69f, 0.55f);
                    var bodyMaterials = renderers[i].materials;
                    for (var j = 0; j < bodyMaterials.Length; j++)
                    {
                        var material = bodyMaterials[j];
                        if (material == null) continue;
                        var materialName = material.name.ToLowerInvariant();
                        if (materialName.Contains("guppy hero") && materialName.Contains("silver-blue skin") && guppyScaleNormal != null)
                        {
                            if (material.HasProperty("_BumpMap")) material.SetTexture("_BumpMap", guppyScaleNormal);
                            if (material.HasProperty("_BumpScale")) material.SetFloat("_BumpScale", 0.032f);
                            material.EnableKeyword("_NORMALMAP");
                            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.44f);
                        }
                        if (materialName.Contains("guppy hero") || (!materialName.Contains("silver") && !materialName.Contains("ivory"))) continue;
                        var baseColor = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : material.color;
                        var shadedColor = Color.Lerp(baseColor, bodyTint, 0.34f);
                        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", shadedColor);
                        if (material.HasProperty("_Color")) material.SetColor("_Color", shadedColor);
                        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0.02f);
                    }
                    renderers[i].materials = bodyMaterials;
                }
                if (!partName.Contains("fin") && !partName.Contains("tail") && !partName.Contains("caudal ray")) continue;
                var materials = renderers[i].materials;
                for (var j = 0; j < materials.Length; j++)
                {
                    if (materials[j] == null) continue;
                    var materialName = materials[j].name.ToLowerInvariant();
                    // Keep species markings, but let the guppy tail membrane and
                    // fine fin rays read as translucent tissue in the close view.
                    if (materialName.Contains("sunset fan tail"))
                    {
                        SetFinTransparency(materials[j], new Color(0.82f, 0.42f, 0.25f, 0.55f));
                    }
                    else if (materialName.Contains("fin rays"))
                    {
                        finColor.a = 0.36f;
                        if (materialName.Contains("guppy hero"))
                        {
                            var authoredColor = materials[j].HasProperty("_BaseColor")
                                ? materials[j].GetColor("_BaseColor") : materials[j].color;
                            authoredColor.a = 0.72f;
                            SetFinTransparency(materials[j], authoredColor);
                        }
                        else SetFinTransparency(materials[j], finColor);
                    }
                    else if (materialName.Contains("amber"))
                    {
                        finColor.a = 0.62f;
                        if (materialName.Contains("guppy hero"))
                        {
                            var authoredColor = materials[j].HasProperty("_BaseColor")
                                ? materials[j].GetColor("_BaseColor") : materials[j].color;
                            authoredColor.a = 0.78f;
                            SetFinTransparency(materials[j], authoredColor);
                        }
                        else SetFinTransparency(materials[j], finColor);
                    }
                }
                renderers[i].materials = materials;
                renderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderers[i].receiveShadows = false;
            }
        }

        private static void SetFinTransparency(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
            if (material.HasProperty("_SrcBlend")) material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend")) material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite")) material.SetInt("_ZWrite", 0);
            material.SetOverrideTag("RenderType", "Transparent");
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        private static void TuneFishSilhouette(GameObject fish, int speciesIndex)
        {
            // Hero assets already have authored proportions. Keep the legacy
            // runtime silhouette corrections away from the new Blender model.
            var filters = fish.GetComponentsInChildren<MeshFilter>();
            for (var i = 0; i < filters.Length; i++)
                if (filters[i].name.Contains("Guppy Hero Body")) return;

            // The exported meshes read too thin in profile, with oversized eyes and
            // very broad, opaque fins. Nudge the runtime silhouette toward a fish:
            // fuller bodies, smaller eyes, and fins that stay close to the body.
            var transforms = fish.GetComponentsInChildren<Transform>();
            for (var i = 0; i < transforms.Length; i++)
            {
                var partName = transforms[i].name.ToLowerInvariant();
                if (partName.Contains("body"))
                {
                    // Neon tetras stay relatively slender; guppies have a deeper
                    // trunk, while panda corys are compact and noticeably robust.
                    var bodyDepth = speciesIndex == 1 ? 1.16f : speciesIndex == 2 ? 1.48f : 1.42f;
                    var bodyHeight = speciesIndex == 1 ? 1.18f : speciesIndex == 2 ? 1.35f : 1.45f;
                    transforms[i].localScale = Vector3.Scale(transforms[i].localScale, new Vector3(1f, bodyDepth, bodyHeight));
                }
                else if (partName.Contains("eye"))
                {
                    transforms[i].localScale *= 0.6f;
                }
                else if (partName.Contains("gill seam"))
                {
                    transforms[i].localScale *= 0.78f;
                }
                else if ((partName.Contains("dorsal fin") || partName.Contains("anal fin")) && !partName.Contains("ray"))
                {
                    transforms[i].localScale *= 0.76f;
                }
                else if (partName.Contains("pectoral fin") && !partName.Contains("ray"))
                {
                    transforms[i].localScale *= 0.84f;
                }
                else if (partName.Contains("fan tail") || partName.Contains("caudal fin"))
                {
                    transforms[i].localScale *= speciesIndex == 0 ? 0.88f : 0.94f;
                }
                else if (partName.Contains("caudal ray"))
                {
                    // Rays should terminate within the fan membrane, not stick out
                    // like wires beyond the visible edge of the tail.
                    transforms[i].localScale *= 0.4f;
                }
            }
        }

        private static void ConfigureNaturalAssetMaterials(GameObject asset, float smoothness)
        {
            var renderers = asset.GetComponentsInChildren<Renderer>();
            for (var i = 0; i < renderers.Length; i++)
            {
                var materials = renderers[i].materials;
                for (var j = 0; j < materials.Length; j++)
                {
                    var material = materials[j];
                    if (material == null) continue;
                    if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 0f);
                    if (material.HasProperty("_AlphaClip")) material.SetFloat("_AlphaClip", 0f);
                    if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 1f);
                    if (material.HasProperty("_Cull")) material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
                    if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
                    if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
                    if (material.HasProperty("_BaseColor"))
                    {
                        var color = material.GetColor("_BaseColor");
                        color.a = 1f;
                        material.SetColor("_BaseColor", color);
                    }
                    if (material.HasProperty("_Color"))
                    {
                        var color = material.GetColor("_Color");
                        color.a = 1f;
                        material.SetColor("_Color", color);
                    }
                    material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
                    material.SetOverrideTag("RenderType", "Opaque");
                    material.DisableKeyword("_ALPHATEST_ON");
                    material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                }
                renderers[i].materials = materials;
            }
        }

        private void CreateBlock(string objectName, Vector3 position, Vector3 scale, Color color, bool opaque, Vector3? euler = null)
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = objectName;
            block.transform.SetParent(transform, false);
            block.transform.localPosition = position;
            block.transform.localScale = scale;
            if (euler.HasValue) block.transform.localEulerAngles = euler.Value;
            SetColor(block.GetComponent<Renderer>(), color, opaque);
            if (!opaque)
            {
                var renderer = block.GetComponent<Renderer>();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            var collider = block.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
        }

        private void PositionCamera()
        {
            demoCamera = Camera.main;
            if (demoCamera == null)
            {
                var cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                demoCamera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
            }
            demoCamera.fieldOfView = 38f;
            demoCamera.clearFlags = CameraClearFlags.SolidColor;
            demoCamera.backgroundColor = new Color(0.08f, 0.17f, 0.20f);
            UpdateCameraTransform();

            if (FindFirstObjectByType<Light>() == null)
            {
                var lightObject = new GameObject("Demo Key Light");
                lightObject.transform.position = transform.TransformPoint(new Vector3(0f, 5.4f, 0f));
                lightObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.35f;
            }
        }

        private void HandleCameraInput()
        {
            if (demoCamera == null || Mouse.current == null) return;

            var mouse = Mouse.current;
            var changed = false;
            if (mouse.rightButton.isPressed)
            {
                var delta = mouse.delta.ReadValue();
                cameraYaw += delta.x * 0.16f;
                cameraPitch = Mathf.Clamp(cameraPitch - delta.y * 0.14f, -8f, 68f);
                changed |= delta.sqrMagnitude > 0f;
            }

            var scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                cameraDistance = selectedFish != null
                    ? Mathf.Clamp(cameraDistance - scroll * 0.012f, 1.8f, 5.5f)
                    : Mathf.Clamp(cameraDistance - scroll * 0.012f, 8.5f, 23f);
                changed = true;
            }

            if (mouse.middleButton.isPressed)
            {
                var delta = mouse.delta.ReadValue();
                var rotation = Quaternion.Euler(cameraPitch, cameraYaw, 0f);
                var pan = -(rotation * Vector3.right) * (delta.x * cameraDistance * 0.0012f)
                    + rotation * Vector3.up * (delta.y * cameraDistance * 0.0012f);
                if (selectedFish != null) selectedCameraOffset += pan;
                else cameraTarget += pan;
                cameraTarget.x = Mathf.Clamp(cameraTarget.x, -4f, 4f);
                cameraTarget.y = Mathf.Clamp(cameraTarget.y, -1.5f, 2.5f);
                cameraTarget.z = Mathf.Clamp(cameraTarget.z, -2.5f, 2.5f);
                changed |= delta.sqrMagnitude > 0f;
            }

            if (Keyboard.current != null && Keyboard.current.homeKey.wasPressedThisFrame)
            {
                ClearSelectedFish();
                cameraTarget = Vector3.zero;
                cameraYaw = 0f;
                cameraPitch = 12f;
                cameraDistance = 16f;
                changed = true;
            }

            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                ClearSelectedFish();

            if (mouse.leftButton.wasPressedThisFrame && !IsPointerOverHud(mouse.position.ReadValue()))
            {
                var mousePosition = mouse.position.ReadValue();
                if (!TrySelectFish(mousePosition)) FeedAtPointer(mousePosition);
            }

            if (changed) UpdateCameraTransform();
        }

        private bool IsPointerOverHud(Vector2 mousePosition)
        {
            var guiPosition = new Vector2(mousePosition.x, Screen.height - mousePosition.y);
            if (new Rect(12f, 12f, 390f, 250f).Contains(guiPosition)) return true;
            return selectedFish != null && new Rect(Screen.width - 310f, 12f, 295f, 190f).Contains(guiPosition);
        }

        private bool TrySelectFish(Vector2 screenPosition)
        {
            if (demoCamera == null) return false;
            var ray = demoCamera.ScreenPointToRay(screenPosition);
            FishBehaviorAgent nearest = null;
            var nearestDistance = float.PositiveInfinity;
            for (var i = 0; i < agents.Count; i++)
            {
                var fish = agents[i];
                if (fish.Root == null) continue;
                var renderers = fish.Root.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) continue;
                var bounds = renderers[0].bounds;
                for (var r = 1; r < renderers.Length; r++) bounds.Encapsulate(renderers[r].bounds);
                bounds.Expand(0.18f);
                if (!bounds.IntersectRay(ray, out var distance) || distance >= nearestDistance) continue;
                nearest = fish;
                nearestDistance = distance;
            }
            if (nearest == null) return false;

            if (selectedFish == null)
            {
                cameraTargetBeforeSelection = cameraTarget;
                cameraDistanceBeforeSelection = cameraDistance;
                cameraYawBeforeSelection = cameraYaw;
                cameraPitchBeforeSelection = cameraPitch;
                selectionCameraSaved = true;
            }
            selectedFish = nearest;
            ApplyInspectionFocus(nearest);
            selectedCameraOffset = Vector3.zero;
            cameraTarget = nearest.Position;
            var fishForward = Vector3.ProjectOnPlane(nearest.Root.right, Vector3.up);
            if (fishForward.sqrMagnitude > 0.001f)
            {
                fishForward.Normalize();
                var sideViewDirection = Vector3.Cross(fishForward, Vector3.up);
                var detailViewDirection = Quaternion.AngleAxis(30f, Vector3.up) * sideViewDirection;
                cameraYaw = Mathf.Atan2(-detailViewDirection.x, -detailViewDirection.z) * Mathf.Rad2Deg;
                cameraPitch = 8f;
            }
            cameraDistance = 2.0f;
            UpdateCameraTransform();
            return true;
        }

        private void ApplyInspectionFocus(FishBehaviorAgent focus)
        {
            RestoreInspectionFish();
            for (var i = 0; i < agents.Count; i++)
            {
                var agent = agents[i];
                if (agent == focus || agent.Root == null) continue;
                var renderers = agent.Root.GetComponentsInChildren<Renderer>(true);
                for (var r = 0; r < renderers.Length; r++)
                {
                    if (!renderers[r].enabled) continue;
                    renderers[r].enabled = false;
                    inspectionHiddenRenderers.Add(renderers[r]);
                }
            }
        }

        private void RestoreInspectionFish()
        {
            for (var i = 0; i < inspectionHiddenRenderers.Count; i++)
                if (inspectionHiddenRenderers[i] != null) inspectionHiddenRenderers[i].enabled = true;
            inspectionHiddenRenderers.Clear();
        }

        private void FollowSelectedFish(float deltaTime)
        {
            if (selectedFish == null || selectedFish.Root == null) return;
            var target = selectedFish.Position + selectedCameraOffset;
            cameraTarget = Vector3.Lerp(cameraTarget, target, 1f - Mathf.Exp(-9f * deltaTime));
            UpdateCameraTransform();
        }

        private void ClearSelectedFish()
        {
            if (selectedFish == null) return;
            RestoreInspectionFish();
            selectedFish = null;
            selectedCameraOffset = Vector3.zero;
            if (!selectionCameraSaved) return;
            cameraTarget = cameraTargetBeforeSelection;
            cameraDistance = cameraDistanceBeforeSelection;
            cameraYaw = cameraYawBeforeSelection;
            cameraPitch = cameraPitchBeforeSelection;
            selectionCameraSaved = false;
            UpdateCameraTransform();
        }

        private void FeedAtPointer(Vector2 screenPosition)
        {
            if (demoCamera == null) return;
            var ray = demoCamera.ScreenPointToRay(screenPosition);
            var sampleHeights = new[] { 0f, 1.25f, -1.25f, 2.35f };
            var foundWaterPoint = false;
            var point = Vector3.zero;
            for (var i = 0; i < sampleHeights.Length; i++)
            {
                var plane = new Plane(Vector3.up, new Vector3(0f, sampleHeights[i], 0f));
                if (!plane.Raycast(ray, out var distance)) continue;
                var candidate = ray.GetPoint(distance);
                if (candidate.x < -5.4f || candidate.x > 5.4f || candidate.z < -2.1f || candidate.z > 2.1f) continue;
                point = candidate;
                foundWaterPoint = true;
                break;
            }
            if (!foundWaterPoint) return;

            const int pelletCount = 8;
            var offsets = new List<Vector2>(pelletCount);
            for (var i = 0; i < pelletCount; i++)
            {
                var offset = Random.insideUnitCircle * 1.25f;
                for (var attempt = 0; attempt < 12; attempt++)
                {
                    var tooClose = false;
                    for (var existing = 0; existing < offsets.Count; existing++)
                    {
                        if (Vector2.Distance(offset, offsets[existing]) >= 0.68f) continue;
                        tooClose = true;
                        break;
                    }
                    if (!tooClose) break;
                    offset = Random.insideUnitCircle * 1.25f;
                }
                offsets.Add(offset);
                var x = Mathf.Clamp(point.x + offset.x, -5.2f, 5.2f);
                var z = Mathf.Clamp(point.z + offset.y, -1.95f, 1.95f);
                var pelletObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                pelletObject.name = "沉降鱼粮";
                pelletObject.transform.SetParent(transform, true);
                pelletObject.transform.position = new Vector3(x, TankHeight * 0.5f - 0.28f, z);
                // Unity's primitive sphere has a two-unit diameter; this yields
                // visible but small 2.4–3.6 cm food beads in the aquarium scale.
                pelletObject.transform.localScale = Vector3.one * Random.Range(0.012f, 0.018f);
                var renderer = pelletObject.GetComponent<Renderer>();
                SetColor(renderer, new Color(0.72f, 0.54f, 0.3f));
                if (renderer.material.HasProperty("_Smoothness")) renderer.material.SetFloat("_Smoothness", 0.38f);
                var collider = pelletObject.GetComponent<Collider>();
                if (collider != null) Destroy(collider);
                var pellet = pelletObject.AddComponent<FishFoodPellet>();
                pellet.Initialize(SubstrateHeight(x, z) + 0.08f, Random.Range(0f, 6.28f), RegisterUneatenFood);
                foodPellets.Add(pellet);
            }
        }

        private void RegisterUneatenFood()
        {
            foodWasteLoad += 0.12f;
            nitrate += 0.12f;
        }

        private void RefreshWater()
        {
            ammonia = Mathf.Max(0.01f, ammonia * 0.32f);
            nitrate *= 0.68f;
            waterPh = Mathf.Lerp(waterPh, 7.1f, 0.65f);
            foodWasteLoad *= 0.35f;
        }

        private void UpdateCameraTransform()
        {
            if (demoCamera == null) return;
            var rotation = Quaternion.Euler(cameraPitch, cameraYaw, 0f);
            demoCamera.transform.position = cameraTarget + rotation * (Vector3.back * cameraDistance);
            demoCamera.transform.rotation = rotation;
        }

        private static void SetColor(Renderer renderer, Color color, bool opaque = true)
        {
            if (renderer == null) return;
            var material = renderer.material;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (!opaque)
            {
                if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
                if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
                if (material.HasProperty("_SrcBlend")) material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                if (material.HasProperty("_DstBlend")) material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                if (material.HasProperty("_ZWrite")) material.SetInt("_ZWrite", 0);
                if (material.HasProperty("_AlphaClip")) material.SetFloat("_AlphaClip", 0f);
                material.SetOverrideTag("RenderType", "Transparent");
                material.DisableKeyword("_ALPHATEST_ON");
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
        }

        private void OnGUI()
        {
            GUI.Box(new Rect(20f, 20f, 370f, 238f), "赛博鱼缸 · 单缸生态预览");
            var guppyCount = 0;
            var tetraCount = 0;
            var coryCount = 0;
            var feedingCount = 0;
            var seekingFoodCount = 0;
            for (var i = 0; i < agents.Count; i++)
            {
                if (agents[i].SpeciesIndex == 0) guppyCount++;
                else if (agents[i].SpeciesIndex == 1) tetraCount++;
                else coryCount++;
                if (agents[i].IsSeekingFood) seekingFoodCount++;
                if (agents[i].IsFeeding) feedingCount++;
            }
            var hour = Mathf.FloorToInt(timeOfDayHours);
            var minute = Mathf.FloorToInt((timeOfDayHours - hour) * 60f);
            var status = ammonia < 0.12f ? "稳定" : ammonia < 0.3f ? "注意水质" : "需要换水";
            var activeFood = 0;
            for (var i = 0; i < foodPellets.Count; i++)
                if (foodPellets[i] != null && foodPellets[i].IsAvailable) activeFood++;

            GUI.Label(new Rect(38f, 50f, 332f, 22f), string.Format("缸内时间 {0:00}:{1:00}    水温 {2:F1}°C    pH {3:F1}", hour, minute, waterTemperature, waterPh));
            GUI.Label(new Rect(38f, 74f, 332f, 22f), string.Format("孔雀鱼 {0}    霓虹灯鱼 {1}    熊猫鼠 {2}", guppyCount, tetraCount, coryCount));
            GUI.Label(new Rect(38f, 98f, 332f, 22f), string.Format("氨氮 {0:F2} mg/L    硝酸盐 {1:F1} mg/L    水质：{2}", ammonia, nitrate, status));
            GUI.Label(new Rect(38f, 122f, 332f, 22f), string.Format("鱼粮 {0} 粒    追食 {1} 条 / 摄食 {2} 条", activeFood, seekingFoodCount, feedingCount));
            if (GUI.Button(new Rect(38f, 151f, 112f, 28f), "换水 30%")) RefreshWater();
            GUI.Label(new Rect(160f, 151f, 210f, 28f), "点鱼看详情 / 点空处投喂");
            GUI.Label(new Rect(38f, 184f, 332f, 22f), "左键：选鱼 / 空处投喂    右键：环绕");
            GUI.Label(new Rect(38f, 207f, 332f, 22f), "中键平移    Esc：退出近景    Home：重置镜头");

            if (selectedFish != null && selectedFish.Root != null)
            {
                var detailRect = new Rect(Screen.width - 310f, 20f, 290f, 174f);
                GUI.Box(detailRect, SpeciesData[selectedFish.SpeciesIndex].Name + " · 个体观察");
                GUI.Label(new Rect(detailRect.x + 16f, detailRect.y + 34f, 258f, 22f), "行为：" + selectedFish.BehaviorDescription);
                GUI.Label(new Rect(detailRect.x + 16f, detailRect.y + 58f, 258f, 22f), "饱食度：" + Mathf.RoundToInt(selectedFish.Satiety * 100f) + "%");
                GUI.Label(new Rect(detailRect.x + 16f, detailRect.y + 82f, 258f, 22f), "水质压力：" + Mathf.RoundToInt(selectedFish.WaterStress * 100f) + "%");
                GUI.Label(new Rect(detailRect.x + 16f, detailRect.y + 106f, 258f, 22f), "位置：" + selectedFish.Position.ToString("F1"));
                if (GUI.Button(new Rect(detailRect.x + 16f, detailRect.y + 136f, 130f, 26f), "退出近景 (Esc)"))
                    ClearSelectedFish();
            }
        }

        private readonly struct Species
        {
            public readonly string Name;
            public readonly string AssetKey;
            public readonly Color Color;
            public readonly int GroupMinimum;
            public readonly int FryMin;
            public readonly int FryMax;

            public Species(string name, string assetKey, Color color, int groupMinimum, int fryMin, int fryMax)
            {
                Name = name;
                AssetKey = assetKey;
                Color = color;
                GroupMinimum = groupMinimum;
                FryMin = fryMin;
                FryMax = fryMax;
            }
        }
    }
}

