using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// OSPREY world builder (Editor only).
/// Menu: OSPREY > Build World. Places a fixed city + search-and-rescue world into the
/// open scene as ordinary GameObjects under "OSPREY_World". After building, edit it by
/// hand like any scene; nothing is generated at runtime. Ctrl+Z undoes the whole build.
///
/// Layout (metres, ground is 400 x 400 centred on the origin):
///   Base         around the drone start (0, -8): helipad, hangar, tower (kept clear)
///   City         north: x -90..120, z 65..185, road grid, downtown towers, a park
///   River        east: x 142..158, with a bridge at z 105
///   Forest       west: trees and low hills
///   Countryside  south and across the river: fields, huts, rescue targets
/// Buildings, huts, trees and the bridge use BoxColliders on the "Obstacle" layer, so
/// ObstacleSensor / ObstacleAvoidance and a future RRT* planner can see them.
/// </summary>
public static class OspreyWorldBuilder
{
    const string RootName = "OSPREY_World";
    const string MatDir = "Assets/Materials/OspreyWorld";
    const int Seed = 42;

    static System.Random rng;
    static int obstacleLayer;
    static int waterLayer;
    static int objectCount;
    static Dictionary<string, Material> mats;
    static readonly List<Rect> blocked = new List<Rect>();
    static readonly List<Vector2> treeSpots = new List<Vector2>();
    static readonly List<Vector4> hills = new List<Vector4>(); // x, z, radius, height

    // ===================================================
    // MENU
    // ===================================================

    [MenuItem("OSPREY/Build World")]
    public static void Build()
    {
        var existing = GameObject.Find(RootName);
        if (existing != null)
        {
            if (!EditorUtility.DisplayDialog("OSPREY world",
                    "An OSPREY_World already exists in this scene. Replace it?",
                    "Replace", "Cancel"))
                return;
            Undo.DestroyObjectImmediate(existing);
        }

        rng = new System.Random(Seed);
        obstacleLayer = LayerMask.NameToLayer("Obstacle");
        if (obstacleLayer < 0) obstacleLayer = 0;
        waterLayer = LayerMask.NameToLayer("Water");
        if (waterLayer < 0) waterLayer = 0;
        objectCount = 0;
        mats = new Dictionary<string, Material>();
        blocked.Clear();
        treeSpots.Clear();
        hills.Clear();

        var root = new GameObject(RootName);

        BuildBase(Group(root, "Base"));
        BuildCity(Group(root, "City"));
        BuildRiver(Group(root, "River"));
        BuildForest(Group(root, "Forest"));
        BuildCountryside(Group(root, "Countryside"));

        // Repaint the existing ground as grass
        var ground = GameObject.Find("Ground");
        if (ground != null && ground.GetComponent<Renderer>() != null)
        {
            var r = ground.GetComponent<Renderer>();
            Undo.RecordObject(r, "Paint ground");
            r.sharedMaterial = Mat("Grass");
        }

        Undo.RegisterCreatedObjectUndo(root, "Build OSPREY world");
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;

        Debug.Log($"[OSPREY] World built: {objectCount} objects. Obstacles are on layer " +
                  $"'{LayerMask.LayerToName(obstacleLayer)}'. Save the scene (Ctrl+S) to keep it.");
    }

    [MenuItem("OSPREY/Remove World")]
    public static void Remove()
    {
        var existing = GameObject.Find(RootName);
        if (existing == null) { Debug.Log("[OSPREY] No OSPREY_World in this scene."); return; }
        Undo.DestroyObjectImmediate(existing);
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
    }

