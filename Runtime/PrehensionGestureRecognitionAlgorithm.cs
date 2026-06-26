using System;
using UnityEngine;

internal class PrehensionGestureRecognitionAlgorithm
{
    private enum State { Idle, Building, Cooldown }

    public event Action<int> CandidateStarted;
    public event Action<int> GestureReleased;

    private int nullClassIndex;

    private State state = State.Idle;
    private int candidateClass = -1;
    private int firedClass = -1;
    private int consecutiveFrames = 0;
    private int totalOffHeld = 0;

    // A non-null class must exceed this confidence for this many consecutive frames to fire
    private const float confidenceThreshold = 0.85f;
    private int[] buildFramesRequired;

    // Either null exceeds confidenceThreshold, or top class drops below this, for this many consecutive frames to reset
    private const float resetThreshold = 0.6f;
    private int cooldownFramesRequired;

    // oppositeOf[i] = class index of gesture i's opposite, or -1 if none
    private int[] oppositeOf;
    // rapidReFire[i] = true if gesture i skips the null-frame cooldown after firing
    private bool[] rapidReFire;
    // isHoldGesture[i] = true if gesture i uses hold semantics (GestureReleased fires when the pose drops)
    private bool[] isHoldGesture;

    public PrehensionGestureRecognitionAlgorithm(int nullClassIndex, int[] buildFramesRequired, int cooldownFramesRequired, int[] oppositeOf, bool[] rapidReFire, bool[] isHoldGesture)
    {
        this.nullClassIndex = nullClassIndex;
        this.buildFramesRequired = buildFramesRequired;
        this.cooldownFramesRequired = cooldownFramesRequired;
        this.oppositeOf = oppositeOf;
        this.rapidReFire = rapidReFire;
        this.isHoldGesture = isHoldGesture;
    }

    private bool IsOpposite(int a, int b)
    {
        return (a >= 0 && a < oppositeOf.Length && oppositeOf[a] == b)
            || (b >= 0 && b < oppositeOf.Length && oppositeOf[b] == a);
    }

    // Returns the recognized gesture class index, or -1 if none
    public int Update(float[] softmax)
    {
        int topClass = ArgMax(softmax);
        float topConfidence = softmax[topClass];

        switch (state)
        {
            case State.Idle:
                if (topClass != nullClassIndex && topConfidence >= confidenceThreshold)
                {
                    candidateClass = topClass;
                    consecutiveFrames = 1;
                    state = State.Building;
                    CandidateStarted?.Invoke(candidateClass);
                }
                break;

            case State.Building:
                if (topClass == candidateClass && topConfidence >= confidenceThreshold)
                {
                    consecutiveFrames++;
                    if (consecutiveFrames >= buildFramesRequired[candidateClass])
                    {
                        firedClass = candidateClass;
                        candidateClass = -1;
                        consecutiveFrames = 0;
                        state = State.Cooldown;
                        return firedClass;
                    }
                }
                else
                {
                    candidateClass = -1;
                    consecutiveFrames = 0;
                    state = State.Idle;
                }
                break;

            case State.Cooldown:
                if (isHoldGesture[firedClass])
                    return UpdateHoldCooldown(topClass, topConfidence);
                else
                    UpdateNormalCooldown(topClass, topConfidence);
                break;
        }

        return -1;
    }

    // Hold gesture cooldown: two independent counters.
    // consecutiveFrames — resets when competitor switches; governs whether a specific gesture fires.
    // totalOffHeld — resets only when held gesture reappears; governs hold timeout.
    // When totalOffHeld hits cooldownFramesRequired, the hold ends. If a competitor was mid-build,
    // we hand off to Building so its frames carry over rather than being discarded.
    private int UpdateHoldCooldown(int topClass, float topConfidence)
    {
        bool isReset = (topClass == nullClassIndex && topConfidence >= confidenceThreshold)
                    || topConfidence < resetThreshold;

        if (!isReset && topClass == firedClass && topConfidence >= confidenceThreshold)
        {
            // Still holding — reset both counters
            candidateClass = -1;
            consecutiveFrames = 0;
            totalOffHeld = 0;
        }
        else if (!isReset && topConfidence >= confidenceThreshold)
        {
            totalOffHeld++;

            // Competing gesture — consecutive counter resets on switch so each gesture
            // still needs its own full buildFrames to confirm
            if (topClass == candidateClass)
            {
                consecutiveFrames++;
                if (consecutiveFrames >= buildFramesRequired[candidateClass])
                {
                    int newGesture = candidateClass;
                    int oldGesture = firedClass;
                    firedClass = newGesture;
                    candidateClass = -1;
                    consecutiveFrames = 0;
                    totalOffHeld = 0;
                    GestureReleased?.Invoke(oldGesture);
                    return newGesture;
                }
            }
            else
            {
                candidateClass = topClass;
                consecutiveFrames = 1;
                CandidateStarted?.Invoke(candidateClass);
            }

            if (totalOffHeld >= cooldownFramesRequired)
            {
                GestureReleased?.Invoke(firedClass);
                firedClass = -1;
                totalOffHeld = 0;

                // Hand off to Building if a competitor was mid-build, preserving its frame count
                if (candidateClass >= 0)
                {
                    state = State.Building;
                }
                else
                {
                    candidateClass = -1;
                    consecutiveFrames = 0;
                    state = State.Idle;
                }
            }
        }
        else
        {
            // Reset frame — track sustained null for hold release.
            // A competing gesture blip that dropped to null resets the null counter.
            totalOffHeld++;

            if (candidateClass != nullClassIndex)
            {
                candidateClass = nullClassIndex;
                consecutiveFrames = 0;
            }
            consecutiveFrames++;
            if (consecutiveFrames >= cooldownFramesRequired)
            {
                GestureReleased?.Invoke(firedClass);
                state = State.Idle;
                firedClass = -1;
                candidateClass = -1;
                consecutiveFrames = 0;
                totalOffHeld = 0;
            }
        }

        return -1;
    }

    private void UpdateNormalCooldown(int topClass, float topConfidence)
    {
        // A different non-null gesture appearing means the user has moved on — let it build immediately
        // Exception: opposites of the just-fired gesture must wait for a full reset first
        if (topClass != nullClassIndex && topClass != firedClass && !IsOpposite(firedClass, topClass) && topConfidence >= confidenceThreshold)
        {
            candidateClass = topClass;
            consecutiveFrames = 1;
            state = State.Building;
            CandidateStarted?.Invoke(candidateClass);
            return;
        }

        // Rapid re-fire: same gesture can re-enter building without waiting for a null reset
        if (topClass == firedClass && rapidReFire[firedClass] && topConfidence >= confidenceThreshold)
        {
            candidateClass = topClass;
            consecutiveFrames = 1;
            state = State.Building;
            CandidateStarted?.Invoke(candidateClass);
            return;
        }

        // Same gesture or ambiguous — require a proper reset before allowing anything to fire
        bool isReset = (topClass == nullClassIndex && topConfidence >= confidenceThreshold)
                    || topConfidence < resetThreshold;
        if (isReset)
        {
            consecutiveFrames++;
            if (consecutiveFrames >= cooldownFramesRequired)
            {
                state = State.Idle;
                consecutiveFrames = 0;
                candidateClass = -1;
                firedClass = -1;
            }
        }
        else
        {
            consecutiveFrames = 0;
        }
    }

    private int ArgMax(float[] arr)
    {
        int maxIdx = 0;
        for (int i = 1; i < arr.Length; i++)
            if (arr[i] > arr[maxIdx]) maxIdx = i;
        return maxIdx;
    }
}
