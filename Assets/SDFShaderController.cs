using UnityEditor;
using UnityEngine;
using static UnityEngine.GraphicsBuffer;

[ExecuteInEditMode]
public class SDFShaderController : MonoBehaviour
{
    [Header("Material")]
    [SerializeField] private Material sdfMaterial;

    [Header("Sphere Settings")]
    [SerializeField] private Vector3 spherePosition = new Vector3(0, 0, 0);
    [SerializeField, Range(0.1f, 5f)] private float sphereRadius = 1f;

    [Header("Cube Settings")]
    [SerializeField] private Vector3 cubePosition = new Vector3(2, 0, 0);
    [SerializeField] private Vector3 cubeSize = new Vector3(1, 1, 1);

    [Header("Blending")]
    [SerializeField, Range(0.01f, 5f)] private float smoothFactor = 0.5f;

    [Header("Raymarching Quality")]
    [SerializeField, Range(10, 200)] private int maxSteps = 100;
    [SerializeField, Range(10f, 200f)] private float maxDistance = 100f;
    [SerializeField, Range(0.0001f, 0.1f)] private float surfaceDistance = 0.01f;

    [Header("Appearance")]
    [SerializeField] private Color objectColor = Color.white;

    [Header("Lighting")]
    [SerializeField] private Vector3 lightPosition = new Vector3(5, 5, 5);
    [SerializeField] private Color lightColor = Color.white;
    [SerializeField, Range(0f, 10f)] private float lightIntensity = 1f;
    [SerializeField] private Color ambientColor = new Color(0.1f, 0.1f, 0.2f);
    [SerializeField, Range(1f, 128f)] private float specularPower = 32f;
    [SerializeField, Range(0f, 1f)] private float specularIntensity = 0.5f;

    [Header("Shadows")]
    [SerializeField, Range(0f, 1f)] private float shadowIntensity = 0.8f;
    [SerializeField, Range(1f, 32f)] private float shadowSoftness = 8f;

    [Header("Effects")]
    [SerializeField, Range(0f, 5f)] private float fresnelPower = 1f;
    [SerializeField, Range(0f, 1f)] private float fresnelIntensity = 0.3f;
    [SerializeField, Range(1, 10)] private int aoSteps = 5;
    [SerializeField, Range(0f, 1f)] private float aoIntensity = 0.5f;
    [SerializeField, Range(0.01f, 0.5f)] private float aoRadius = 0.1f;

    [Header("Animation (Optional)")]
    [SerializeField] private bool animateSphere = false;
    [SerializeField] private float sphereAnimSpeed = 1f;
    [SerializeField] private float sphereAnimRadius = 2f;

    [SerializeField] private bool animateCube = false;
    [SerializeField] private float cubeRotationSpeed = 30f;

    [SerializeField] private bool animateBlending = false;
    [SerializeField] private float blendAnimSpeed = 1f;
    [SerializeField] private float blendAnimMin = 0.1f;
    [SerializeField] private float blendAnimMax = 1f;

    [SerializeField] private bool animateLight = false;
    [SerializeField] private float lightOrbitSpeed = 0.5f;
    [SerializeField] private float lightOrbitRadius = 8f;
    [SerializeField] private float lightOrbitHeight = 5f;

    private Renderer rend;
    private float time;

