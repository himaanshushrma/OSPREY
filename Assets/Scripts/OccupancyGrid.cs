using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// OccupancyGrid (v2.0): the swarm's shared 2.5D map of the world.
///
/// The 400 x 400 m world is split into 4 m cells; each cell stores the top height of the
/// obstacles in it. Two layers:
///   - prior  : scanned once at start, like a satellite map (optional, usePriorMap)
///   - sensed : built live from every drone's obstacle sensors and down-looking ray,
///              delivered over SwarmComms (followers out of radio range report nothing)
/// The RRT* planner asks Blocked(point, altitude) against this map; the minimap
/// (toggle M) shows what the swarm has explored, the planned route, search coverage,
/// drones and survivors.
/// </summary>
public class OccupancyGrid : MonoBehaviour
{
    public static OccupancyGrid Instance { get; private set; }

    [Header("Grid")]
    public Vector2 center = Vector2.zero;
    public float size = 400f;
    public float cellSize = 4f;
    [Tooltip("Layers scanned for the prior map. Empty = the 'Obstacle' layer.")]
    public LayerMask obstacleMask = 0;

    [Header("Map sources")]
    [Tooltip("Use a scanned prior map (like satellite data) as well as what the sensors see.")]
    public bool usePriorMap = true;

    [Header("Minimap")]
    public bool showMinimap = true;
    public KeyCode toggleKey = KeyCode.M;

    public int Cells => n;
    public int SeenCells { get; private set; }
    public float ExploredPercent => total > 0 ? 100f * SeenCells / total : 0f;

    int n, total;
    float[] priorTop, sensedTop;
    bool[] seen;

    // Things the minimap overlays
    public static List<Vector3> PlannedRoute = new List<Vector3>();

    Texture2D tex;
    Color32[] pixels;
    float nextTex;

    const float None = -1000f;

    // ===================================================
    // SETUP
    // ===================================================

    void Awake()
    {
        Instance = this;
        if (obstacleMask.value == 0)
        {
            int l = LayerMask.NameToLayer("Obstacle");
            obstacleMask = l >= 0 ? (1 << l) : Physics.DefaultRaycastLayers;
        }

        n = Mathf.CeilToInt(size / cellSize);
        total = n * n;
        priorTop = new float[total];
        sensedTop = new float[total];
        seen = new bool[total];
        for (int i = 0; i < total; i++) { priorTop[i] = None; sensedTop[i] = None; }

        ScanPrior();

        tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        pixels = new Color32[total];
    }

    void ScanPrior()
    {
        int blocked = 0;
        Vector3 half = new Vector3(cellSize * 0.5f, 0.05f, cellSize * 0.5f);
        for (int iz = 0; iz < n; iz++)
        for (int ix = 0; ix < n; ix++)
        {
            Vector3 c = CellCenter(ix, iz);
            if (Physics.BoxCast(new Vector3(c.x, 300f, c.z), half, Vector3.down, out RaycastHit hit,
                                Quaternion.identity, 400f, obstacleMask, QueryTriggerInteraction.Ignore))
            {
                priorTop[iz * n + ix] = hit.point.y;
                blocked++;
            }
        }
        Debug.Log($"[OccupancyGrid] {n}x{n} cells of {cellSize} m. Prior map: {blocked} obstacle cells. " +
                  $"Sensor mapping on; prior map {(usePriorMap ? "used" : "ignored")} for planning.");
    }

    // ===================================================
    // GEOMETRY
    // ===================================================

    public Vector3 CellCenter(int ix, int iz)
    {
        float x0 = center.x - size * 0.5f, z0 = center.y - size * 0.5f;
        return new Vector3(x0 + (ix + 0.5f) * cellSize, 0, z0 + (iz + 0.5f) * cellSize);
    }

    public bool ToCell(Vector3 p, out int ix, out int iz)
    {
        ix = Mathf.FloorToInt((p.x - (center.x - size * 0.5f)) / cellSize);
        iz = Mathf.FloorToInt((p.z - (center.y - size * 0.5f)) / cellSize);
        return ix >= 0 && iz >= 0 && ix < n && iz < n;
    }

    public Rect Bounds => new Rect(center.x - size * 0.5f, center.y - size * 0.5f, size, size);

    /// <summary>Highest obstacle top known in a cell (sensed, plus prior if enabled).</summary>
    public float KnownTop(int ix, int iz)
    {
        int i = iz * n + ix;
        float t = sensedTop[i];
        if (usePriorMap && priorTop[i] > t) t = priorTop[i];
        return t;
    }

    /// <summary>
    /// True if a drone at p (its y = flight altitude) would come within `clearance` (vertical)
    /// of an obstacle anywhere within `inflate` metres horizontally. Outside the map = blocked.
    /// </summary>
    public bool Blocked(Vector3 p, float clearance, float inflate)
    {
        int r = Mathf.CeilToInt(inflate / cellSize);
        if (!ToCell(p, out int cx, out int cz)) return true;
        for (int dz = -r; dz <= r; dz++)
        for (int dx = -r; dx <= r; dx++)
        {
            int ix = cx + dx, iz = cz + dz;
            if (ix < 0 || iz < 0 || ix >= n || iz >= n) continue;   // nothing known beyond the edge
            if (dx * dx + dz * dz > (r + 0.5f) * (r + 0.5f)) continue;
            if (KnownTop(ix, iz) > p.y - clearance) return true;
        }
        return false;
    }

