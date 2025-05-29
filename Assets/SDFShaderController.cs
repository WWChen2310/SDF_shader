using UnityEngine;
using System.Collections.Generic;
using static UnityEngine.GraphicsBuffer;
using UnityEditor;
using UnityEditorInternal;

[ExecuteInEditMode]
public class SDFArrayController : MonoBehaviour
{
    public enum PrimitiveType
    {
        Sphere = 0,
        Cube = 1,
        Cylinder = 2,
        Torus = 3
    }

    [System.Serializable]
    public class SDFPrimitive
    {
        public string name = "Primitive";
        public bool enabled = true;
        public PrimitiveType type = PrimitiveType.Sphere;
        public Vector3 position = Vector3.zero;
        public Vector3 rotation = Vector3.zero;
        public Vector3 scale = Vector3.one;

        [Header("Animation")]
        public bool animatePosition = false;
        public Vector3 positionAnimSpeed = Vector3.one;
        public Vector3 positionAnimRadius = Vector3.one;

        public bool animateRotation = false;
        public Vector3 rotationSpeed = new Vector3(10f, 20f, 30f);

        public bool animateScale = false;
        public float scaleAnimSpeed = 1f;
        public float scaleAnimMin = 0.5f;
        public float scaleAnimMax = 1.5f;
    }

    [Header("Material")]
    [SerializeField] private Material sdfMaterial;

    [Header("Primitives")]
    [SerializeField] private List<SDFPrimitive> primitives = new List<SDFPrimitive>();
    [SerializeField] private int maxPrimitives = 16;

    [Header("Global Settings")]
    [SerializeField, Range(0.01f, 5f)] private float globalSmoothFactor = 0.5f;
    [SerializeField] private Color objectColor = Color.white;

    [Header("Scene Transform")]
    [SerializeField] private Vector3 sceneRotation = Vector3.zero;
    [SerializeField] private bool animateSceneRotation = false;
    [SerializeField] private Vector3 sceneRotationSpeed = new Vector3(0, 10f, 0);

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

    [Header("Raymarching Quality")]
    [SerializeField, Range(10, 200)] private int maxSteps = 100;
    [SerializeField, Range(10f, 200f)] private float maxDistance = 100f;
    [SerializeField, Range(0.0001f, 0.1f)] private float surfaceDistance = 0.01f;

    [Header("Light Animation")]
    [SerializeField] private bool animateLight = false;
    [SerializeField] private float lightOrbitSpeed = 0.5f;
    [SerializeField] private float lightOrbitRadius = 8f;
    [SerializeField] private float lightOrbitHeight = 5f;

    // Internal
    private Texture2D primitiveDataTexture;
    private Color[] primitiveData;
    private float time;
    private Renderer rend;

    // Shader property IDs
    private static readonly int PrimitiveDataID = Shader.PropertyToID("_PrimitiveData");
    private static readonly int MaxPrimitivesID = Shader.PropertyToID("_MaxPrimitives");
    private static readonly int SmoothFactorID = Shader.PropertyToID("_SmoothFactor");
    private static readonly int SceneRotationID = Shader.PropertyToID("_SceneRotation");
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
    private static readonly int MaxStepsID = Shader.PropertyToID("_MaxSteps");
    private static readonly int MaxDistID = Shader.PropertyToID("_MaxDist");
    private static readonly int SurfaceDistID = Shader.PropertyToID("_SurfaceDist");

    void Start()
    {
        Initialize();
    }

    void OnEnable()
    {
        Initialize();
    }

    void Initialize()
    {
        rend = GetComponent<Renderer>();

        if (sdfMaterial == null && rend != null)
        {
            sdfMaterial = rend.sharedMaterial;
        }

        // Create primitive data texture
        CreatePrimitiveDataTexture();

        // Initialize with some default primitives if empty
        if (primitives.Count == 0)
        {
            primitives.Add(new SDFPrimitive
            {
                name = "Sphere 1",
                type = PrimitiveType.Sphere,
                position = Vector3.zero,
                scale = Vector3.one
            });

            primitives.Add(new SDFPrimitive
            {
                name = "Cube 1",
                type = PrimitiveType.Cube,
                position = new Vector3(3, 0, 0),
                scale = Vector3.one
            });
        }

        UpdateShaderProperties();
    }

