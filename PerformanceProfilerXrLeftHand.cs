using UnityEngine;
using TMPro;
using UnityEngine.Profiling;
using Unity.Profiling;
using System.Text;

public class PerformanceProfilerXrLeftHand : MonoBehaviour
{
    [Header("VR Hand Tracking")]
    [Tooltip("The camera or head tracking transform. Will default to Camera.main if null.")]
    public Transform headTransform;
    
    [Tooltip("The UI Panel to show/hide based on wrist rotation.")]
    public GameObject profilerPanel;
    
    [Tooltip("The Text component to display the performance statistics.")]
    public TextMeshProUGUI profilerText;
    
    [Tooltip("The local vector on this transform that points 'up' out of the wrist/watch face.")]
    public Vector3 watchUpAxis = Vector3.up;
    
    [Tooltip("The maximum angle between the watch up axis and the direction to the head for the panel to show.")]
    public float activationAngleThreshold = 60f;

    [Header("Wrist Rotation Range")]
    [Tooltip("The target local X rotation when you are looking at your wrist.")]
    public float targetXRotation = 0f;
    [Tooltip("The allowed deviation from the Target X Rotation (e.g., 60 for a 120-degree total range).")]
    public float xRotationTolerance = 60f;

    [Header("Profiler Settings")]
    [Tooltip("How often to update the profiler text (in seconds).")]
    public float updateInterval = 0.5f;

    [Tooltip("If true, the panel will automatically rotate to face the camera when active.")]
    public bool autoRotateToCamera = true;

    [Header("Debug & Troubleshooting")]
    [Tooltip("If true, the panel will always be active and show current Angle/X in VR for calibration.")]
    public bool debugMode = false;

    [Header("Pin on Lift")]
    [Tooltip("If the hand is higher than head height minus this offset, it will detach.")]
    public float liftHeightOffset = 0.2f;
    private bool isPinned = false;
    private Transform originalParent;
    private Vector3 originalLocalPos;
    private Quaternion originalLocalRot;

    private float updateTimer = 0f;
    private StringBuilder sb = new StringBuilder();

    // Stats for Display
    private float currentAngle = 0f;
    private float currentXDist = 0f;
    private float lastXRaw = 0f;

    // Profiler Recorders
    private ProfilerRecorder drawCallsRecorder;
    private ProfilerRecorder batchesRecorder;
    private ProfilerRecorder trianglesRecorder;

    private float peakMemory = 0f;
    private FrameTiming[] frameTimings = new FrameTiming[1];

    void OnEnable()
    {
        drawCallsRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
        batchesRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
        trianglesRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
    }

    void OnDisable()
    {
        drawCallsRecorder.Dispose();
        batchesRecorder.Dispose();
        trianglesRecorder.Dispose();
    }

    void Start()
    {
        if (profilerPanel != null)
        {
            originalParent = profilerPanel.transform.parent;
            originalLocalPos = profilerPanel.transform.localPosition;
            originalLocalRot = profilerPanel.transform.localRotation;
            if (!debugMode) profilerPanel.SetActive(false);
        }
    }

    void Update()
    {
        CheckWristRotation();

        bool isFacing = IsWristPositionValid();
        float handY = transform.position.y;
        float headY = (headTransform != null) ? headTransform.position.y : (Camera.main != null ? Camera.main.transform.position.y : 0f);
        bool isLifted = handY > (headY - liftHeightOffset);

        // Pinning Logic
        if (isFacing && isLifted && !isPinned && profilerPanel != null)
        {
            isPinned = true;
            profilerPanel.transform.SetParent(null);
        }
        else if (isFacing && !isLifted && isPinned && profilerPanel != null)
        {
            isPinned = false;
            profilerPanel.transform.SetParent(originalParent);
            profilerPanel.transform.localPosition = originalLocalPos;
            profilerPanel.transform.localRotation = originalLocalRot;
        }

        bool shouldBeOpen = debugMode || isPinned || isFacing;

        if (profilerPanel != null && profilerPanel.activeSelf != shouldBeOpen)
        {
            profilerPanel.SetActive(shouldBeOpen);
        }

        // Update stats and orientation if panel is active
        if (profilerPanel != null && profilerPanel.activeSelf)
        {
            if (autoRotateToCamera && headTransform != null)
            {
                profilerPanel.transform.LookAt(profilerPanel.transform.position + (profilerPanel.transform.position - headTransform.position));
                profilerPanel.transform.rotation = Quaternion.LookRotation(profilerPanel.transform.position - headTransform.position, headTransform.up);
            }

            updateTimer += Time.deltaTime;
            if (updateTimer >= updateInterval)
            {
                updateTimer = 0f;
                UpdateProfilerStats();
            }
        }
    }