    // ===================================================
    // SENSOR UPDATES
    // ===================================================

    public void ReportHit(Vector3 point, float top)
    {
        if (!ToCell(point, out int ix, out int iz)) return;
        int i = iz * n + ix;
        if (!seen[i]) { seen[i] = true; SeenCells++; }
        if (top > sensedTop[i]) sensedTop[i] = top;
    }

    /// <summary>Mark cells along a clear ray as seen (free at that height).</summary>
    public void ReportFree(Vector3 from, Vector3 to)
    {
        Vector3 d = to - from;
        d.y = 0;
        float len = d.magnitude;
        int steps = Mathf.Max(1, Mathf.CeilToInt(len / (cellSize * 0.7f)));
        for (int s = 0; s <= steps; s++)
        {
            Vector3 p = from + d * (s / (float)steps);
            if (!ToCell(p, out int ix, out int iz)) continue;
            int i = iz * n + ix;
            if (!seen[i]) { seen[i] = true; SeenCells++; }
        }
    }

    /// <summary>Down-looking ray: the cell below is seen, with its ground/roof height.</summary>
    public void ReportGround(Vector3 point)
    {
        if (!ToCell(point, out int ix, out int iz)) return;
        int i = iz * n + ix;
        if (!seen[i]) { seen[i] = true; SeenCells++; }
        if (point.y > 0.5f && point.y > sensedTop[i]) sensedTop[i] = point.y;
    }

    // ===================================================
    // MINIMAP
    // ===================================================

    void Update()
    {
        if (Input.GetKeyDown(toggleKey)) showMinimap = !showMinimap;
    }

    void RefreshTexture()
    {
        for (int i = 0; i < total; i++)
        {
            float top = sensedTop[i];
            Color32 c;
            if (seen[i])
            {
                if (top > 2f)
                {
                    byte k = (byte)Mathf.Clamp(120 + top * 2.2f, 120, 255);
                    c = new Color32(k, (byte)(k * 0.42f), 40, 255);              // sensed obstacle
                }
                else c = new Color32(34, 78, 52, 255);                            // sensed free
            }
            else if (usePriorMap && priorTop[i] > 2f) c = new Color32(52, 58, 84, 255); // prior only
            else c = new Color32(16, 22, 43, 255);                                // unknown
            pixels[i] = c;
        }

        var search = SearchMission.Instance;
        if (search != null && search.HasArea) search.Tint(this, pixels, n);

        foreach (var p in PlannedRoute)
            if (ToCell(p, out int ix, out int iz)) pixels[iz * n + ix] = new Color32(255, 214, 64, 255);

        tex.SetPixels32(pixels);
        tex.Apply(false);
    }

    static Texture2D dot;
    static GUIStyle titleStyle;

    void OnGUI()
    {
        if (!showMinimap || tex == null) return;
        if (Time.unscaledTime >= nextTex) { nextTex = Time.unscaledTime + 0.4f; RefreshTexture(); }
        if (dot == null) { dot = new Texture2D(1, 1); dot.SetPixel(0, 0, Color.white); dot.Apply(); }
        if (titleStyle == null) titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, richText = true };

        const float S = 300f;
        var box = new Rect(Screen.width - 330, 84, 320, S + 46);
        GUI.Box(box, GUIContent.none);
        GUI.Label(new Rect(box.x + 8, box.y + 4, 310, 20),
            $"<b>Swarm map</b>  explored {ExploredPercent:0}%   <color=#AAAAAA>[M] hide</color>", titleStyle);
        var map = new Rect(box.x + 10, box.y + 26, S, S);
        // Texture row 0 = south; GUI y grows downward, so flip vertically
        GUI.DrawTextureWithTexCoords(map, tex, new Rect(0, 0, 1, 1));

        // Drones
        var mission = FindFirstObjectByType<MissionManager>();
        if (mission != null && mission.leader != null) Dot(map, mission.leader.transform.position, new Color(1f, 0.55f, 0.24f), 6);
        var swarm = FindFirstObjectByType<SwarmManager>();
        if (swarm != null)
            foreach (var f in swarm.followers)
                if (f != null) Dot(map, f.transform.position, f.Alive ? (f.LinkOnline ? new Color(0.24f, 0.86f, 0.6f) : new Color(1f, 0.85f, 0.2f)) : new Color(0.9f, 0.25f, 0.25f), 4);

        // Survivors found
        var det = mission != null && mission.leader != null ? mission.leader.GetComponent<ObjectDetector>() : null;
        if (det != null) foreach (var s in det.SurvivorsFound) Dot(map, s, Color.white, 6);

        GUI.Label(new Rect(box.x + 8, box.y + S + 26, 310, 20),
            "<color=#FF8C3D>■</color> leader  <color=#3DDC97>■</color> drone  <color=#FFD640>■</color> route  <color=#FFFFFF>■</color> survivor", titleStyle);
    }

    void Dot(Rect map, Vector3 world, Color c, float px)
    {
        Rect b = Bounds;
        float u = (world.x - b.xMin) / b.width, v = (world.z - b.yMin) / b.height;
        if (u < 0 || v < 0 || u > 1 || v > 1) return;
        float x = map.x + u * map.width, y = map.y + (1 - v) * map.height;
        GUI.color = c;
        GUI.DrawTexture(new Rect(x - px / 2, y - px / 2, px, px), dot);
        GUI.color = Color.white;
    }
}
