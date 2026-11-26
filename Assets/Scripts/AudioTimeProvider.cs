using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
///     一条 SV / SC 曲线。
///     主谱面用一条（group 0），每个 &lt;GR*x&gt; 插入的组各用一条——组内的 SV/SC 只在组内生效，
///     组外的变化也不会渗进组里。
/// </summary>
public class SVCurve
{
    public readonly List<float> SVList = new();
    public readonly List<double> SVTime = new();
    public readonly List<float> SVTrans = new(); //每次 SV 的渐变时长（秒），0 = 瞬间
    public readonly List<float> SVStartVal = new(); //显式起始值（<SV*-1:0#4:1> 里的 -1），没给时无效
    public readonly List<bool> SVHasStart = new();

    public readonly List<float> ScaleList = new();
    public readonly List<double> ScaleTime = new();
    public readonly List<float> ScaleTrans = new();
    public readonly List<float> ScaleStartVal = new();
    public readonly List<bool> ScaleHasStart = new();

    // 位置分段：段 i 覆盖 [segT[i], segT[i+1])，段内速度从 segV[i] 线性变化到 segV[i+1]。
    // 这里必须用 double：高密度 SV 谱面（上方有上万个控制点、含千万量级的脉冲）累积位置能到几千秒，
    // float 在那个量级只剩 0.001 秒分辨率，累加到最后相邻音符的位置差会被抹成 0（音符全叠在一起）。
    private readonly List<double> segT = new();
    private readonly List<double> segP = new();
    private readonly List<double> segV = new();
    private readonly List<double> segK = new();

    // 曲线起点：主谱面是 (0, 0, 1, 1)，组曲线是"插入点时刻、主谱面在该时刻的位置、组内的初始值"。
    public double startPos;
    public float startScale = 1f;
    public double startSV = 1.0;
    public double startT;

    /// <summary>组曲线的起点时间由谱面显式给出（组开头可能是空拍），不要用第一个时间点推断</summary>
    public bool hasExplicitStart;

    public void Clear()
    {
        SVList.Clear();
        SVTime.Clear();
        SVTrans.Clear();
        SVStartVal.Clear();
        SVHasStart.Clear();
        ScaleList.Clear();
        ScaleTime.Clear();
        ScaleTrans.Clear();
        ScaleStartVal.Clear();
        ScaleHasStart.Clear();
        segT.Clear();
        segP.Clear();
        segV.Clear();
        segK.Clear();
        startT = 0d;
        startPos = 0d;
        startSV = 1.0;
        startScale = 1f;
        hasExplicitStart = false;
    }

