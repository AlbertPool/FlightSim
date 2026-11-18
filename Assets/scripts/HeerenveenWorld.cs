using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Builds a lightweight "Heerenveen around 1910" landscape around the existing terrain:
/// flat Frisian polder land (meadows, ditches and canals painted into one ground texture),
/// the town along the Compagnonsvaart with brick houses, a church, drawbridges and barges,
/// farms, windmills, tree-lined lanes and the Oranjewoud woods.
///
/// Performance:
/// - Everything is generated procedurally at start-up, so there are no extra assets to load.
/// - All static objects are merged into a few meshes per 750 m chunk (one draw call per material per chunk).
/// - Each chunk has a LODGroup: small details and full-detail trees only near the camera,
///   cheap 8-triangle trees further away, and nothing past the fog.
/// - No colliders except the ground, and no per-object scripts (only the windmill sails rotate).
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class HeerenveenWorld : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Terrain the landscape is built around. Uses the active terrain when empty.")]
    [SerializeField] private Terrain terrain;
    [Tooltip("A URP Lit material used as a template for all generated materials (makes sure the shader is included in builds).")]
    [SerializeField] private Material baseMaterial;

    [Header("World")]
    [SerializeField] private int seed = 1910;
    [Tooltip("Half the width of the generated world, measured from the centre of the terrain.")]
    [SerializeField, Min(1000f)] private float worldHalfSize = 4500f;
    [Tooltip("Distance over which the outer ground blends from the terrain edge height to the flat polder height.")]
    [SerializeField, Min(20f)] private float edgeBlendDistance = 200f;
    [SerializeField, Range(512, 4096)] private int groundTextureSize = 2048;
    [SerializeField, Min(200f)] private float chunkSize = 750f;
    [Tooltip("Scale of houses, trees, mills and boats. Raise it if the buildings look too small next to the plane.")]
    [SerializeField, Min(0.1f)] private float objectScale = 1f;

    [Header("Heerenveen (town)")]
    [Tooltip("Town centre relative to the middle of the terrain's north (+Z) edge.")]
    [SerializeField] private Vector2 townOffset = new Vector2(150f, 620f);
    [SerializeField, Min(200f)] private float townLength = 760f;
    [SerializeField, Min(4f)] private float canalWidth = 18f;

    [Header("Countryside")]
    [SerializeField, Min(0)] private int extraFarms = 40;
    [SerializeField, Min(0)] private int windmills = 14;
    [SerializeField, Min(0)] private int treeClumps = 60;
    [SerializeField, Min(6f)] private float laneTreeSpacing = 14f;
    [SerializeField, Min(8f)] private float forestTreeSpacing = 22f;

    [Header("Performance")]
    [Tooltip("Small details (windows, streets, boats) and full-detail trees are drawn up to this distance.")]
    [SerializeField, Min(100f)] private float detailDistance = 800f;
    [Tooltip("Low-detail trees are drawn up to this distance. Keep it close to the fog end.")]
    [SerializeField, Min(200f)] private float farTreeDistance = 2800f;
    [SerializeField] private bool castShadows = true;
    [SerializeField] private bool previewInEditMode = true;
    [SerializeField, Range(0f, 60f)] private float sailSpeed = 20f;

    private const string RootName = "Heerenveen 1910 (generated)";

    // ------------------------------------------------------------------ materials

    private enum M
    {
        Ground, Brick, BrickDark, Plaster, RoofRed, RoofDark, Thatch, Trim, Glass, Door,
        Water, Quay, Paving, Leaf1, Leaf2, Leaf3, Trunk, Hull, Canvas, Sail, Count
    }

    private static readonly Color[] Colors =
    {
        Color.white,                       // Ground (textured)
        new Color(0.55f, 0.27f, 0.20f),    // Brick
        new Color(0.42f, 0.21f, 0.16f),    // BrickDark
        new Color(0.84f, 0.82f, 0.76f),    // Plaster
        new Color(0.56f, 0.26f, 0.17f),    // RoofRed
        new Color(0.19f, 0.20f, 0.22f),    // RoofDark (glazed tiles)
        new Color(0.36f, 0.32f, 0.22f),    // Thatch
        new Color(0.90f, 0.89f, 0.85f),    // Trim (white woodwork)
        new Color(0.07f, 0.08f, 0.09f),    // Glass
        new Color(0.17f, 0.25f, 0.20f),    // Door (dark green)
        new Color(0.24f, 0.31f, 0.31f),    // Water
        new Color(0.46f, 0.39f, 0.34f),    // Quay
        new Color(0.44f, 0.40f, 0.35f),    // Paving
        new Color(0.20f, 0.31f, 0.13f),    // Leaf1
        new Color(0.27f, 0.37f, 0.16f),    // Leaf2
        new Color(0.15f, 0.25f, 0.12f),    // Leaf3
        new Color(0.25f, 0.20f, 0.15f),    // Trunk
        new Color(0.12f, 0.11f, 0.10f),    // Hull (tarred wood)
        new Color(0.47f, 0.33f, 0.24f),    // Canvas (tanned barge sails)
        new Color(0.80f, 0.78f, 0.72f),    // Sail (mill sails)
    };

    private static readonly float[] Smoothness =
    {
        0f, 0.1f, 0.1f, 0.15f, 0.25f, 0.45f, 0f, 0.3f, 0.7f, 0.3f,
        0.9f, 0.1f, 0.05f, 0f, 0f, 0f, 0f, 0.2f, 0f, 0.05f
    };

    private enum Layer { Static, Near, Far }

    // ------------------------------------------------------------------ mesh batching

    private sealed class Batch
    {
        public readonly List<Vector3> Verts = new List<Vector3>(4096);
        public readonly List<Vector3> Normals = new List<Vector3>(4096);
        public readonly List<int>[] Tris = new List<int>[(int)M.Count];
        public Vector2Int Chunk;
    }

    private readonly Dictionary<long, Batch> batches = new Dictionary<long, Batch>();
    private Batch cur;

    // ------------------------------------------------------------------ state

    private readonly List<Object> generated = new List<Object>();
    private readonly List<Transform> sails = new List<Transform>();
    private readonly List<float> sailSpeeds = new List<float>();
    private Material[] materials;
    private GameObject root;
    private System.Random rng;
    private float S; // objectScale shortcut

    // world layout
    private Vector2 center;          // XZ centre of the world (centre of the terrain)
    private Rect terrainRect;        // XZ rect of the terrain
    private bool hasTerrain;
    private float baseY;             // height of the flat polder land
    private float waterY;
    private Vector2 town, townAxis, townRight;

    private sealed class Polyline
    {
        public readonly List<Vector2> Points = new List<Vector2>();
        public float Width;
        public bool InTown;
    }

    private readonly List<Polyline> canals = new List<Polyline>();
    private readonly List<Polyline> roads = new List<Polyline>();

    // simple spatial hash of occupied circles (houses, farms, trees...)
    private struct Occupied { public Vector2 P; public float R; public int Row; }
    private readonly List<Occupied> occupied = new List<Occupied>();
    private readonly Dictionary<long, List<int>> occupiedGrid = new Dictionary<long, List<int>>();
    private const float OccCell = 32f;

    // forest (Oranjewoud)
    private Vector2 forestCenter;
    private Vector2 forestRadii;

    private static Vector3[] sphereHigh;
    private static Vector3[] sphereLow;

#if UNITY_EDITOR
    private bool rebuildQueued;
#endif

    // ================================================================== lifecycle

    private void OnEnable()
    {
        if (!Application.isPlaying && !previewInEditMode)
            return;
        Build();
    }

    private void OnDisable()
    {
        Clear();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (Application.isPlaying || !isActiveAndEnabled || rebuildQueued || root == null)
            return;
        rebuildQueued = true;
        UnityEditor.EditorApplication.delayCall += () =>
        {
            rebuildQueued = false;
            if (this == null || !isActiveAndEnabled) return;
            Clear();
            if (previewInEditMode) Build();
        };
    }
