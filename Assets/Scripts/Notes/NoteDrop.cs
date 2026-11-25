using UnityEngine;

public class NoteDrop : MonoBehaviour
{
    public double time;
    public int noteSortOrder;
    // <GR*x> 插入的组编号，0 = 主谱面。下落脚本按它选 SV/SC 曲线
    public int svGroup;
}

public class NoteLongDrop : NoteDrop
{
    public float LastFor = 1f;
}
