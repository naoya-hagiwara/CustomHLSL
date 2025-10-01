using UnityEngine;
using System.Linq;
using UnityEngine.VFX;

public class NearestObjectsToVFX : MonoBehaviour
{
    [Header("対象設定")]
    public Transform TargetObject;
    public LayerMask TargetLayers = ~0;

    [Header("距離→値 マッピング設定")]
    public float NearDistance = 0.5f;   // この距離以下は MaxValue
    public float FarDistance  = 1.0f;   // この距離以上は MinValue
    public float MaxValue     = 1.0f;
    public float MinValue     = 0.0f;

    [Header("ドラッグ検出設定")]
    public bool UseMouseButton = true;         // 左クリック状態でドラッグ扱い
    public bool RequireHitTargetOnPress = false; // 押し始めは TargetObject 上限定にする？
    public Camera RaycastCamera;               // 未指定なら Camera.main を使用
    private bool _isDragging = false;

    [Header("VFX Graph 出力")]
    public VisualEffect Vfx;
    public string DistanceProperty = "NearestObjDistance";

    void Update()
    {
        // --- ドラッグ判定 ---
        if (UseMouseButton)
        {
            // 押し始め判定
            if (Input.GetMouseButtonDown(0))
            {
                if (RequireHitTargetOnPress && TargetObject != null)
                    _isDragging = IsPointerOverTarget(TargetObject, RaycastCamera);
                else
                    _isDragging = true;
            }
            // 押下維持/離す
            if (Input.GetMouseButton(0) == false)
                _isDragging = false;
        }

        // --- 値計算 ---
        float mappedValue = MinValue;  // デフォは「マウス離してる間 = MinValue」
        float nearestDist = -1f;

        if (_isDragging && TargetObject != null)
        {
            var nearestList = FindObjectsOfType<Transform>()
                .Where(t =>
                    t != null &&
                    t.gameObject != TargetObject.gameObject &&
                    ((1 << t.gameObject.layer) & TargetLayers.value) != 0)
                .Select(t => Vector3.Distance(TargetObject.position, t.position))
                .OrderBy(d => d)
                .ToList();

            if (nearestList.Count > 0)
            {
                nearestDist = nearestList[0];

                if (nearestDist <= NearDistance)      mappedValue = MaxValue;
                else if (nearestDist >= FarDistance)  mappedValue = MinValue;
                else
                {
                    float t = Mathf.InverseLerp(FarDistance, NearDistance, nearestDist);
                    mappedValue = Mathf.Lerp(MinValue, MaxValue, t);
                }
            }
        }

        // --- VFX に送信 ---
        if (Vfx != null && Vfx.HasFloat(DistanceProperty))
            Vfx.SetFloat(DistanceProperty, mappedValue);

        // --- ログ ---
        if (_isDragging)
            Debug.Log($"[Drag] 距離: {nearestDist:F3}, 送信値: {mappedValue:F3}");
        else
            Debug.Log($"[Idle] マウス非ドラッグ → 送信値: {mappedValue:F3}");
    }

    // クリック位置が TargetObject 上かチェック（RequireHitTargetOnPress=true のとき使用）
    private bool IsPointerOverTarget(Transform target, Camera cam)
    {
        if (target == null) return false;
        if (cam == null) cam = Camera.main;
        if (cam == null) return false;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity))
            return hit.transform == target || hit.transform.IsChildOf(target);

        return false;
    }
}