    /// <summary>
    /// The test walls were children of the 40x-scaled Ground, which blew them up into
    /// 240 m slabs. Move them out at their intended size, 30 m east of the helipad,
    /// and make them 8 m tall so a hovering drone actually meets them.
    /// </summary>
    [MenuItem("OSPREY/Fix Test Walls")]
    public static void FixTestWalls()
    {
        var ground = GameObject.Find("Ground");
        if (ground == null) { Debug.LogWarning("[OSPREY] No Ground object found."); return; }

        var walls = new List<Transform>();
        foreach (Transform child in ground.transform)
            if (child.name.StartsWith("Wall_")) walls.Add(child);
        if (walls.Count == 0) { Debug.Log("[OSPREY] No test walls under Ground; nothing to fix."); return; }

        var course = new GameObject("TestObstacleCourse");
        Undo.RegisterCreatedObjectUndo(course, "Fix test walls");
        course.transform.position = new Vector3(30, 0, 0);

        foreach (var w in walls)
        {
            Vector3 localPos = w.localPosition, localScale = w.localScale;
            Undo.SetTransformParent(w, course.transform, "Fix test walls");
            Undo.RecordObject(w, "Fix test walls");
            w.localRotation = Quaternion.identity;
            w.localScale = new Vector3(localScale.x, 8f, localScale.z);
            w.localPosition = new Vector3(localPos.x, 4f, localPos.z);
            w.gameObject.layer = LayerMask.NameToLayer("Obstacle") >= 0 ? LayerMask.NameToLayer("Obstacle") : w.gameObject.layer;
        }

        EditorSceneManager.MarkSceneDirty(course.scene);
        Selection.activeGameObject = course;
        Debug.Log($"[OSPREY] Moved {walls.Count} test walls to TestObstacleCourse at (30, 0, 0), 8 m tall. Save the scene to keep it.");
    }

    // ===================================================
    // BASE (drone start area, kept clear)
    // ===================================================

    static void BuildBase(Transform p)
    {
        blocked.Add(Rect.MinMaxRect(-32, -32, 66, 52));

        Box(p, "Helipad", new Vector3(0, 0.05f, -8), new Vector3(24, 0.1f, 24), "Concrete", false);
        Box(p, "Helipad_H_Left", new Vector3(-2.5f, 0.11f, -8), new Vector3(1.2f, 0.02f, 8), "Marking", false);
        Box(p, "Helipad_H_Right", new Vector3(2.5f, 0.11f, -8), new Vector3(1.2f, 0.02f, 8), "Marking", false);
        Box(p, "Helipad_H_Bar", new Vector3(0, 0.11f, -8), new Vector3(5, 0.02f, 1.2f), "Marking", false);

        Box(p, "Apron", new Vector3(-22, 0.04f, -14), new Vector3(22, 0.08f, 30), "Asphalt", false);
        Box(p, "Hangar", new Vector3(-24, 4, -22), new Vector3(16, 8, 12), "Metal", true);
        Box(p, "Hangar_Roof", new Vector3(-24, 8.3f, -22), new Vector3(17, 0.6f, 13), "Roof", false);
        Box(p, "ControlTower", new Vector3(-24, 6, 6), new Vector3(4, 12, 4), "Concrete", true);
        Box(p, "ControlTower_Cab", new Vector3(-24, 13, 6), new Vector3(6, 2.5f, 6), "Glass", true);

        // Road from the base to the city
        Box(p, "Road_ToCity", new Vector3(20, 0.03f, 40), new Vector3(8, 0.06f, 50), "Asphalt", false);
    }

    // ===================================================
    // CITY (north)
    // ===================================================

