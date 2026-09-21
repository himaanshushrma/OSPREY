using UnityEngine;

public class FormationManager : MonoBehaviour
{
    public enum FormationType
    {
        V,
        Line,
        Column
    }

    public FormationType currentFormation = FormationType.V;

    [Header("Spacing")]
    public float spacing = 2.5f;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1))
            currentFormation = FormationType.V;

        if (Input.GetKeyDown(KeyCode.Alpha2))
            currentFormation = FormationType.Line;

        if (Input.GetKeyDown(KeyCode.Alpha3))
            currentFormation = FormationType.Column;
    }

    public Vector3 GetOffset(int id)
    {
        switch (currentFormation)
        {
            case FormationType.Line:
                return Line(id);

            case FormationType.Column:
                return Column(id);

            default:
                return V(id);
        }
    }

    Vector3 V(int id)
    {
        switch (id)
        {
            case 0: return new Vector3(-spacing, 0, -spacing);
            case 1: return new Vector3(spacing, 0, -spacing);
            case 2: return new Vector3(-2 * spacing, 0, -2 * spacing);
            case 3: return new Vector3(2 * spacing, 0, -2 * spacing);
        }

        return Vector3.zero;
    }

    Vector3 Line(int id)
    {
        float x = (-1.5f + id) * spacing;
        return new Vector3(x, 0, -spacing);
    }

    Vector3 Column(int id)
    {
        return new Vector3(0, 0, -(id + 1) * spacing);
    }
}