    void CreatePrimitiveDataTexture()
    {
        // Each primitive needs 3 pixels (12 floats)
        int textureWidth = maxPrimitives * 3;

        if (primitiveDataTexture == null || primitiveDataTexture.width != textureWidth)
        {
            primitiveDataTexture = new Texture2D(textureWidth, 1, TextureFormat.RGBAFloat, false);
            primitiveDataTexture.filterMode = FilterMode.Point;
            primitiveDataTexture.wrapMode = TextureWrapMode.Clamp;
        }

        primitiveData = new Color[textureWidth];
    }

    void Update()
    {
        if (sdfMaterial == null) return;

        time += Time.deltaTime;

        // Update primitive data
        UpdatePrimitiveData();

        // Handle scene rotation animation
        Vector3 currentSceneRotation = sceneRotation;
        if (animateSceneRotation)
        {
            currentSceneRotation = sceneRotation + sceneRotationSpeed * time;
        }

        // Handle light animation
        Vector3 currentLightPos = lightPosition;
        if (animateLight)
        {
            currentLightPos = new Vector3(
                Mathf.Cos(time * lightOrbitSpeed) * lightOrbitRadius,
                lightOrbitHeight + Mathf.Sin(time * lightOrbitSpeed * 2f),
                Mathf.Sin(time * lightOrbitSpeed) * lightOrbitRadius
            );
        }

        // Update all shader properties
        UpdateShaderProperties(currentSceneRotation, currentLightPos);
    }

    void UpdatePrimitiveData()
    {
        // Clear data
        for (int i = 0; i < primitiveData.Length; i++)
        {
            primitiveData[i] = Color.clear;
        }

        // Update each primitive
        for (int i = 0; i < Mathf.Min(primitives.Count, maxPrimitives); i++)
        {
            SDFPrimitive prim = primitives[i];

            // Apply animations
            Vector3 pos = prim.position;
            Vector3 rot = prim.rotation;
            Vector3 scale = prim.scale;

            if (prim.animatePosition)
            {
                pos += new Vector3(
                    Mathf.Sin(time * prim.positionAnimSpeed.x) * prim.positionAnimRadius.x,
                    Mathf.Sin(time * prim.positionAnimSpeed.y) * prim.positionAnimRadius.y,
                    Mathf.Sin(time * prim.positionAnimSpeed.z) * prim.positionAnimRadius.z
                );
            }

            if (prim.animateRotation)
            {
                rot += new Vector3(
                    time * prim.rotationSpeed.x,
                    time * prim.rotationSpeed.y,
                    time * prim.rotationSpeed.z
                );
            }

            if (prim.animateScale)
            {
                float scaleFactor = Mathf.Lerp(prim.scaleAnimMin, prim.scaleAnimMax,
                    (Mathf.Sin(time * prim.scaleAnimSpeed) + 1f) * 0.5f);
                scale *= scaleFactor;
            }

            // Pack data into texture pixels
            int baseIndex = i * 3;

            // Pixel 0: position.xyz, type
            primitiveData[baseIndex] = new Color(pos.x, pos.y, pos.z, (float)prim.type);

            // Pixel 1: rotation.xyz, enabled
            primitiveData[baseIndex + 1] = new Color(rot.x, rot.y, rot.z, prim.enabled ? 1f : 0f);

            // Pixel 2: scale.xyz, reserved
            primitiveData[baseIndex + 2] = new Color(scale.x, scale.y, scale.z, 0f);
        }

        // Update texture
        primitiveDataTexture.SetPixels(primitiveData);
        primitiveDataTexture.Apply();
    }

