using System.Collections;
using System.Collections.Generic;
using Unity.MLAgents.Sensors;
using UnityEngine.InputSystem;
using Unity.MLAgents;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class WaterTank : Agent
{
    // Training and control settings
    [Tooltip("If true, enable training mode")]
    public bool trainingMode;
    public bool isAIControl = true; // Toggle between AI and Manual control
    public int stepTimeout = 100000;
    private float nextStepTimeout;

    // UI buttons for manual control
    public Button hotButton;
    public Button coldButton;
    public Button overflowButton;
    public Button toRequesterButton;
    public Button toConsumerButton;

    // Manual control actions
    private float hotValveAction = 0f;
    private float coldValveAction = 0f;
    private float overflowValveAction = 0f;
    private float requesterValveAction = 0f;
    private float consumerValveAction = 0f;

    // UI display elements
    public TextMeshProUGUI tankVolumeText;
    public TextMeshProUGUI tankTemperatureText;
    public TextMeshProUGUI targetTempText;
    public TextMeshProUGUI targetVolumeText;

    // Main tank state
    public float tankCapacity = 100f;
    public float tankCurrentVolume = 0f;
    public float tankTargetVolume;
    public float tankTargetTemperature;
    public float tankCurrentTemperature = 0f;

    // Source flow settings
    public float hotWaterTemp = 80f;
    public float coldWaterTemp = 20f;
    public float hotFlowRate = 2f;
    public float coldFlowRate = 2f;
    public float overflowRate = 1f;
    public float transferRate = 1.5f;

    // Requester tank state
    public float requesterCapacity = 30f;
    public float requesterCurrentVolume = 0f;
    public float requesterTargetVolume = 20f;

    // Consumer tank state
    public float consumerCapacity = 25f;
    public float consumerCurrentVolume = 0f;
    public float consumerTargetVolume = 15f;
    public float usageRate = 1.2f;

    // Valve states for AI logic
    private bool hotValveOpen = false;
    private bool coldValveOpen = false;
    private bool overflowValveOpen = false;
    private bool toRequesterOpen = false;
    private bool toConsumerOpen = false;

    // AI mode control flow flags
    private bool mainTankReady = false;
    private bool requesterReady = false;

    public override void Initialize()
    {
        MaxStep = trainingMode ? stepTimeout : 0;
    }

    public override void OnEpisodeBegin()
    {
        // Reset tank volumes and temperatures
        tankCurrentVolume = 0f;
        tankCurrentTemperature = 0f;
        requesterCurrentVolume = 0f;
        consumerCurrentVolume = 0f;

        // Randomize new targets
        tankTargetVolume = Random.Range(20f, tankCapacity);
        tankTargetTemperature = Random.Range(coldWaterTemp, hotWaterTemp);

        // Reset AI phase control
        mainTankReady = false;
        requesterReady = false;

        nextStepTimeout = StepCount + stepTimeout;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // Normalize tank and valve states for AI input
        sensor.AddObservation(tankCurrentVolume / tankCapacity);
        sensor.AddObservation(tankTargetVolume / tankCapacity);
        sensor.AddObservation(tankCurrentTemperature / hotWaterTemp);
        sensor.AddObservation(tankTargetTemperature / hotWaterTemp);

        sensor.AddObservation(requesterCurrentVolume / requesterCapacity);
        sensor.AddObservation(requesterTargetVolume / requesterCapacity);

        sensor.AddObservation(consumerCurrentVolume / consumerCapacity);
        sensor.AddObservation(consumerTargetVolume / consumerCapacity);

        sensor.AddObservation(hotValveOpen ? 1f : 0f);
        sensor.AddObservation(coldValveOpen ? 1f : 0f);
        sensor.AddObservation(overflowValveOpen ? 1f : 0f);
        sensor.AddObservation(toRequesterOpen ? 1f : 0f);
        sensor.AddObservation(toConsumerOpen ? 1f : 0f);
    }

    public override void OnActionReceived(float[] vectorAction)
    {
        if (isAIControl)
        {
            // AI controls valves based on current phase
            if (!mainTankReady)
            {
                // AI controls mixing (hot/cold) and overflow
                hotValveOpen = vectorAction[0] == 1;
                coldValveOpen = vectorAction[1] == 1;
                overflowValveOpen = vectorAction[2] == 1;

                // Disable other valves during this phase
                toRequesterOpen = false;
                toConsumerOpen = false;
            }
            else if (!requesterReady)
            {
                // AI transfers to requester
                toRequesterOpen = vectorAction[3] == 1;

                // Disable other valves during this phase
                hotValveOpen = false;
                coldValveOpen = false;
                overflowValveOpen = false;
                toConsumerOpen = false;
            }
            else
            {
                // AI transfers from requester to consumer
                toConsumerOpen = vectorAction[4] == 1;

                // All other valves disabled
                hotValveOpen = false;
                coldValveOpen = false;
                overflowValveOpen = false;
                toRequesterOpen = false;
            }
        }

        ProcessFlows();

        // Track AI progress toward goals
        float tempDiff = Mathf.Abs(tankCurrentTemperature - tankTargetTemperature);
        float volDiff = Mathf.Abs(tankCurrentVolume - tankTargetVolume);

        // Reward shaping
        AddReward(-1f / MaxStep);

        if (!mainTankReady && tempDiff < 2f && volDiff < 2f)
        {
            AddReward(2f);
            mainTankReady = true;
        }

        if (mainTankReady && !requesterReady && Mathf.Abs(requesterCurrentVolume - requesterTargetVolume) < 1f)
        {
            AddReward(2f);
            requesterReady = true;
        }

        if (requesterReady && Mathf.Abs(consumerCurrentVolume - consumerTargetVolume) < 1f)
        {
            AddReward(3f);
            EndEpisode();
        }

        if (StepCount > nextStepTimeout) EndEpisode();
    }

    private void ProcessFlows()
    {
        float delta = Time.deltaTime;

        // Apply water flows based on valve states or manual action
        if (hotValveOpen || hotValveAction == 1f)
            FillTank(hotFlowRate * delta, hotWaterTemp);

        if (coldValveOpen || coldValveAction == 1f)
            FillTank(coldFlowRate * delta, coldWaterTemp);

        if (overflowValveOpen || overflowValveAction == 1f)
            EliminateFromTank(overflowRate * delta);

        if ((toRequesterOpen || requesterValveAction == 1f) && tankCurrentVolume > 0)
        {
            float vol = Mathf.Min(transferRate * delta, requesterCapacity - requesterCurrentVolume);
            requesterCurrentVolume += vol;
            tankCurrentVolume -= vol;
        }

        if ((toConsumerOpen || consumerValveAction == 1f) && requesterCurrentVolume > 0)
        {
            float vol = Mathf.Min(usageRate * delta, consumerCapacity - consumerCurrentVolume);
            consumerCurrentVolume += vol;
            requesterCurrentVolume -= vol;
        }
    }

    private void FillTank(float addedVolume, float temp)
    {
        float spaceLeft = tankCapacity - tankCurrentVolume;
        float volumeToAdd = Mathf.Min(addedVolume, spaceLeft);

        // Weighted temperature update
        float newTemp = (tankCurrentTemperature * tankCurrentVolume + temp * volumeToAdd) / (tankCurrentVolume + volumeToAdd);

        tankCurrentVolume += volumeToAdd;
        tankCurrentTemperature = newTemp;
    }

    private void EliminateFromTank(float volume)
    {
        tankCurrentVolume = Mathf.Max(0f, tankCurrentVolume - volume);
    }

    public override void Heuristic(float[] actionsOut)
    {
        // Manual control fallback
        actionsOut[0] = hotValveAction;
        actionsOut[1] = coldValveAction;
        actionsOut[2] = overflowValveAction;
        actionsOut[3] = requesterValveAction;
        actionsOut[4] = consumerValveAction;
    }

    private void Update()
    {
        // Live data displayed in UI
        tankVolumeText.text = $"Volume: {tankCurrentVolume:F1} L";
        tankTemperatureText.text = $"Temp: {tankCurrentTemperature:F1} °C";
        targetTempText.text = $"Target Temp: {tankTargetTemperature:F1} °C";
        targetVolumeText.text = $"Target Vol: {tankTargetVolume:F1} L";
    }

    // Manual mode button control methods
    public void OnHotPress() => hotValveAction = 1f;
    public void OnHotRelease() => hotValveAction = 0f;
    public void OnColdPress() => coldValveAction = 1f;
    public void OnColdRelease() => coldValveAction = 0f;
    public void OnOverflowPress() => overflowValveAction = 1f;
    public void OnOverflowRelease() => overflowValveAction = 0f;
    public void OnRequesterPress() => requesterValveAction = 1f;
    public void OnRequesterRelease() => requesterValveAction = 0f;
    public void OnConsumerPress() => consumerValveAction = 1f;
    public void OnConsumerRelease() => consumerValveAction = 0f;

    public void ToggleControlMode()
    {
        isAIControl = !isAIControl;
        Debug.Log("Control mode: " + (isAIControl ? "AI" : "Manual"));
    }
}