    // Shader property IDs for better performance
    private static readonly int SpherePos1ID = Shader.PropertyToID("_SpherePos1");
    private static readonly int SphereRadius1ID = Shader.PropertyToID("_SphereRadius1");
    private static readonly int CubePos1ID = Shader.PropertyToID("_CubePos1");
    private static readonly int CubeSize1ID = Shader.PropertyToID("_CubeSize1");
    private static readonly int SmoothFactorID = Shader.PropertyToID("_SmoothFactor");
    private static readonly int MaxStepsID = Shader.PropertyToID("_MaxSteps");
    private static readonly int MaxDistID = Shader.PropertyToID("_MaxDist");
    private static readonly int SurfaceDistID = Shader.PropertyToID("_SurfaceDist");
    private static readonly int ColorID = Shader.PropertyToID("_Color");
    private static readonly int LightPosID = Shader.PropertyToID("_LightPos");
    private static readonly int LightColorID = Shader.PropertyToID("_LightColor");
    private static readonly int LightIntensityID = Shader.PropertyToID("_LightIntensity");
    private static readonly int AmbientColorID = Shader.PropertyToID("_AmbientColor");
    private static readonly int SpecularPowerID = Shader.PropertyToID("_SpecularPower");
    private static readonly int SpecularIntensityID = Shader.PropertyToID("_SpecularIntensity");
    private static readonly int ShadowIntensityID = Shader.PropertyToID("_ShadowIntensity");
    private static readonly int ShadowSoftnessID = Shader.PropertyToID("_ShadowSoftness");
    private static readonly int FresnelPowerID = Shader.PropertyToID("_FresnelPower");
    private static readonly int FresnelIntensityID = Shader.PropertyToID("_FresnelIntensity");
    private static readonly int AOStepsID = Shader.PropertyToID("_AOSteps");
    private static readonly int AOIntensityID = Shader.PropertyToID("_AOIntensity");
    private static readonly int AORadiusID = Shader.PropertyToID("_AORadius");

    void Start()
    {
        SetupRenderer();
    }

    void OnEnable()
    {
        SetupRenderer();
    }

    void SetupRenderer()
    {
        rend = GetComponent<Renderer>();

        // If no material is assigned, try to get it from the renderer
        if (sdfMaterial == null && rend != null)
        {
            sdfMaterial = rend.sharedMaterial;
        }

        // Apply initial values
        UpdateShaderProperties();
    }

    void Update()
    {
        if (sdfMaterial == null) return;

        time += Time.deltaTime;

        // Handle animations
        Vector3 currentSpherePos = spherePosition;
        Vector3 currentCubePos = cubePosition;
        float currentSmoothFactor = smoothFactor;
        Vector3 currentLightPos = lightPosition;

        if (animateSphere)
        {
            currentSpherePos = spherePosition + new Vector3(
                Mathf.Sin(time * sphereAnimSpeed) * sphereAnimRadius,
                Mathf.Cos(time * sphereAnimSpeed * 0.7f) * sphereAnimRadius * 0.5f,
                0
            );
        }

        if (animateCube)
        {
            // Rotate cube position around origin
            float angle = time * cubeRotationSpeed * Mathf.Deg2Rad;
            float dist = cubePosition.magnitude;
            currentCubePos = new Vector3(
                Mathf.Cos(angle) * dist,
                cubePosition.y,
                Mathf.Sin(angle) * dist
            );
        }

        if (animateBlending)
        {
            currentSmoothFactor = Mathf.Lerp(blendAnimMin, blendAnimMax,
                (Mathf.Sin(time * blendAnimSpeed) + 1f) * 0.5f);
        }

        if (animateLight)
        {
            currentLightPos = new Vector3(
                Mathf.Cos(time * lightOrbitSpeed) * lightOrbitRadius,
                lightOrbitHeight + Mathf.Sin(time * lightOrbitSpeed * 2f),
                Mathf.Sin(time * lightOrbitSpeed) * lightOrbitRadius
            );
        }

        // Update shader properties
        UpdateShaderProperties(currentSpherePos, currentCubePos, currentSmoothFactor, currentLightPos);
    }

