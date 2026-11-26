using System.Collections.Generic;
using UnityEngine;

public class TapDrop : NoteDrop
{
    // Start is called before the first frame update
    // public float time;
    public int startPosition = 1;
    public float speed = 1;

    public bool isEach;
    public bool isBreak;
    public bool isEX;
    public bool isMine;
    public bool isFakeStarRotate;
    public bool canSVAffect;

    // 宽音符：从 startPosition 沿 isWideClockwise 方向铺到 wideEndPosition（含两端），
    // 用两条管带沿弧画出来（主体 + 外发光），厚度和半径无关。只有 Tap / Break 会带这三个字段。
    public bool isWide;
    public int wideEndPosition;
    public bool isWideClockwise;

    private LineRenderer wideLine;    // 主体（最里）
    private LineRenderer wideWhite;   // 白边
    private LineRenderer wideBlack;   // 黑边（最外）
    // 整圈（5>5）时轮廓是外圆 + 内圆两条独立闭环，一条 LineRenderer 画不了，
    // 所以每层再配一条只画内圆的线。
    private LineRenderer wideLineB;
    private LineRenderer wideWhiteB;
    private LineRenderer wideBlackB;
    private MeshRenderer wideGlowMr;  // 发光：一条网格带，顶点色做从内到外的连续衰减
    private GameObject tapLineB;      // 宽音符第二个端点的 tapline

    // 宽音符是一个"空心胶囊弧"：外弧 + 内弧 + 两端半圆，线本身才是可见的部分。
    // 从里到外四层：主体色 → 白边 → 黑边 →（ex）发光，所以看起来是"一圈白一圈黑"。
    // 全部固定在世界单位，不随半径放大——否则远处会变成一根粗棒。
    private const float WideHalf = 0.36f;          // 环高 0.72
    private const float WideLineWidth = 0.16f;     // 主体线宽（空心时是描边，实心时是整条带的厚度基准）
    private const float WideWhiteExtra = 0.04f;    // 白边单边宽度
    private const float WideBlackExtra = 0.045f;   // 黑边单边宽度
    private const float WideGlowExtra = 0.20f;     // 发光从轮廓外缘向外延伸的宽度

    /// <summary>
    ///     宽音符是不是空心。true = 双线轮廓（外弧 + 内弧 + 两端半圆），false = 一条填充带。
    ///     默认空心，由 Edit 的编辑器设置「宽音符空心轮廓」下发覆盖。
    /// </summary>
    public static bool WideHollow = true;

    private static readonly Color WideWhiteColor = Color.white;
    private static readonly Color WideBlackColor = new Color(0.06f, 0.06f, 0.09f, 1f);

    // 每帧重建的顶点缓冲。用 List + 复用数组，避免 ToArray 每帧分配。
    private readonly List<Vector3> widePtsA = new(512);
    private readonly List<Vector3> wideNrmA = new(512);
    private readonly List<Vector3> widePtsB = new(512);
    private readonly List<Vector3> wideNrmB = new(512);

    // 发光单独走一套路径：实心时画线只有中线，靠它偏移补不出端头和内侧的光，
    // 所以发光一律按"轮廓"生成（空心时可以直接复用画线那套）。
    private readonly List<Vector3> glowPtsA = new(512);
    private readonly List<Vector3> glowNrmA = new(512);
    private readonly List<Vector3> glowPtsB = new(512);
    private readonly List<Vector3> glowNrmB = new(512);

    private readonly List<Vector3> glowVerts = new(2048);
    private readonly List<Color> glowCols = new(2048);
    private readonly List<int> glowTris = new(4096);
    private Mesh wideGlowMesh;
    // 发光沿宽度的衰减档位：贴本体最亮，越往外越淡，最后一档全透明。
    private static readonly float[] GlowAlphaStops = { 1f, 0.72f, 0.40f, 0f };

    private Color wideBodyA = Color.white;
    private Color wideBodyB = Color.white;
    private Color wideGlowColor = Color.white;
    private float wideGlowAlpha = 0.5f;
    private static bool wideLoggedAny;

    public Sprite normalSpr;
    public Sprite eachSpr;
    public Sprite breakSpr;
    public Sprite exSpr;
    public Sprite mineSpr;

    public Sprite eachLine;
    public Sprite breakLine;
    public Sprite mineLine;

    public RuntimeAnimatorController BreakShine;

    public GameObject tapLine;

    public Color exEffectTap;
    public Color exEffectEach;
    public Color exEffectBreak;
    private Animator animator;

    private bool breakAnimStart;
    private SpriteRenderer exSpriteRender;
    private SpriteRenderer lineSpriteRender;

    private ObjectCounter ObjectCounter;

    private SpriteRenderer spriteRenderer;

    private AudioTimeProvider timeProvider;

