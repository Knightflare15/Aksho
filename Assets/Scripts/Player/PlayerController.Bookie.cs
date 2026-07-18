using UnityEngine;
using UnityEngine.InputSystem;

public partial class PlayerController : MonoBehaviour
{
    void ResolveBookieReferences()
    {
        if (bookieTransform == null)
            bookieTransform = FindChildRecursive(transform, "Bookie");

        if (bookieRenderer == null && bookieTransform != null)
            bookieRenderer = bookieTransform.GetComponentInChildren<SkinnedMeshRenderer>(true);

        if (bookieRenderer == null)
            bookieRenderer = FindBookieRendererInChildren();

        if (bookieTransform == null && bookieRenderer != null)
            bookieTransform = bookieRenderer.transform;

        bookieCloseBlendShapeIndex = FindBlendShapeIndex(bookieCloseBlendShapeName);
        WarnForMissingBookieSetup();
        if (!bookieToggleBlendShapeInitialized && IsValidBookieBlendShape(bookieToggleBlendShapeIndex))
        {
            float currentWeight = bookieRenderer.GetBlendShapeWeight(bookieToggleBlendShapeIndex);
            float midpoint = (bookieClosedWeight + bookieOpenWeight) * 0.5f;
            bookieToggleBlendShapeOn = currentWeight >= midpoint;
            bookieToggleTargetWeight = bookieToggleBlendShapeOn ? bookieClosedWeight : bookieOpenWeight;
            bookieToggleBlendShapeInitialized = true;
        }
        else if (!IsValidBookieBlendShape(bookieToggleBlendShapeIndex))
        {
            bookieToggleBlendShapeInitialized = false;
        }
    }

    void UpdateBookieSlotToggleBlendShape()
    {
        ResolveBookieReferences();
        if (!IsValidBookieBlendShape(bookieToggleBlendShapeIndex))
            return;
        if (BookieCloseAndToggleShareBlendShape())
            return;

        if (!bookieToggleBlendShapeInitialized)
            bookieToggleTargetWeight = bookieRenderer.GetBlendShapeWeight(bookieToggleBlendShapeIndex);

        float currentWeight = bookieRenderer.GetBlendShapeWeight(bookieToggleBlendShapeIndex);
        float maxDelta = Mathf.Abs(bookieClosedWeight - bookieOpenWeight) /
                         Mathf.Max(0.01f, bookieToggleBlendSeconds) *
                         Time.deltaTime;
        float nextWeight = Mathf.MoveTowards(currentWeight, bookieToggleTargetWeight, maxDelta);
        bookieRenderer.SetBlendShapeWeight(bookieToggleBlendShapeIndex, nextWeight);
    }

    void SetBookieToggleBlendTarget()
    {
        ResolveBookieReferences();
        if (!IsValidBookieBlendShape(bookieToggleBlendShapeIndex))
            return;

        if (!bookieToggleBlendShapeInitialized)
        {
            bookieToggleBlendShapeOn = bookieRenderer.GetBlendShapeWeight(bookieToggleBlendShapeIndex) >=
                                       (bookieClosedWeight + bookieOpenWeight) * 0.5f;
            bookieToggleBlendShapeInitialized = true;
        }

        bookieToggleBlendShapeOn = !bookieToggleBlendShapeOn;
        bookieToggleTargetWeight = bookieToggleBlendShapeOn ? bookieClosedWeight : bookieOpenWeight;
    }

    void UpdateBookieMovementBlendShapes()
    {
        ResolveBookieReferences();
        if (!IsValidBookieBlendShape(bookieCloseBlendShapeIndex))
            return;

        bool isRunning = moveInput.sqrMagnitude > 0.001f;
        bool isJumping = controller != null && (!controller.isGrounded || yVelocity > 0.05f);
        float targetWeight = isRunning || isJumping
            ? bookieClosedWeight
            : BookieCloseAndToggleShareBlendShape()
                ? bookieToggleTargetWeight
                : bookieOpenWeight;
        float currentWeight = bookieRenderer.GetBlendShapeWeight(bookieCloseBlendShapeIndex);
        float maxDelta = Mathf.Abs(bookieClosedWeight - bookieOpenWeight) /
                         Mathf.Max(0.01f, bookieMovementBlendSeconds) *
                         Time.deltaTime;
        float nextWeight = Mathf.MoveTowards(currentWeight, targetWeight, maxDelta);
        bookieRenderer.SetBlendShapeWeight(bookieCloseBlendShapeIndex, nextWeight);
    }