    void UpdateShaderProperties(Vector3? animSpherePos = null, Vector3? animCubePos = null, float? animSmoothFactor = null, Vector3? animLightPos = null)
    {
        if (sdfMaterial == null) return;

        // Use animated values if provided, otherwise use inspector values
        Vector3 spherePos = animSpherePos ?? spherePosition;
        Vector3 cubePos = animCubePos ?? cubePosition;
        float smooth = animSmoothFactor ?? smoothFactor;
        Vector3 lightPos = animLightPos ?? lightPosition;

        // Update all shader properties
        sdfMaterial.SetVector(SpherePos1ID, spherePos);
        sdfMaterial.SetFloat(SphereRadius1ID, sphereRadius);
        sdfMaterial.SetVector(CubePos1ID, cubePos);
        sdfMaterial.SetVector(CubeSize1ID, cubeSize);
        sdfMaterial.SetFloat(SmoothFactorID, smooth);
        sdfMaterial.SetInt(MaxStepsID, maxSteps);
        sdfMaterial.SetFloat(MaxDistID, maxDistance);
        sdfMaterial.SetFloat(SurfaceDistID, surfaceDistance);
        sdfMaterial.SetColor(ColorID, objectColor);

        // Lighting properties
        sdfMaterial.SetVector(LightPosID, lightPos);
        sdfMaterial.SetColor(LightColorID, lightColor);
        sdfMaterial.SetFloat(LightIntensityID, lightIntensity);
        sdfMaterial.SetColor(AmbientColorID, ambientColor);
        sdfMaterial.SetFloat(SpecularPowerID, specularPower);
        sdfMaterial.SetFloat(SpecularIntensityID, specularIntensity);

        // Shadow properties
        sdfMaterial.SetFloat(ShadowIntensityID, shadowIntensity);
        sdfMaterial.SetFloat(ShadowSoftnessID, shadowSoftness);

        // Effect properties
        sdfMaterial.SetFloat(FresnelPowerID, fresnelPower);
        sdfMaterial.SetFloat(FresnelIntensityID, fresnelIntensity);
        sdfMaterial.SetInt(AOStepsID, aoSteps);
        sdfMaterial.SetFloat(AOIntensityID, aoIntensity);
        sdfMaterial.SetFloat(AORadiusID, aoRadius);
    }

    // Called when values change in the inspector
    void OnValidate()
    {
        // Clamp values to reasonable ranges
        sphereRadius = Mathf.Max(0.1f, sphereRadius);
        cubeSize = new Vector3(
            Mathf.Max(0.1f, cubeSize.x),
            Mathf.Max(0.1f, cubeSize.y),
            Mathf.Max(0.1f, cubeSize.z)
        );
        maxSteps = Mathf.Max(10, maxSteps);
        maxDistance = Mathf.Max(10f, maxDistance);
        surfaceDistance = Mathf.Clamp(surfaceDistance, 0.0001f, 0.1f);

        // Update shader properties when inspector values change
        UpdateShaderProperties();
    }

    // Helper method to create a full-screen quad
    [ContextMenu("Create Fullscreen Quad")]
    void CreateFullscreenQuad()
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "SDF Fullscreen Quad";
        quad.transform.position = new Vector3(0, 0, 5);
        quad.transform.localScale = new Vector3(20, 20, 1);

        if (sdfMaterial != null)
        {
            quad.GetComponent<Renderer>().material = sdfMaterial;
        }

        // Add this script to the quad
        quad.AddComponent<SDFShaderController>();
    }

    // Gizmos for visualization in Scene view
    void OnDrawGizmos()
    {
        // Draw sphere
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(spherePosition, sphereRadius);

        // Draw cube
        Gizmos.color = Color.yellow;
        Matrix4x4 oldMatrix = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(cubePosition, Quaternion.identity, cubeSize * 2);
        Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
        Gizmos.matrix = oldMatrix;

        // Draw line between shapes when blending
        if (smoothFactor > 0.01f)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(spherePosition, cubePosition);
        }
    }
}

// Custom Editor for better organization (optional)
#if UNITY_EDITOR

[CustomEditor(typeof(SDFShaderController))]
public class SDFShaderControllerEditor : Editor
{
    private bool showAnimationSettings = false;

    public override void OnInspectorGUI()
    {
        SDFShaderController controller = (SDFShaderController)target;

        // Draw default inspector
        DrawDefaultInspector();

        EditorGUILayout.Space();

        // Add buttons for common actions
        if (GUILayout.Button("Reset to Default"))
        {
            Undo.RecordObject(controller, "Reset SDF Settings");
            controller.GetType().GetMethod("Start", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(controller, null);
        }

        if (GUILayout.Button("Create Fullscreen Quad"))
        {
            controller.GetType().GetMethod("CreateFullscreenQuad", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(controller, null);
        }
    }
}
#endif