    void UpdateShaderProperties(Vector3? animSceneRot = null, Vector3? animLightPos = null)
    {
        if (sdfMaterial == null) return;

        Vector3 sceneRot = animSceneRot ?? sceneRotation;
        Vector3 lightPos = animLightPos ?? lightPosition;

        // Update texture and primitive settings
        sdfMaterial.SetTexture(PrimitiveDataID, primitiveDataTexture);
        sdfMaterial.SetInt(MaxPrimitivesID, maxPrimitives);
        sdfMaterial.SetFloat(SmoothFactorID, globalSmoothFactor);
        sdfMaterial.SetVector(SceneRotationID, sceneRot);
        sdfMaterial.SetColor(ColorID, objectColor);

        // Update lighting
        sdfMaterial.SetVector(LightPosID, lightPos);
        sdfMaterial.SetColor(LightColorID, lightColor);
        sdfMaterial.SetFloat(LightIntensityID, lightIntensity);
        sdfMaterial.SetColor(AmbientColorID, ambientColor);
        sdfMaterial.SetFloat(SpecularPowerID, specularPower);
        sdfMaterial.SetFloat(SpecularIntensityID, specularIntensity);

        // Update shadows
        sdfMaterial.SetFloat(ShadowIntensityID, shadowIntensity);
        sdfMaterial.SetFloat(ShadowSoftnessID, shadowSoftness);

        // Update effects
        sdfMaterial.SetFloat(FresnelPowerID, fresnelPower);
        sdfMaterial.SetFloat(FresnelIntensityID, fresnelIntensity);
        sdfMaterial.SetInt(AOStepsID, aoSteps);
        sdfMaterial.SetFloat(AOIntensityID, aoIntensity);
        sdfMaterial.SetFloat(AORadiusID, aoRadius);

        // Update raymarching
        sdfMaterial.SetInt(MaxStepsID, maxSteps);
        sdfMaterial.SetFloat(MaxDistID, maxDistance);
        sdfMaterial.SetFloat(SurfaceDistID, surfaceDistance);
    }

    void OnValidate()
    {
        maxPrimitives = Mathf.Max(1, maxPrimitives);

        if (primitiveDataTexture == null || primitiveDataTexture.width != maxPrimitives * 3)
        {
            CreatePrimitiveDataTexture();
        }

        UpdateShaderProperties();
    }

    // Context menu helpers
    [ContextMenu("Add Sphere")]
    void AddSphere()
    {
        primitives.Add(new SDFPrimitive
        {
            name = $"Sphere {primitives.Count + 1}",
            type = PrimitiveType.Sphere,
            position = Random.insideUnitSphere * 3f,
            scale = Vector3.one * Random.Range(0.5f, 1.5f)
        });
    }

    [ContextMenu("Add Cube")]
    void AddCube()
    {
        primitives.Add(new SDFPrimitive
        {
            name = $"Cube {primitives.Count + 1}",
            type = PrimitiveType.Cube,
            position = Random.insideUnitSphere * 3f,
            rotation = Random.insideUnitSphere * 180f,
            scale = Vector3.one * Random.Range(0.5f, 1.5f)
        });
    }

    [ContextMenu("Add Cylinder")]
    void AddCylinder()
    {
        primitives.Add(new SDFPrimitive
        {
            name = $"Cylinder {primitives.Count + 1}",
            type = PrimitiveType.Cylinder,
            position = Random.insideUnitSphere * 3f,
            rotation = Random.insideUnitSphere * 180f,
            scale = new Vector3(0.5f, 1f, 0.5f)
        });
    }

    [ContextMenu("Add Torus")]
    void AddTorus()
    {
        primitives.Add(new SDFPrimitive
        {
            name = $"Torus {primitives.Count + 1}",
            type = PrimitiveType.Torus,
            position = Random.insideUnitSphere * 3f,
            rotation = Random.insideUnitSphere * 180f,
            scale = new Vector3(1f, 0.3f, 1f)
        });
    }

    [ContextMenu("Clear All Primitives")]
    void ClearAllPrimitives()
    {
        primitives.Clear();
    }

