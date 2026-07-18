using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Routes spoken/written battle words into grammar creature combat. Legacy spell pages remain for non-grammar modes.
/// </summary>
[RequireComponent(typeof(CreatureCombatController))]

public partial class WordActionHandler : MonoBehaviour
{
    void StopCombatVoiceListening()
    {
        if (voiceCastRecognizer == null ||
            voiceCastRecognizer.ActiveMode != VoiceUnlockRecognizer.VoiceInputMode.CombatAutoListen)
            return;

        voiceCastRecognizer.StopListening();
        voiceCastRecognizer.CombatPronunciationInsightEnabled = true;
    }

    void UpdateCombatPronunciationGate()
    {
        if (voiceCastRecognizer == null)
            return;

        voiceCastRecognizer.CombatPronunciationInsightEnabled = HasSelectedEnemyTarget();
    }

    bool HasSelectedEnemyTarget()
    {
        if (aimAssist == null)
            aimAssist = GetComponent<PlayerAimAssist>() ?? FindAnyObjectByType<PlayerAimAssist>();

        return aimAssist != null && aimAssist.TryGetSelectedTarget(out _);
    }

    bool SpawnProjectile(
        SpellDefinition definition,
        string spellWord,
        SpellTarget target,
        SpellPillarObjective pillarTarget,
        bool area)
    {
        if (definition == null)
            return false;

        Transform aimTransform = spellOrigin != null
            ? spellOrigin
            : Camera.main != null
                ? Camera.main.transform
                : transform;

        Vector3 origin = spellOrigin != null
            ? spellOrigin.position
            : aimTransform.position + aimTransform.forward * 0.6f;

        Vector3 direction = ResolveShotDirection(aimTransform, target, pillarTarget, origin);
        if (direction.sqrMagnitude < 0.001f)
            return false;

        GameObject go = definition.projectilePrefab != null
            ? Instantiate(definition.projectilePrefab, origin, Quaternion.identity)
            : new GameObject($"{spellWord}_Projectile");

        go.transform.position = origin;
        go.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        SpellProjectile.SpawnEffectPrefab(
            definition.castEffectPrefab,
            origin,
            go.transform.rotation,
            definition.projectileColour,
            definition.castEffectLifetime,
            area ? 1.25f : 0.75f,
            "Spell Cast FX");

        var projectile = go.GetComponent<SpellProjectile>();
        if (projectile == null)
            projectile = go.AddComponent<SpellProjectile>();

        projectile.Launch(
            target,
            spellWord,
            direction.normalized,
            definition.projectileSpeed <= 0f ? 16f : definition.projectileSpeed,
            definition.projectileColour,
            area,
            definition.specialAreaRadius,
            definition.impactEffectPrefab,
            definition.areaImpactEffectPrefab,
            definition.impactEffectLifetime,
            pillarTarget);

        return true;
    }

    Vector3 ResolveShotDirection(Transform aimTransform, SpellTarget target, SpellPillarObjective pillarTarget, Vector3 origin)
    {
        if (target != null && !target.IsDefeated)
            return (target.GetAimPoint() - origin).normalized;
        if (pillarTarget != null && pillarTarget.IsTargetable)
            return (pillarTarget.GetAimPoint() - origin).normalized;

        Camera cam = Camera.main;
        if (cam != null)
        {
            Ray aimRay = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            if (Physics.Raycast(aimRay, out RaycastHit hitInfo, 200f, ~0, QueryTriggerInteraction.Ignore))
                return (hitInfo.point - origin).normalized;

            return aimRay.direction;
        }

        if (target != null && !target.IsDefeated)
            return (target.GetAimPoint() - origin).normalized;
        if (pillarTarget != null && pillarTarget.IsTargetable)
            return (pillarTarget.GetAimPoint() - origin).normalized;

        return aimTransform.forward;
    }

    void AcquireSpellAimTarget(string spellWord, out SpellTarget target, out SpellPillarObjective pillar)
    {
        target = null;
        pillar = null;

        if (aimAssist == null)
            aimAssist = GetComponent<PlayerAimAssist>() ?? FindAnyObjectByType<PlayerAimAssist>();

        if (aimAssist != null)
        {
            if (aimAssist.TryGetSelectedTarget(out target))
                return;
            if (aimAssist.TryGetSelectedPillar(out pillar))
                return;
        }

        if (TryGetAimedPillar(out pillar))
            return;

        target = AcquireBestTarget(spellWord);
    }

    SpellTarget AcquireBestTarget(string spellWord)
    {
        if (aimAssist == null)
            aimAssist = GetComponent<PlayerAimAssist>() ?? FindAnyObjectByType<PlayerAimAssist>();
        if (aimAssist != null && aimAssist.TryGetSelectedTarget(out SpellTarget assistedTarget))
            return assistedTarget;

        SpellTarget[] candidates = GetCachedTargets();
        if (candidates == null || candidates.Length == 0)
            return null;

        Camera cam = Camera.main;
        Ray aimRay = cam != null
            ? cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f))
            : new Ray(transform.position + Vector3.up, transform.forward);

        SpellTarget best = null;
        float bestScore = float.MaxValue;
        foreach (SpellTarget candidate in candidates)
        {
            if (candidate == null || candidate.IsDefeated)
                continue;

            Vector3 aimPoint = candidate.GetAimPoint();
            float rayDistance = DistanceToRay(aimRay, aimPoint);
            float worldDistance = Vector3.Distance(transform.position, aimPoint);
            float score = rayDistance * 4f + worldDistance * 0.2f;
            if (candidate.IsWeakTo(spellWord))
                score *= 0.55f;
            if (score < bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    bool TryGetAimedPillar(out SpellPillarObjective pillar)
    {
        pillar = null;
        Camera cam = Camera.main;
        if (cam == null)
            return false;

        Ray aimRay = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (Physics.SphereCast(aimRay, 0.35f, out RaycastHit hitInfo, 200f, ~0, QueryTriggerInteraction.Collide))
            pillar = hitInfo.collider.GetComponentInParent<SpellPillarObjective>();

        return pillar != null && pillar.IsTargetable;
    }

    SpellTarget[] GetCachedTargets()
    {
        if (cachedTargets == null || Time.unscaledTime >= nextTargetRefreshAt)
        {
            cachedTargets = FindObjectsByType<SpellTarget>();
            nextTargetRefreshAt = Time.unscaledTime + 0.35f;
        }

        return cachedTargets;
    }

    static float DistanceToRay(Ray ray, Vector3 point)
    {
        Vector3 direction = ray.direction.normalized;
        Vector3 projected = ray.origin + Vector3.Project(point - ray.origin, direction);
        return Vector3.Distance(projected, point);
    }
}
