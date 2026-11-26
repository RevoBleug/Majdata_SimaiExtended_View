using UnityEngine;

public class NoteDrop : MonoBehaviour
{
    public double time;
    public int noteSortOrder;
    // <GR*x> 插入的组编号，0 = 主谱面。下落脚本按它选 SV/SC 曲线
    public int svGroup;

    /// <summary>
    ///     场上还活着的音符数（所有下落脚本共用）。
    ///     All Perfect 用它判断"打完了"：O(1)，而且不会被 slide 判定条 / hold 特效
    ///     这些挂在 Notes 下面、播完也不销毁的物体干扰。
    ///     子类都没有实现 Awake / OnDestroy，所以不会被隐藏。
    /// </summary>
    public static int LiveCount { get; private set; }

    protected virtual void Awake()
    {
        LiveCount++;
    }

    protected virtual void OnDestroy()
    {
        LiveCount--;
    }
}

public class NoteLongDrop : NoteDrop
{
    public float LastFor = 1f;
}
