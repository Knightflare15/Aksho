using System.Collections.Generic;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

#if UNITY_EDITOR
using UnityEditor;
#endif


public sealed partial class LevelGridDirector
{
    void OnRenderObject()
    {
        if (!Application.isPlaying || !drawRuntimeGridInPlay)
            return;

        Material gridMaterial = GetRuntimeGridLineMaterial();
        if (gridMaterial == null)
            return;

        gridMaterial.SetPass(0);

        GL.PushMatrix();
        GL.MultMatrix(Matrix4x4.TRS(transform.position + transform.up * yOffset, transform.rotation, Vector3.one));
        DrawRuntimeGridLines();
        GL.PopMatrix();
    }

    void DrawRuntimeGridLines()
    {
        int width = GridWidth;
        int height = GridHeight;
        float size = CellSize;
        float halfWidth = width * size * 0.5f;
        float halfHeight = height * size * 0.5f;

        GL.Begin(GL.LINES);

        for (int x = 0; x <= width; x++)
        {
            float localX = x * size - halfWidth;
            GL.Color(GetLineColor(x, width));
            GL.Vertex3(localX, 0f, -halfHeight);
            GL.Vertex3(localX, 0f, halfHeight);
        }

        for (int z = 0; z <= height; z++)
        {
            float localZ = z * size - halfHeight;
            GL.Color(GetLineColor(z, height));
            GL.Vertex3(-halfWidth, 0f, localZ);
            GL.Vertex3(halfWidth, 0f, localZ);
        }

        GL.Color(boundsColor);
        GL.Vertex3(-halfWidth, 0f, -halfHeight);
        GL.Vertex3(halfWidth, 0f, -halfHeight);
        GL.Vertex3(halfWidth, 0f, -halfHeight);
        GL.Vertex3(halfWidth, 0f, halfHeight);
        GL.Vertex3(halfWidth, 0f, halfHeight);
        GL.Vertex3(-halfWidth, 0f, halfHeight);
        GL.Vertex3(-halfWidth, 0f, halfHeight);
        GL.Vertex3(-halfWidth, 0f, -halfHeight);

        GL.End();
    }

    Material GetRuntimeGridLineMaterial()
    {
        if (runtimeGridLineMaterial != null)
            return runtimeGridLineMaterial;

        if (fallbackRuntimeGridLineMaterial != null)
            return fallbackRuntimeGridLineMaterial;

        Shader shader = Shader.Find("Hidden/Internal-Colored");
        if (shader == null)
            return null;

        fallbackRuntimeGridLineMaterial = new Material(shader)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        fallbackRuntimeGridLineMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        fallbackRuntimeGridLineMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        fallbackRuntimeGridLineMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        fallbackRuntimeGridLineMaterial.SetInt("_ZWrite", 0);
        fallbackRuntimeGridLineMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);

        return fallbackRuntimeGridLineMaterial;
    }

    static Vector3 ReadMousePosition()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.mousePosition;
#elif ENABLE_INPUT_SYSTEM
        return Mouse.current != null ? (Vector3)Mouse.current.position.ReadValue() : Vector3.zero;
#else
        return Vector3.zero;
#endif
    }

    static bool IsMouseButtonHeld(int button)
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetMouseButton(button))
            return true;
#endif
#if ENABLE_INPUT_SYSTEM
        Mouse mouse = Mouse.current;
        if (mouse == null)
            return false;

        return button switch
        {
            0 => mouse.leftButton.isPressed,
            1 => mouse.rightButton.isPressed,
            2 => mouse.middleButton.isPressed,
            _ => false,
        };
#else
        return false;
#endif
    }

    static bool IsShiftHeld()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            return true;
#endif
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
#else
        return false;
#endif
    }

    static bool WasFreeCameraTogglePressed()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.E))
            return true;
#endif
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard.eKey.wasPressedThisFrame;
#else
        return false;
#endif
    }

    void OnDrawGizmos()
    {
        if (!drawGrid)
            return;

        Matrix4x4 oldMatrix = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(transform.position + Vector3.up * yOffset, transform.rotation, Vector3.one);

        DrawGridLines();
        DrawBounds();

        Gizmos.matrix = oldMatrix;
    }

    void DrawGridLines()
    {
        int width = GridWidth;
        int height = GridHeight;
        float size = CellSize;
        float halfWidth = width * size * 0.5f;
        float halfHeight = height * size * 0.5f;

        for (int x = 0; x <= width; x++)
        {
            float localX = x * size - halfWidth;
            Gizmos.color = GetLineColor(x, width);
            Gizmos.DrawLine(new Vector3(localX, 0f, -halfHeight), new Vector3(localX, 0f, halfHeight));
        }

        for (int z = 0; z <= height; z++)
        {
            float localZ = z * size - halfHeight;
            Gizmos.color = GetLineColor(z, height);
            Gizmos.DrawLine(new Vector3(-halfWidth, 0f, localZ), new Vector3(halfWidth, 0f, localZ));
        }
    }

    void DrawBounds()
    {
        float halfWidth = GridWidth * CellSize * 0.5f;
        float halfHeight = GridHeight * CellSize * 0.5f;

        Gizmos.color = boundsColor;
        Gizmos.DrawLine(new Vector3(-halfWidth, 0f, -halfHeight), new Vector3(halfWidth, 0f, -halfHeight));
        Gizmos.DrawLine(new Vector3(halfWidth, 0f, -halfHeight), new Vector3(halfWidth, 0f, halfHeight));
        Gizmos.DrawLine(new Vector3(halfWidth, 0f, halfHeight), new Vector3(-halfWidth, 0f, halfHeight));
        Gizmos.DrawLine(new Vector3(-halfWidth, 0f, halfHeight), new Vector3(-halfWidth, 0f, -halfHeight));
    }

    Color GetLineColor(int index, int total)
    {
        if (index * 2 == total)
            return centerAxisColor;

        if (index % majorLineInterval == 0)
            return majorLineColor;

        return minorLineColor;
    }
}
