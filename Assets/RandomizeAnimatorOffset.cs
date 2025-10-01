using UnityEngine;
using System.Collections.Generic;

public class RandomizeAnimatorOffset : MonoBehaviour
{
    [Header("対象")]
    public bool IncludeSelf = true;
    public bool IncludeChildren = false;

    [Header("オフセット設定")]
    [Min(0f)] public float MinNormalizedTime = 0f;
    [Min(0f)] public float MaxNormalizedTime = 1f;

    [Header("速度設定")]
    [Tooltip("速度をランダム化するか")]
    public bool RandomizeSpeed = false;

    [Tooltip("速度の最小値（例: 0.5 で半分速、2.0 で2倍速）")]
    public float MinSpeed = 0.8f;

    [Tooltip("速度の最大値")]
    public float MaxSpeed = 1.2f;

    [Header("実行タイミング")]
    public bool ApplyOnEnable = true;
    public bool ApplyOnStart = true;

    [Header("その他")]
    public bool ForceInitializeAnimator = true;

    void OnEnable()
    {
        if (ApplyOnEnable) Apply();
    }

    void Start()
    {
        if (ApplyOnStart) Apply();
    }

    public void Apply()
    {
        if (MaxNormalizedTime < MinNormalizedTime)
        {
            float tmp = MinNormalizedTime;
            MinNormalizedTime = MaxNormalizedTime;
            MaxNormalizedTime = tmp;
        }

        List<Animator> targets = new List<Animator>();
        if (IncludeSelf)
        {
            var a = GetComponent<Animator>();
            if (a) targets.Add(a);
        }
        if (IncludeChildren)
        {
            foreach (var a in GetComponentsInChildren<Animator>(includeInactive: true))
            {
                if (!IncludeSelf && a.gameObject == this.gameObject) continue;
                if (!targets.Contains(a)) targets.Add(a);
            }
        }

        foreach (var anim in targets)
        {
            if (!anim.runtimeAnimatorController) continue;

            if (ForceInitializeAnimator)
            {
                anim.Update(0f);
            }

            int layerCount = anim.layerCount;
            for (int layer = 0; layer < layerCount; layer++)
            {
                AnimatorStateInfo stateInfo = anim.GetCurrentAnimatorStateInfo(layer);
                if (anim.IsInTransition(layer))
                {
                    var next = anim.GetNextAnimatorStateInfo(layer);
                    if (next.fullPathHash != 0) stateInfo = next;
                }

                if (stateInfo.fullPathHash == 0) continue;

                float normTime = Random.Range(MinNormalizedTime, MaxNormalizedTime);
                anim.Play(stateInfo.fullPathHash, layer, normTime);

                if (RandomizeSpeed)
                {
                    anim.speed = Random.Range(MinSpeed, MaxSpeed);
                }

                anim.Update(0f);
            }
        }
    }
}