    private int GetSortOrder()
    {
        return noteSortOrder;
    }

    private void Start()
    {
        var notes = GameObject.Find("Notes").transform;
        tapLine = Instantiate(tapLine, notes);
        tapLine.SetActive(false);
        lineSpriteRender = tapLine.GetComponent<SpriteRenderer>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        exSpriteRender = transform.GetChild(0).GetComponent<SpriteRenderer>();
        timeProvider = GameObject.Find("AudioTimeProvider").GetComponent<AudioTimeProvider>();
        ObjectCounter = GameObject.Find("ObjectCounter").GetComponent<ObjectCounter>();

        spriteRenderer.sortingOrder += noteSortOrder;
        exSpriteRender.sortingOrder += noteSortOrder;

        spriteRenderer.sprite = normalSpr;
        exSpriteRender.sprite = exSpr;

        if (isEX && !isMine) exSpriteRender.color = exEffectTap;
        if (isEach && !isMine)
        {
            spriteRenderer.sprite = eachSpr;
            lineSpriteRender.sprite = eachLine;
            if (isEX) exSpriteRender.color = exEffectEach;
        }
        if (isMine)
        {
            spriteRenderer.sprite = mineSpr;
            lineSpriteRender.sprite = mineLine;
        }
        if (isBreak && !isMine)
        {
            spriteRenderer.sprite = breakSpr;
            lineSpriteRender.sprite = breakLine;
            if (isEX) exSpriteRender.color = exEffectBreak;
            var anim = gameObject.AddComponent<Animator>(); // break tap闪烁
            anim.runtimeAnimatorController = BreakShine;
            anim.enabled = false;
            animator = anim;
        }

        spriteRenderer.forceRenderingOff = true;
        exSpriteRender.forceRenderingOff = true;

        if (isWide)
        {
            SetupWideLine();
            SetupWideTapLine();
        }
    }

    /// <summary>宽音符两端各有一条 tapline，像扇形的两条边。</summary>
    private void SetupWideTapLine()
    {
        if (tapLine == null) return;
        tapLineB = Instantiate(tapLine, tapLine.transform.parent);
        tapLineB.SetActive(false);
        var sr = tapLineB.GetComponent<SpriteRenderer>();
        sr.sprite = lineSpriteRender.sprite;
        sr.sortingOrder = lineSpriteRender.sortingOrder;
    }

    /// <summary>宽音符是四层同路径的线：黑边 → 白边 → 主体 →（ex）发光。</summary>
    private void SetupWideLine()
    {
        ResolveWideColors();

        var baseOrder = spriteRenderer.sortingOrder;
        // 层号必须往下排。noteSortOrder 是递减分配的，宽音符拿到 base 之后
        // 占用的就是 base ~ base-3；用 base+1 / base+2 会落进前一个音符的区间，
        // 表现就是宽音符盖在了前面的 note 上。
        wideLine = CreateWideRenderer(null, baseOrder, WideBodyMaterial());
        wideWhite = CreateWideRenderer("WideWhite", baseOrder - 1, WideBodyMaterial());
        wideBlack = CreateWideRenderer("WideBlack", baseOrder - 2, WideBodyMaterial());
        if (wideLine == null) return;

        // 整圈（起止同键位）的轮廓是外圆 + 内圆两个独立闭环，一条线连不起来，
        // 所以每层再加一条只画内圆的，层号和主路径一致。
        if (wideEndPosition == startPosition)
        {
            wideLineB = CreateWideRenderer("WideLineIn", baseOrder, WideBodyMaterial());
            wideWhiteB = CreateWideRenderer("WideWhiteIn", baseOrder - 1, WideBodyMaterial());
            wideBlackB = CreateWideRenderer("WideBlackIn", baseOrder - 2, WideBodyMaterial());
        }

        // 发光只有 ex 才有，用一条网格带：顶点色沿宽度从亮到透明，
        // 所以是连续渐变，而不是几层硬边叠出来的台阶。
        if (isEX) SetupWideGlow();

        ApplyWideColors();
    }

    /// <summary>发光网格：一条从轮廓外缘向外延伸、alpha 递减的带子。</summary>
    private void SetupWideGlow()
    {
        var mat = WideGlowMaterial();
        if (mat == null) return;

        var go = new GameObject("WideGlowMesh");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        wideGlowMesh = new Mesh { name = "WideGlow" };
        wideGlowMesh.MarkDynamic();
        go.AddComponent<MeshFilter>().mesh = wideGlowMesh;

        wideGlowMr = go.AddComponent<MeshRenderer>();
        wideGlowMr.material = mat;
        wideGlowMr.sortingLayerID = spriteRenderer.sortingLayerID;
        wideGlowMr.sortingOrder = spriteRenderer.sortingOrder - 3;
        wideGlowMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        wideGlowMr.receiveShadows = false;
        wideGlowMr.enabled = false;
    }

