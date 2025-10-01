// Assets/VFXParticlesToTransformsFull.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;
using UnityEngine.Rendering;

[RequireComponent(typeof(VisualEffect))]
public class VFXParticlesToTransformsFull : MonoBehaviour
{
    [Header("VFX Initialize.Capacity と一致させる")]
    public int capacity = 1000;

    [Header("index = particleId で並べる（未指定なら自動生成）")]
    public Transform[] targets;

    [Header("位置スムージング")]
    public bool smoothPos = true;
    [Tooltip("半分まで追従するまでの秒数（短いほどキビキビ）。フレームレート非依存。")]
    [Min(0.0001f)] public float posHalfLife = 0.08f;
    [Tooltip("ワープと見なして即時追従する距離（メートル）。")]
    public float teleportDistance = 0.5f;
    [Tooltip("追従時の最大速度 [m/s]（0以下で無効）。")]
    public float maxFollowSpeed = 0f;

    [Header("回転オプション")]
    public bool yawOnly = false;                 // 水平（ヨー）のみ合わせたいとき
    public bool smoothRot = true;                // スムージングON
    [Tooltip("回転のハーフライフ（秒）")]
    [Min(0.0001f)] public float rotHalfLife = 0.12f;
    public float pitchOffsetDeg = 0f;            // 追加のピッチ補正（微調整用）

    public enum Axis { PlusX, MinusX, PlusY, MinusY, PlusZ, MinusZ }
    [Header("モデルのローカル前方/上（FBXの軸に合わせる）")]
    public Axis modelForward = Axis.PlusZ;
    public Axis modelUp      = Axis.PlusY;

    static readonly int PositionsID = Shader.PropertyToID("Positions");
    static readonly int ForwardsID  = Shader.PropertyToID("Forwards");
    static readonly int CapacityID  = Shader.PropertyToID("Capacity");

    VisualEffect vfx;
    GraphicsBuffer posGB, fwdGB;     // GPU
    Vector3[] latestPos, latestFwd;  // CPU
    bool rbPosPending, rbFwdPending;
    Quaternion modelCorrection;      // 軸補正