    static void BuildCity(Transform p)
    {
        const float x0 = -80, z0 = 70, block = 30, road = 10, pitch = block + road;
        const int cols = 5, rows = 3;

        blocked.Add(Rect.MinMaxRect(x0 - 12, z0 - 12, x0 + cols * pitch + 2, z0 + rows * pitch + 2));

        var roads = Group(p.gameObject, "Roads");
        float zMin = z0 - road, zMax = z0 + rows * pitch;
        float xMin = x0 - road, xMax = x0 + cols * pitch;

        for (int i = 0; i <= cols; i++)
        {
            float x = x0 - road / 2 + i * pitch;
            Box(roads, $"Road_NS_{i}", new Vector3(x, 0.03f, (zMin + zMax) / 2), new Vector3(road, 0.06f, zMax - zMin), "Asphalt", false);
        }
        for (int j = 0; j <= rows; j++)
        {
            float z = z0 - road / 2 + j * pitch;
            Box(roads, $"Road_EW_{j}", new Vector3((xMin + xMax) / 2, 0.035f, z), new Vector3(xMax - xMin, 0.06f, road), "Asphalt", false);
        }

        for (int i = 0; i < cols; i++)
        for (int j = 0; j < rows; j++)
        {
            float bx = x0 + i * pitch, bz = z0 + j * pitch;
            var blk = Group(p.gameObject, $"Block_{i}_{j}");

            Box(blk, "Sidewalk", new Vector3(bx + block / 2, 0.06f, bz + block / 2), new Vector3(block, 0.12f, block), "Concrete", false);

            if (i == 1 && j == 1) { BuildPark(blk, bx, bz, block); continue; }

            bool downtown = (i == 2 || i == 3) && j >= 1;

            for (int lx = 0; lx < 2; lx++)
            for (int lz = 0; lz < 2; lz++)
            {
                float cx = bx + 7.5f + lx * 15 + Rand(-1, 1);
                float cz = bz + 7.5f + lz * 15 + Rand(-1, 1);
                float w = Rand(9, 12.5f), d = Rand(9, 12.5f);
                float h = downtown ? Rand(26, 58) : Rand(8, 24);
                string m = Pick("Building1", "Building2", "Building3", "Building4");
                if (downtown && rng.NextDouble() < 0.4) m = "Glass";

                var b = Box(blk, "Building", new Vector3(cx, h / 2, cz), new Vector3(w, h, d), m, true);
                b.name = $"Building_{(int)h}m";

                if (rng.NextDouble() < 0.35)
                {
                    float h2 = Rand(5, 12);
                    Box(blk, "Setback", new Vector3(cx, h + h2 / 2, cz), new Vector3(w * 0.6f, h2, d * 0.6f), m, true);
                    h += h2;
                }
                Box(blk, "Rooftop_Unit", new Vector3(cx + Rand(-2, 2), h + 0.8f, cz + Rand(-2, 2)), new Vector3(2.5f, 1.6f, 2), "Metal", false);

                if (downtown && h > 50)
                    Box(blk, "Antenna", new Vector3(cx, h + 5, cz), new Vector3(0.3f, 8, 0.3f), "Metal", true);
            }
        }
    }

    static void BuildPark(Transform blk, float bx, float bz, float size)
    {
        blk.name = "Park";
        Box(blk, "Lawn", new Vector3(bx + size / 2, 0.13f, bz + size / 2), new Vector3(size - 2, 0.04f, size - 2), "GrassDark", false);
        Cyl(blk, "Pond", new Vector3(bx + size / 2, 0.16f, bz + size / 2), new Vector3(10, 0.02f, 8), "Water", false);
        for (int k = 0; k < 9; k++)
        {
            float a = k / 9f * Mathf.PI * 2;
            Tree(blk, bx + size / 2 + Mathf.Cos(a) * 11, bz + size / 2 + Mathf.Sin(a) * 11, 0.15f);
        }
    }

    // ===================================================
    // RIVER (east) + bridge
    // ===================================================

    static void BuildRiver(Transform p)
    {
        const float rx = 150, width = 16;
        blocked.Add(Rect.MinMaxRect(rx - width / 2 - 4, -200, rx + width / 2 + 4, 200));

        var water = Box(p, "Water", new Vector3(rx, 0.04f, 0), new Vector3(width, 0.08f, 400), "Water", false);
        water.layer = waterLayer;
        Box(p, "Bank_West", new Vector3(rx - width / 2 - 1.5f, 0.05f, 0), new Vector3(3, 0.1f, 400), "Sand", false);
        Box(p, "Bank_East", new Vector3(rx + width / 2 + 1.5f, 0.05f, 0), new Vector3(3, 0.1f, 400), "Sand", false);

        // City road (z = 105) continues east over the bridge
        Box(p, "Road_ToBridge", new Vector3(131, 0.035f, 105), new Vector3(22, 0.06f, 10), "Asphalt", false);
        Box(p, "Bridge_Deck", new Vector3(rx, 1.4f, 105), new Vector3(26, 0.8f, 10), "Concrete", true);
        Box(p, "Bridge_Pier_W", new Vector3(rx - 5, 0.6f, 105), new Vector3(1.5f, 1.6f, 8), "Concrete", true);
        Box(p, "Bridge_Pier_E", new Vector3(rx + 5, 0.6f, 105), new Vector3(1.5f, 1.6f, 8), "Concrete", true);
        Box(p, "Road_EastBank", new Vector3(182, 0.035f, 105), new Vector3(36, 0.06f, 10), "Asphalt", false);
    }

    // ===================================================
    // FOREST (west) with low hills
    // ===================================================