    [ContextMenu("Create Demo Scene")]
    void CreateDemoScene()
    {
        primitives.Clear();

        // Create a nice demo arrangement
        primitives.Add(new SDFPrimitive
        {
            name = "Central Sphere",
            type = PrimitiveType.Sphere,
            position = Vector3.zero,
            scale = Vector3.one * 1.2f,
            animateScale = true,
            scaleAnimSpeed = 0.5f,
            scaleAnimMin = 0.8f,
            scaleAnimMax = 1.2f
        });

        // Orbiting cubes
        for (int i = 0; i < 4; i++)
        {
            float angle = i * 90f * Mathf.Deg2Rad;
            primitives.Add(new SDFPrimitive
            {
                name = $"Orbiting Cube {i + 1}",
                type = PrimitiveType.Cube,
                position = new Vector3(Mathf.Cos(angle) * 3f, 0, Mathf.Sin(angle) * 3f),
                scale = Vector3.one * 0.6f,
                animateRotation = true,
                rotationSpeed = new Vector3(10f, 20f, 30f)
            });
        }

        // Floating torus
        primitives.Add(new SDFPrimitive
        {
            name = "Floating Torus",
            type = PrimitiveType.Torus,
            position = new Vector3(0, 2, 0),
            scale = new Vector3(1.5f, 0.2f, 1.5f),
            animateRotation = true,
            rotationSpeed = new Vector3(0, 45f, 0)
        });

        // Enable scene rotation for the demo
        animateSceneRotation = true;
        sceneRotationSpeed = new Vector3(0, 15f, 0);
    }

    [ContextMenu("Reset Scene Rotation")]
    void ResetSceneRotation()
    {
        sceneRotation = Vector3.zero;
        animateSceneRotation = false;
    }

