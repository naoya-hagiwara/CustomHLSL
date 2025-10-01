using UnityEngine;

public class ShowObjectsInRadius : MonoBehaviour
{
    public float radius = 3f;          // 半径
    public LayerMask targetLayer;      // 対象レイヤー

    void Update()
    {
        // 半径内にあるColliderを取得
        Collider[] hitColliders = Physics.OverlapSphere(transform.position, radius, targetLayer);

        // 数をカウント
        int objectCount = hitColliders.Length;
        Debug.Log("範囲内のオブジェクト数: " + objectCount);

        // 各オブジェクトの名前を表示
        foreach (Collider col in hitColliders)
        {
            Debug.Log(" - " + col.gameObject.name);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
