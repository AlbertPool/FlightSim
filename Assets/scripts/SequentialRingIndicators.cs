using System.Collections.Generic;
using TargetIndicators;
using TargetIndicators.Samples;
using UnityEngine;

public class SequentialRingIndicators : MonoBehaviour
{
    [SerializeField] private VisualIndicatorManager visualIndicatorManager;
    [SerializeField] private RingManager ringManager;
    [SerializeField] private List<RingTrigger> orderedRings = new();

    private readonly List<RingTrigger> trackedRings = new();
    private readonly List<TargetIndicatorId> indicatorIds = new();
    private int activeRingIndex;

    private void OnEnable()
    {
        if (visualIndicatorManager == null)
            visualIndicatorManager = FindAnyObjectByType<VisualIndicatorManager>();

        if (ringManager == null)
            ringManager = RingManager.Instance != null
                ? RingManager.Instance
                : FindAnyObjectByType<RingManager>();

        if (visualIndicatorManager == null || ringManager == null)
        {
            Debug.LogError("Sequential ring indicators need a VisualIndicatorManager and RingManager.", this);
            return;
        }

        if (orderedRings.Count == 0)
        {
            Debug.LogError("Assign the rings in the order they should be completed.", this);
            return;
        }

        visualIndicatorManager.AddIndicatorMode = AddIndicatorMode.Manual;
        ringManager.RingWasHit += HandleRingWasHit;

        foreach (var ring in orderedRings)
        {
            if (ring == null)
            {
                Debug.LogError("The ordered ring list contains an unassigned ring.", this);
                continue;
            }

            if (!visualIndicatorManager.TryAddVisualIndicator(ring.transform, out var id))
            {
                Debug.LogError($"Could not create an indicator for ring '{ring.name}'.", ring);
                continue;
            }

            trackedRings.Add(ring);
            indicatorIds.Add(id);
            visualIndicatorManager.HideVisualIndicator(id);
        }

        activeRingIndex = 0;
        ShowCurrentIndicator();
    }

    private void OnDisable()
    {
        if (ringManager != null)
            ringManager.RingWasHit -= HandleRingWasHit;

        if (visualIndicatorManager != null)
        {
            foreach (var id in indicatorIds)
                visualIndicatorManager.RemoveTargetIndicator(id);
        }

        trackedRings.Clear();
        indicatorIds.Clear();
        activeRingIndex = 0;
    }

    private void HandleRingWasHit(RingTrigger ring)
    {
        if (activeRingIndex >= trackedRings.Count || trackedRings[activeRingIndex] != ring)
            return;

        visualIndicatorManager.HideVisualIndicator(indicatorIds[activeRingIndex]);
        activeRingIndex++;
        ShowCurrentIndicator();
    }

    private void ShowCurrentIndicator()
    {
        if (activeRingIndex < indicatorIds.Count)
            visualIndicatorManager.ShowVisualIndicator(indicatorIds[activeRingIndex]);
    }
}