    static void BuildForest(Transform p)
    {
        // Low hills: drones cruise above them (max ~8 m)
        for (int k = 0; k < 6; k++)
        {
            float hx = Rand(-180, -70), hz = Rand(-170, 40), r = Rand(16, 30), h = Rand(4, 8);
            hills.Add(new Vector4(hx, hz, r, h));
            var hill = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            hill.name = "Hill";
            hill.transform.SetParent(p, false);
            hill.transform.localPosition = new Vector3(hx, 0, hz);
            hill.transform.localScale = new Vector3(r * 2, h * 2, r * 2);
            Paint(hill, "GrassDark");
            Object.DestroyImmediate(hill.GetComponent<Collider>());
            hill.AddComponent<MeshCollider>();
            MakeStatic(hill);
            objectCount++;
        }

        Scatter(p, 230, -195, -40, -190, 190, 4.5f);
    }

    // ===================================================
    // COUNTRYSIDE (south + across the river)
    // ===================================================

    static void BuildCountryside(Transform p)
    {
        var fields = Group(p.gameObject, "Fields");
        var placed = new List<Rect>();
        for (int tries = 0, n = 0; n < 9 && tries < 200; tries++)
        {
            float w = Rand(28, 50), d = Rand(20, 38);
            float cx = Rand(-25 + w / 2, 135 - w / 2), cz = Rand(-192 + d / 2, -48 - d / 2);
            var r = new Rect(cx - w / 2 - 3, cz - d / 2 - 3, w + 6, d + 6);
            if (placed.Exists(o => o.Overlaps(r))) continue;
            placed.Add(r);
            Box(fields, $"Field_{n++}", new Vector3(cx, 0.03f, cz), new Vector3(w, 0.06f, d), Pick("Field", "FieldGreen"), false);
        }

        var huts = Group(p.gameObject, "Huts");
        var targets = Group(p.gameObject, "RescueTargets");
        int hutCount = 0, targetCount = 0;

        // South huts sit at field edges; east-bank huts across the river
        for (int tries = 0; hutCount < 13 && tries < 400; tries++)
        {
            float hx = Rand(-30, 132), hz = Rand(-190, -45);
            if (IsBlocked(hx, hz, 6) || placed.Exists(o => o.Contains(new Vector2(hx, hz)))) continue;
            Hut(huts, hx, hz, ref hutCount);
        }
        for (int tries = 0, n = 0; n < 6 && tries < 300; tries++)
        {
            float hx = Rand(166, 194), hz = Rand(-180, 180);
            if (IsBlocked(hx, hz, 6) || Mathf.Abs(hz - 105) < 10) continue;
            Hut(huts, hx, hz, ref hutCount);
            n++;
        }

        // Rescue targets (bright markers, no collider) for future search-and-rescue missions
        var spots = new List<Vector2>();
        for (int tries = 0; targetCount < 6 && tries < 400; tries++)
        {
            float tx = tries % 2 == 0 ? Rand(-185, -60) : Rand(165, 195);
            float tz = Rand(-180, 180);
            if (IsBlocked(tx, tz, 3) && !(tx > 160)) continue;
            if (spots.Exists(s => Vector2.Distance(s, new Vector2(tx, tz)) < 40)) continue;
            spots.Add(new Vector2(tx, tz));
            var t = Cyl(targets, $"RescueTarget_{++targetCount}", new Vector3(tx, GroundHeight(tx, tz) + 0.9f, tz), new Vector3(1.2f, 0.9f, 1.2f), "Rescue", false);
            t.layer = 0;
        }

        Scatter(p, 60, -30, 135, -195, -45, 6f);   // trees between the fields
        Scatter(p, 45, 165, 196, -195, 195, 5f);   // east bank woods
    }

