using UnityEngine;
using UnityEngine.VFX;

public class VFXClickDepthDragSelector : MonoBehaviour
{
    [Header("Refs")]
    public Camera cam;
    public VisualEffect vfx;

    [Header("Selection Volume")]
    [Min(0f)] public float radius = 0.2f;        // 抽出チューブ半径
    [Min(0f)] public float maxDepth = 5.0f;      // 奥行き上限

    [Header("Raycast")]
    public bool usePhysicsHit = true;            // コライダーに当てて正確なWSを使う
    public float fallbackPlaneDist = 3f;         // 当たらなければカメラ前の仮想平面

    [Header("Smoothing (ドラッグ時の追従なめらかさ)")]
    public bool smoothWhileDrag = true;
    [Range(0f, 1f)] public float posLerp = 0.25f; // 0=即時, 1=ゆっくり
    public bool smoothOffOnRelease = true;
    [Range(0f, 1f)] public float releaseLerp = 0.5f;

    [Header("Behavior")]
    public bool keepSelectingWhileHeld = true;   // 押下中は常に選別ON
    public KeyCode toggleKey = KeyCode.None;     // 任意のトグルキー

    [Header("Strength Fade (選別の強さフェード)")]
    public bool driveSelectStrength = true;      // true のとき強さを0↔1でフェード
    public string strengthProperty = "SelectStrength"; // VFX 側の Exposed 名
    [Tooltip("有効化時に 0→1 へ上がる時間（秒）")]
    [Min(0.001f)] public float riseSeconds = 0.15f;
    [Tooltip("無効化時に 1→0 へ下がる時間（秒）")]
    [Min(0.001f)] public float fallSeconds = 0.25f;
    [Tooltip("SelectEnabled を使わず、SelectStrength>epsilon を有効フラグにみなす場合")]
    public bool deriveEnabledFromStrength = false;
    [Range(0f, 1f)] public float enabledEpsilon = 0.01f;

    // VFX 側に Exposed しておくプロパティ名
    const string kClickPos = "ClickPosWS";
    const string kRayDir   = "RayDirWS";
    const string kRadius   = "SelectRadius";
    const string kMaxDepth = "SelectMaxDepth";
    const string kEnabled  = "SelectEnabled";

    bool _dragging;
    Vector3 _currPosWS;      // スムージング後の現在基点
    Vector3 _currRayDirWS;   // スムージング後の現在レイ方向

    // 強さの内部状態
    float _strength = 0f;        // 送出中の SelectStrength
    float _targetStrength = 0f;  // 目標（0 or 1）

    void Start()
    {
        if (!cam) cam = Camera.main;

        // 初期値を送っておく
        vfx.SetFloat(kRadius, radius);
        vfx.SetFloat(kMaxDepth, maxDepth);

        // 初期は無効
        SafeSetBool(kEnabled, false);

        if (driveSelectStrength)
            vfx.SetFloat(strengthProperty, 0f);
    }

    void Update()
    {
        // 半径/奥行きをインスペクタ値から反映（動的に変えるなら）
        vfx.SetFloat(kRadius, radius);
        vfx.SetFloat(kMaxDepth, maxDepth);

        if (toggleKey != KeyCode.None && Input.GetKeyDown(toggleKey))
        {
            bool en = SafeGetBool(kEnabled);
            SafeSetBool(kEnabled, !en);
        }

        if (Input.GetMouseButtonDown(0))
        {
            _dragging = true;
            // 押下時に即時更新して選別ON
            SampleRayAndApply(immediate:true);
            SafeSetBool(kEnabled, true);
            // 目標強さを 1 へ（上昇はゆっくり）
            _targetStrength = 1f;
        }

        if (_dragging && keepSelectingWhileHeld && Input.GetMouseButton(0))
        {
            // ドラッグ中は毎フレーム更新
            SampleRayAndApply(immediate:!smoothWhileDrag);
            _targetStrength = 1f; // 押下中は強さを上げ続ける
        }

        if (Input.GetMouseButtonUp(0))
        {
            _dragging = false;

            if (smoothOffOnRelease)
            {
                // ここでは見た目だけ少し遅らせてオフ
                // より厳密な半径フェード等は VFX グラフ側で対応推奨
                SafeSetBool(kEnabled, false);
            }
            else
            {
                SafeSetBool(kEnabled, false);
            }

            // 目標強さを 0 へ（下降は fallSeconds）
            _targetStrength = 0f;
        }

        // --- SelectStrength のフェード（指数補間：フレームレート非依存）---
        if (driveSelectStrength)
        {
            // 上がるときは riseSeconds、下がるときは fallSeconds を適用
            bool rising = _targetStrength > _strength;
            float tau = rising ? Mathf.Max(0.001f, riseSeconds)
                               : Mathf.Max(0.001f, fallSeconds);

            // 1 - exp(-dt/tau) で係数化（0..1）
            float a = 1f - Mathf.Exp(-Time.deltaTime / tau);
            _strength = Mathf.Lerp(_strength, _targetStrength, a);

            vfx.SetFloat(strengthProperty, _strength);

            // SelectEnabled を使わない構成でも運用できるようオプション提供
            if (deriveEnabledFromStrength)
            {
                bool en = _strength > enabledEpsilon;
                SafeSetBool(kEnabled, en);
            }
        }
    }

    void SampleRayAndApply(bool immediate)
    {
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);

        Vector3 targetPosWS;
        if (usePhysicsHit && Physics.Raycast(ray, out var hit, 5000f))
        {
            targetPosWS = hit.point;
        }
        else
        {
            targetPosWS = ray.origin + ray.direction.normalized * fallbackPlaneDist;
        }

        Vector3 targetDirWS = ray.direction.normalized;

        if (immediate || (_currPosWS == Vector3.zero && _currRayDirWS == Vector3.zero))
        {
            _currPosWS = targetPosWS;
            _currRayDirWS = targetDirWS;
        }
        else
        {
            float k = 1f - Mathf.Pow(1f - posLerp, Time.deltaTime * 60f);
            _currPosWS   = Vector3.Lerp(_currPosWS,   targetPosWS, k);
            _currRayDirWS= Vector3.Slerp(_currRayDirWS, targetDirWS, k);
        }

        vfx.SetVector3(kClickPos, _currPosWS);
        vfx.SetVector3(kRayDir,   _currRayDirWS);
    }

    // --- 安全ラッパー（Exposed 名の打ち間違いで止まらないように）---
    bool SafeGetBool(string name)
    {
        try { return vfx.GetBool(name); }
        catch { return false; }
    }
    void SafeSetBool(string name, bool value)
    {
        try { vfx.SetBool(name, value); }
        catch { /* Exposed 名が無いときの例外を握りつぶす */ }
    }
}
