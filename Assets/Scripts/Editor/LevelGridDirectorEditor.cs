using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[CustomEditor(typeof(LevelGridDirector))]
public sealed class LevelGridDirectorEditor : Editor
{
    static bool paintMode;
    static readonly List<Vector2Int> brushCells = new List<Vector2Int>();
    static readonly HashSet<Vector2Int> editStrokeCells = new HashSet<Vector2Int>();

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        LevelGridDirector director = (LevelGridDirector)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Cell Painting", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Empty cells are water. Brush size is 2x2 to 50x50. Brush strength is the peak height change at the brush center and fades outward with a normal distribution. 1-65 uses the active grass/dirt brush, 66-85 forces dirt, 86-100 forces rock. Terrain Render Mode can emit exposed block faces or a smooth surface-net mesh with optional cliff preservation; enable Show Debug Cubes when you want to inspect raw block columns. Shift or right-click lowers. In Play mode, press E to switch between top-down painting and the perspective fly camera.",
            MessageType.Info);

        EditorGUILayout.LabelField("Painted land cells", director.PaintedCellCount.ToString());

        bool nextPaintMode = GUILayout.Toggle(paintMode, paintMode ? "Paint Mode: On" : "Paint Mode: Off", "Button");
        if (nextPaintMode != paintMode)
        {
            paintMode = nextPaintMode;
            SceneView.RepaintAll();
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Rebuild Terrain"))
            {
                Undo.RegisterFullObjectHierarchyUndo(director.gameObject, "Rebuild Painted Cells");
                director.RebuildPaintedCellObjects();
                MarkDirty(director);
            }

            if (GUILayout.Button("Clear Painted Cells") &&
                EditorUtility.DisplayDialog("Clear Painted Cells", "Remove all painted land cells from this grid?", "Clear", "Cancel"))
            {
                Undo.RegisterFullObjectHierarchyUndo(director.gameObject, "Clear Painted Cells");
                director.ClearPaintedCells();
                MarkDirty(director);
            }
        }

    }

    void OnSceneGUI()
    {
        if (!paintMode)
            return;

        LevelGridDirector director = (LevelGridDirector)target;
        Event current = Event.current;

        if (current.type == EventType.MouseUp)
            editStrokeCells.Clear();

        if (!current.alt && current.type == EventType.Layout)
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

        if (!TryGetCellUnderMouse(director, current.mousePosition, out Vector2Int cell))
            return;

        bool erase = current.shift || current.button == 1;
        director.GetBrushCells(cell, brushCells);
        DrawCellPreview(director, brushCells, erase);

        if (current.alt)
            return;

        if ((current.type == EventType.MouseDown || current.type == EventType.MouseDrag) &&
            (current.button == 0 || current.button == 1))
        {
            if (current.type == EventType.MouseDown)
                editStrokeCells.Clear();

            PaintOrEraseCells(director, cell, brushCells, erase);
            current.Use();
        }
    }

    static void PaintOrEraseCells(LevelGridDirector director, Vector2Int anchorCell, List<Vector2Int> cells, bool erase)
    {
        Undo.RegisterCompleteObjectUndo(director, erase ? "Lower Level Cells" : "Raise Level Cells");

        bool changed = false;
        for (int i = 0; i < cells.Count; i++)
        {
            Vector2Int cell = cells[i];
            if (!editStrokeCells.Add(cell))
                continue;

            int amount = director.GetBrushAmountForCell(anchorCell, cell);
            changed |= erase ? director.LowerCellHeight(cell, amount) : director.RaiseCellHeight(cell, amount);
        }

        if (!changed)
            return;

        MarkDirty(director);
        SceneView.RepaintAll();
    }

    static bool TryGetCellUnderMouse(LevelGridDirector director, Vector2 mousePosition, out Vector2Int cell)
    {
        Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);
        return director.TryGetCellFromWorldRay(ray, out cell);
    }

    static void DrawCellPreview(LevelGridDirector director, List<Vector2Int> cells, bool erase)
    {
        Color paintColor = director.ActiveSurfaceBrush == LevelGridDirector.SurfaceBrush.Dirt
            ? new Color(0.55f, 0.32f, 0.14f, 1f)
            : new Color(0.3f, 1f, 0.35f, 1f);
        Color fill = erase
            ? new Color(1f, 0.2f, 0.12f, 0.18f)
            : new Color(paintColor.r, paintColor.g, paintColor.b, 0.18f);
        Color outline = erase
            ? new Color(1f, 0.2f, 0.12f, 0.8f)
            : new Color(paintColor.r, paintColor.g, paintColor.b, 0.8f);

        Vector3 right = director.transform.right * director.CellSize * 0.5f;
        Vector3 forward = director.transform.forward * director.CellSize * 0.5f;

        for (int i = 0; i < cells.Count; i++)
        {
            Vector2Int cell = cells[i];
            Vector3 center = director.GridToWorld(cell.x, cell.y) + director.transform.up * 0.03f;
            Vector3[] corners =
            {
                center - right - forward,
                center - right + forward,
                center + right + forward,
                center + right - forward
            };

            Handles.DrawSolidRectangleWithOutline(corners, fill, outline);
        }
    }

    static void MarkDirty(LevelGridDirector director)
    {
        EditorUtility.SetDirty(director);

        if (!Application.isPlaying)
            EditorSceneManager.MarkSceneDirty(director.gameObject.scene);
    }
}