    static void Hut(Transform parent, float x, float z, ref int count)
    {
        var hut = new GameObject($"Hut_{++count}");
        hut.transform.SetParent(parent, false);
        hut.transform.localPosition = new Vector3(x, 0, z);
        hut.transform.localRotation = Quaternion.Euler(0, rng.Next(0, 4) * 90 + Rand(-15, 15), 0);
        hut.layer = obstacleLayer;
        var bc = hut.AddComponent<BoxCollider>();
        bc.center = new Vector3(0, 2.6f, 0);
        bc.size = new Vector3(5.4f, 5.2f, 4.4f);
        MakeStatic(hut);
        objectCount++;

        Box(hut.transform, "Walls", new Vector3(0, 1.75f, 0), new Vector3(5, 3.5f, 4), "Wood", false);
        var roof = Box(hut.transform, "Roof", new Vector3(0, 3.5f, 0), new Vector3(3.7f, 3.7f, 4.4f), "Roof", false);
        roof.transform.localRotation = Quaternion.Euler(0, 0, 45);
        Box(hut.transform, "Door", new Vector3(0, 1, 2.01f), new Vector3(1.1f, 2, 0.05f), "Trunk", false);

        blocked.Add(new Rect(x - 5, z - 5, 10, 10));
    }

    // ===================================================
    // TREES
    // ===================================================

    static void Scatter(Transform parent, int count, float xMin, float xMax, float zMin, float zMax, float minGap)
    {
        var g = Group(parent.gameObject, "Trees");
        int placed = 0;
        for (int tries = 0; placed < count && tries < count * 30; tries++)
        {
            float x = Rand(xMin, xMax), z = Rand(zMin, zMax);
            if (IsBlocked(x, z, 2)) continue;
            var v = new Vector2(x, z);
            if (treeSpots.Exists(s => (s - v).sqrMagnitude < minGap * minGap)) continue;
            treeSpots.Add(v);
            Tree(g, x, z, GroundHeight(x, z));
            placed++;
        }
    }

    static void Tree(Transform parent, float x, float z, float y0)
    {
        bool conifer = rng.NextDouble() < 0.45;
        float h = Rand(4.5f, 8.5f), r = conifer ? Rand(1.4f, 2.2f) : Rand(2f, 3.2f);

        var t = new GameObject(conifer ? "Pine" : "Tree");
        t.transform.SetParent(parent, false);
        t.transform.localPosition = new Vector3(x, y0, z);
        t.layer = obstacleLayer;
        MakeStatic(t);
        objectCount++;

        float trunkH = h * 0.45f;
        Cyl(t.transform, "Trunk", new Vector3(0, trunkH / 2, 0), new Vector3(0.45f, trunkH / 2, 0.45f), "Trunk", false);

        float canopyH = conifer ? h * 0.85f : r * 1.8f;
        float canopyY = trunkH + canopyH * 0.35f;
        var canopy = GameObject.CreatePrimitive(conifer ? PrimitiveType.Capsule : PrimitiveType.Sphere);
        canopy.name = "Canopy";
        canopy.transform.SetParent(t.transform, false);
        canopy.transform.localPosition = new Vector3(0, canopyY, 0);
        canopy.transform.localScale = conifer ? new Vector3(r * 2, canopyH / 2, r * 2) : new Vector3(r * 2, canopyH, r * 2);
        Paint(canopy, conifer ? "Pine" : "Leaf");
        Object.DestroyImmediate(canopy.GetComponent<Collider>());
        MakeStatic(canopy);
        objectCount++;

        float top = canopyY + canopyH / 2;
        var bc = t.AddComponent<BoxCollider>();
        bc.center = new Vector3(0, top / 2, 0);
        bc.size = new Vector3(r * 1.6f, top, r * 1.6f);
    }

    // ===================================================
    // HELPERS
    // ===================================================

    static Transform Group(GameObject parent, string name)
    {
        var g = new GameObject(name);
        g.transform.SetParent(parent.transform, false);
        return g.transform;
    }

    static GameObject Box(Transform parent, string name, Vector3 pos, Vector3 size, string mat, bool obstacle)
        => Prim(PrimitiveType.Cube, parent, name, pos, size, mat, obstacle);

    // Cylinder primitive is 2 units tall: pass half the height in scale.y
    static GameObject Cyl(Transform parent, string name, Vector3 pos, Vector3 scale, string mat, bool obstacle)
        => Prim(PrimitiveType.Cylinder, parent, name, pos, scale, mat, obstacle);

    static GameObject Prim(PrimitiveType type, Transform parent, string name, Vector3 pos, Vector3 scale, string mat, bool obstacle)
    {
        var g = GameObject.CreatePrimitive(type);
        g.name = name;
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        Paint(g, mat);

        var col = g.GetComponent<Collider>();
        if (obstacle)
        {
            g.layer = obstacleLayer;
            if (!(col is BoxCollider))
            {
                Object.DestroyImmediate(col);
                g.AddComponent<BoxCollider>();
            }
        }
        else
        {
            Object.DestroyImmediate(col);
        }

        MakeStatic(g);
        objectCount++;
        return g;
    }