    /// <summary>
    ///     把 SV 指令展开成"控制点"，再按段建位置函数。
    ///     相邻控制点之间速度线性变化，位置就是二次函数——这样阶跃（渐变时长 0）和渐变可以走同一套逻辑。
    /// </summary>
    public void Calc()
    {
        segT.Clear();
        segP.Clear();
        segV.Clear();
        segK.Clear();

        // 1) 展开控制点：曲线起点之前保持起点的速度
        var times = new List<double> { startT };
        var values = new List<double> { startSV };

        for (var i = 0; i < SVList.Count; i++)
        {
            var t = SVTime[i];
            var target = (double)SVList[i];
            var trans = i < SVTrans.Count ? (double)SVTrans[i] : 0d;
            // <SV*-1:0#4:1>：显式给了起点，就在 t 处先跳到 -1 再开始渐变，
            // 而不是从"上一条指令留下的当前值"接（那是旧的 <SV*0#4:1> 的语义）
            var hasStart = i < SVHasStart.Count && SVHasStart[i] && i < SVStartVal.Count;
            var startValue = hasStart ? (double)SVStartVal[i] : double.NaN;

            // 新指令出现在上一条还没走完的渐变中间时，按"新指令覆盖旧指令"处理：
            // 把旧渐变的终点截断到 t（值取那一刻的插值），再照常展开新指令。
            // 这里原来是 if (t < times[last]) continue —— 直接把新指令整条丢掉。
            // 后果是两条 #4:1 前后脚写在同一格时，后一条被前一条的渐变终点挡住而不生效，
            // 表现为"SV 忽大忽小、弹跳节奏对不上"。
            while (times.Count >= 2 && times[times.Count - 1] > t + 1e-9)
            {
                var tEnd = times[times.Count - 1];
                var vEnd = values[values.Count - 1];
                times.RemoveAt(times.Count - 1);
                values.RemoveAt(values.Count - 1);

                var tPrev = times[times.Count - 1];
                var vPrev = values[values.Count - 1];

                if (t > tPrev + 1e-9 && tEnd > tPrev + 1e-9)
                {
                    var vAt = vPrev + (vEnd - vPrev) * (t - tPrev) / (tEnd - tPrev);
                    times.Add(t);
                    values.Add(vAt);
                }
            }

            if (t < times[0]) continue; // 真正的时间倒退才丢弃

            // 截断之后"此刻真正生效"的值，而不是上一条指令的目标值
            var current = values[values.Count - 1];

            if (trans > 0.0001)
            {
                if (t > times[times.Count - 1])
                {
                    times.Add(t);
                    values.Add(current);
                }

                if (!double.IsNaN(startValue))
                {
                    // 同一时刻插一个控制点：零长度段 = 阶跃到起值
                    times.Add(t);
                    values.Add(startValue);
                }

                times.Add(t + trans);
                values.Add(target);
            }
            else
            {
                // 阶跃要在同一时刻放两个控制点：前一个保持旧速度到 t，后一个从 t 起变成新速度。
                // 只放一个会被当成"从上一个点到 t 线性变化"，那就变成渐变了。
                if (t > times[times.Count - 1])
                {
                    times.Add(t);
                    values.Add(current);
                }

                times.Add(t);
                values.Add(target);
            }
        }

        // 2) 按控制点建段
        var lastPos = startPos;
        for (var i = 0; i < times.Count; i++)
        {
            var t0 = times[i];
            var v0 = values[i];
            var hasNext = i + 1 < times.Count;
            var t1 = hasNext ? times[i + 1] : t0;
            var v1 = hasNext ? values[i + 1] : v0;

            var dt = t1 - t0;
            // 阈值必须小：这种谱面最短的段只有 0.0156ms，用原来 0.1ms 的阈值会把整段位移整段丢掉
            var k = dt > 1e-7 ? (v1 - v0) / dt : 0d;

            segT.Add(t0);
            segP.Add(lastPos);
            segV.Add(v0);
            segK.Add(k);

            if (dt > 1e-7)
                lastPos += v0 * dt + 0.5 * k * dt * dt;
        }

        // 3) 末尾补一段恒定速度，保证 audioT 超过最后一个控制点时仍有解
        segT.Add(times[times.Count - 1]);
        segP.Add(lastPos);
        segV.Add(values[values.Count - 1]);
        segK.Add(0d);
    }