    // Gizmos for visualization
    void OnDrawGizmos()
    {
        if (!Application.isPlaying && primitives != null)
        {
            // Update primitive data for gizmo visualization
            UpdatePrimitiveData();
        }

        // Apply scene rotation to gizmos
        Matrix4x4 sceneMatrix = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(sceneRotation), Vector3.one);
        if (Application.isPlaying && animateSceneRotation)
        {
            Vector3 animatedRotation = sceneRotation + sceneRotationSpeed * time;
            sceneMatrix = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(animatedRotation), Vector3.one);
        }

        Matrix4x4 originalMatrix = Gizmos.matrix;
        Gizmos.matrix = sceneMatrix;

        for (int i = 0; i < primitives.Count && i < maxPrimitives; i++)
        {
            SDFPrimitive prim = primitives[i];
            if (!prim.enabled) continue;

            // Apply animations for gizmo display
            Vector3 pos = prim.position;
            Vector3 rot = prim.rotation;
            Vector3 scale = prim.scale;

            if (Application.isPlaying)
            {
                if (prim.animatePosition)
                {
                    pos += new Vector3(
                        Mathf.Sin(time * prim.positionAnimSpeed.x) * prim.positionAnimRadius.x,
                        Mathf.Sin(time * prim.positionAnimSpeed.y) * prim.positionAnimRadius.y,
                        Mathf.Sin(time * prim.positionAnimSpeed.z) * prim.positionAnimRadius.z
                    );
                }

                if (prim.animateRotation)
                {
                    rot += new Vector3(
                        time * prim.rotationSpeed.x,
                        time * prim.rotationSpeed.y,
                        time * prim.rotationSpeed.z
                    );
                }

                if (prim.animateScale)
                {
                    float scaleFactor = Mathf.Lerp(prim.scaleAnimMin, prim.scaleAnimMax,
                        (Mathf.Sin(time * prim.scaleAnimSpeed) + 1f) * 0.5f);
                    scale *= scaleFactor;
                }
            }

            // Set color based on type
            switch (prim.type)
            {
                case PrimitiveType.Sphere:
                    Gizmos.color = Color.cyan;
                    Gizmos.DrawWireSphere(pos, scale.x);
                    break;

                case PrimitiveType.Cube:
                    Gizmos.color = Color.yellow;
                    Matrix4x4 oldMatrix = Gizmos.matrix;
                    Gizmos.matrix = Matrix4x4.TRS(pos, Quaternion.Euler(rot), scale * 2);
                    Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
                    Gizmos.matrix = oldMatrix;
                    break;

                case PrimitiveType.Cylinder:
                    Gizmos.color = Color.green;
                    DrawWireCylinder(pos, rot, scale.x, scale.y);
                    break;

                case PrimitiveType.Torus:
                    Gizmos.color = Color.magenta;
                    DrawWireTorus(pos, rot, scale.x, scale.y);
                    break;
            }
        }

        // Draw light (also affected by scene rotation)
        if (Application.isPlaying && animateLight)
        {
            Vector3 lightPos = new Vector3(
                Mathf.Cos(time * lightOrbitSpeed) * lightOrbitRadius,
                lightOrbitHeight + Mathf.Sin(time * lightOrbitSpeed * 2f),
                Mathf.Sin(time * lightOrbitSpeed) * lightOrbitRadius
            );
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(lightPos, 0.5f);
            Gizmos.DrawLine(lightPos, lightPos - Vector3.up * 2f);
        }
        else
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(lightPosition, 0.5f);
            Gizmos.DrawLine(lightPosition, lightPosition - Vector3.up * 2f);
        }

        // Reset gizmo matrix
        Gizmos.matrix = originalMatrix;

        // Draw scene rotation indicator
        if (animateSceneRotation || sceneRotation != Vector3.zero)
        {
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.5f);
            Gizmos.DrawWireCube(Vector3.zero, Vector3.one * 8f);

            // Draw rotation axis
            if (animateSceneRotation)
            {
                Gizmos.color = Color.green;
                Vector3 rotAxis = sceneRotationSpeed.normalized * 4f;
                Gizmos.DrawLine(-rotAxis, rotAxis);
                Gizmos.DrawWireSphere(rotAxis, 0.2f);
                Gizmos.DrawWireSphere(-rotAxis, 0.2f);
            }
        }
    }

    void DrawWireCylinder(Vector3 position, Vector3 rotation, float radius, float height)
    {
        Matrix4x4 oldMatrix = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(position, Quaternion.Euler(rotation), Vector3.one);

        int segments = 16;
        float angleStep = 360f / segments;

        // Draw top and bottom circles
        for (int i = 0; i < segments; i++)
        {
            float angle1 = i * angleStep * Mathf.Deg2Rad;
            float angle2 = (i + 1) * angleStep * Mathf.Deg2Rad;

            Vector3 p1 = new Vector3(Mathf.Cos(angle1) * radius, height, Mathf.Sin(angle1) * radius);
            Vector3 p2 = new Vector3(Mathf.Cos(angle2) * radius, height, Mathf.Sin(angle2) * radius);
            Vector3 p3 = new Vector3(Mathf.Cos(angle1) * radius, -height, Mathf.Sin(angle1) * radius);
            Vector3 p4 = new Vector3(Mathf.Cos(angle2) * radius, -height, Mathf.Sin(angle2) * radius);

            Gizmos.DrawLine(p1, p2);
            Gizmos.DrawLine(p3, p4);

            if (i % 4 == 0)
            {
                Gizmos.DrawLine(p1, p3);
            }
        }

        Gizmos.matrix = oldMatrix;
    }

    void DrawWireTorus(Vector3 position, Vector3 rotation, float majorRadius, float minorRadius)
    {
        Matrix4x4 oldMatrix = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(position, Quaternion.Euler(rotation), Vector3.one);

        int segments = 16;
        float angleStep = 360f / segments;

        // Draw major circle
        for (int i = 0; i < segments; i++)
        {
            float angle1 = i * angleStep * Mathf.Deg2Rad;
            float angle2 = (i + 1) * angleStep * Mathf.Deg2Rad;

            Vector3 p1 = new Vector3(Mathf.Cos(angle1) * majorRadius, 0, Mathf.Sin(angle1) * majorRadius);
            Vector3 p2 = new Vector3(Mathf.Cos(angle2) * majorRadius, 0, Mathf.Sin(angle2) * majorRadius);

            Gizmos.DrawLine(p1, p2);
        }

        // Draw minor circles at key points
        for (int i = 0; i < 8; i++)
        {
            float majorAngle = i * 45f * Mathf.Deg2Rad;
            Vector3 center = new Vector3(Mathf.Cos(majorAngle) * majorRadius, 0, Mathf.Sin(majorAngle) * majorRadius);

            for (int j = 0; j < 8; j++)
            {
                float minorAngle1 = j * 45f * Mathf.Deg2Rad;
                float minorAngle2 = (j + 1) * 45f * Mathf.Deg2Rad;

                Vector3 p1 = center + new Vector3(
                    Mathf.Cos(majorAngle) * Mathf.Cos(minorAngle1) * minorRadius,
                    Mathf.Sin(minorAngle1) * minorRadius,
                    Mathf.Sin(majorAngle) * Mathf.Cos(minorAngle1) * minorRadius
                );

                Vector3 p2 = center + new Vector3(
                    Mathf.Cos(majorAngle) * Mathf.Cos(minorAngle2) * minorRadius,
                    Mathf.Sin(minorAngle2) * minorRadius,
                    Mathf.Sin(majorAngle) * Mathf.Cos(minorAngle2) * minorRadius
                );

                Gizmos.DrawLine(p1, p2);
            }
        }

        Gizmos.matrix = oldMatrix;
    }
}

