using UnityEngine;

public class FormationManager : MonoBehaviour
{
    public enum FormationType
    {
        V,
        Line,
        Diamond,
        Wedge,
        EchelonLeft,
        EchelonRight,
        Column,
        Circle,
        Grid,
        Arrowhead
    }

    [Header("References")]
    public Transform leader;

    [Header("Formation")]
    public FormationType currentFormation = FormationType.V;

    [Header("Settings")]
    public float spacing = 3f;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1)) currentFormation = FormationType.V;
        if (Input.GetKeyDown(KeyCode.Alpha2)) currentFormation = FormationType.Line;
        if (Input.GetKeyDown(KeyCode.Alpha3)) currentFormation = FormationType.Diamond;
        if (Input.GetKeyDown(KeyCode.Alpha4)) currentFormation = FormationType.Wedge;
        if (Input.GetKeyDown(KeyCode.Alpha5)) currentFormation = FormationType.EchelonLeft;
        if (Input.GetKeyDown(KeyCode.Alpha6)) currentFormation = FormationType.EchelonRight;
        if (Input.GetKeyDown(KeyCode.Alpha7)) currentFormation = FormationType.Column;
        if (Input.GetKeyDown(KeyCode.Alpha8)) currentFormation = FormationType.Circle;
        if (Input.GetKeyDown(KeyCode.Alpha9)) currentFormation = FormationType.Grid;
        if (Input.GetKeyDown(KeyCode.Alpha0)) currentFormation = FormationType.Arrowhead;
    }

    // Followers call this
    public Vector3 GetOffset(int id)
    {
        return GetLocalCoordinate(id);
    }

    // Matrix based world target (future use)
    public Vector3 GetWorldTarget(int id)
    {
        Vector3 local = GetLocalCoordinate(id);

        Matrix4x4 T = Matrix4x4.TRS(
            leader.position,
            leader.rotation,
            Vector3.one);

        return T.MultiplyPoint3x4(local);
    }

    Vector3 GetLocalCoordinate(int id)
    {
        switch (currentFormation)
        {
            case FormationType.V:             return GenerateV(id);
            case FormationType.Line:          return GenerateLine(id);
            case FormationType.Diamond:       return GenerateDiamond(id);
            case FormationType.Wedge:         return GenerateWedge(id);
            case FormationType.EchelonLeft:   return GenerateEchelon(id, -1);
            case FormationType.EchelonRight:  return GenerateEchelon(id, 1);
            case FormationType.Column:        return GenerateColumn(id);
            case FormationType.Circle:        return GenerateCircle(id);
            case FormationType.Grid:          return GenerateGrid(id);
            case FormationType.Arrowhead:     return GenerateArrowhead(id);
        }

        return Vector3.zero;
    }

    // ---------------- V ----------------

    Vector3 GenerateV(int id)
    {
        int row = id / 2 + 1;
        float side = (id % 2 == 0) ? -1 : 1;

        return new Vector3(
            side * row * spacing,
            0,
            -row * spacing
        );
    }

    // -------------- Line --------------

    Vector3 GenerateLine(int id)
    {
        return new Vector3(0, 0, -(id + 1) * spacing);
    }

    // ------------ Diamond -------------

    Vector3 GenerateDiamond(int id)
    {
        if (id == 0) return new Vector3(-spacing,0,-spacing);
        if (id == 1) return new Vector3( spacing,0,-spacing);

        int row = id - 1;

        return new Vector3(
            0,
            0,
            -(row + 1) * spacing
        );
    }

    // ------------- Wedge -------------

    Vector3 GenerateWedge(int id)
    {
        int row = id / 2 + 1;
        float side = (id % 2 == 0) ? -1 : 1;

        return new Vector3(
            side * row * spacing,
            0,
            -spacing
        );
    }

    // ---------- Echelon -------------

    Vector3 GenerateEchelon(int id, int direction)
    {
        int row = id + 1;

        return new Vector3(
            direction * row * spacing,
            0,
            -row * spacing
        );
    }

    // ----------- Column -------------

    Vector3 GenerateColumn(int id)
    {
        return new Vector3(
            0,
            0,
            -(id + 1) * spacing
        );
    }

    // ----------- Circle -------------

    Vector3 GenerateCircle(int id)
    {
        float radius = spacing * 2f;

        float angle = (360f / 20f) * id;

        float rad = angle * Mathf.Deg2Rad;

        return new Vector3(
            Mathf.Cos(rad) * radius,
            0,
            Mathf.Sin(rad) * radius
        );
    }

    // ------------ Grid -------------

    Vector3 GenerateGrid(int id)
    {
        int cols = Mathf.CeilToInt(Mathf.Sqrt(20));

        int x = id % cols;
        int z = id / cols;

        return new Vector3(
            (x - cols / 2f) * spacing,
            0,
            -z * spacing
        );
    }

    // -------- Arrowhead ------------

    Vector3 GenerateArrowhead(int id)
    {
        if (id == 0) return new Vector3(0,0,-spacing);

        int row = (id + 1) / 2;
        float side = (id % 2 == 1) ? -1 : 1;

        return new Vector3(
            side * row * spacing,
            0,
            -(row + 1) * spacing
        );
    }
}