    public double GetPosition(double audioT)
    {
        var count = segT.Count;
        if (count == 0) return startPos + startSV * (audioT - startT);
        if (audioT < segT[0]) return startPos + startSV * (audioT - startT);

        // 这里原来是"从后往前线性扫"。转换来的谱面时间点能有 1.5 万个以上，
        // 每帧每个音符都要取好几次位置，线性扫会直接把帧率吃掉，改成二分。
        // segT 是升序的（同一时刻的阶跃会有重复项，取最后一个）。
        var lo = 0;
        var hi = count - 1;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) >> 1;
            if (segT[mid] <= audioT) lo = mid;
            else hi = mid - 1;
        }

        var x = audioT - segT[lo];
        return segP[lo] + segV[lo] * x + 0.5 * segK[lo] * x * x;
    }

    /// <summary>取音频时刻的 SC 缩放值（渐变期间线性插值）</summary>
    public float GetScale(double audioT)
    {
        var count = ScaleTime.Count;
        if (count == 0) return startScale;

        // 逐段求值。每段的起点值取"上一条指令那段在 t 时刻的值"，
        // 所以新指令可以直接打断上一条还没走完的渐变（覆盖语义）。
        // 原来是拿上一条的目标值接着走，渐变被打断时会从错误的值起步。
        var sT = 0d;
        var sFrom = (double)startScale;
        var sTarget = (double)startScale;
        var sTrans = 0d;
        var hasSeg = false;

        for (var i = 0; i < count; i++)
        {
            var t = ScaleTime[i];
            var target = (double)ScaleList[i];
            var trans = i < ScaleTrans.Count ? (double)ScaleTrans[i] : 0d;
            var hasStart = i < ScaleHasStart.Count && ScaleHasStart[i] && i < ScaleStartVal.Count;

            var from = hasStart ? (double)ScaleStartVal[i]
                     : hasSeg ? ValueAt(sT, sFrom, sTarget, sTrans, t)
                     : (double)startScale;

            if (audioT < t)
                return hasSeg ? (float)ValueAt(sT, sFrom, sTarget, sTrans, audioT) : startScale;

            sT = t;
            sFrom = from;
            sTarget = target;
            sTrans = trans;
            hasSeg = true;
        }

        return hasSeg ? (float)ValueAt(sT, sFrom, sTarget, sTrans, audioT) : startScale;
    }

    /// <summary>某一段（from → target，时长 trans）在时刻 x 的值</summary>
    private static double ValueAt(double t0, double from, double target, double trans, double x)
    {
        // 瞬时跳变：t0 之前保持 from，t0 及之后就变成 target
        if (trans <= 1e-9) return x < t0 ? from : target;
        if (x <= t0) return from;
        if (x >= t0 + trans) return target;
        return from + (target - from) * ((x - t0) / trans);
    }
}

public class AudioTimeProvider : MonoBehaviour
{
    // 必须是 double：音符时间也是 double，两边都用 double 才能保证
    // 百万级 SV 附近"当前时刻"与"音符时刻"的差不被微秒级量化放大成秒级误差。
    public double AudioTime; //notes get this value
    public float ScrollDist; //表示当前状况在正常流速且无SC下的时间，没有出现scene control的时候与Audio Time相同
    // ScrollDist 的 double 版本。这种密集 SV 谱面的累积位置能到几千秒，
    // float 在那个量级只剩 0.001 秒分辨率，音符位置会在百万级 SV 附近被放大成几个屏幕单位的误差。
    public double ScrollDistD;
    public float CurrentScale = 1f; //当前 SC 谱面缩放（主谱面）
    public bool isStart;
    public bool isRecord;
    public float offset;

    /// <summary>主谱面曲线（&lt;GR*x&gt; 之外的一切）</summary>
    public readonly SVCurve MainCurve = new();

    /// <summary>每个 &lt;GR*x&gt; 插入的组各一条曲线，键是组的编号</summary>
    private readonly Dictionary<int, SVCurve> groupCurves = new();

    public List<float> SVList => MainCurve.SVList;
    public List<double> SVTime => MainCurve.SVTime;
    public List<float> SVTrans => MainCurve.SVTrans;

    public List<float> ScaleList => MainCurve.ScaleList;
    public List<double> ScaleTime => MainCurve.ScaleTime;
    public List<float> ScaleTrans => MainCurve.ScaleTrans;

    private float speed;
    private double startTime;
    private long ticks;

    public float CurrentSpeed => isRecord ? Time.timeScale : speed;

    /// <summary>取某条曲线；组编号不存在时创建一条空曲线（后续收集会填进去）</summary>
    public SVCurve GetCurve(int group)
    {
        if (group <= 0) return MainCurve;
        if (groupCurves.TryGetValue(group, out var curve)) return curve;
        curve = new SVCurve();
        groupCurves[group] = curve;
        return curve;
    }

