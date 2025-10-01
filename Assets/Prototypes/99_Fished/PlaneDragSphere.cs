using UnityEngine;
using UnityEngine.VFX;

public class PlaneDragObject : MonoBehaviour
{
    [Header("Setup")]
    public Camera Cam;
    [Min(0.01f)] public float Distance = 3f;
    public GameObject TargetObject;

    [Header("VFX (Attraction: Float)")]
    public VisualEffect Vfx;
    public string AttractionProperty = "Attraction";
    public float AttractionWhileDrag = 10f;   // 押下・ドラッグ中の値
    public float AttractionOnRelease = 0f;    // 離し後の値
    [Tooltip("押下時の Attraction の補間秒数（0 で即時）")]
    public float SmoothSecondsOnPress = 0.25f;
    [Tooltip("離し時の Attraction の補間秒数（0 で即時）")]
    public float SmoothSecondsOnRelease = 0f; // ← 離し時は無効（即時）にしたいなら 0

    [Header("Snap（マウス離し時の確定位置）")]
    public Transform SnapTarget;              // 離したとき確定させる位置
    [Tooltip("押下時の追従スムーズ＆離し時のスナップ移動の時間（0 で即時/ダイレクト）")]
    public float SnapMoveSeconds = 0.15f;

    private bool _dragging;
    private Plane _dragPlane;

    private Coroutine _attractRoutine;        // Attraction 補間
    private Coroutine _snapRoutine;           // 離し時のスナップ移動
    private float _currentAttraction;

    // ドラッグ追従（押下時にも適用）のためのスムーズ追従用
    private Vector3 _followVelocity;          // SmoothDamp 用
    private bool _following;                  // ドラッグ中の追従フラグ

    void Start()
    {
        if (Cam == null) Cam = Camera.main;
        if (!Cam || !TargetObject || !Vfx)
        {
            if (!Cam) Debug.LogError("[PlaneDragObject] Camera が見つかりません。");
            if (!TargetObject) Debug.LogError("[PlaneDragObject] TargetObject が未設定です。");
            if (!Vfx) Debug.LogError("[PlaneDragObject] Vfx が未設定です。");
            enabled = false; return;
        }

        RebuildPlane();

        // 初期は「離している状態」として Attraction をセット
        SetAttractionImmediate(AttractionOnRelease);
    }

    void Update()
    {
        RebuildPlane();

        // ===== 押下（クリック） =====
        if (Input.GetMouseButtonDown(0))
        {
            _dragging = true;
            _following = true;
            _followVelocity = Vector3.zero;

            // 進行中コルーチン停止
            if (_snapRoutine != null) StopCoroutine(_snapRoutine);
            if (_attractRoutine != null) StopCoroutine(_attractRoutine);

            // 押下時：Attraction をドラッグ値へ補間（押下専用の秒数を使用）
            if (SmoothSecondsOnPress > 0f)
                _attractRoutine = StartCoroutine(SmoothAttraction(_currentAttraction, AttractionWhileDrag, SmoothSecondsOnPress));
            else
                SetAttractionImmediate(AttractionWhileDrag);
        }

        // ===== ドラッグ中：平面上のマウス位置へスムーズ追従 =====
        if (_dragging && Input.GetMouseButton(0))
        {
            if (TryGetPlaneHit(Input.mousePosition, out Vector3 hit))
            {
                if (SnapMoveSeconds > 0f)
                {
                    // 押下直後から SmoothDamp で追従（SnapMoveSeconds を追従時間として再利用）
                    TargetObject.transform.position = Vector3.SmoothDamp(
                        TargetObject.transform.position,
                        hit,
                        ref _followVelocity,
                        SnapMoveSeconds
                    );
                }
                else
                {
                    // 即時追従
                    TargetObject.transform.position = hit;
                }
            }

            // 保険でドラッグ中は常にドラッグ値を維持
            Vfx.SetFloat(AttractionProperty, AttractionWhileDrag);
            _currentAttraction = AttractionWhileDrag;
        }

        // ===== 離し =====
        if (Input.GetMouseButtonUp(0))
        {
            _dragging = false;
            _following = false;

            // 離したらスナップ先へ（SnapMoveSeconds を“戻す”にも使用）
            if (SnapTarget != null)
            {
                if (_snapRoutine != null) StopCoroutine(_snapRoutine);
                if (SnapMoveSeconds > 0f)
                    _snapRoutine = StartCoroutine(SmoothMove(TargetObject.transform, SnapTarget.position, SnapMoveSeconds));
                else
                    TargetObject.transform.position = SnapTarget.position;
            }

            // Attraction を離し値へ戻す（← 離し専用の秒数を使用：0 なら即時＝“無効”）
            if (_attractRoutine != null) StopCoroutine(_attractRoutine);
            if (SmoothSecondsOnRelease > 0f)
                _attractRoutine = StartCoroutine(SmoothAttraction(_currentAttraction, AttractionOnRelease, SmoothSecondsOnRelease));
            else
                SetAttractionImmediate(AttractionOnRelease);
        }
    }

    // ===== 平面構築 =====
    void RebuildPlane()
    {
        var camTr = Cam.transform;
        Vector3 planePoint = camTr.position + camTr.forward * Distance;
        _dragPlane = new Plane(camTr.forward, planePoint);
    }

    bool TryGetPlaneHit(Vector2 screenPos, out Vector3 hit)
    {
        var ray = Cam.ScreenPointToRay(screenPos);
        if (_dragPlane.Raycast(ray, out float enter))
        {
            hit = ray.GetPoint(enter);
            return true;
        }
        hit = default;
        return false;
    }

    // ===== Attraction 制御 =====
    void SetAttractionImmediate(float value)
    {
        Vfx.SetFloat(AttractionProperty, value);
        _currentAttraction = value;
    }

    System.Collections.IEnumerator SmoothAttraction(float from, float to, float dur)
    {
        if (dur <= 0f) { SetAttractionImmediate(to); yield break; }

        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float v = Mathf.Lerp(from, to, Mathf.Clamp01(t / dur));
            Vfx.SetFloat(AttractionProperty, v);
            _currentAttraction = v;
            yield return null;
        }
        SetAttractionImmediate(to);
        _attractRoutine = null;
    }

    // ===== 位置スナップ（離し時） =====
    System.Collections.IEnumerator SmoothMove(Transform tr, Vector3 dst, float dur)
    {
        if (dur <= 0f) { tr.position = dst; yield break; }

        Vector3 src = tr.position;
        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            tr.position = Vector3.Lerp(src, dst, k);
            yield return null;
        }
        tr.position = dst;
        _snapRoutine = null;
    }
}