    /// <summary>
    ///     建一条线。positionCount 从 0 开始——LineRenderer 默认带两个 (0,0,0) 点，
    ///     如果后面没来得及写坐标，那两点会合一个圆帽画在屏幕正中，看起来就是"中间一个白圆"。
    /// </summary>
    private LineRenderer CreateWideRenderer(string childName, int sortingOrder, Material mat)
    {
        if (mat == null)
        {
            Debug.LogError("[Wide] 宽音符材质创建失败，检查 Assets/Resources/WideNote.shader");
            return null;
        }

        var go = gameObject;
        if (!string.IsNullOrEmpty(childName))
        {
            go = new GameObject(childName);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
        }

        var lr = go.AddComponent<LineRenderer>();
        if (lr == null)
        {
            Debug.LogError("[Wide] LineRenderer 添加失败：" + go.name);
            return null;
        }

        lr.useWorldSpace = true;
        lr.alignment = LineAlignment.View;
        lr.numCapVertices = 10;    // 两端半圆
        lr.numCornerVertices = 4;
        lr.textureMode = LineTextureMode.Stretch;
        lr.positionCount = 0;
        lr.loop = false;
        lr.sortingLayerID = spriteRenderer.sortingLayerID;
        lr.sortingOrder = sortingOrder;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.material = mat;
        lr.enabled = false;        // 等第一次 UpdateWide 写进坐标再打开
        return lr;
    }

    /// <summary>主体正常混合；发光层用加法混合，没有后处理也看得出在发光。</summary>
    private static Material wideBodyMat;
    private static Material wideGlowMat;

    private static Shader WideShader()
    {
        // 放 Resources 里由 Resources.Load 取，避免打包时被裁掉
        var sh = Resources.Load<Shader>("WideNote");
        if (sh == null) sh = Shader.Find("Maj/WideNote");
        return sh;
    }