    /// <summary>重新加载谱面时清空全部曲线</summary>
    public void ClearCurves()
    {
        MainCurve.Clear();
        groupCurves.Clear();
    }

    private void Start()
    {
    }

    private void Update()
    {
        if (isStart)
        {
            if (isRecord)
                AudioTime = Time.timeAsDouble - startTime + offset;
            else
                AudioTime = (Time.realtimeSinceStartupAsDouble - startTime) * speed + offset;
            ScrollDistD = MainCurve.GetPosition(AudioTime);
            ScrollDist = (float)ScrollDistD;
            CurrentScale = MainCurve.GetScale(AudioTime);
        }
    }

    public void SetStartTime(long _ticks, float _offset, float _speed, bool _isRecord = false)
    {
        ticks = _ticks;
        offset = _offset;
        AudioTime = offset;
        var dateTime = new DateTime(ticks);
        var seconds = (dateTime - DateTime.Now).TotalSeconds;
        isRecord = _isRecord;
        if (_isRecord)
        {
            startTime = Time.timeAsDouble + 5;
            Time.timeScale = _speed;
            Time.captureFramerate = 60;
        }
        else
        {
            startTime = Time.realtimeSinceStartupAsDouble + seconds;
            speed = _speed;
            Time.captureFramerate = 0;
        }

        ScrollDistD = MainCurve.GetPosition(AudioTime);
        ScrollDist = (float)ScrollDistD;
        CurrentScale = MainCurve.GetScale(AudioTime);
        isStart = true;
    }

    public void ResetStartTime()
    {
        offset = 0f;
        isStart = false;
    }

    public void CalcSVPos()
    {
        // 主曲线先建：组曲线的起点位置要用主曲线在插入点的位置
        MainCurve.startT = 0d;
        MainCurve.startPos = 0d;
        MainCurve.startSV = 1d;
        MainCurve.startScale = 1f;
        MainCurve.Calc();

        foreach (var pair in groupCurves)
        {
            var curve = pair.Value;

            // 组的起点由谱面显式给出（插入点）；万一没给，才退回用组内第一个时间点
            var startT = curve.hasExplicitStart ? curve.startT : curve.SVTime.Count > 0 ? curve.SVTime[0] : 0d;
            if (!curve.hasExplicitStart && curve.ScaleTime.Count > 0 && curve.ScaleTime[0] < startT)
                startT = curve.ScaleTime[0];

            curve.startT = startT;
            // 组内一开始继承插入点的 SV/SC，组外的变化不再影响它
            // （正常情况已经在收集时由谱面显式给出，这里只是兜底）
            if (!curve.hasExplicitStart)
            {
                curve.startSV = curve.SVList.Count > 0 ? curve.SVList[0] : 1f;
                curve.startScale = curve.ScaleList.Count > 0 ? curve.ScaleList[0] : 1f;
            }
            // 位置接着主谱面在插入点的位置走，否则组内音符会整体偏移
            curve.startPos = MainCurve.GetPosition(startT);
            curve.Calc();
        }

        // 诊断：播放开始时 Console 里打一次。
        // 主曲线只打前几条；每条 <GR> 组曲线单独打——组内音符走的是组曲线，
        // 组曲线没数据的话，组里的 touch 会退化成"匀速滚动"，四个 C 位置差固定
        // → 扇叶全部张到最大、完全重叠、而且一直不动。
        var sb = new System.Text.StringBuilder();
        var probe = Math.Min(4, MainCurve.SVList.Count);
        for (var i = 0; i < probe; i++)
        {
            var hasStart = i < MainCurve.SVHasStart.Count && MainCurve.SVHasStart[i];
            var start = i < MainCurve.SVStartVal.Count ? MainCurve.SVStartVal[i] : 0f;
            sb.Append("\n  [").Append(i).Append("] t=").Append(MainCurve.SVTime[i].ToString("F3"))
              .Append(" v=").Append(MainCurve.SVList[i].ToString("F2"))
              .Append(" trans=").Append(MainCurve.SVTrans[i].ToString("F3"))
              .Append(" hasStart=").Append(hasStart)
              .Append(" start=").Append(start.ToString("F2"));
        }

        foreach (var pair in groupCurves)
        {
            var c = pair.Value;
            sb.Append("\n<GR:").Append(pair.Key).Append("> segs=").Append(c.SVList.Count)
              .Append(" startT=").Append(c.startT.ToString("F3"))
              .Append(" explicit=").Append(c.hasExplicitStart)
              .Append(" startSV=").Append(c.startSV.ToString("F2"))
              .Append(" startPos=").Append(c.startPos.ToString("F3"));

            var gp = Math.Min(8, c.SVList.Count);
            for (var i = 0; i < gp; i++)
            {
                var hasStart = i < c.SVHasStart.Count && c.SVHasStart[i];
                var start = i < c.SVStartVal.Count ? c.SVStartVal[i] : 0f;
                sb.Append("\n    g[").Append(i).Append("] t=").Append(c.SVTime[i].ToString("F3"))
                  .Append(" v=").Append(c.SVList[i].ToString("F2"))
                  .Append(" trans=").Append(c.SVTrans[i].ToString("F3"))
                  .Append(" hasStart=").Append(hasStart)
                  .Append(" start=").Append(start.ToString("F2"));
            }
        }

        UnityEngine.Debug.Log($"SV segments: {MainCurve.SVTime.Count}, groups: {groupCurves.Count}{sb}");
    }