// Custom property drawer for better inspector UI
#if UNITY_EDITOR

[CustomEditor(typeof(SDFArrayController))]
public class SDFArrayControllerEditor : Editor
{
    private ReorderableList primitiveList;
    private bool showGlobalSettings = true;
    private bool showLighting = true;
    private bool showEffects = true;
    private bool showQuality = true;

    private void OnEnable()
    {
        primitiveList = new ReorderableList(serializedObject,
            serializedObject.FindProperty("primitives"),
            true, true, true, true);

        primitiveList.drawHeaderCallback = (Rect rect) => {
            EditorGUI.LabelField(rect, "SDF Primitives");
        };

        primitiveList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) => {
            SerializedProperty element = primitiveList.serializedProperty.GetArrayElementAtIndex(index);
            SerializedProperty nameProp = element.FindPropertyRelative("name");
            SerializedProperty enabledProp = element.FindPropertyRelative("enabled");
            SerializedProperty typeProp = element.FindPropertyRelative("type");

            rect.y += 2;
            float lineHeight = EditorGUIUtility.singleLineHeight;

            // Enabled checkbox
            EditorGUI.PropertyField(new Rect(rect.x, rect.y, 20, lineHeight), enabledProp, GUIContent.none);

            // Name
            EditorGUI.PropertyField(new Rect(rect.x + 25, rect.y, rect.width * 0.3f - 30, lineHeight),
                nameProp, GUIContent.none);

            // Type
            EditorGUI.PropertyField(new Rect(rect.x + rect.width * 0.3f + 5, rect.y, rect.width * 0.2f, lineHeight),
                typeProp, GUIContent.none);

            // Show position for reference
            SerializedProperty posProp = element.FindPropertyRelative("position");
            Vector3 pos = posProp.vector3Value;
            EditorGUI.LabelField(new Rect(rect.x + rect.width * 0.5f + 10, rect.y, rect.width * 0.5f - 10, lineHeight),
                $"Pos: ({pos.x:F1}, {pos.y:F1}, {pos.z:F1})");
        };

        primitiveList.elementHeightCallback = (int index) => {
            return EditorGUIUtility.singleLineHeight + 4;
        };