    private static Material MakeWideMaterial(bool additive)
    {
        var sh = WideShader();
        if (sh == null)
        {
            Debug.LogError("[Wide] 找不到 Maj/WideNote，宽音符无法绘制");
            return null;
        }

        // 都用纯白纹理、硬边：发光靠"多层不同偏移 + 不同 alpha"堆出从内向外衰减，
        // 而不是靠一张横跨线宽的渐变图——那种只会让线中心最亮，看起来是"两侧往中间亮"。
        var mat = new Material(sh) { mainTexture = Texture2D.whiteTexture };
        var src = (float)UnityEngine.Rendering.BlendMode.SrcAlpha;
        var dst = additive
            ? (float)UnityEngine.Rendering.BlendMode.One
            : (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha;
        mat.SetFloat("_SrcBlend", src);
        mat.SetFloat("_DstBlend", dst);
        return mat;
    }

    private static Material WideBodyMaterial()
    {
        return wideBodyMat != null ? wideBodyMat : wideBodyMat = MakeWideMaterial(false);
    }

    private static Material WideGlowMaterial()
    {
        return wideGlowMat != null ? wideGlowMat : wideGlowMat = MakeWideMaterial(true);
    }

    /// <summary>颜色照 tap 系列贴图取：普通粉、each 黄、break 橙、mine 灰、ex 淡紫外圈。</summary>
    private void ResolveWideColors()
    {
        if (isMine)
        {
            wideBodyA = wideBodyB = new Color(0.62f, 0.62f, 0.62f);
            wideGlowColor = new Color(0.86f, 0.86f, 0.86f);
            wideGlowAlpha = 0.18f;
        }
        else if (isBreak)
        {
            wideBodyA = new Color(1f, 0.28f, 0.04f);
            wideBodyB = new Color(1f, 0.72f, 0.02f);
            wideGlowColor = new Color(1f, 0.72f, 0.34f);
            wideGlowAlpha = 0.38f;
        }
        else if (isEach)
        {
            wideBodyA = new Color(1f, 0.90f, 0.06f);
            wideBodyB = new Color(1f, 0.66f, 0.02f);
            wideGlowColor = new Color(1f, 0.90f, 0.46f);
            wideGlowAlpha = 0.32f;
        }
        else
        {
            wideBodyA = new Color(1f, 0.32f, 0.68f);
            wideBodyB = new Color(0.94f, 0.10f, 0.47f);
            wideGlowColor = new Color(1f, 0.64f, 0.87f);
            wideGlowAlpha = 0.32f;
        }

        if (isEX)
        {
            // ex 的外圈换成淡紫白、更亮更宽，和 tap_ex 的观感一致
            wideGlowColor = Color.Lerp(new Color(0.86f, 0.82f, 1f), Color.white, 0.35f);
            wideGlowAlpha = 0.90f;
        }
    }

    /// <summary>
    ///     break 的闪光节奏照抄 BreakShine.anim：周期 0.5333s，峰值 1.3 出现在 0.1333s。
    ///     普通 break 靠 ShineMaterial 加亮度，宽音符的 sprite 是关掉的，只能自己算。
    /// </summary>
    private float BreakPulse()
    {
        if (!isBreak) return 1f;
        const float period = 0.5333334f;
        const float peak = 0.1333334f;
        var t = Mathf.Repeat(Time.time, period);
        if (t < peak) return Mathf.Lerp(1f, 1.3f, t / peak);
        if (t < peak * 2f) return Mathf.Lerp(1.3f, 1f, (t - peak) / peak);
        return 1f;
    }

    private static readonly int WideColorId = Shader.PropertyToID("_Color");
    private MaterialPropertyBlock wideMpb;

    /// <summary>
    ///     颜色走材质属性 `_Color` 而不是 LineRenderer 的顶点色。
    ///     positionCount 一变，顶点色就会被内部重置成默认白，那条路不可靠。
    /// </summary>
    private void ApplyWideColors()
    {
        if (wideLine == null) return;
        if (wideMpb == null) wideMpb = new MaterialPropertyBlock();

        var k = (BreakPulse() - 1f) / 0.3f;   // 0（最暗）~ 1（闪光峰值）

        var body = Color.Lerp(Color.Lerp(wideBodyA, wideBodyB, 0.5f), Color.white, k * 0.35f);
        body.a = 1f;
        wideMpb.SetColor(WideColorId, body);
        wideLine.SetPropertyBlock(wideMpb);
        if (wideLineB != null) wideLineB.SetPropertyBlock(wideMpb);

        wideMpb.SetColor(WideColorId, WideWhiteColor);
        if (wideWhite != null) wideWhite.SetPropertyBlock(wideMpb);
        if (wideWhiteB != null) wideWhiteB.SetPropertyBlock(wideMpb);

        wideMpb.SetColor(WideColorId, WideBlackColor);
        if (wideBlack != null) wideBlack.SetPropertyBlock(wideMpb);
        if (wideBlackB != null) wideBlackB.SetPropertyBlock(wideMpb);

        if (wideGlowMr != null)
        {
            var g = Color.Lerp(wideGlowColor, Color.white, k * 0.5f);
            g.a = Mathf.Clamp01(wideGlowAlpha * (1f + k * 0.6f));
            wideMpb.SetColor(WideColorId, g);
            wideGlowMr.SetPropertyBlock(wideMpb);
        }
    }

    /// <summary>从 startPosition 走到 wideEndPosition 要跨几个键位（1~8）。</summary>
    private int WideSteps()
    {
        // 数据异常时不要算 (0-5+8)%8=5 这种大跨度，那会绕到圆弧对面去
        if (wideEndPosition < 1 || wideEndPosition > 8) return 1;
        if (wideEndPosition == startPosition) return 8; // 起止相同 = 整圈
        return isWideClockwise
            ? (wideEndPosition - startPosition + 8) % 8
            : (startPosition - wideEndPosition + 8) % 8;
    }

    /// <summary>键位对应的角度，和 getPositionFromDistance 保持一致。</summary>
    private static float AngleOf(int position)
    {
        return (position * -2f + 5f) * 0.125f * Mathf.PI;
    }

    /// <summary>
    ///     弧的中点最接近哪个键位。跨度是偶数键时正好落在中间那个键上，
    ///     奇数键时取更近的一侧（最多差 22.5°）。打击特效用它定位。
    /// </summary>
    private int WideMidPosition()
    {
        var steps = WideSteps();
        var closed = wideEndPosition == startPosition;
        var totalDeg = closed ? 360f : steps * 45f;
        var dir = isWideClockwise ? -1f : 1f;
        var amDeg = (AngleOf(startPosition) + dir * Mathf.Deg2Rad * totalDeg * 0.5f) * Mathf.Rad2Deg;

        var best = startPosition;
        var bestDiff = float.MaxValue;
        for (var p = 1; p <= 8; p++)
        {
            var d = Mathf.Abs(Mathf.DeltaAngle(amDeg, AngleOf(p) * Mathf.Rad2Deg));
            if (d < bestDiff)
            {
                bestDiff = d;
                best = p;
            }
        }

        return best;
    }

    private static Vector3 Polar(float angle, float radius)    {
        return new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
    }

    private static Vector3 PolarDir(float angle)
    {
        return new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
    }

    /// <summary>
    ///     画宽音符。空心时是一条胶囊轮廓（外弧 + 两端半圆 + 内弧），实心时是一条填充带。
    ///     四层共用同一组点，线宽从里到外递增，所以看起来是"主体 + 一圈白 + 一圈黑"。
    ///     半径随时在变，但轮廓粗细和环高恒定。
    /// </summary>
    /// <summary>
    ///     画宽音符。空心时是一条胶囊轮廓（外弧 + 两端半圆 + 内弧），实心时是一条填充带。
    ///     整圈（5>5）的轮廓拆成外圆 + 内圆两条闭环，各画各的，不会留径向接缝。
    ///     半径随时在变，但轮廓粗细和环高恒定。
    /// </summary>
    private void UpdateWide(float distance, float scale)
    {
        if (wideLine == null) return;

        var steps = WideSteps();
        var closed = wideEndPosition == startPosition;
        var totalDeg = closed ? 360f : steps * 45f;
        var dir = isWideClockwise ? -1f : 1f;              // 顺时针 = 角度递减
        var a0 = AngleOf(startPosition);
        var sweep = dir * Mathf.Deg2Rad * totalDeg;        // 有符号扫过的角度

        // 尺寸主要看 scale（退到判定圈以内才缩小），再叠一层很轻的近细远粗，
        // 幅度只有 22%，不会像之前那样看起来像透视。
        var near = Mathf.Clamp01(distance / 6f);
        var sizeK = Mathf.Lerp(0.78f, 1f, near) * scale;
        var kw = sizeK;
        var half = WideHalf * sizeK;
        var outer = distance + half;
        var inner = Mathf.Max(distance - half, 0.01f);

        var filled = !WideHollow;

        // 整圈的内圆那条线可能错过 Start 时的创建（EndPosition 由 JsonDataLoader 赋值，
        // 顺序上未必早于 Start），这里补一次，只在真整圈时建。
        if (closed && wideLineB == null && wideLine != null)
        {
            var order = spriteRenderer.sortingOrder;
            wideLineB = CreateWideRenderer("WideLineIn", order, WideBodyMaterial());
            wideWhiteB = CreateWideRenderer("WideWhiteIn", order - 1, WideBodyMaterial());
            wideBlackB = CreateWideRenderer("WideBlackIn", order - 2, WideBodyMaterial());
        }

        // 弧线每 3° 一个采样点；非整圈时两端各用 10 段拼半圆
        var nArc = Mathf.Clamp(Mathf.CeilToInt(totalDeg / 3f), 4, 160);
        var cap = closed ? 0 : 10;

        widePtsA.Clear();
        wideNrmA.Clear();
        widePtsB.Clear();
        wideNrmB.Clear();
        glowPtsA.Clear();
        glowNrmA.Clear();
        glowPtsB.Clear();
        glowNrmB.Clear();
        glowVerts.Clear();
        glowCols.Clear();
        glowTris.Clear();

        // ---- 画线路径 ----
        if (filled)
        {
            // 实心：沿环中线一条粗带，宽度就是环高；"向外"就是径向
            AppendArc(widePtsA, wideNrmA, a0, a0 + sweep, distance, 1f, nArc, !closed);
        }
        else if (closed)
        {
            // 整圈空心：外圆和内圆是两条独立的闭环，用两条线各画一圈，
            // 这样接缝处不会拉出那条"从外圈直连内圈"的径向线。
            AppendArc(widePtsA, wideNrmA, a0, a0 + sweep, outer, 1f, nArc, false);
            AppendArc(widePtsB, wideNrmB, a0, a0 + sweep, inner, -1f, nArc, false);
        }
        else
        {
            BuildOutline(widePtsA, wideNrmA, a0, sweep, distance, half, dir, nArc, cap);
        }

        // 从里到外：主体 → 白边 → 黑边，宽度依次加宽
        float wBody, wWhite, wBlack;
        if (!filled)
        {
            wBody = WideLineWidth * kw;
            wWhite = (WideLineWidth + WideWhiteExtra * 2f) * kw;
            wBlack = (WideLineWidth + (WideWhiteExtra + WideBlackExtra) * 2f) * kw;
        }
        else
        {
            var bw = half * 2f;      // 已经跟着半径缩过了，不再乘 kw
            wBody = bw;
            wWhite = bw + WideWhiteExtra * 2f * kw;
            wBlack = bw + (WideWhiteExtra + WideBlackExtra) * 2f * kw;
        }

        PushWide(wideBlack, widePtsA, wBlack, closed);
        PushWide(wideWhite, widePtsA, wWhite, closed);
        PushWide(wideLine, widePtsA, wBody, closed);
        PushWide(wideBlackB, widePtsB, wBlack, true);
        PushWide(wideWhiteB, widePtsB, wWhite, true);
        PushWide(wideLineB, widePtsB, wBody, true);

        if (wideGlowMr != null)
        {
            // 网格顶点是世界坐标（和 LineRenderer 的 useWorldSpace 一致），
            // 所以网格物体本身必须待在世界原点，不然会被父级变换再叠一次位移。
            wideGlowMr.transform.position = Vector3.zero;

            // 发光一律沿"轮廓"铺：外弧 + 两端的半圆 + 内弧。
            // 实心时画线只有一条中线，如果还按中线铺，端头那两个半圆和内侧就永远没有光。
            var gPtsA = widePtsA;
            var gNrmA = wideNrmA;
            var gPtsB = widePtsB;
            var gNrmB = wideNrmB;
            var gLoopA = closed;
            var gLoopB = true;

            if (filled)
            {
                if (closed)
                {
                    AppendArc(glowPtsA, glowNrmA, a0, a0 + sweep, outer, 1f, nArc, false);
                    AppendArc(glowPtsB, glowNrmB, a0, a0 + sweep, inner, -1f, nArc, false);
                    gLoopA = true;
                }
                else
                {
                    BuildOutline(glowPtsA, glowNrmA, a0, sweep, distance, half, dir, nArc, cap);
                    gLoopA = false;
                }

                gPtsA = glowPtsA;
                gNrmA = glowNrmA;
                gPtsB = glowPtsB;
                gNrmB = glowNrmB;
            }

            // 发光的起点。
            // 空心时轮廓路径是"描边线的中心线"，要再推半个线宽才到轮廓外缘；
            // 实心时发光路径本身已经按 half 生成成带子的边缘了，再推一次就会浮在
            // 带子外面一段距离，看起来就是"边框大了一圈"。
            var edge = filled ? 0f : wBlack * 0.5f;
            var gw = WideGlowExtra * kw;
            AppendGlow(gPtsA, gNrmA, edge, gw, gLoopA, 1f);
            AppendGlow(gPtsB, gNrmB, edge, gw, gLoopB, 1f);
            UploadGlow();
        }

        // 每帧重设一次：positionCount 变化时顶点色会被重置，颜色统一走 _Color
        ApplyWideColors();

        if (!wideLoggedAny)
        {
            wideLoggedAny = true;
            var mpb = new MaterialPropertyBlock();
            wideLine.GetPropertyBlock(mpb);
            Debug.Log("[Wide] " + startPosition + "->" + wideEndPosition
                      + " cw=" + isWideClockwise + " steps=" + steps + " closed=" + closed
                      + " filled=" + filled + " dist=" + distance.ToString("F2")
                      + " a=" + widePtsA.Count + " b=" + widePtsB.Count
                      + " | line pc=" + wideLine.positionCount + " en=" + wideLine.enabled
                      + " w=" + wideLine.widthMultiplier.ToString("F3")
                      + " p0=" + wideLine.GetPosition(0).ToString("F2")
                      + " col=" + mpb.GetColor(WideColorId));
        }
    }

    /// <summary>把一段圆弧展开成"点 + 向外法线"。整圈时 includeEnd 传 false，交给 loop 闭合。</summary>
    private static void AppendArc(List<Vector3> pts, List<Vector3> nrm,
        float aFrom, float aTo, float radius, float normalSign, int segs, bool includeEnd)
    {
        var n = includeEnd ? segs + 1 : segs;
        for (var i = 0; i < n; i++)
        {
            var a = Mathf.Lerp(aFrom, aTo, (float)i / segs);
            pts.Add(Polar(a, radius));
            nrm.Add(PolarDir(a) * normalSign);
        }
    }

    /// <summary>
    ///     生成一条"胶囊轮廓"：外弧 → 终点半圆 → 内弧 → 起点半圆，首尾接回起点。
    ///     每个点都带一个"朝胶囊外侧"的法线，内外弧方向相反，所以发光永远朝带子外面走。
    /// </summary>
    private static void BuildOutline(List<Vector3> pts, List<Vector3> nrm,
        float a0, float sweep, float distance, float half, float dir, int nArc, int cap)
    {
        var outer = distance + half;
        var inner = Mathf.Max(distance - half, 0.01f);

        // 1) 外弧 a0 → a1，向外 = 径向向外
        AppendArc(pts, nrm, a0, a0 + sweep, outer, 1f, nArc, true);

        // 2) 终点半圆：绕终点处的环中点，从外径转到内径，向 +切向鼓出
        if (cap > 0)
        {
            var a1 = a0 + sweep;
            var ur = PolarDir(a1);
            var ut = new Vector3(-ur.y, ur.x, 0f) * dir;
            for (var i = 1; i < cap; i++)
            {
                var th = Mathf.PI * i / cap;
                var c = Mathf.Cos(th);
                var sn = Mathf.Sin(th);
                pts.Add(ur * distance + ur * (half * c) + ut * (half * sn));
                nrm.Add(ur * c + ut * sn);
            }
        }

        // 3) 内弧 a1 → a0，向外 = 背离圆心（也就是朝胶囊外侧）
        AppendArc(pts, nrm, a0 + sweep, a0, inner, -1f, nArc, true);

        // 4) 起点半圆：从内径回到外径，向 -切向鼓出，最后一点正好接回序列起点
        if (cap > 0)
        {
            var ur = PolarDir(a0);
            var ut = new Vector3(-ur.y, ur.x, 0f) * dir;
            for (var i = 1; i <= cap; i++)
            {
                var th = Mathf.PI * i / cap;
                var c = Mathf.Cos(th);
                var sn = Mathf.Sin(th);
                pts.Add(ur * distance - ur * (half * c) - ut * (half * sn));
                nrm.Add(-ur * c - ut * sn);
            }
        }
    }

    /// <summary>宽音符两端各画一条 tapline，从判定圈中心指向弧的两个端点。</summary>
    private void UpdateWideTapLines(float distance, float scale)
    {
        if (tapLine == null) return;

        var show = scale > 0.3f;
        tapLine.SetActive(show);
        if (tapLineB != null) tapLineB.SetActive(show);
        if (!show) return;

        var s = Mathf.Abs(distance / 4.8f);
        tapLine.transform.rotation = Quaternion.Euler(0, 0, -22.5f + -45f * (startPosition - 1));
        tapLine.transform.localScale = new Vector3(s, s, 1f);

        if (tapLineB != null)
        {
            // 整圈（5>5）两端重合，第二条拉到对位（5 对 1），否则两条会叠在同一根上
            var endPos = wideEndPosition == startPosition
                ? (startPosition + 3) % 8 + 1
                : wideEndPosition;
            tapLineB.transform.rotation = Quaternion.Euler(0, 0, -22.5f + -45f * (endPos - 1));
            tapLineB.transform.localScale = new Vector3(s, s, 1f);
        }
    }

    private void PushWide(LineRenderer lr, List<Vector3> pts, float width, bool loop)
    {
        if (lr == null) return;
        if (pts.Count < 2)
        {
            lr.enabled = false;
            return;
        }

        // 顺序很重要：先定 positionCount 再灌坐标，最后才设 loop。
        // 直接 SetPositions 在某些状态（positionCount 还是 0、loop 已改）下不会重建顶点，
        // 结果就是"点都算好了，但画面上什么都没有"。
        var arr = pts.ToArray();
        lr.positionCount = arr.Length;
        lr.widthMultiplier = width;
        lr.SetPositions(arr);
        lr.loop = loop;
        lr.enabled = true;
    }

    /// <summary>
    ///     把一条路径展开成发光带：从 edge 起、往外 width 宽，顶点色 alpha 逐档递减。
    ///     sign 取 -1 时整条带到法线反侧，用来给实心带补上内侧那一圈光。
    /// </summary>
    private void AppendGlow(List<Vector3> pts, List<Vector3> nrm, float edge, float width, bool loop,
        float sign)
    {
        if (pts.Count < 2) return;

        var stops = GlowAlphaStops.Length;
        var baseIndex = glowVerts.Count;

        for (var i = 0; i < pts.Count; i++)
        {
            for (var k = 0; k < stops; k++)
            {
                var t = (float)k / (stops - 1);
                glowVerts.Add(pts[i] + nrm[i] * (sign * (edge + width * t)));
                glowCols.Add(new Color(1f, 1f, 1f, GlowAlphaStops[k]));
            }
        }

        var segs = loop ? pts.Count : pts.Count - 1;
        for (var i = 0; i < segs; i++)
        {
            var next = (i + 1) % pts.Count;
            var a = baseIndex + i * stops;
            var b = baseIndex + next * stops;
            for (var k = 0; k < stops - 1; k++)
            {
                glowTris.Add(a + k);
                glowTris.Add(b + k);
                glowTris.Add(b + k + 1);

                glowTris.Add(a + k);
                glowTris.Add(b + k + 1);
                glowTris.Add(a + k + 1);
            }
        }
    }

    private void UploadGlow()
    {
        if (wideGlowMr == null || wideGlowMesh == null) return;
        if (glowVerts.Count < 4)
        {
            wideGlowMr.enabled = false;
            return;
        }

        wideGlowMesh.Clear();
        wideGlowMesh.SetVertices(glowVerts);
        wideGlowMesh.SetColors(glowCols);
        wideGlowMesh.SetTriangles(glowTris, 0);
        wideGlowMesh.RecalculateBounds();
        wideGlowMr.enabled = true;
    }

    private void SetWideEnabled(bool on)
    {
        if (wideLine != null) wideLine.enabled = on;
        if (wideWhite != null) wideWhite.enabled = on;
        if (wideBlack != null) wideBlack.enabled = on;
        if (wideLineB != null) wideLineB.enabled = on;
        if (wideWhiteB != null) wideWhiteB.enabled = on;
        if (wideBlackB != null) wideBlackB.enabled = on;
        if (wideGlowMr != null) wideGlowMr.enabled = on;
    }

    // Update is called once per frame
    private void Update()
    {
        var timing = timeProvider.GetTiming(time, svGroup);
        var realtime = (float)(timeProvider.AudioTime - time);
        var distance = timing * speed * timeProvider.GetScaleFor(svGroup) + 4.8f;
        var realDistance = realtime * speed * timeProvider.GetScaleFor(svGroup) + 4.8f;
        if(!canSVAffect)
        {
            timing = realtime;
            distance = realDistance;
        }
        var destScale = distance * 0.4f + 0.51f;
        if (destScale < 0f)
        {
            destScale = 0f;
            tapLine.SetActive(false);
            if (tapLineB != null) tapLineB.SetActive(false);
            spriteRenderer.forceRenderingOff = true;
            SetWideEnabled(false);
            return;
        }

        spriteRenderer.forceRenderingOff = false;
        if (isEX) exSpriteRender.forceRenderingOff = false;
        if (isBreak && !breakAnimStart)
        {
            breakAnimStart = true;
            animator.enabled = true;
            animator.Play("BreakShine", -1, 0.5f);
        }

        if (realtime > 0)
        {
            // 宽音符的打击特效放在弧的中点，而不是起始键位——否则跨度一大，
            // 特效会贴在弧的一端，看起来和音符对不上。
            if(!isMine) GameObject.Find("NoteEffects").GetComponent<NoteEffectManager>()
                .PlayEffect(isWide ? WideMidPosition() : startPosition, isBreak);
            if (isMine) ObjectCounter.mineCount++;
            else if (isBreak) ObjectCounter.breakCount++;
            else ObjectCounter.tapCount++;

            Destroy(tapLine);
            if (tapLineB != null) Destroy(tapLineB);
            Destroy(gameObject);
        }

        if (isWide)
        {
            // 宽音符：单个 tap 的 sprite 不要了，用管带画整段弧。
            // 厚度是固定的世界单位，所以远处不会变粗——这正是贴图拼接做不到的。
            spriteRenderer.forceRenderingOff = true;
            exSpriteRender.forceRenderingOff = true;

            // 普通 tap 退到判定圈以内时是"位置钳在 1.225、自己缩小"，宽音符要一致。
            // 一旦 distance 变负，(cos,sin)*distance 会把整条弧翻到圆心对面。
            var wideDist = distance;
            var wideScale = 1f;
            if (wideDist < 1.225f)
            {
                wideScale = Mathf.Max(destScale, 0f);
                wideDist = 1.225f;
            }

            UpdateWide(wideDist, wideScale);
            UpdateWideTapLines(wideDist, wideScale);
            return;
        }

        if (isFakeStarRotate)
            transform.Rotate(0f, 0f, 400f * Time.deltaTime);
        else
            transform.rotation = Quaternion.Euler(0, 0, -22.5f + -45f * (startPosition - 1));
        tapLine.transform.rotation = Quaternion.Euler(0, 0, -22.5f + -45f * (startPosition - 1));

        if (distance < 1.225f)
        {
            transform.localScale = new Vector3(destScale, destScale);

            distance = 1.225f;
            var pos = getPositionFromDistance(distance);
            transform.position = pos;
        }
        else
        {
            var pos = getPositionFromDistance(distance);
            transform.position = pos;
            transform.localScale = new Vector3(1f, 1f);
        }

        var lineScale = Mathf.Abs(distance / 4.8f);
        tapLine.transform.localScale = new Vector3(lineScale, lineScale, 1f);
        // 线的显隐必须每帧统一判断：
        // 负 SV 回弹时 distance 可能一帧就从判定圈内侧跨到外面，跳过上面的 <1.225 分支，
        // 而 SetActive(true) 只写在那分支里，line 就会一直停在上一次的关闭状态（表现为时有时无）。
        // 注意：线是从判定圈中心连到音符的，远处也要显示（靠 localScale 拉长），
        // 所以这里不能加 distance 条件，只按"音符是否退到太内侧"判断。
        tapLine.SetActive(destScale > 0.3f);
        //lineSpriteRender.color = new Color(1f, 1f, 1f, lineScale);
    }

    private Vector3 getPositionFromDistance(float distance)
    {
        return new Vector3(
            distance * Mathf.Cos((startPosition * -2f + 5f) * 0.125f * Mathf.PI),
            distance * Mathf.Sin((startPosition * -2f + 5f) * 0.125f * Mathf.PI));
    }
}