    public float GetPositionAtTime(double audioT)
    {
        return (float)MainCurve.GetPosition(audioT);
    }

    /// <summary>按组取位置：组内音符走组自己的曲线</summary>
    public float GetPositionAtTime(double audioT, int group)
    {
        return (float)GetPositionAtTimeD(audioT, group);
    }

    /// <summary>音符位置（double），组内音符走组自己的曲线</summary>
    public double GetPositionAtTimeD(double audioT, int group)
    {
        if (group <= 0) return MainCurve.GetPosition(audioT);
        return groupCurves.TryGetValue(group, out var curve)
            ? curve.GetPosition(audioT)
            : MainCurve.GetPosition(audioT);
    }

    /// <summary>
    ///     某个组"此刻"的滚动位置。主谱面直接用缓存值；
    ///     组内必须走组自己的曲线——否则主谱面的 SV 波动会把组内音符一起带走
    ///     （组内只受组内 SV 影响，这是 &lt;GR&gt; 的核心约定）。
    /// </summary>
    public double GetScrollDistD(int group)
    {
        if (group <= 0) return ScrollDistD;
        return GetPositionAtTimeD(AudioTime, group);
    }

    /// <summary>
    ///     音符的下落距离（滚动秒）。每一步都用 double：
    ///     百万级 SV 附近的音符，float 的几微秒时间误差会被放大成几个屏幕单位。
    /// </summary>
    public float GetTiming(double noteTime, int group)
    {
        return (float)(GetScrollDistD(group) - GetPositionAtTimeD(noteTime, group));
    }

    public float GetScaleAtTime(double audioT)
    {
        return MainCurve.GetScale(audioT);
    }

    /// <summary>按组取 SC：组内音符缩放只受组内 SC 影响</summary>
    public float GetScaleAtTime(double audioT, int group)
    {
        if (group <= 0) return MainCurve.GetScale(audioT);
        return groupCurves.TryGetValue(group, out var curve)
            ? curve.GetScale(audioT)
            : MainCurve.GetScale(audioT);
    }

    /// <summary>当前帧某个组的缩放值（下落脚本每帧调用，方便起见包一层）</summary>
    public float GetScaleFor(int group)
    {
        if (group <= 0) return CurrentScale;
        return GetScaleAtTime(AudioTime, group);
    }
}