#endif

    [ContextMenu("Regenerate")]
    private void Regenerate()
    {
        Clear();
        Build();
    }

    private void Update()
    {
        if (sails.Count == 0) return;
        float dt = Application.isPlaying ? Time.deltaTime : 0f;
        if (dt <= 0f) return;
        for (int i = 0; i < sails.Count; i++)
        {
            if (sails[i] != null)
                sails[i].Rotate(0f, 0f, sailSpeeds[i] * sailSpeed * dt, Space.Self);
        }
    }

    private void Clear()
    {
        for (int i = generated.Count - 1; i >= 0; i--)
        {
            if (generated[i] == null) continue;
            if (Application.isPlaying) Destroy(generated[i]);
            else DestroyImmediate(generated[i]);
        }
        generated.Clear();
        sails.Clear();
        sailSpeeds.Clear();
        sailMeshes.Clear();
        sailMaterials.Clear();
        root = null;
    }

    private T Track<T>(T obj) where T : Object
    {
        if (!Application.isPlaying)
            obj.hideFlags = HideFlags.DontSave;
        generated.Add(obj);
        return obj;
    }

    // ================================================================== build

    private void Build()
    {
        Clear();

        // remove leftovers (for example after a script reload in the editor)
        for (int guard = 0; guard < 8; guard++)
        {
            GameObject old = GameObject.Find(RootName);
            if (old == null) break;
            if (Application.isPlaying) { old.name = RootName + " (old)"; Destroy(old); }
            else DestroyImmediate(old);
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();
        rng = new System.Random(seed);
        S = objectScale;
        batches.Clear();
        canals.Clear();
        roads.Clear();
        occupied.Clear();
        occupiedGrid.Clear();
        EnsureSpheres();

        root = Track(new GameObject(RootName));
        root.transform.SetParent(null);
        root.isStatic = true;

        SetupLayout();
        CreateMaterials();

        BuildTown();
        BuildCountryside();
        BuildForest();
        BuildWater();

        int triangles = BuildGround();
        triangles += FlushBatches();

        if (Application.isPlaying)
            Debug.Log($"[HeerenveenWorld] Generated in {watch.ElapsedMilliseconds} ms, {triangles / 1000}k triangles in {batches.Count} batches.", this);
    }

    // ------------------------------------------------------------------ layout

    private void SetupLayout()
    {
        if (terrain == null) terrain = Terrain.activeTerrain;
        hasTerrain = terrain != null && terrain.terrainData != null;

        if (hasTerrain)
        {
            Vector3 p = terrain.GetPosition();
            Vector3 size = terrain.terrainData.size;
            terrainRect = new Rect(p.x, p.z, size.x, size.z);
            center = terrainRect.center;

            // flat polder height = average height along the terrain border
            float sum = 0f;
            int n = 0;
            for (int i = 0; i <= 64; i++)
            {
                float t = i / 64f;
                sum += TerrainHeight(new Vector2(Mathf.Lerp(terrainRect.xMin, terrainRect.xMax, t), terrainRect.yMin));
                sum += TerrainHeight(new Vector2(Mathf.Lerp(terrainRect.xMin, terrainRect.xMax, t), terrainRect.yMax));
                sum += TerrainHeight(new Vector2(terrainRect.xMin, Mathf.Lerp(terrainRect.yMin, terrainRect.yMax, t)));
                sum += TerrainHeight(new Vector2(terrainRect.xMax, Mathf.Lerp(terrainRect.yMin, terrainRect.yMax, t)));
                n += 4;
            }
            baseY = sum / n;
        }
        else
        {
            center = new Vector2(transform.position.x, transform.position.z);
            terrainRect = new Rect(center, Vector2.zero);
            baseY = transform.position.y;
        }

        waterY = baseY + 0.3f;

        // ---- town on the Compagnonsvaart, north of the terrain
        float north = hasTerrain ? terrainRect.yMax : center.y;
        town = new Vector2(center.x + townOffset.x, north + townOffset.y);
        float a = 8f * Mathf.Deg2Rad;
        townAxis = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
        townRight = new Vector2(townAxis.y, -townAxis.x);

        float H = worldHalfSize;
        float clearN = north + edgeBlendDistance + 60f;
        Vector2 ts = town - townAxis * townLength * 0.5f;
        Vector2 te = town + townAxis * townLength * 0.5f;
        float westZ = Mathf.Max(clearN, ts.y - 40f);

        var main = new Polyline { Width = canalWidth };
        main.Points.Add(new Vector2(center.x - H - 20f, westZ));
        main.Points.Add(new Vector2(ts.x - 140f, westZ));
        if (ts.y > westZ + 1f) main.Points.Add(ts);
        main.Points.Add(te);
        main.Points.Add(te + townAxis * 600f + townRight * 250f);
        main.Points.Add(new Vector2(te.x + 900f, center.y + H + 20f));
        canals.Add(main);

        // side canal branching east in the town centre
        var side = new Polyline { Width = canalWidth * 0.7f };
        Vector2 junction = town + townAxis * 110f;
        side.Points.Add(junction);
        side.Points.Add(junction + townRight * 900f);
        side.Points.Add(new Vector2(center.x + H + 20f, junction.y + townRight.y * 900f - 150f));
        canals.Add(side);

        // straight peat canals ("vaarten") further out
        float ex = Mathf.Max(terrainRect.width, 0f) * 0.5f + edgeBlendDistance;
        float ez = Mathf.Max(terrainRect.height, 0f) * 0.5f + edgeBlendDistance;
        AddStraightCanal(new Vector2(center.x - ex - 1500f, center.y - H - 20f), new Vector2(center.x - ex - 1500f, westZ), 9f);
        AddStraightCanal(new Vector2(center.x - H - 20f, center.y - ez - 1300f), new Vector2(center.x + H + 20f, center.y - ez - 1300f), 9f);
        {
            // runs south from the side canal
            Vector2 s1 = side.Points[1], s2 = side.Points[2];
            float x = center.x + ex + 2300f;
            float t = Mathf.Clamp01((x - s1.x) / Mathf.Max(1f, s2.x - s1.x));
            AddStraightCanal(new Vector2(x, center.y - H - 20f), new Vector2(x, Mathf.Lerp(s1.y, s2.y, t)), 8f);
        }
        AddStraightCanal(new Vector2(center.x - H - 20f, town.y + 1400f), new Vector2(center.x + H + 20f, town.y + 1400f), 8f);

        // ---- Oranjewoud, south-east of the town
        forestCenter = new Vector2(center.x + ex + 1100f, center.y + 150f);
        forestRadii = new Vector2(650f, 800f);

        // ---- lanes leaving the town
        AddRoad(town - townRight * 60f, new Vector2(center.x - H - 20f, town.y + 120f));
        AddRoad(town + townAxis * 260f + townRight * 60f, new Vector2(center.x + H + 20f, town.y + 520f));
        AddRoad(te - townRight * 60f, new Vector2(te.x - 1700f, center.y + H + 20f));
        AddRoad(new[]
        {
            town + townRight * 120f - townAxis * 120f,
            new Vector2(center.x + ex + 280f, north + 120f),
            new Vector2(center.x + ex + 280f, center.y - H - 20f)
        });
        AddRoad(new[]
        {
            new Vector2(center.x - ex - 300f, westZ + 120f),
            new Vector2(center.x - ex - 300f, center.y - H - 20f)
        });
        AddRoad(new Vector2(center.x - H - 20f, center.y - ez - 500f), new Vector2(center.x + ex + 280f, center.y - ez - 500f));
    }

    private void AddStraightCanal(Vector2 a, Vector2 b, float width)
    {
        var c = new Polyline { Width = width };
        c.Points.Add(a);
        c.Points.Add(b);
        canals.Add(c);
    }

    private void AddRoad(Vector2 a, Vector2 b) => AddRoad(new[] { a, b });

    private void AddRoad(Vector2[] pts)
    {
        var r = new Polyline { Width = 6f };
        r.Points.AddRange(pts);
        roads.Add(r);
    }

    private float TerrainHeight(Vector2 p)
    {
        if (!hasTerrain) return baseY;
        return terrain.SampleHeight(new Vector3(p.x, 0f, p.y)) + terrain.GetPosition().y;
    }

    private bool InWorld(Vector2 p, float margin)
    {
        return Mathf.Abs(p.x - center.x) < worldHalfSize - margin && Mathf.Abs(p.y - center.y) < worldHalfSize - margin;
    }

    private bool NearTerrain(Vector2 p, float margin)
    {
        if (!hasTerrain) return false;
        float m = edgeBlendDistance + margin;
        return p.x > terrainRect.xMin - m && p.x < terrainRect.xMax + m && p.y > terrainRect.yMin - m && p.y < terrainRect.yMax + m;
    }

    private bool NearCanal(Vector2 p, float margin)
    {
        foreach (var c in canals)
            if (DistanceToPolyline(p, c) < c.Width * 0.5f + margin) return true;
        return false;
    }

    private bool NearRoad(Vector2 p, float margin)
    {
        foreach (var r in roads)
            if (DistanceToPolyline(p, r) < r.Width * 0.5f + margin) return true;
        return false;
    }

    private bool InTownArea(Vector2 p, float margin)
    {
        Vector2 d = p - town;
        float along = Vector2.Dot(d, townAxis);
        float across = Vector2.Dot(d, townRight);
        return Mathf.Abs(along) < townLength * 0.5f + 80f + margin && across > -330f - margin && across < 400f + margin;
    }

    private float ForestValue(Vector2 p)
    {
        Vector2 d = p - forestCenter;
        float ang = Mathf.Atan2(d.y, d.x);
        float wobble = 1f + 0.12f * Mathf.Sin(ang * 3f + seed) + 0.07f * Mathf.Sin(ang * 7f + seed * 0.3f);
        float e = new Vector2(d.x / forestRadii.x, d.y / forestRadii.y).magnitude / wobble;
        return 1f - e; // > 0 inside
    }

    private bool OnForestLane(Vector2 p)
    {
        // Oranjewoud's star-shaped lanes
        Vector2 d = p - forestCenter;
        for (int i = 0; i < 4; i++)
        {
            float a = i * Mathf.PI / 4f + 0.2f;
            Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            float off = Mathf.Abs(d.x * dir.y - d.y * dir.x);
            if (off < 7f) return true;
        }
        return false;
    }

    private static float DistanceToPolyline(Vector2 p, Polyline line)
    {
        float best = float.MaxValue;
        for (int i = 0; i < line.Points.Count - 1; i++)
            best = Mathf.Min(best, DistanceToSegment(p, line.Points[i], line.Points[i + 1]));
        return best;
    }

    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
        return Vector2.Distance(p, a + ab * t);
    }

    // ------------------------------------------------------------------ occupancy

    private static long CellKey(int x, int z) => ((long)x << 32) ^ (uint)z;

    private bool IsFree(Vector2 p, float r, int row = -1, int alsoIgnore = int.MinValue)
    {
        int x0 = Mathf.FloorToInt((p.x - r) / OccCell) - 1, x1 = Mathf.FloorToInt((p.x + r) / OccCell) + 1;
        int z0 = Mathf.FloorToInt((p.y - r) / OccCell) - 1, z1 = Mathf.FloorToInt((p.y + r) / OccCell) + 1;
        for (int x = x0; x <= x1; x++)
        for (int z = z0; z <= z1; z++)
        {
            if (!occupiedGrid.TryGetValue(CellKey(x, z), out var list)) continue;
            foreach (int i in list)
            {
                Occupied o = occupied[i];
                if ((row >= 0 && o.Row == row) || o.Row == alsoIgnore) continue;
                float rr = o.R + r;
                if ((o.P - p).sqrMagnitude < rr * rr) return false;
            }
        }
        return true;
    }

    private void Occupy(Vector2 p, float r, int row = -1)
    {
        occupied.Add(new Occupied { P = p, R = r, Row = row });
        long key = CellKey(Mathf.FloorToInt(p.x / OccCell), Mathf.FloorToInt(p.y / OccCell));
        if (!occupiedGrid.TryGetValue(key, out var list))
            occupiedGrid[key] = list = new List<int>();
        list.Add(occupied.Count - 1);
    }

    // ------------------------------------------------------------------ random helpers

    private float Rand() => (float)rng.NextDouble();
    private float Rand(float min, float max) => min + (max - min) * (float)rng.NextDouble();
    private bool Chance(float p) => rng.NextDouble() < p;

    private static uint Hash(int a, int b, int c)
    {
        unchecked
        {
            uint h = (uint)a * 0x8da6b343u ^ (uint)b * 0xd8163841u ^ (uint)c * 0xcb1ab31fu;
            h ^= h >> 15; h *= 0x2c1b3c6du; h ^= h >> 12; h *= 0x297a2d39u; h ^= h >> 15;
            return h;
        }
    }

    private static Vector3 V3(Vector2 p, float y) => new Vector3(p.x, y, p.y);
    private static float Yaw(Vector2 dir) => Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
    private static Vector2 Perp(Vector2 d) => new Vector2(d.y, -d.x);

    // ================================================================== town

    private void BuildTown()
    {
        Polyline main = canals[0];
        Polyline side = canals[1];
        float quay = 9f * S;
        float setback = main.Width * 0.5f + quay;
        Vector2 ts = town - townAxis * townLength * 0.5f;
        Vector2 te = town + townAxis * townLength * 0.5f;

        // reserve the canal itself
        foreach (var c in new[] { main, side })
            for (int i = 0; i < c.Points.Count - 1; i++)
            {
                Vector2 a = c.Points[i], b = c.Points[i + 1];
                float len = Vector2.Distance(a, b);
                for (float t = 0f; t <= len; t += 6f)
                    Occupy(Vector2.Lerp(a, b, t / len), c.Width * 0.5f + 2f);
            }

        // church on a square east of the canal, south of the junction
        Vector2 churchPos = town + townRight * (setback + 45f * S) - townAxis * 70f;
        Church(churchPos, Yaw(-townRight));
        Occupy(churchPos, 30f * S);
        Vector2 square = town + townRight * (setback + 22f * S) + townAxis * 10f;
        Occupy(square, 16f * S);
        Begin(square, Layer.Near);
        Paving(square - townAxis * 55f * S, square + townAxis * 25f * S, 36f * S);

        // reserve cross streets (with bridges over the canal) and back streets first
        var streets = new List<StreetSpec>();
        float[] crossAt = { -0.36f, -0.12f, 0.32f };
        var bridges = new List<Vector2>();
        int streetId = 100000;
        for (int k = 0; k < crossAt.Length; k++)
        {
            Vector2 c = town + townAxis * (crossAt[k] * townLength);
            bridges.Add(c);
            Bridge(c, townRight, main.Width, k != 1);
            foreach (int s in new[] { -1, 1 })
                streets.Add(ReserveStreet(c + townRight * s * (main.Width * 0.5f + 1f), c + townRight * s * Rand(230f, 300f), streetId++));
        }
        foreach (int s in new[] { -1, 1 })
        {
            Vector2 off = townRight * s * (setback + 75f * S);
            streets.Add(ReserveStreet(town - townAxis * townLength * 0.38f + off, town + townAxis * townLength * 0.42f + off, streetId++));
        }

        int row = 0;

        // quays along the main canal through the town
        foreach (int s in new[] { -1, 1 })
        {
            Vector2 n = townRight * s;
            Begin(town, Layer.Static);
            Vector2 qa = ts + n * (main.Width * 0.5f + 0.3f);
            Vector2 qb = te + n * (main.Width * 0.5f + 0.3f);
            QuayWall(qa, qb);
            Begin(town, Layer.Near);
            PavingChunks(ts + n * (main.Width * 0.5f + quay * 0.5f), te + n * (main.Width * 0.5f + quay * 0.5f), quay);
            HouseRow(ts + n * setback, te + n * setback, n, row++, 0.92f, true);
            if (s < 0) QuayTrees(ts + n * (main.Width * 0.5f + 3.5f * S), town + n * (main.Width * 0.5f + 3.5f * S));
        }

        // side canal: quays and houses
        {
            Vector2 a = side.Points[0], b = side.Points[1];
            Vector2 dir = (b - a).normalized;
            Vector2 n0 = Perp(dir);
            float sb = side.Width * 0.5f + quay;
            Vector2 start = a + dir * (setback + 4f);
            Vector2 end = a + dir * 420f;
            foreach (int s in new[] { -1, 1 })
            {
                Vector2 n = n0 * s;
                Begin(start, Layer.Static);
                QuayWall(start + n * (side.Width * 0.5f + 0.3f), end + n * (side.Width * 0.5f + 0.3f));
                Begin(start, Layer.Near);
                PavingChunks(start + n * (side.Width * 0.5f + quay * 0.5f), end + n * (side.Width * 0.5f + quay * 0.5f), quay);
                HouseRow(start + n * sb, end + n * sb, n, row++, 0.85f, true);
            }
            QuayTrees(start + n0 * (side.Width * 0.5f + 3.5f * S), end + n0 * (side.Width * 0.5f + 3.5f * S));
        }

        // houses along the streets
        foreach (var st in streets)
        {
            Street(st, row, row + 1);
            row += 2;
        }

        // barges moored along the canal
        foreach (int s in new[] { -1, 1 })
        {
            for (float t = 30f; t < townLength - 30f; t += Rand(35f, 90f))
            {
                Vector2 p = ts + townAxis * t + townRight * s * (main.Width * 0.5f - 2.6f * S);
                if (Vector2.Distance(p, town + townAxis * 110f) < 25f) continue;
                bool atBridge = false;
                foreach (var br in bridges) atBridge |= Vector2.Distance(p, br) < 16f * S;
                if (atBridge) continue;
                Barge(p, Yaw(townAxis) + (Chance(0.5f) ? 180f : 0f), Chance(0.4f));
            }
        }
        for (int i = 0; i < 4; i++)
        {
            Vector2 p = side.Points[0] + (side.Points[1] - side.Points[0]).normalized * Rand(80f, 600f);
            Barge(p + Perp((side.Points[1] - side.Points[0]).normalized) * (side.Width * 0.5f - 2.6f * S),
                Yaw(side.Points[1] - side.Points[0]), Chance(0.5f));
        }

        // a corn mill on each end of town (seen in the background of the postcard)
        Vector2 m1 = ts - townAxis * 70f + townRight * 70f;
        Vector2 m2 = te + townAxis * 40f - townRight * 90f;
        Windmill(m1, true);
        Windmill(m2, true);
    }

    private struct StreetSpec { public Vector2 A, B; public int Id; }

    /// <summary>Keeps the street free of houses that are placed before it is built.</summary>
    private StreetSpec ReserveStreet(Vector2 a, Vector2 b, int id)
    {
        float len = Vector2.Distance(a, b);
        for (float t = 0f; t <= len; t += 4f)
            Occupy(Vector2.Lerp(a, b, t / len), 4.2f * S, id);
        return new StreetSpec { A = a, B = b, Id = id };
    }

    private void Street(StreetSpec st, int rowA, int rowB)
    {
        Vector2 a = st.A, b = st.B;
        Vector2 dir = (b - a).normalized;
        Vector2 n = Perp(dir);
        float half = 4f * S;
        PavingChunks(a, b, half * 2f);
        HouseRow(a + n * half, b + n * half, n, rowA, 0.8f, false, st.Id);
        HouseRow(a - n * half, b - n * half, -n, rowB, 0.8f, false, st.Id);
    }

    /// <summary>A row of houses along the line a-b, facing -n (towards the street or canal).</summary>
    private void HouseRow(Vector2 a, Vector2 b, Vector2 n, int row, float density, bool canalSide, int street = int.MinValue)
    {
        Vector2 dir = (b - a).normalized;
        float len = Vector2.Distance(a, b);
        float t = Rand(0f, 4f);
        while (t < len)
        {
            HouseSpec spec = RandomHouse(canalSide);
            float w = spec.Width * S, d = spec.Depth * S;
            if (t + w > len) break;

            Vector2 pos = a + dir * (t + w * 0.5f) + n * (d * 0.5f + 0.5f);
            float r = Mathf.Max(w, d) * 0.5f;
            bool ok = Chance(density) && InWorld(pos, 50f) && !NearTerrain(pos, 0f) && IsFree(pos, r * 0.92f, row, street);

            if (ok)
            {
                Occupy(pos, r, row);
                House(pos, Yaw(-n), spec);

                // a tree or two in the back garden
                if (Chance(0.45f))
                {
                    Vector2 tp = pos + n * (d * 0.5f + Rand(5f, 12f) * S) + dir * Rand(-w * 0.4f, w * 0.4f);
                    if (IsFree(tp, 3f * S) && !NearCanal(tp, 3f))
                    {
                        Occupy(tp, 3f * S);
                        Tree(tp, Rand(9f, 15f));
                    }
                }
                t += w + (Chance(0.15f) ? Rand(4f, 12f) : Rand(0f, 1.2f)) * S;
            }
            else
            {
                t += Rand(3f, 8f) * S;
            }
        }
    }

    private void QuayTrees(Vector2 a, Vector2 b)
    {
        float len = Vector2.Distance(a, b);
        for (float t = 8f; t < len; t += Rand(13f, 20f) * S)
        {
            Vector2 p = Vector2.Lerp(a, b, t / len);
            if (Chance(0.75f) && IsFree(p, 1f))
                Tree(p, Rand(13f, 18f));
        }
    }

    private struct HouseSpec
    {
        public float Width, Depth, Eave, Roof;
        public bool RidgeAcross, Hip;
        public M Wall, RoofMat;
    }

    private HouseSpec RandomHouse(bool canalSide)
    {
        var h = new HouseSpec();
        float roll = Rand();
        h.Wall = roll < 0.55f ? M.Brick : roll < 0.85f ? M.BrickDark : M.Plaster;
        h.RoofMat = Chance(0.6f) ? M.RoofRed : M.RoofDark;

        float kind = Rand();
        if (kind < (canalSide ? 0.14f : 0.06f))
        {
            // herenhuis: large, two/three storeys, hipped roof
            h.Hip = true;
            h.Width = Rand(11f, 15f);
            h.Depth = Rand(11f, 13f);
            h.Eave = Rand(7f, 8.5f);
            h.Roof = Rand(4f, 5f);
            if (Chance(0.5f)) h.Wall = M.Plaster;
        }
        else if (kind < 0.4f)
        {
            // eaves to the street
            h.RidgeAcross = true;
            h.Width = Rand(8f, 12f);
            h.Depth = Rand(7f, 9f);
            h.Eave = Rand(3.4f, 5f);
            h.Roof = h.Depth * 0.5f * Rand(1.1f, 1.35f);
        }
        else
        {
            // narrow gable-fronted house
            h.Width = Rand(5.5f, 8f);
            h.Depth = Rand(9f, 13f);
            h.Eave = Chance(0.35f) ? Rand(6f, 7.2f) : Rand(3.6f, 4.8f);
            h.Roof = h.Width * 0.5f * Rand(1.15f, 1.4f);
        }
        return h;
    }

    private void House(Vector2 pos, float yaw, HouseSpec h)
    {
        Matrix4x4 tr = Matrix4x4.TRS(V3(pos, baseY), Quaternion.Euler(0f, yaw, 0f), Vector3.one * S);
        float w = h.Width, d = h.Depth;

        Begin(pos, Layer.Static);
        Box(h.Wall, tr * Matrix4x4.Translate(new Vector3(0f, h.Eave * 0.5f - 0.5f, 0f)), new Vector3(w, h.Eave + 1f, d), false);

        Matrix4x4 roofTr = tr * Matrix4x4.Translate(new Vector3(0f, h.Eave, 0f));
        if (h.Hip && w > d)
            HipRoof(h.RoofMat, roofTr * Matrix4x4.Rotate(Quaternion.Euler(0f, 90f, 0f)), d, w, h.Roof, Mathf.Max(0f, w * 0.5f - d * 0.35f), 0.4f);
        else if (h.Hip)
            HipRoof(h.RoofMat, roofTr, w, d, h.Roof, Mathf.Max(0f, d * 0.5f - w * 0.35f), 0.4f);
        else if (h.RidgeAcross)
            GableRoof(h.RoofMat, h.Wall, roofTr * Matrix4x4.Rotate(Quaternion.Euler(0f, 90f, 0f)), d, w, h.Roof, 0.35f);
        else
            GableRoof(h.RoofMat, h.Wall, roofTr, w, d, h.Roof, 0.3f);

        // details: chimney, windows, door
        Begin(pos, Layer.Near);
        if (Chance(0.7f))
        {
            float cx = h.RidgeAcross ? Rand(-w * 0.3f, w * 0.3f) : 0f;
            float cz = h.RidgeAcross ? -0.4f : Rand(-d * 0.3f, d * 0.2f);
            Box(M.BrickDark, tr * Matrix4x4.Translate(new Vector3(cx, h.Eave + h.Roof * 0.8f, cz)), new Vector3(0.8f, 2.2f, 0.8f), false);
        }

        float front = d * 0.5f;
        int floors = h.Eave > 6.5f ? 3 : h.Eave > 5.2f ? 2 : 1;
        float floorH = h.Eave / floors;
        int cols = Mathf.Max(2, Mathf.FloorToInt(w / 2.3f));
        int doorCol = rng.Next(cols);
        for (int f = 0; f < floors; f++)
        for (int c = 0; c < cols; c++)
        {
            float x = -w * 0.5f + w * (c + 0.5f) / cols;
            float y0 = f * floorH + floorH * 0.28f;
            if (f == 0 && c == doorCol)
            {
                FacadeRect(M.Door, tr, x - 0.55f, 0.05f, x + 0.55f, Mathf.Min(2.3f, floorH * 0.85f), front + 0.06f);
                continue;
            }
            float ww = Mathf.Min(0.75f, w / cols * 0.3f);
            float y1 = y0 + floorH * 0.5f;
            FacadeRect(M.Trim, tr, x - ww - 0.12f, y0 - 0.12f, x + ww + 0.12f, y1 + 0.12f, front + 0.04f);
            FacadeRect(M.Glass, tr, x - ww, y0, x + ww, y1, front + 0.08f);
        }

        // attic window in a front gable
        if (!h.Hip && !h.RidgeAcross)
        {
            float y0 = h.Eave + h.Roof * 0.18f;
            FacadeRect(M.Trim, tr, -0.6f, y0 - 0.1f, 0.6f, y0 + 1.2f, front + 0.04f);
            FacadeRect(M.Glass, tr, -0.48f, y0, 0.48f, y0 + 1.0f, front + 0.08f);
        }
    }

    private void Church(Vector2 pos, float yaw)
    {
        Matrix4x4 tr = Matrix4x4.TRS(V3(pos, baseY), Quaternion.Euler(0f, yaw, 0f), Vector3.one * S);
        Begin(pos, Layer.Static);
        // nave
        Box(M.BrickDark, tr * Matrix4x4.Translate(new Vector3(0f, 4.5f, -4f)), new Vector3(13f, 10f, 28f), false);
        GableRoof(M.RoofDark, M.BrickDark, tr * Matrix4x4.Translate(new Vector3(0f, 9.5f, -4f)), 13f, 28f, 8.5f, 0.4f);
        // tower at the front
        Box(M.Brick, tr * Matrix4x4.Translate(new Vector3(0f, 11f, 13.5f)), new Vector3(7.5f, 23f, 7.5f), false);
        Box(M.Brick, tr * Matrix4x4.Translate(new Vector3(0f, 23f, 13.5f)), new Vector3(8.3f, 1f, 8.3f), false);
        Frustum(M.RoofDark, tr * Matrix4x4.Translate(new Vector3(0f, 23.5f, 13.5f)), 5.6f, 0f, 15f, 8, false);

        Begin(pos, Layer.Near);
        // tall windows on both sides of the nave
        for (int i = 0; i < 4; i++)
        {
            float z = -14f + i * 6.5f;
            foreach (int s in new[] { -1, 1 })
            {
                Matrix4x4 sideTr = tr * Matrix4x4.TRS(new Vector3(0f, 0f, z), Quaternion.Euler(0f, 90f * s, 0f), Vector3.one);
                FacadeRect(M.Glass, sideTr, -1f, 2.5f, 1f, 7.5f, 6.58f);
            }
        }
        FacadeRect(M.Door, tr, -1.2f, 0f, 1.2f, 3.2f, 17.3f);
        // clock faces
        FacadeRect(M.Trim, tr, -1.2f, 18.5f, 1.2f, 20.9f, 17.3f);
    }

    private void Bridge(Vector2 c, Vector2 across, float span, bool drawbridge)
    {
        Matrix4x4 tr = Matrix4x4.TRS(V3(c, baseY), Quaternion.Euler(0f, Yaw(across), 0f), Vector3.one);
        float len = span + 6f;
        Begin(c, Layer.Static);
        Box(M.Hull, tr * Matrix4x4.Translate(new Vector3(0f, 1.05f, 0f)), new Vector3(5.5f * S, 0.4f, len), false);
        Box(M.Trim, tr * Matrix4x4.Translate(new Vector3(-2.7f * S, 1.75f, 0f)), new Vector3(0.15f, 1f, len), false);
        Box(M.Trim, tr * Matrix4x4.Translate(new Vector3(2.7f * S, 1.75f, 0f)), new Vector3(0.15f, 1f, len), false);

        if (!drawbridge) return;

        // Frisian drawbridge: white posts with a beam on top and two long arms over the deck
        float bank = span * 0.5f + 1.5f;
        float top = 1.2f + 7.5f * S;
        float x = 3.3f * S;
        foreach (int s in new[] { -1, 1 })
        {
            Beam(M.Trim, V3(c + across * bank + Perp(across) * x * s, baseY + 1f), V3(c + across * bank + Perp(across) * x * s, top), 0.4f * S);
            Vector3 armBack = V3(c + across * (bank + 6f * S) + Perp(across) * x * s, top + 0.3f);
            Vector3 armFront = V3(c - across * (span * 0.5f - 1f) + Perp(across) * x * s, top + 0.3f);
            Beam(M.Trim, armBack, armFront, 0.35f * S);
            Beam(M.Hull, armFront, V3(c - across * (span * 0.5f - 0.5f) + Perp(across) * (x - 0.6f * S) * s, baseY + 1.3f), 0.08f);
        }
        Beam(M.Trim, V3(c + across * bank + Perp(across) * x, top), V3(c + across * bank - Perp(across) * x, top), 0.4f * S);
        Beam(M.Trim, V3(c - across * (span * 0.5f - 1f) + Perp(across) * x, top + 0.3f), V3(c - across * (span * 0.5f - 1f) - Perp(across) * x, top + 0.3f), 0.3f * S);
        Beam(M.Trim, V3(c + across * (bank + 6f * S) + Perp(across) * x, top + 0.3f), V3(c + across * (bank + 6f * S) - Perp(across) * x, top + 0.3f), 0.5f * S);
    }

    private void Barge(Vector2 pos, float yaw, bool sailUp)
    {
        Matrix4x4 tr = Matrix4x4.TRS(V3(pos, waterY), Quaternion.Euler(0f, yaw, 0f), Vector3.one * S);
        Begin(pos, Layer.Near);
        // tjalk: rounded flat-bottomed hull
        Vector2[] outline =
        {
            new Vector2(0f, 9f), new Vector2(1.6f, 7.8f), new Vector2(2f, 5f), new Vector2(2f, -5.5f),
            new Vector2(1.6f, -7.2f), new Vector2(0f, -7.8f), new Vector2(-1.6f, -7.2f), new Vector2(-2f, -5.5f),
            new Vector2(-2f, 5f), new Vector2(-1.6f, 7.8f)
        };
        Loft(M.Hull, tr, outline, -0.5f, 0.75f, 1.1f, 1f, true);
        Box(M.Trim, tr * Matrix4x4.Translate(new Vector3(0f, 1.6f, -5.6f)), new Vector3(2.4f, 1f, 2.6f), false);
        Box(M.Canvas, tr * Matrix4x4.Translate(new Vector3(0f, 1.3f, 0.5f)), new Vector3(3f, 0.4f, 8f), false);

        // mast and sail
        Vector3 mastFoot = tr.MultiplyPoint3x4(new Vector3(0f, 1f, 4.2f));
        Vector3 mastTop = tr.MultiplyPoint3x4(new Vector3(0f, 13f, 4.2f));
        Beam(M.Hull, mastFoot, mastTop, 0.25f * S);
        if (sailUp)
        {
            Vector3 a = tr.MultiplyPoint3x4(new Vector3(0.15f, 2.2f, 3.9f));
            Vector3 b = tr.MultiplyPoint3x4(new Vector3(0.15f, 12f, 3.9f));
            Vector3 c = tr.MultiplyPoint3x4(new Vector3(0.15f, 9f, -2.5f));
            Vector3 d = tr.MultiplyPoint3x4(new Vector3(0.15f, 2.4f, -3.5f));
            Vector3 side = tr.MultiplyVector(Vector3.right);
            Quad(M.Canvas, a, b, c, d, side);
            Quad(M.Canvas, a, d, c, b, -side);
        }
        else
        {
            Beam(M.Canvas, tr.MultiplyPoint3x4(new Vector3(0f, 2.4f, 4f)), tr.MultiplyPoint3x4(new Vector3(0f, 2.6f, -3.5f)), 0.5f * S);
        }
    }

    // ================================================================== countryside

    private void BuildCountryside()
    {
        // tree-lined lanes with farms along them
        foreach (var r in roads)
        {
            for (int i = 0; i < r.Points.Count - 1; i++)
            {
                Vector2 a = r.Points[i], b = r.Points[i + 1];
                Vector2 dir = (b - a).normalized;
                Vector2 n = Perp(dir);
                float len = Vector2.Distance(a, b);

                for (float t = 0f; t < len; t += laneTreeSpacing * Rand(0.85f, 1.15f))
                {
                    foreach (int s in new[] { -1, 1 })
                    {
                        Vector2 p = a + dir * t + n * s * 6.5f;
                        if (!LandOk(p, 6f) || InTownArea(p, 0f) || ForestValue(p) > 0f) continue;
                        if (Chance(0.08f)) continue; // gaps
                        Tree(p, Rand(11f, 16f));
                    }
                }

                // farms along the lane
                for (float t = Rand(80f, 250f); t < len; t += Rand(220f, 480f))
                {
                    int s = Chance(0.5f) ? 1 : -1;
                    Vector2 p = a + dir * t + n * s * 22f * S;
                    TryFarm(p, -n * s);
                }

                // bridges where the lane crosses a canal
                foreach (var c in canals)
                    for (int k = 0; k < c.Points.Count - 1; k++)
                        if (SegmentIntersect(a, b, c.Points[k], c.Points[k + 1], out Vector2 hit) && !InTownArea(hit, 0f))
                            Bridge(hit, dir, c.Width, false);
            }
        }

        // farms scattered over the land, each on its own yard with trees
        for (int i = 0, tries = 0; i < extraFarms && tries < extraFarms * 30; tries++)
        {
            Vector2 p = RandomLandPoint();
            float a = rng.Next(4) * 90f + Rand(-4f, 4f);
            Vector2 facing = new Vector2(Mathf.Sin(a * Mathf.Deg2Rad), Mathf.Cos(a * Mathf.Deg2Rad));
            if (TryFarm(p, facing)) i++;
        }

        // polder mills along the canals
        int mills = 0;
        for (int tries = 0; mills < windmills && tries < windmills * 40; tries++)
        {
            Polyline c = canals[rng.Next(canals.Count)];
            int k = rng.Next(c.Points.Count - 1);
            Vector2 a = c.Points[k], b = c.Points[k + 1];
            Vector2 n = Perp((b - a).normalized) * (Chance(0.5f) ? 1f : -1f);
            Vector2 p = Vector2.Lerp(a, b, Rand(0.05f, 0.95f)) + n * (c.Width * 0.5f + 9f * S);
            if (!LandOk(p, 12f) || InTownArea(p, 120f) || ForestValue(p) > -0.1f || !IsFree(p, 12f * S)) continue;
            Occupy(p, 12f * S);
            Windmill(p, false);
            mills++;
        }

        // tree clumps and hedgerow fragments in the fields
        for (int i = 0, tries = 0; i < treeClumps && tries < treeClumps * 20; tries++)
        {
            Vector2 p = RandomLandPoint();
            if (InTownArea(p, 50f) || ForestValue(p) > -0.05f) continue;
            i++;
            int count = rng.Next(3, 10);
            bool line = Chance(0.5f);
            Vector2 dir = Chance(0.5f) ? Vector2.right : Vector2.up;
            for (int k = 0; k < count; k++)
            {
                Vector2 q = line ? p + dir * k * 11f : p + new Vector2(Rand(-14f, 14f), Rand(-14f, 14f));
                if (LandOk(q, 4f) && IsFree(q, 2.5f))
                {
                    Occupy(q, 2.5f);
                    Tree(q, Rand(9f, 16f));
                }
            }
        }
    }

    private bool LandOk(Vector2 p, float margin)
    {
        return InWorld(p, 40f) && !NearTerrain(p, margin) && !NearCanal(p, margin);
    }

    private Vector2 RandomLandPoint()
    {
        for (int i = 0; i < 50; i++)
        {
            Vector2 p = center + new Vector2(Rand(-1f, 1f), Rand(-1f, 1f)) * (worldHalfSize - 100f);
            if (LandOk(p, 30f) && !NearRoad(p, 15f)) return p;
        }
        return center + Vector2.one * worldHalfSize * 2f; // outside, will be rejected
    }

    private bool TryFarm(Vector2 p, Vector2 facing)
    {
        float r = 30f * S;
        if (!LandOk(p, 20f) || InTownArea(p, 60f) || ForestValue(p) > -0.05f || NearRoad(p, 8f) || !IsFree(p, r)) return false;
        Vector2 back = p - facing * 22f * S;
        if (!LandOk(back, 20f) || NearRoad(back, 8f) || !IsFree(back, r)) return false;

        Occupy(p, r);
        Occupy(back, r);
        Farm(p, facing);
        return true;
    }

    /// <summary>Frisian "stelp" farm: front house plus a big barn under one pyramid roof, with trees around the yard.</summary>
    private void Farm(Vector2 pos, Vector2 facing)
    {
        float yaw = Yaw(facing);
        Matrix4x4 tr = Matrix4x4.TRS(V3(pos, baseY), Quaternion.Euler(0f, yaw, 0f), Vector3.one * S);
        float barn = Rand(18f, 24f);
        float houseW = Rand(8f, 10f), houseD = Rand(8f, 11f);
        M roof = Chance(0.6f) ? M.Thatch : M.RoofDark;
        M wall = Chance(0.7f) ? M.Brick : M.BrickDark;

        Begin(pos, Layer.Static);
        // front house (gable to the road)
        Box(wall, tr * Matrix4x4.Translate(new Vector3(0f, 1.6f, 0f)), new Vector3(houseW, 4.2f, houseD), false);
        GableRoof(Chance(0.5f) ? M.RoofRed : M.RoofDark, wall, tr * Matrix4x4.Translate(new Vector3(0f, 3.7f, 0f)), houseW, houseD, houseW * 0.65f, 0.3f);
        // neck + barn
        float bz = -houseD * 0.5f - barn * 0.5f + 1f;
        Box(wall, tr * Matrix4x4.Translate(new Vector3(0f, 1.2f, bz)), new Vector3(barn, 3.4f, barn), false);
        HipRoof(roof, tr * Matrix4x4.Translate(new Vector3(0f, 2.9f, bz)), barn, barn, barn * 0.55f, 0f, 0.5f);

        Begin(pos, Layer.Near);
        float front = houseD * 0.5f;
        FacadeRect(M.Door, tr, -0.5f, 0f, 0.5f, 2.1f, front + 0.06f);
        foreach (float x in new[] { -houseW * 0.3f, houseW * 0.3f })
        {
            FacadeRect(M.Trim, tr, x - 0.75f, 0.9f, x + 0.75f, 2.5f, front + 0.04f);
            FacadeRect(M.Glass, tr, x - 0.6f, 1.0f, x + 0.6f, 2.4f, front + 0.08f);
        }

        // yard trees in a U around the barn
        Vector2 right = Perp(facing);
        int trees = rng.Next(6, 13);
        for (int i = 0; i < trees; i++)
        {
            float u = Rand(-1f, 1f);
            Vector2 p;
            float half = barn * 0.5f + 7f;
            if (Chance(0.6f)) p = pos + right * Mathf.Sign(u) * half * S - facing * (Rand(0f, barn + houseD)) * S;
            else p = pos + right * u * half * S - facing * (houseD + barn + 6f) * S;
            if (LandOk(p, 3f) && !NearRoad(p, 2f))
                Tree(p, Rand(10f, 16f));
        }
    }

    private void Windmill(Vector2 pos, bool cornMill)
    {
        // all mills turn their caps into the same (south-west) wind
        Vector2 wind = new Vector2(-0.7f, -0.7f).normalized;
        float yaw = Yaw(wind);
        float baseH = cornMill ? 6f : 1.2f;
        float bodyH = cornMill ? 13f : 10f;
        float r0 = cornMill ? 4.6f : 3.8f;
        float r1 = cornMill ? 2.6f : 2.2f;

        Matrix4x4 tr = Matrix4x4.TRS(V3(pos, baseY), Quaternion.Euler(0f, yaw, 0f), Vector3.one * S);
        Begin(pos, Layer.Static);
        Frustum(M.Brick, tr, r0 + 0.6f, r0 + 0.3f, baseH, 8, false);
        if (cornMill)
        {
            // stage (gallery) around the mill
            Frustum(M.Hull, tr * Matrix4x4.Translate(new Vector3(0f, baseH, 0f)), r0 + 2.6f, r0 + 2.6f, 0.3f, 8, true);
        }
        Frustum(M.Thatch, tr * Matrix4x4.Translate(new Vector3(0f, baseH, 0f)), r0, r1, bodyH, 8, false);

        float capY = baseH + bodyH;
        Box(M.Hull, tr * Matrix4x4.Translate(new Vector3(0f, capY + 0.8f, 0.3f)), new Vector3(r1 * 1.9f, 1.6f, r1 * 2.4f), false);
        GableRoof(M.Thatch, M.Hull, tr * Matrix4x4.Translate(new Vector3(0f, capY + 1.6f, 0.3f)), r1 * 1.9f, r1 * 2.4f, 1.8f, 0.2f);

        // rotating sails: separate small object
        var sailsGo = Track(new GameObject("Sails"));
        sailsGo.transform.SetParent(root.transform, false);
        sailsGo.transform.SetPositionAndRotation(
            tr.MultiplyPoint3x4(new Vector3(0f, capY + 1f, r1 * 1.2f + 0.8f)),
            Quaternion.Euler(-8f, yaw, rng.Next(90)));
        sailsGo.transform.localScale = Vector3.one * S;
        var mf = sailsGo.AddComponent<MeshFilter>();
        var mr = sailsGo.AddComponent<MeshRenderer>();
        mf.sharedMesh = SailMesh(cornMill ? 12f : 11f, out Material[] mats);
        mr.sharedMaterials = mats;
        mr.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        sails.Add(sailsGo.transform);
        sailSpeeds.Add(Chance(0.25f) ? 0f : Rand(0.7f, 1.3f));
    }

    private readonly Dictionary<float, Mesh> sailMeshes = new Dictionary<float, Mesh>();
    private readonly Dictionary<float, Material[]> sailMaterials = new Dictionary<float, Material[]>();

    private Mesh SailMesh(float length, out Material[] mats)
    {
        if (sailMeshes.TryGetValue(length, out Mesh cached))
        {
            mats = sailMaterials[length];
            return cached;
        }
        var b = new Batch();
        Batch prev = cur;
        cur = b;
        for (int i = 0; i < 4; i++)
        {
            Quaternion rot = Quaternion.Euler(0f, 0f, i * 90f);
            Vector3 tip = rot * new Vector3(0f, length, 0f);
            Beam(M.Hull, Vector3.zero, tip, 0.35f);
            // lattice sail frame on one side of the stock
            Vector3 side = rot * new Vector3(2.1f, 0f, 0f);
            Vector3 a = rot * new Vector3(0.2f, length * 0.22f, 0.05f);
            Vector3 c = rot * new Vector3(0.2f, length * 0.98f, 0.05f);
            Vector3 fwd = Vector3.forward;
            Quad(M.Sail, a, c, c + side, a + side, fwd);
            Quad(M.Sail, a, a + side, c + side, c, -fwd);
            Beam(M.Hull, a + side, c + side, 0.12f);
        }
        Beam(M.Hull, new Vector3(0f, 0f, 0.3f), new Vector3(0f, 0f, -1.5f), 0.6f);
        cur = prev;

        Mesh mesh = ToMesh(b, "Sails", out mats);
        sailMeshes[length] = mesh;
        sailMaterials[length] = mats;
        return mesh;
    }

    // ================================================================== forest

    private void BuildForest()
    {
        // a manor house in a clearing (Oranjewoud)
        Vector2 manor = forestCenter + new Vector2(-30f, 40f);
        var spec = new HouseSpec { Width = 22f, Depth = 13f, Eave = 9f, Roof = 5f, Hip = true, Wall = M.Plaster, RoofMat = M.RoofDark };
        House(manor, Yaw(Vector2.left), spec);
        Occupy(manor, 40f * S);

        Rect box = new Rect(forestCenter - forestRadii * 1.25f, forestRadii * 2.5f);
        float step = forestTreeSpacing;
        for (float x = box.xMin; x < box.xMax; x += step)
        for (float z = box.yMin; z < box.yMax; z += step)
        {
            Vector2 p = new Vector2(x + Rand(-0.4f, 0.4f) * step, z + Rand(-0.4f, 0.4f) * step);
            float f = ForestValue(p);
            if (f <= 0f) continue;
            if (f < 0.12f && Chance(0.5f)) continue;
            if (OnForestLane(p) || !LandOk(p, 3f) || NearRoad(p, 2f) || !IsFree(p, 2f)) continue;
            Tree(p, Rand(16f, 25f), true);
        }

    }

    // ================================================================== water

    private void BuildWater()
    {
        foreach (var c in canals)
        {
            for (int i = 0; i < c.Points.Count - 1; i++)
            {
                Vector2 a = c.Points[i], b = c.Points[i + 1];
                float len = Vector2.Distance(a, b);
                Vector2 dir = (b - a) / len;
                int pieces = Mathf.Max(1, Mathf.CeilToInt(len / 200f));
                for (int k = 0; k < pieces; k++)
                {
                    float t0 = len * k / pieces - (k == 0 ? c.Width * 0.5f : 0f);
                    float t1 = len * (k + 1) / pieces + (k == pieces - 1 ? c.Width * 0.5f : 0f);
                    Vector2 p0 = a + dir * t0, p1 = a + dir * t1;
                    Begin((p0 + p1) * 0.5f, Layer.Static);
                    Strip(M.Water, p0, p1, c.Width, waterY);
                }
            }
        }
    }

    // ================================================================== ground

    private int BuildGround()
    {
        float H = worldHalfSize;
        float minX = center.x - H, maxX = center.x + H;
        float minZ = center.y - H, maxZ = center.y + H;

        // grid lines: fine near the terrain (to follow its edge), coarse further out
        List<float> xs = GridLines(minX, maxX, terrainRect.xMin, terrainRect.xMax);
        List<float> zs = GridLines(minZ, maxZ, terrainRect.yMin, terrainRect.yMax);

        var verts = new List<Vector3>(xs.Count * zs.Count);
        var uvs = new List<Vector2>(xs.Count * zs.Count);
        for (int j = 0; j < zs.Count; j++)
        for (int i = 0; i < xs.Count; i++)
        {
            var p = new Vector2(xs[i], zs[j]);
            verts.Add(new Vector3(p.x, GroundHeight(p), p.y));
            uvs.Add(new Vector2((p.x - minX) / (2f * H), (p.y - minZ) / (2f * H)));
        }

        var tris = new List<int>(xs.Count * zs.Count * 6);
        for (int j = 0; j < zs.Count - 1; j++)
        for (int i = 0; i < xs.Count - 1; i++)
        {
            Vector2 c = new Vector2((xs[i] + xs[i + 1]) * 0.5f, (zs[j] + zs[j + 1]) * 0.5f);
            if (hasTerrain && terrainRect.Contains(c)) continue;
            int v00 = j * xs.Count + i, v10 = v00 + 1, v01 = v00 + xs.Count, v11 = v01 + 1;
            tris.Add(v00); tris.Add(v01); tris.Add(v11);
            tris.Add(v00); tris.Add(v11); tris.Add(v10);
        }

        // skirt hanging down from the terrain edge to hide cracks between the two surfaces
        if (hasTerrain)
        {
            Vector2[] corners =
            {
                new Vector2(terrainRect.xMin, terrainRect.yMin), new Vector2(terrainRect.xMax, terrainRect.yMin),
                new Vector2(terrainRect.xMax, terrainRect.yMax), new Vector2(terrainRect.xMin, terrainRect.yMax)
            };
            for (int e = 0; e < 4; e++)
            {
                Vector2 a = corners[e], b = corners[(e + 1) % 4];
                Vector2 outward = Perp((b - a).normalized); // rect is walked counter-clockwise seen from above
                int steps = Mathf.CeilToInt(Vector2.Distance(a, b) / 4f);
                for (int s = 0; s < steps; s++)
                {
                    Vector2 p0 = Vector2.Lerp(a, b, s / (float)steps);
                    Vector2 p1 = Vector2.Lerp(a, b, (s + 1) / (float)steps);
                    float h0 = TerrainHeight(p0), h1 = TerrainHeight(p1);
                    int vi = verts.Count;
                    verts.Add(V3(p0, h0)); verts.Add(V3(p1, h1)); verts.Add(V3(p1, h1 - 8f)); verts.Add(V3(p0, h0 - 8f));
                    Vector2 uv0 = new Vector2((p0.x - minX) / (2f * H), (p0.y - minZ) / (2f * H));
                    Vector2 uv1 = new Vector2((p1.x - minX) / (2f * H), (p1.y - minZ) / (2f * H));
                    uvs.Add(uv0); uvs.Add(uv1); uvs.Add(uv1); uvs.Add(uv0);
                    Vector3 n = Vector3.Cross(verts[vi + 1] - verts[vi], verts[vi + 2] - verts[vi]);
                    if (Vector3.Dot(n, V3(outward, 0f)) >= 0f)
                    {
                        tris.Add(vi); tris.Add(vi + 1); tris.Add(vi + 2);
                        tris.Add(vi); tris.Add(vi + 2); tris.Add(vi + 3);
                    }
                    else
                    {
                        tris.Add(vi); tris.Add(vi + 2); tris.Add(vi + 1);
                        tris.Add(vi); tris.Add(vi + 3); tris.Add(vi + 2);
                    }
                }
            }
        }

        var mesh = Track(new Mesh { name = "Polder Ground" });
        mesh.indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var go = Track(new GameObject("Polder Ground"));
        go.transform.SetParent(root.transform, false);
        go.isStatic = true;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = materials[(int)M.Ground];
        mr.shadowCastingMode = ShadowCastingMode.Off;
        go.AddComponent<MeshCollider>().sharedMesh = mesh;

        materials[(int)M.Ground].SetTexture("_BaseMap", PaintGroundTexture());
        materials[(int)M.Ground].SetTexture("_MainTex", materials[(int)M.Ground].GetTexture("_BaseMap"));

        return tris.Count / 3;
    }

    private List<float> GridLines(float min, float max, float tMin, float tMax)
    {
        const float coarse = 125f;
        var lines = new List<float>();
        if (!hasTerrain || tMax <= tMin)
        {
            for (float v = min; v < max; v += coarse) lines.Add(v);
            lines.Add(max);
            return lines;
        }

        int n = Mathf.Max(8, Mathf.CeilToInt((tMax - tMin) / 16f));
        float fine = (tMax - tMin) / n;
        float fineMin = tMin - edgeBlendDistance - fine;
        float fineMax = tMax + edgeBlendDistance + fine;

        var set = new SortedSet<float>();
        for (int k = -1; tMin + k * fine >= Mathf.Max(min, fineMin); k--) set.Add(tMin + k * fine);
        for (int k = 0; tMin + k * fine <= Mathf.Min(max, fineMax) + 0.01f; k++) set.Add(k == n ? tMax : tMin + k * fine);
        for (float v = fineMin - coarse; v > min; v -= coarse) set.Add(v);
        for (float v = fineMax + coarse; v < max; v += coarse) set.Add(v);
        set.Add(min);
        set.Add(max);
        foreach (float v in set) if (v >= min && v <= max) lines.Add(v);
        return lines;
    }

    private float GroundHeight(Vector2 p)
    {
        if (!hasTerrain) return baseY;
        Vector2 q = new Vector2(Mathf.Clamp(p.x, terrainRect.xMin, terrainRect.xMax), Mathf.Clamp(p.y, terrainRect.yMin, terrainRect.yMax));
        float d = Vector2.Distance(p, q);
        float edge = TerrainHeight(q) - 0.15f;
        float t = Mathf.Clamp01(d / edgeBlendDistance);
        t = t * t * (3f - 2f * t);
        return Mathf.Lerp(edge, baseY, t);
    }

    // ------------------------------------------------------------------ ground texture

    private Texture2D PaintGroundTexture()
    {
        int size = groundTextureSize;
        float H = worldHalfSize;
        float minX = center.x - H, minZ = center.y - H;
        float px = 2f * H / size;
        var pixels = new Color32[size * size];

        // 1. meadow parcels separated by ditches
        for (int j = 0; j < size; j++)
        {
            float z = minZ + (j + 0.5f) * px;
            for (int i = 0; i < size; i++)
            {
                float x = minX + (i + 0.5f) * px;
                pixels[j * size + i] = Parcel(x, z, px);
            }
        }

        // 2. town, forest, lanes and canals painted on top
        PaintArea(pixels, size, minX, minZ, px, new Rect(town - Vector2.one * (townLength * 0.5f + 450f), Vector2.one * (townLength + 900f)), (p, c) =>
        {
            if (!InTownArea(p, -60f)) return c;
            uint h = Hash(Mathf.FloorToInt(p.x / 9f), Mathf.FloorToInt(p.y / 9f), seed);
            float v = (h & 255) / 255f;
            return v < 0.45f ? new Color32(104, 96, 80, 255) : v < 0.8f ? new Color32(70, 90, 50, 255) : new Color32(120, 70, 52, 255);
        });

        PaintArea(pixels, size, minX, minZ, px, new Rect(forestCenter - forestRadii * 1.3f, forestRadii * 2.6f), (p, c) =>
        {
            float f = ForestValue(p);
            if (f <= 0f) return c;
            if (OnForestLane(p)) return new Color32(118, 108, 84, 255);
            uint h = Hash(Mathf.FloorToInt(p.x / 12f), Mathf.FloorToInt(p.y / 12f), seed + 7);
            byte g = (byte)(52 + (h & 15));
            return new Color32((byte)(g * 0.6f), g, (byte)(g * 0.45f), 255);
        });

        foreach (var r in roads)
            PaintPolyline(pixels, size, minX, minZ, px, r, r.Width * 0.5f + px * 0.5f, new Color32(150, 136, 106, 255), new Color32(0, 0, 0, 0));
        foreach (var c in canals)
            PaintPolyline(pixels, size, minX, minZ, px, c, c.Width * 0.5f + px * 0.5f, new Color32(52, 68, 68, 255), new Color32(58, 74, 44, 255));

        var tex = Track(new Texture2D(size, size, TextureFormat.RGB24, true));
        tex.name = "Polder Ground";
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Trilinear;
        tex.anisoLevel = 8;
        tex.SetPixels32(pixels);
        tex.Apply(true, false);
        if (SystemInfo.SupportsTextureFormat(TextureFormat.DXT1))
            tex.Compress(false);
        tex.Apply(false, Application.isPlaying);
        return tex;
    }

    private Color32 Parcel(float x, float z, float px)
    {
        const float block = 520f;
        int bx = Mathf.FloorToInt(x / block), bz = Mathf.FloorToInt(z / block);
        uint hb = Hash(bx, bz, seed);
        bool vertical = (hb & 1) == 0;
        float stripW = 22f + ((hb >> 4) & 31);

        float across = vertical ? x : z;
        float along = vertical ? z : x;
        float u = across / stripW;
        int strip = Mathf.FloorToInt(u);
        float f = (u - strip) * stripW;

        // block borders: wider ditches
        float bu = (along - Mathf.Floor(along / block) * block);
        float bv = (across - Mathf.Floor(across / block) * block);
        if (bu < px * 1.1f || bv < px * 1.1f)
            return new Color32(44, 60, 50, 255);

        // strips are sometimes divided along their length
        uint hs = Hash(strip, bx * 31 + bz, seed + 1);
        float segL = 140f + (hs & 255);
        int seg = Mathf.FloorToInt(along / segL);
        float g = along - seg * segL;

        if (f < px * 0.85f || g < px * 0.6f)
            return new Color32(52, 70, 56, 255); // ditch

        uint hc = Hash(strip, seg, (int)hb);
        int pick = (int)(hc % 100);
        Color32 c;
        if (pick < 30) c = new Color32(88, 116, 56, 255);
        else if (pick < 55) c = new Color32(100, 126, 62, 255);
        else if (pick < 72) c = new Color32(78, 104, 50, 255);
        else if (pick < 84) c = new Color32(112, 132, 70, 255);
        else if (pick < 92) c = new Color32(132, 134, 78, 255);  // mown hay
        else if (pick < 97) c = new Color32(70, 96, 46, 255);
        else c = new Color32(104, 88, 64, 255);                   // dug peat / ploughed

        // subtle noise so the parcels aren't flat
        uint hn = Hash(Mathf.FloorToInt(x / (px * 2f)), Mathf.FloorToInt(z / (px * 2f)), seed + 3);
        int n = (int)(hn & 15) - 8;
        return new Color32((byte)Mathf.Clamp(c.r + n, 0, 255), (byte)Mathf.Clamp(c.g + n, 0, 255), (byte)Mathf.Clamp(c.b + n / 2, 0, 255), 255);
    }

    private delegate Color32 PixelFunc(Vector2 p, Color32 current);

    private static void PaintArea(Color32[] pixels, int size, float minX, float minZ, float px, Rect area, PixelFunc func)
    {
        int i0 = Mathf.Clamp(Mathf.FloorToInt((area.xMin - minX) / px), 0, size - 1);
        int i1 = Mathf.Clamp(Mathf.CeilToInt((area.xMax - minX) / px), 0, size - 1);
        int j0 = Mathf.Clamp(Mathf.FloorToInt((area.yMin - minZ) / px), 0, size - 1);
        int j1 = Mathf.Clamp(Mathf.CeilToInt((area.yMax - minZ) / px), 0, size - 1);
        for (int j = j0; j <= j1; j++)
        for (int i = i0; i <= i1; i++)
        {
            var p = new Vector2(minX + (i + 0.5f) * px, minZ + (j + 0.5f) * px);
            int idx = j * size + i;
            pixels[idx] = func(p, pixels[idx]);
        }
    }

    private static void PaintPolyline(Color32[] pixels, int size, float minX, float minZ, float px, Polyline line, float halfWidth, Color32 color, Color32 bank)
    {
        float bankW = bank.a > 0 ? px * 1.2f : 0f;
        for (int k = 0; k < line.Points.Count - 1; k++)
        {
            Vector2 a = line.Points[k], b = line.Points[k + 1];
            float r = halfWidth + bankW + px;
            Rect area = Rect.MinMaxRect(Mathf.Min(a.x, b.x) - r, Mathf.Min(a.y, b.y) - r, Mathf.Max(a.x, b.x) + r, Mathf.Max(a.y, b.y) + r);
            PaintArea(pixels, size, minX, minZ, px, area, (p, c) =>
            {
                float d = DistanceToSegment(p, a, b);
                if (d < halfWidth) return color;
                if (d < halfWidth + bankW) return bank;
                return c;
            });
        }
    }

    // ================================================================== trees

    private void Tree(Vector2 p, float height, bool forest = false)
    {
        height *= S;
        float yaw = Rand(0f, 360f);
        M leaf = (M)((int)M.Leaf1 + rng.Next(3));
        float crownR = height * (forest ? Rand(0.3f, 0.38f) : Rand(0.3f, 0.4f));
        float crownH = height * Rand(0.36f, 0.46f);
        Vector3 crown = V3(p, baseY + height - crownH);
        Vector3 radii = new Vector3(crownR * Rand(0.85f, 1.15f), crownH, crownR * Rand(0.85f, 1.15f));

        Begin(p, Layer.Near);
        float trunkR = 0.18f + height * 0.014f;
        Frustum(M.Trunk, Matrix4x4.Translate(V3(p, baseY - 0.2f)), trunkR, trunkR * 0.6f, height - crownH * 1.2f + 0.2f, 4, false);
        Blob(leaf, crown, radii, yaw, sphereHigh);

        Begin(p, Layer.Far);
        Blob(leaf, crown, radii * 1.05f, yaw, sphereLow);
    }

    // ================================================================== primitives

    private void Begin(Vector2 pos, Layer layer)
    {
        float H = worldHalfSize;
        int cx = Mathf.FloorToInt((pos.x - (center.x - H)) / chunkSize);
        int cz = Mathf.FloorToInt((pos.y - (center.y - H)) / chunkSize);
        long key = ((long)(cx + 1000) * 4096 + (cz + 1000)) * 4 + (int)layer;
        if (!batches.TryGetValue(key, out cur))
        {
            cur = new Batch { Chunk = new Vector2Int(cx, cz) };
            batches[key] = cur;
        }
    }

    private void Tri(M m, Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
    {
        Vector3 n = Vector3.Cross(b - a, c - a);
        float len = n.magnitude;
        if (len < 1e-8f) return;
        n /= len;
        if (Vector3.Dot(n, outward) < 0f)
        {
            (b, c) = (c, b);
            n = -n;
        }
        var list = cur.Tris[(int)m] ??= new List<int>();
        int i = cur.Verts.Count;
        cur.Verts.Add(a); cur.Verts.Add(b); cur.Verts.Add(c);
        cur.Normals.Add(n); cur.Normals.Add(n); cur.Normals.Add(n);
        list.Add(i); list.Add(i + 1); list.Add(i + 2);
    }

    private void Quad(M m, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
    {
        Tri(m, a, b, c, outward);
        Tri(m, a, c, d, outward);
    }

    /// <summary>Box centred on the matrix origin.</summary>
    private void Box(M m, Matrix4x4 tr, Vector3 size, bool bottom)
    {
        Vector3 C(int i, int j, int k) => tr.MultiplyPoint3x4(new Vector3((i - 0.5f) * size.x, (j - 0.5f) * size.y, (k - 0.5f) * size.z));
        Vector3 center = tr.MultiplyPoint3x4(Vector3.zero);
        void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d) => Quad(m, a, b, c, d, (a + b + c + d) * 0.25f - center);

        Face(C(1, 0, 0), C(1, 1, 0), C(1, 1, 1), C(1, 0, 1));
        Face(C(0, 0, 0), C(0, 0, 1), C(0, 1, 1), C(0, 1, 0));
        Face(C(0, 0, 1), C(1, 0, 1), C(1, 1, 1), C(0, 1, 1));
        Face(C(0, 0, 0), C(0, 1, 0), C(1, 1, 0), C(1, 0, 0));
        Face(C(0, 1, 0), C(0, 1, 1), C(1, 1, 1), C(1, 1, 0));
        if (bottom) Face(C(0, 0, 0), C(1, 0, 0), C(1, 0, 1), C(0, 0, 1));
    }

    /// <summary>Thin box between two points.</summary>
    private void Beam(M m, Vector3 a, Vector3 b, float thickness)
    {
        Vector3 d = b - a;
        float len = d.magnitude;
        if (len < 1e-4f) return;
        Vector3 up = Mathf.Abs(Vector3.Dot(d / len, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up;
        Matrix4x4 tr = Matrix4x4.TRS((a + b) * 0.5f, Quaternion.LookRotation(d, up), Vector3.one);
        Box(m, tr, new Vector3(thickness, thickness, len), true);
    }

    /// <summary>Gable roof, origin at the middle of the eaves, ridge along local Z.</summary>
    private void GableRoof(M roof, M gable, Matrix4x4 tr, float width, float length, float height, float overhang)
    {
        float W = width * 0.5f + overhang, D = length * 0.5f + overhang;
        Vector3 P(float x, float y, float z) => tr.MultiplyPoint3x4(new Vector3(x, y, z));
        Vector3 inner = P(0f, height * 0.3f, 0f);

        Vector3 l0 = P(-W, 0f, -D), l1 = P(-W, 0f, D), r0 = P(W, 0f, -D), r1 = P(W, 0f, D);
        Vector3 t0 = P(0f, height, -D), t1 = P(0f, height, D);
        Quad(roof, l0, l1, t1, t0, (l0 + t1) * 0.5f - inner);
        Quad(roof, r0, t0, t1, r1, (r0 + t1) * 0.5f - inner);

        float w = width * 0.5f, d = length * 0.5f;
        float gh = height * (1f - w / W);
        foreach (int s in new[] { -1, 1 })
        {
            Vector3 a = P(-w, 0f, d * s), b = P(w, 0f, d * s), c = P(0f, height - gh * 0.2f, d * s);
            Tri(gable, a, b, c, tr.MultiplyVector(new Vector3(0f, 0f, s)));
        }
    }

    /// <summary>Hipped roof (pyramid when ridgeHalf is 0), origin at the middle of the eaves.</summary>
    private void HipRoof(M roof, Matrix4x4 tr, float width, float length, float height, float ridgeHalf, float overhang)
    {
        float W = width * 0.5f + overhang, D = length * 0.5f + overhang;
        Vector3 P(float x, float y, float z) => tr.MultiplyPoint3x4(new Vector3(x, y, z));
        Vector3 inner = P(0f, height * 0.25f, 0f);
        Vector3 a = P(-W, 0f, -D), b = P(-W, 0f, D), c = P(W, 0f, D), d = P(W, 0f, -D);
        Vector3 r0 = P(0f, height, -ridgeHalf), r1 = P(0f, height, ridgeHalf);

        Quad(roof, a, b, r1, r0, (a + b + r1 + r0) * 0.25f - inner);
        Quad(roof, d, r0, r1, c, (d + c + r1 + r0) * 0.25f - inner);
        Tri(roof, b, c, r1, (b + c + r1) / 3f - inner);
        Tri(roof, a, r0, d, (a + d + r0) / 3f - inner);
    }

    /// <summary>N-sided tapered column from local y=0 to y=height (a cone when topRadius is 0).</summary>
    private void Frustum(M m, Matrix4x4 tr, float bottomRadius, float topRadius, float height, int sides, bool capTop)
    {
        Vector3 axisTop = tr.MultiplyPoint3x4(new Vector3(0f, height, 0f));
        for (int i = 0; i < sides; i++)
        {
            float a0 = (i + 0.5f) * Mathf.PI * 2f / sides, a1 = (i + 1.5f) * Mathf.PI * 2f / sides;
            Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
            Vector3 b0 = tr.MultiplyPoint3x4(d0 * bottomRadius), b1 = tr.MultiplyPoint3x4(d1 * bottomRadius);
            Vector3 out_ = tr.MultiplyVector(d0 + d1);
            if (topRadius <= 0.001f)
            {
                Tri(m, b0, b1, axisTop, out_);
            }
            else
            {
                Vector3 t0 = tr.MultiplyPoint3x4(d0 * topRadius + Vector3.up * height);
                Vector3 t1 = tr.MultiplyPoint3x4(d1 * topRadius + Vector3.up * height);
                Quad(m, b0, b1, t1, t0, out_);
                if (capTop) Tri(m, t0, t1, axisTop, tr.MultiplyVector(Vector3.up));
            }
        }
    }

    /// <summary>Extruded convex outline (local XZ) from y0 to y1, each ring scaled separately.</summary>
    private void Loft(M m, Matrix4x4 tr, Vector2[] outline, float y0, float scale0, float y1, float scale1, bool capTop)
    {
        Vector3 mid = tr.MultiplyPoint3x4(new Vector3(0f, (y0 + y1) * 0.5f, 0f));
        Vector3 top = tr.MultiplyPoint3x4(new Vector3(0f, y1, 0f));
        for (int i = 0; i < outline.Length; i++)
        {
            Vector2 p = outline[i], q = outline[(i + 1) % outline.Length];
            Vector3 a = tr.MultiplyPoint3x4(new Vector3(p.x * scale0, y0, p.y * scale0));
            Vector3 b = tr.MultiplyPoint3x4(new Vector3(q.x * scale0, y0, q.y * scale0));
            Vector3 c = tr.MultiplyPoint3x4(new Vector3(q.x * scale1, y1, q.y * scale1));
            Vector3 d = tr.MultiplyPoint3x4(new Vector3(p.x * scale1, y1, p.y * scale1));
            Vector3 outward = (a + b + c + d) * 0.25f - mid;
            outward.y = 0f;
            Quad(m, a, b, c, d, outward);
            if (capTop) Tri(m, d, c, top, tr.MultiplyVector(Vector3.up));
        }
    }

    /// <summary>Flat rectangle on a facade at local depth z (facing local +Z).</summary>
    private void FacadeRect(M m, Matrix4x4 tr, float x0, float y0, float x1, float y1, float z)
    {
        Vector3 a = tr.MultiplyPoint3x4(new Vector3(x0, y0, z)), b = tr.MultiplyPoint3x4(new Vector3(x1, y0, z));
        Vector3 c = tr.MultiplyPoint3x4(new Vector3(x1, y1, z)), d = tr.MultiplyPoint3x4(new Vector3(x0, y1, z));
        Quad(m, a, b, c, d, tr.MultiplyVector(Vector3.forward));
    }

    private void Strip(M m, Vector2 a, Vector2 b, float width, float y)
    {
        Vector2 n = Perp((b - a).normalized) * width * 0.5f;
        Quad(m, V3(a - n, y), V3(a + n, y), V3(b + n, y), V3(b - n, y), Vector3.up);
    }

    private void Paving(Vector2 a, Vector2 b, float width) => Strip(M.Paving, a, b, width, baseY + 0.12f);

    private void PavingChunks(Vector2 a, Vector2 b, float width)
    {
        float len = Vector2.Distance(a, b);
        int pieces = Mathf.Max(1, Mathf.CeilToInt(len / 150f));
        for (int i = 0; i < pieces; i++)
        {
            Vector2 p0 = Vector2.Lerp(a, b, i / (float)pieces), p1 = Vector2.Lerp(a, b, (i + 1) / (float)pieces);
            Begin((p0 + p1) * 0.5f, Layer.Near);
            Paving(p0, p1, width);
        }
    }

    private void QuayWall(Vector2 a, Vector2 b)
    {
        float len = Vector2.Distance(a, b);
        int pieces = Mathf.Max(1, Mathf.CeilToInt(len / 150f));
        for (int i = 0; i < pieces; i++)
        {
            Vector2 p0 = Vector2.Lerp(a, b, i / (float)pieces), p1 = Vector2.Lerp(a, b, (i + 1) / (float)pieces);
            Begin((p0 + p1) * 0.5f, Layer.Static);
            Beam(M.Quay, V3(p0, baseY + 0.35f), V3(p1, baseY + 0.35f), 0.8f);
        }
    }

    private void Blob(M m, Vector3 c, Vector3 radii, float yaw, Vector3[] unitTris)
    {
        Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
        for (int i = 0; i < unitTris.Length; i += 3)
        {
            Vector3 a = c + rot * Vector3.Scale(unitTris[i], radii);
            Vector3 b = c + rot * Vector3.Scale(unitTris[i + 1], radii);
            Vector3 d = c + rot * Vector3.Scale(unitTris[i + 2], radii);
            Tri(m, a, b, d, (a + b + d) / 3f - c);
        }
    }

    private static void EnsureSpheres()
    {
        if (sphereHigh != null) return;
        Vector3[] o =
        {
            Vector3.up, Vector3.forward, Vector3.right,
            Vector3.up, Vector3.right, Vector3.back,
            Vector3.up, Vector3.back, Vector3.left,
            Vector3.up, Vector3.left, Vector3.forward,
            Vector3.down, Vector3.right, Vector3.forward,
            Vector3.down, Vector3.back, Vector3.right,
            Vector3.down, Vector3.left, Vector3.back,
            Vector3.down, Vector3.forward, Vector3.left,
        };
        sphereLow = o;

        var hi = new List<Vector3>();
        for (int i = 0; i < o.Length; i += 3)
        {
            Vector3 a = o[i], b = o[i + 1], c = o[i + 2];
            Vector3 ab = (a + b).normalized, bc = (b + c).normalized, ca = (c + a).normalized;
            hi.AddRange(new[] { a, ab, ca, ab, b, bc, ca, bc, c, ab, bc, ca });
        }
        // flatten the bottom a little so crowns look like deciduous trees
        for (int i = 0; i < hi.Count; i++)
        {
            Vector3 v = hi[i];
            if (v.y < 0f) v.y *= 0.7f;
            hi[i] = v;
        }
        sphereHigh = hi.ToArray();
    }

    private static bool SegmentIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out Vector2 hit)
    {
        hit = default;
        Vector2 r = b - a, s = d - c;
        float den = r.x * s.y - r.y * s.x;
        if (Mathf.Abs(den) < 1e-6f) return false;
        Vector2 ca = c - a;
        float t = (ca.x * s.y - ca.y * s.x) / den;
        float u = (ca.x * r.y - ca.y * r.x) / den;
        if (t < 0f || t > 1f || u < 0f || u > 1f) return false;
        hit = a + r * t;
        return true;
    }

    // ================================================================== materials & meshes

    private void CreateMaterials()
    {
        Shader shader = baseMaterial != null ? baseMaterial.shader : null;
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Simple Lit");
        if (shader == null) shader = Shader.Find("Standard");

        materials = new Material[(int)M.Count];
        for (int i = 0; i < materials.Length; i++)
        {
            Material mat = baseMaterial != null ? new Material(baseMaterial) : new Material(shader);
            mat.name = "Heerenveen " + (M)i;
            mat.SetTexture("_BaseMap", null);
            mat.SetTexture("_MainTex", null);
            mat.SetColor("_BaseColor", Colors[i]);
            mat.SetColor("_Color", Colors[i]);
            mat.SetFloat("_Smoothness", Smoothness[i]);
            mat.SetFloat("_Glossiness", Smoothness[i]);
            mat.SetFloat("_Metallic", 0f);
            mat.enableInstancing = true;
            materials[i] = Track(mat);
        }
    }

    private Mesh ToMesh(Batch b, string name, out Material[] mats)
    {
        var mesh = Track(new Mesh { name = name });
        mesh.indexFormat = b.Verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.SetVertices(b.Verts);
        mesh.SetNormals(b.Normals);

        var used = new List<int>();
        for (int m = 0; m < b.Tris.Length; m++)
            if (b.Tris[m] != null && b.Tris[m].Count > 0) used.Add(m);

        mesh.subMeshCount = used.Count;
        mats = new Material[used.Count];
        for (int s = 0; s < used.Count; s++)
        {
            mesh.SetTriangles(b.Tris[used[s]], s, false);
            mats[s] = materials[used[s]];
        }
        mesh.RecalculateBounds();
        if (Application.isPlaying) mesh.UploadMeshData(true);
        return mesh;
    }

    // LODGroups measure distance from the chunk centre; a fixed nominal size keeps the
    // screen-height thresholds below 1 so they map directly to the distances above.
    private const float LodSize = 100f;

    private int FlushBatches()
    {
        int triangles = 0;
        float tanHalfFov = Mathf.Tan(30f * Mathf.Deg2Rad);
        var chunks = new Dictionary<Vector2Int, GameObject>();
        var chunkLods = new Dictionary<Vector2Int, (Renderer near, Renderer far)>();

        foreach (var kv in batches)
        {
            Batch b = kv.Value;
            if (b.Verts.Count == 0) continue;
            var layer = (Layer)(kv.Key & 3);

            if (!chunks.TryGetValue(b.Chunk, out GameObject chunkGo))
            {
                chunkGo = Track(new GameObject($"Chunk {b.Chunk.x},{b.Chunk.y}"));
                chunkGo.transform.SetParent(root.transform, false);
                chunkGo.isStatic = true;
                chunks[b.Chunk] = chunkGo;
            }

            var go = Track(new GameObject(layer.ToString()));
            go.transform.SetParent(chunkGo.transform, false);
            go.isStatic = true;
            Mesh mesh = ToMesh(b, $"{chunkGo.name} {layer}", out Material[] mats);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats;
            mr.shadowCastingMode = castShadows && layer != Layer.Far ? ShadowCastingMode.On : ShadowCastingMode.Off;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            foreach (var list in b.Tris) if (list != null) triangles += list.Count / 3;

            chunkLods.TryGetValue(b.Chunk, out var pair);
            if (layer == Layer.Near) pair.near = mr;
            else if (layer == Layer.Far) pair.far = mr;
            else
            {
                // static buildings are culled just beyond the far tree distance
                var lg = go.AddComponent<LODGroup>();
                lg.SetLODs(new[] { new LOD(LodSize / (2f * (farTreeDistance + 600f) * tanHalfFov), new Renderer[] { mr }) });
                lg.localReferencePoint = mesh.bounds.center;
                lg.size = LodSize;
            }
            chunkLods[b.Chunk] = pair;
        }

        // near details + full trees -> low trees -> culled
        foreach (var kv in chunkLods)
        {
            var (near, far) = kv.Value;
            if (near == null && far == null) continue;
            GameObject chunkGo = chunks[kv.Key];
            var lg = chunkGo.AddComponent<LODGroup>();
            float h0 = Mathf.Min(0.9f, LodSize / (2f * detailDistance * tanHalfFov));
            float h1 = LodSize / (2f * farTreeDistance * tanHalfFov);
            if (h1 >= h0) h1 = h0 * 0.5f;
            lg.SetLODs(new[]
            {
                new LOD(h0, near != null ? new Renderer[] { near } : new Renderer[0]),
                new LOD(h1, far != null ? new Renderer[] { far } : new Renderer[0]),
            });
            float H = worldHalfSize;
            lg.localReferencePoint = new Vector3(center.x - H + (kv.Key.x + 0.5f) * chunkSize, baseY, center.y - H + (kv.Key.y + 0.5f) * chunkSize);
            lg.size = LodSize;
        }

        return triangles;
    }
}