    void UpdateBookieSlotToggleInput()
    {
        if (!Input.GetKeyDown(KeyCode.Q) && !Input.GetKeyDown(KeyCode.E))
            return;

        ResolveBookieReferences();
        if (!IsValidBookieBlendShape(bookieToggleBlendShapeIndex))
            return;

        SetBookieToggleBlendTarget();
    }

    SkinnedMeshRenderer FindBookieRendererInChildren()
    {
        SkinnedMeshRenderer[] renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (SkinnedMeshRenderer candidate in renderers)
        {
            if (HasTransformNamed(candidate.transform, "Bookie"))
                return candidate;
        }

        return null;
    }

    int FindBlendShapeIndex(string blendShapeName)
    {
        if (bookieRenderer == null || bookieRenderer.sharedMesh == null || string.IsNullOrWhiteSpace(blendShapeName))
            return -1;

        int exactIndex = bookieRenderer.sharedMesh.GetBlendShapeIndex(blendShapeName);
        if (exactIndex >= 0)
            return exactIndex;

        string normalizedTarget = NormalizeBlendShapeName(blendShapeName);
        for (int i = 0; i < bookieRenderer.sharedMesh.blendShapeCount; i++)
        {
            string candidate = bookieRenderer.sharedMesh.GetBlendShapeName(i);
            string normalizedCandidate = NormalizeBlendShapeName(candidate);
            if (string.Equals(normalizedCandidate, normalizedTarget, System.StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    bool BookieCloseAndToggleShareBlendShape()
    {
        return IsValidBookieBlendShape(bookieCloseBlendShapeIndex) &&
               bookieCloseBlendShapeIndex == bookieToggleBlendShapeIndex;
    }

    void WarnForMissingBookieSetup()
    {
        if (bookieRenderer == null)
        {
            if (!warnedMissingBookieRenderer)
            {
                Debug.LogWarning("[PlayerController] Bookie SkinnedMeshRenderer was not found under the player.");
                warnedMissingBookieRenderer = true;
            }

            return;
        }

        if (bookieCloseBlendShapeIndex < 0 && !warnedMissingBookieCloseBlendShape)
        {
            Debug.LogWarning(
                $"[PlayerController] Bookie blend shape '{bookieCloseBlendShapeName}' was not found on {bookieRenderer.name}. Available: {GetBookieBlendShapeNames()}");
            warnedMissingBookieCloseBlendShape = true;
        }

        if (bookieCloseBlendShapeIndex >= 0 && !loggedBookieBlendShapeSetup)
        {
            Debug.Log(
                $"[PlayerController] Bookie close blend shape resolved to index {bookieCloseBlendShapeIndex} ({bookieRenderer.sharedMesh.GetBlendShapeName(bookieCloseBlendShapeIndex)}). Toggle index is {bookieToggleBlendShapeIndex}.");
            loggedBookieBlendShapeSetup = true;
        }
    }

    string GetBookieBlendShapeNames()
    {
        if (bookieRenderer == null || bookieRenderer.sharedMesh == null)
            return "none";

        System.Text.StringBuilder names = new System.Text.StringBuilder();
        for (int i = 0; i < bookieRenderer.sharedMesh.blendShapeCount; i++)
        {
            if (i > 0)
                names.Append(", ");

            names.Append(i);
            names.Append(":");
            names.Append(bookieRenderer.sharedMesh.GetBlendShapeName(i));
        }

        return names.Length > 0 ? names.ToString() : "none";
    }

    static string NormalizeBlendShapeName(string blendShapeName)
    {
        if (string.IsNullOrWhiteSpace(blendShapeName))
            return string.Empty;

        int lastDot = blendShapeName.LastIndexOf('.');
        int lastSlash = blendShapeName.LastIndexOf('/');
        int separator = Mathf.Max(lastDot, lastSlash);
        return separator >= 0 && separator < blendShapeName.Length - 1
            ? blendShapeName.Substring(separator + 1)
            : blendShapeName;
    }

    bool IsValidBookieBlendShape(int blendShapeIndex)
    {
        return bookieRenderer != null &&
               bookieRenderer.sharedMesh != null &&
               blendShapeIndex >= 0 &&
               blendShapeIndex < bookieRenderer.sharedMesh.blendShapeCount;
    }

    static Transform FindChildRecursive(Transform root, string childName)
    {
        if (root == null)
            return null;

        foreach (Transform child in root)
        {
            if (child.name == childName)
                return child;

            Transform nested = FindChildRecursive(child, childName);
            if (nested != null)
                return nested;
        }

        return null;
    }

    static bool HasTransformNamed(Transform root, string name)
    {
        while (root != null)
        {
            if (root.name == name)
                return true;

            root = root.parent;
        }

        return false;
    }
}
