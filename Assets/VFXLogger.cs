using UnityEngine;
using UnityEngine.VFX;
using UnityEngine.Rendering;

[RequireComponent(typeof(VisualEffect))]
public class VFXLogger : MonoBehaviour
{
    public int capacity = 1000;

    static readonly int TestValueID = Shader.PropertyToID("TestValue");

    VisualEffect vfx;
    GraphicsBuffer testGB;
    bool rbPendingTest;

    void OnEnable()
    {
        vfx = GetComponent<VisualEffect>();

        // RWStructuredBuffer<float3> 用（stride=12）
        testGB = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, sizeof(float) * 3);

        vfx.SetGraphicsBuffer(TestValueID, testGB);

        vfx.Reinit();
    }

    void OnDisable()
    {
        testGB?.Dispose();
        testGB = null;
    }

    void LateUpdate()
    {
        if (!rbPendingTest)
        {
            rbPendingTest = true;
            AsyncGPUReadback.Request(testGB, req =>
            {
                rbPendingTest = false;
                if (req.hasError) return;

                var src = req.GetData<Vector3>();
                if (src.Length > 0)
                {
                    for (int i = 0; i < Mathf.Min(5, src.Length); i++)
                    {
                        Debug.Log($"[VFX] Particle[{i}] TestValue = {src[i]}");
                    }
                }
            });
        }
    }
}