        primitiveList.onAddCallback = (ReorderableList list) => {
            var controller = target as SDFArrayController;
            Undo.RecordObject(controller, "Add Primitive");

            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("Sphere"), false, () => AddPrimitive(SDFArrayController.PrimitiveType.Sphere));
            menu.AddItem(new GUIContent("Cube"), false, () => AddPrimitive(SDFArrayController.PrimitiveType.Cube));
            menu.AddItem(new GUIContent("Cylinder"), false, () => AddPrimitive(SDFArrayController.PrimitiveType.Cylinder));
            menu.AddItem(new GUIContent("Torus"), false, () => AddPrimitive(SDFArrayController.PrimitiveType.Torus));
            menu.ShowAsContext();
        };
    }

    private void AddPrimitive(SDFArrayController.PrimitiveType type)
    {
        var controller = target as SDFArrayController;
        serializedObject.Update();

        var primitivesProperty = serializedObject.FindProperty("primitives");
        primitivesProperty.InsertArrayElementAtIndex(primitivesProperty.arraySize);

        var newElement = primitivesProperty.GetArrayElementAtIndex(primitivesProperty.arraySize - 1);
        newElement.FindPropertyRelative("name").stringValue = $"{type} {primitivesProperty.arraySize}";
        newElement.FindPropertyRelative("type").enumValueIndex = (int)type;
        newElement.FindPropertyRelative("enabled").boolValue = true;
        newElement.FindPropertyRelative("position").vector3Value = Random.insideUnitSphere * 3f;
        newElement.FindPropertyRelative("scale").vector3Value = Vector3.one;

        serializedObject.ApplyModifiedProperties();
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // Material field
        EditorGUILayout.PropertyField(serializedObject.FindProperty("sdfMaterial"));
        EditorGUILayout.Space();

        // Primitives list
        primitiveList.DoLayoutList();

        // Show selected primitive details
        if (primitiveList.index >= 0 && primitiveList.index < primitiveList.count)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Selected Primitive", EditorStyles.boldLabel);

            SerializedProperty selectedPrimitive = primitiveList.serializedProperty.GetArrayElementAtIndex(primitiveList.index);

            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(selectedPrimitive, true);
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space();

        // Global settings
        showGlobalSettings = EditorGUILayout.Foldout(showGlobalSettings, "Global Settings", true);
        if (showGlobalSettings)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(serializedObject.FindProperty("maxPrimitives"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("globalSmoothFactor"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("objectColor"));
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Scene Transform", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("sceneRotation"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("animateSceneRotation"));
            if (serializedObject.FindProperty("animateSceneRotation").boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("sceneRotationSpeed"));
                EditorGUI.indentLevel--;
            }
            EditorGUI.indentLevel--;
        }

        // Lighting
        showLighting = EditorGUILayout.Foldout(showLighting, "Lighting", true);
        if (showLighting)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(serializedObject.FindProperty("lightPosition"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("lightColor"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("lightIntensity"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("ambientColor"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("specularPower"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("specularIntensity"));
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("shadowIntensity"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("shadowSoftness"));
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("animateLight"));
            if (serializedObject.FindProperty("animateLight").boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("lightOrbitSpeed"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("lightOrbitRadius"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("lightOrbitHeight"));
                EditorGUI.indentLevel--;
            }
            EditorGUI.indentLevel--;
        }

        // Effects
        showEffects = EditorGUILayout.Foldout(showEffects, "Effects", true);
        if (showEffects)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(serializedObject.FindProperty("fresnelPower"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("fresnelIntensity"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("aoSteps"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("aoIntensity"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("aoRadius"));
            EditorGUI.indentLevel--;
        }

        // Quality
        showQuality = EditorGUILayout.Foldout(showQuality, "Raymarching Quality", true);
        if (showQuality)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(serializedObject.FindProperty("maxSteps"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("maxDistance"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("surfaceDistance"));
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space();

        // Action buttons
        EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Create Demo Scene"))
        {
            var controller = target as SDFArrayController;
            Undo.RecordObject(controller, "Create Demo Scene");
            controller.GetType().GetMethod("CreateDemoScene",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.Invoke(controller, null);
        }

        if (GUILayout.Button("Reset Scene Rotation"))
        {
            var controller = target as SDFArrayController;
            Undo.RecordObject(controller, "Reset Scene Rotation");
            controller.GetType().GetMethod("ResetSceneRotation",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.Invoke(controller, null);
        }
        EditorGUILayout.EndHorizontal();

        serializedObject.ApplyModifiedProperties();
    }
}
#endif