    private void CheckWristRotation()
    {
        if (headTransform == null)
        {
            if (Camera.main != null) headTransform = Camera.main.transform;
            else return;
        }

        Vector3 toHead = (headTransform.position - transform.position).normalized;
        Vector3 watchUp = transform.TransformDirection(watchUpAxis).normalized;
        
        currentAngle = Vector3.Angle(watchUp, toHead);
        
        lastXRaw = transform.localEulerAngles.x;
        currentXDist = Mathf.Abs(Mathf.DeltaAngle(lastXRaw, targetXRotation));
    }

    private bool IsWristPositionValid()
    {
        return currentAngle <= activationAngleThreshold && currentXDist <= xRotationTolerance;
    }

    private void UpdateProfilerStats()
    {
        if (profilerText == null) return;

        sb.Clear();

        if (isPinned)
        {
            sb.AppendLine("<color=orange><b>[ PINNED TO WORLD ]</b></color>");
            sb.AppendLine("<hr>");
        }
        else if (debugMode)
        {
            sb.AppendLine($"<color=yellow><b>DEBUG: Angle:{currentAngle:F0} X:{lastXRaw:F0}</b></color>");
            sb.AppendLine($"<color=yellow>DistX:{currentXDist:F0} / Tol:{xRotationTolerance}</color>");
            sb.AppendLine("<hr>");
        }
        
        // --- FPS & General ---
        FrameTimingManager.CaptureFrameTimings();
        FrameTimingManager.GetLatestTimings(1, frameTimings);
        float gpuTime = (float)frameTimings[0].gpuFrameTime;
        float cpuTime = (float)frameTimings[0].cpuFrameTime;
        
        float dt = Time.unscaledDeltaTime;
        float currentFps = 1.0f / dt;
        int fps = Mathf.RoundToInt(currentFps);
        
        string fpsColor = fps >= 60 ? "#00FF00" : (fps >= 30 ? "#FFFF00" : "#FF0000");
        sb.AppendLine($"<color={fpsColor}><b>FPS: {currentFps:F1} ({dt * 1000.0f:F1}ms)</b></color>");
        sb.AppendLine($"<color=#AAAAAA>GPU: {gpuTime:F1}ms | CPU: {cpuTime:F1}ms</color>");
        
        sb.AppendLine(GetProgressBar(currentFps / 120f, 20, fpsColor));

        string bottleneck = (gpuTime > cpuTime * 1.2f) ? "<color=#FF5555>GPU Bound</color>" : 
                            (cpuTime > gpuTime * 1.2f) ? "<color=#5555FF>CPU Bound</color>" : "Balanced";
        sb.AppendLine($"<size=80%>Bottleneck: {bottleneck}</size>");

        // --- Render ---
        long drawCalls = drawCallsRecorder.Valid ? drawCallsRecorder.LastValue : 0;
        long batches = batchesRecorder.Valid ? batchesRecorder.LastValue : 0;
        long tris = trianglesRecorder.Valid ? trianglesRecorder.LastValue : 0;
        sb.AppendLine($"<size=90%>Drw:{drawCalls} | Btc:{batches} | Tris:{tris / 1000.0f:F1}k</size>");

        // --- Memory ---
        float allocatedMem = Profiler.GetTotalAllocatedMemoryLong() / 1048576f;
        if (allocatedMem > peakMemory) peakMemory = allocatedMem;
        float totalMemLimit = SystemInfo.systemMemorySize;
        
        sb.AppendLine($"<size=90%>Mem: {allocatedMem:F0}MB / {totalMemLimit}MB</size>");
        sb.AppendLine(GetProgressBar(allocatedMem / totalMemLimit, 20, "#FFA500"));

        // --- Hardware ---
        sb.AppendLine($"<size=70%><color=#AAAAAA>{SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType})</color></size>");

        profilerText.text = sb.ToString();
    }

    private string GetProgressBar(float percent, int length, string color)
    {
        StringBuilder tempSb = new StringBuilder();
        int filledLength = Mathf.RoundToInt(Mathf.Clamp01(percent) * length);
        tempSb.Append($"<color={color}>");
        for (int i = 0; i < length; i++)
        {
            if (i < filledLength) tempSb.Append("■");
            else tempSb.Append("<color=#444444>■</color>");
        }
        tempSb.Append("</color>");
        return tempSb.ToString();
    }
}
