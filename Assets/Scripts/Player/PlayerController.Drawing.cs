using UnityEngine;
using UnityEngine.InputSystem;

public partial class PlayerController : MonoBehaviour
{
    void ToggleDrawMode()
    {
        if (isDrawingMode)
            ExitDrawMode(true);
        else
            EnterDrawMode();
    }

    public void SetMobileMoveInput(Vector2 input)
    {
        if (battleInputLocked)
        {
            mobileMoveInput = Vector2.zero;
            return;
        }

        mobileMoveInput = Vector2.ClampMagnitude(input, 1f);
    }

    public void AddMobileLookInput(Vector2 delta)
    {
        if (IsLookInputBlocked())
            return;

        mobileLookInput += delta;
    }

    public void MobileJump()
    {
        if (!IsGameplayInputPaused() && !battleInputLocked)
            TryJump();
    }

    public void SetBattleInputLocked(bool locked)
    {
        if (battleInputLocked == locked)
            return;

        battleInputLocked = locked;
        moveInput = Vector2.zero;
        lookInput = Vector2.zero;
        mobileMoveInput = Vector2.zero;
        mobileLookInput = Vector2.zero;
        lastJumpPressedAt = -999f;

        if (locked)
        {
            horizontalVelocity = Vector3.zero;
            animatorHorizontalVelocity = Vector3.zero;
            knockbackVelocity = Vector3.zero;
            knockbackEndsAt = -999f;
            TriggerMovementState(false);
        }
    }

    public void MobileOpenLetterForge()
    {
        if (!IsGameplayInputPaused())
            EnterDrawMode(false);
    }

    public void MobileOpenWordForge()
    {
        if (!IsGameplayInputPaused())
            EnterDrawMode(true);
    }

    public void MobileBeginVoiceCast()
    {
        if (!IsGameplayInputPaused() && grammarVoiceCombat != null && grammarVoiceCombat.IsCombatEncounterActive)
            HandleAttackStarted();
    }

    public void MobileEndVoiceCast()
    {
        HandleAttackCanceled();
    }

    void HandleForgeStarted()
    {
        if (isDrawingMode)
        {
            ExitDrawMode(true);
            forgeKeyPressedAt = -1f;
            return;
        }
        forgeKeyPressedAt = Time.unscaledTime;
        forgeHoldTriggered = false;
    }

    void HandleForgeCanceled()
    {
        if (forgeKeyPressedAt < 0f)
            return;
        forgeKeyPressedAt = -1f;
        if (forgeHoldTriggered)
            return;

        EnterDrawMode(false);
    }

    void UpdateForgeHold()
    {
        if (forgeKeyPressedAt < 0f || forgeHoldTriggered || isDrawingMode)
            return;

        if (Time.unscaledTime - forgeKeyPressedAt < Mathf.Max(0f, specialForgeHoldSeconds))
            return;

        forgeHoldTriggered = true;
        forgeKeyPressedAt = -1f;
        EnterDrawMode(true);
    }

    void HandleAttackStarted()
    {
        if (isDrawingMode || TemplateRecorderUI.IsOpen || GrimoireUI.IsOpen || ChestMiniGameState.IsOpen)
        {
            Debug.Log(
                $"[PlayerController] Attack pressed but ignored. drawing={isDrawingMode} templateOpen={TemplateRecorderUI.IsOpen} grimoireOpen={GrimoireUI.IsOpen} chestMiniGameOpen={ChestMiniGameState.IsOpen}");
            return;
        }

        attackInputHeld = true;
        Debug.Log("[PlayerController] Attack pressed. Starting voice cast.");
        grammarVoiceCombat?.BeginHold();
    }

    void HandleAttackCanceled()
    {
        if (!attackInputHeld)
            return;
        attackInputHeld = false;
        Debug.Log("[PlayerController] Attack released. Stopping voice cast.");
        grammarVoiceCombat?.EndHold();
    }

    void UpdateAttackHoldInput()
    {
        if (grammarVoiceCombat == null || !grammarVoiceCombat.IsCombatEncounterActive)
        {
            if (attackInputHeld)
                HandleAttackCanceled();
            return;
        }
        bool pressed = controls != null && controls.Movement.Attack.IsPressed();
        if (pressed == attackInputHeld)
            return;

        if (pressed)
            HandleAttackStarted();
        else
            HandleAttackCanceled();
    }

    void ReloadBindingOverrides()
    {
        controls.Disable();
        controls.asset.RemoveAllBindingOverrides();
        GameSettings.ApplyBindingOverrides(controls.asset);
        controls.Enable();
    }

    public void EnterDrawMode()
    {
        EnterDrawMode(false);
    }

    public void EnterDrawMode(bool specialForge)
    {
        if (isDrawingMode) return;
        ResolveWordActionHandler()?.CancelVoiceCast();

        if (drawController != null && drawController.ActiveMode is ChallengeMode challengeMode)
        {
            if (wordActionHandler == null)
                ResolveWordActionHandler();

            bool hasExistingSession = drawController != null && drawController.HasActiveSession();
            if (!hasExistingSession && wordActionHandler != null && !wordActionHandler.CanForgeSelectedSlot(out string forgeMessage))
            {
                wordActionHandler.SetStatusHint(forgeMessage);
                return;
            }

            if (!hasExistingSession)
            {
                bool grammarBattleForge = wordActionHandler != null && wordActionHandler.IsGrammarBattleFlowActive;
                if (specialForge || grammarBattleForge) challengeMode.PrepareSpecialForge();
                else challengeMode.PrepareLetterForge();
            }
        }

        isDrawingMode      = true;

        FreeCursor();

        if (drawingPanel  != null) drawingPanel.SetActive(true);
        if (drawController != null) drawController.EnterDrawing();
    }

    /// <summary>
    /// Called by F-key toggle (cancel) or DrawController after word submit.
    /// </summary>
    public void ExitDrawMode()
    {
        ExitDrawMode(false);
    }

    public void ExitDrawMode(bool preserveProgress)
    {
        if (!isDrawingMode) return;
        isDrawingMode = false;

        LockCursor();

        if (drawingPanel  != null) drawingPanel.SetActive(false);
        if (drawController != null)
        {
            if (preserveProgress && drawController.HasActiveSession())
                drawController.PauseDrawing();
            else
                drawController.CancelAndReset();
        }
    }

    public void ClearDrawSession()
    {
        isDrawingMode = false;
        LockCursor();

        if (drawingPanel != null)
            drawingPanel.SetActive(false);

        drawController?.CancelAndReset();
    }

    public void PlayAttackAnimation()
    {
        TriggerConfiguredAnimatorTrigger(attackTriggerName);
    }
}
