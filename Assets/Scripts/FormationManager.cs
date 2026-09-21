using UnityEngine;

public class FormationManager : MonoBehaviour
{
    public enum FormationType
    {
        V,
        Line,
        Diamond
    }

    public FormationType currentFormation = FormationType.V;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1))
            currentFormation = FormationType.V;

        if (Input.GetKeyDown(KeyCode.Alpha2))
            currentFormation = FormationType.Line;

        if (Input.GetKeyDown(KeyCode.Alpha3))
            currentFormation = FormationType.Diamond;
    }

    public Vector3 GetOffset(int id)
    {
        switch (currentFormation)
        {
            case FormationType.Line:
                return Line(id);

            case FormationType.Diamond:
                return Diamond(id);

            default:
                return V(id);
        }
    }

    Vector3 V(int i)
    {
        Vector3[] p =
        {
            new Vector3(-2,0,-2),
            new Vector3( 2,0,-2),
            new Vector3(-4,0,-4),
            new Vector3( 4,0,-4)
        };

        return p[i];
    }

    Vector3 Line(int i)
    {
        Vector3[] p =
        {
            new Vector3(-3,0,-2),
            new Vector3(-1,0,-2),
            new Vector3( 1,0,-2),
            new Vector3( 3,0,-2)
        };

        return p[i];
    }

    Vector3 Diamond(int i)
    {
        Vector3[] p =
        {
            new Vector3(0,0,-2),
            new Vector3(-2,0,-4),
            new Vector3(2,0,-4),
            new Vector3(0,0,-6)
        };

        return p[i];
    }
}