    static void MakeStatic(GameObject g)
    {
        GameObjectUtility.SetStaticEditorFlags(g, StaticEditorFlags.BatchingStatic);
    }

    static void Paint(GameObject g, string mat)
    {
        var r = g.GetComponent<Renderer>();
        if (r != null) r.sharedMaterial = Mat(mat);
    }

    static bool IsBlocked(float x, float z, float margin)
    {
        foreach (var r in blocked)
            if (x > r.xMin - margin && x < r.xMax + margin && z > r.yMin - margin && z < r.yMax + margin)
                return true;
        return false;
    }

    static float GroundHeight(float x, float z)
    {
        float y = 0;
        foreach (var h in hills)
        {
            float d2 = ((x - h.x) * (x - h.x) + (z - h.y) * (z - h.y)) / (h.z * h.z);
            if (d2 < 1) y = Mathf.Max(y, h.w * Mathf.Sqrt(1 - d2));
        }
        return y;
    }

    static float Rand(float a, float b) => a + (float)rng.NextDouble() * (b - a);
    static string Pick(params string[] options) => options[rng.Next(options.Length)];

    // ===================================================
    // MATERIALS (saved as assets so the scene keeps them)
    // ===================================================

    static Material Mat(string name)
    {
        if (mats.TryGetValue(name, out var m)) return m;

        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
            AssetDatabase.CreateFolder("Assets", "Materials");
        if (!AssetDatabase.IsValidFolder(MatDir))
            AssetDatabase.CreateFolder("Assets/Materials", "OspreyWorld");

        string path = $"{MatDir}/{name}.mat";
        m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            var shader = Shader.Find("HDRP/Lit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            m = new Material(shader);
            AssetDatabase.CreateAsset(m, path);
        }

        Color c; float smooth;
        Palette(name, out c, out smooth);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
        EditorUtility.SetDirty(m);

        mats[name] = m;
        return m;
    }

    static void Palette(string name, out Color c, out float smooth)
    {
        smooth = 0.25f;
        switch (name)
        {
            case "Grass":      c = new Color(0.34f, 0.52f, 0.25f); break;
            case "GrassDark":  c = new Color(0.25f, 0.42f, 0.20f); break;
            case "Concrete":   c = new Color(0.62f, 0.62f, 0.60f); break;
            case "Asphalt":    c = new Color(0.17f, 0.17f, 0.19f); smooth = 0.15f; break;
            case "Marking":    c = new Color(0.95f, 0.95f, 0.92f); break;
            case "Building1":  c = new Color(0.74f, 0.71f, 0.66f); break;
            case "Building2":  c = new Color(0.55f, 0.58f, 0.63f); break;
            case "Building3":  c = new Color(0.80f, 0.73f, 0.60f); break;
            case "Building4":  c = new Color(0.44f, 0.46f, 0.50f); break;
            case "Glass":      c = new Color(0.33f, 0.48f, 0.60f); smooth = 0.85f; break;
            case "Metal":      c = new Color(0.52f, 0.55f, 0.58f); smooth = 0.55f; break;
            case "Roof":       c = new Color(0.58f, 0.22f, 0.18f); break;
            case "Water":      c = new Color(0.16f, 0.40f, 0.62f); smooth = 0.9f; break;
            case "Sand":       c = new Color(0.78f, 0.72f, 0.55f); break;
            case "Trunk":      c = new Color(0.40f, 0.27f, 0.16f); break;
            case "Leaf":       c = new Color(0.22f, 0.50f, 0.22f); break;
            case "Pine":       c = new Color(0.13f, 0.36f, 0.20f); break;
            case "Field":      c = new Color(0.80f, 0.69f, 0.38f); break;
            case "FieldGreen": c = new Color(0.47f, 0.62f, 0.28f); break;
            case "Wood":       c = new Color(0.62f, 0.46f, 0.30f); break;
            case "Rescue":     c = new Color(1.00f, 0.45f, 0.05f); smooth = 0.4f; break;
            default:           c = Color.magenta; break;
        }
    }
}