    void OnEnable()
    {
        vfx = GetComponent<VisualEffect>();

        // GPUバッファを作成（stride=12 = float3）
        posGB = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, sizeof(float) * 3);
        fwdGB = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, sizeof(float) * 3);

        // VFX へ結線（Blackboard名と一致）
        vfx.SetGraphicsBuffer(PositionsID, posGB);
        vfx.SetGraphicsBuffer(ForwardsID,  fwdGB);
        vfx.SetInt(CapacityID, capacity);

        // 初回ディスパッチ前に必ず
        vfx.Reinit();

        latestPos = new Vector3[capacity];
        latestFwd = new Vector3[capacity];

        // 追従対象が未設定なら軽い球を用意（見た目確認用）
        if (targets == null || targets.Length == 0)
        {
            var list = new List<Transform>(Mathf.Min(capacity, 1024)); // 作りすぎ注意
            for (int i = 0; i < list.Capacity; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.transform.localScale = Vector3.one * 0.05f;
                list.Add(go.transform);
            }
            targets = list.ToArray();
        }

        modelCorrection = ComputeModelAxisCorrection();
    }

    void OnDisable()
    {
        posGB?.Dispose(); fwdGB?.Dispose();
        posGB = fwdGB = null;
    }

    void LateUpdate()
    {
        // GPU -> CPU 非同期読み戻し（各1本）
        if (!rbPosPending)
        {
            rbPosPending = true;
            AsyncGPUReadback.Request(posGB, req =>
            {
                rbPosPending = false;
                if (req.hasError) return;
                var src = req.GetData<Vector3>();
                int n = Mathf.Min(src.Length, latestPos.Length);
                for (int i = 0; i < n; i++) latestPos[i] = src[i];
            });
        }
        if (!rbFwdPending)
        {
            rbFwdPending = true;
            AsyncGPUReadback.Request(fwdGB, req =>
            {
                rbFwdPending = false;
                if (req.hasError) return;
                var src = req.GetData<Vector3>();
                int n = Mathf.Min(src.Length, latestFwd.Length);
                for (int i = 0; i < n; i++) latestFwd[i] = src[i];
            });
        }

        // フレームレート非依存のLerp係数（ハーフライフ→指数平滑）
        float posAlpha = HalfLifeToLerp(posHalfLife, Time.deltaTime);
        float rotAlpha = HalfLifeToLerp(rotHalfLife, Time.deltaTime);

        // 反映
        int count = Mathf.Min(capacity, targets.Length);
        float teleportSqr = teleportDistance * teleportDistance;
        for (int i = 0; i < count; i++)
        {
            // ===== 位置 =====
            var p = latestPos[i];
            if (!float.IsNaN(p.x) && !float.IsNaN(p.y) && !float.IsNaN(p.z))
            {
                var cur = targets[i].position;
                if (!smoothPos)
                {
                    targets[i].position = p;
                }
                else
                {
                    // 大ジャンプは即時（テレポート）
                    var to = p - cur;
                    float d2 = to.sqrMagnitude;
                    if (d2 > teleportSqr)
                    {
                        targets[i].position = p;
                    }
                    else
                    {
                        // ハーフライフ型の追従
                        Vector3 next = Vector3.Lerp(cur, p, posAlpha);

                        // 速度上限（任意）
                        if (maxFollowSpeed > 0f)
                        {
                            float maxStep = maxFollowSpeed * Time.deltaTime;
                            Vector3 delta = next - cur;
                            float dl = delta.magnitude;
                            if (dl > maxStep) next = cur + delta / dl * maxStep;
                        }

                        targets[i].position = next;
                    }
                }
            }

            // ===== 回転 =====
            var f = latestFwd[i];
            if (f.sqrMagnitude > 1e-8f)
            {
                f.Normalize();

                Quaternion worldRot;
                if (yawOnly)
                {
                    // 水平成分だけで向ける（ピッチしない）
                    var flat = Vector3.ProjectOnPlane(f, Vector3.up);
                    if (flat.sqrMagnitude < 1e-8f) flat = Vector3.forward;
                    worldRot = Quaternion.LookRotation(flat.normalized, Vector3.up);
                }
                else
                {
                    // forward と十分直交な up を安全に作る
                    var up = SafeUp(f);
                    worldRot = Quaternion.LookRotation(f, up);
                }

                // モデルのローカル軸補正 ＋ 追加ピッチ微調整
                var extraLocal = Quaternion.Euler(pitchOffsetDeg, 0f, 0f);
                var targetRot = worldRot * modelCorrection * extraLocal;

                targets[i].rotation = smoothRot
                    ? SlerpExp(targets[i].rotation, targetRot, rotAlpha)
                    : targetRot;
            }
        }
    }

    // ---------- ユーティリティ ----------

    // ハーフライフ(秒) -> 今フレームの Lerp 係数（0..1）
    static float HalfLifeToLerp(float halfLifeSec, float dt)
    {
        // alpha = 1 - 0.5^(dt / halfLife)
        // halfLife が短いほど alpha が大きい（俊敏）
        if (halfLifeSec <= 0f) return 1f;
        return 1f - Mathf.Exp(-Mathf.Log(2f) * dt / halfLifeSec);
    }

    static Quaternion SlerpExp(Quaternion from, Quaternion to, float alpha)
    {
        // Quaternion.Slerp は t がフレーム依存の時に効きが変わるため、
        // ここではフレームレート非依存の alpha をそのまま使う
        return Quaternion.Slerp(from, to, Mathf.Clamp01(alpha));
    }

    Vector3 AxisToVector(Axis a) => a switch
    {
        Axis.PlusX  => Vector3.right,
        Axis.MinusX => Vector3.left,
        Axis.PlusY  => Vector3.up,
        Axis.MinusY => Vector3.down,
        Axis.PlusZ  => Vector3.forward,
        _           => Vector3.back
    };

    Quaternion ComputeModelAxisCorrection()
    {
        // モデルのローカル前方/上 → 正規直交基底を作る
        var f = AxisToVector(modelForward);
        var u = AxisToVector(modelUp);

        var r = Vector3.Cross(u, f);
        if (r.sqrMagnitude < 1e-8f) r = Vector3.right;
        r.Normalize();
        u = Vector3.Cross(f, r).normalized;
        f = f.normalized;

        // Unity基準(+Z前,+Y上) → モデル軸 への回転
        var qModel = Quaternion.LookRotation(f, u);
        // その逆（モデル軸→Unity基準）で補正
        return Quaternion.Inverse(qModel);
    }

    Vector3 SafeUp(Vector3 fwd)
    {
        // forward に直交な up を作る。up が消える方向ではX軸で再生成
        var up = Vector3.ProjectOnPlane(Vector3.up, fwd);
        if (up.sqrMagnitude < 1e-6f)
            up = Vector3.ProjectOnPlane(Vector3.right, fwd);
        return up.normalized;
    }
}
