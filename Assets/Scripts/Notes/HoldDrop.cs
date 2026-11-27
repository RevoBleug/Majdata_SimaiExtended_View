using System.Collections.Generic;
using UnityEngine;

public class HoldDrop : NoteLongDrop
{
    public int startPosition = 1;
    public float speed = 1;

    public bool isEach;
    public bool isEX;
    public bool isMine;
    public bool isBreak;
    public bool canSVAffect;

    // 宽音符 hold：头在判定圈、尾跟着 SV 走，各画一条宽环，中间用半透明色带连起来。
    // 只从 Edit 侧 5>3h[4:1] 这种语法来（h 必须紧贴在时长前面）。
    public bool isWide;
    public int wideEndPosition;
    public bool isWideClockwise;

    public Sprite tapSpr;
    public Sprite eachSpr;
    public Sprite exSpr;
    public Sprite breakSpr;
    public Sprite mineSpr;

    public Sprite eachLine;
    public Sprite breakLine;
    public Sprite mineLine;

    public Sprite holdEachEnd;
    public Sprite holdMineEnd;
    public Sprite holdBreakEnd;

    public RuntimeAnimatorController HoldShine;
    public RuntimeAnimatorController BreakShine;

    public GameObject holdEffect;

    public GameObject tapLine;

    public Color exEffectTap;
    public Color exEffectEach;
    public Color exEffectBreak;
    private Animator animator;

    private bool breakAnimStart;
    private SpriteRenderer exSpriteRender;
    private bool holdAnimStart;
    private SpriteRenderer holdEndRender;
    private SpriteRenderer lineSpriteRender;

    private SpriteRenderer spriteRenderer;

    private AudioTimeProvider timeProvider;

    // ---------------- 宽音符 hold 的绘制资源 ----------------
    // 和 TapDrop 的宽音符用同一套几何与观感常量，只是这里要画一条头环 + 一条色带。
    private const float WideHalf = 0.36f;          // 环高 0.72
    private const float WideLineWidth = 0.16f;     // 主体线宽
    private const float WideWhiteExtra = 0.04f;    // 白边单边宽度
    private const float WideBlackExtra = 0.045f;   // 黑边单边宽度
    private const float WideGlowExtra = 0.13f;     // ex 发光从轮廓外缘向外延伸的宽度

    /// <summary>
    ///     中间色带的透明度。由 Edit 的编辑器设置下发，改了要重新播放才生效。
    ///     0.35 是原来的写死值，也是默认值。
    /// </summary>
    public static float WideBandAlpha = 0.35f;

    /// <summary>一条宽环 = 黑边 / 白边 / 主体三层。整圈时每层再配一条只画内圆的线。</summary>
    private sealed class WideRing
    {
        public string Prefix;
        public int BaseOrder;
        public LineRenderer Line, White, Black;
        public LineRenderer LineB, WhiteB, BlackB;

        // ex 的外发光：一条从轮廓外缘向外 alpha 递减的网格带。
        // 普通混合叠不出"越往外越淡"，只能靠顶点色做连续渐变。
        public MeshRenderer Glow;
        public Mesh GlowMesh;
        public readonly List<Vector3> GlowVerts = new(1024);
        public readonly List<Color> GlowCols = new(1024);
        public readonly List<int> GlowTris = new(1024);
        public readonly List<Vector3> GlowPtsA = new(256);
        public readonly List<Vector3> GlowNrmA = new(256);
        public readonly List<Vector3> GlowPtsB = new(256);
        public readonly List<Vector3> GlowNrmB = new(256);
    }

    private WideRing wideHeadRing;
    private GameObject tapLineB;                   // 宽音符第二个端点的 tapline

    private MeshRenderer wideBandMr;
    private Mesh wideBandMesh;
    private Color wideBodyA = Color.white;
    private Color wideBodyB = Color.white;
    private Color wideGlowColor = Color.white;
    private float wideGlowAlpha = 0.5f;

    private readonly List<Vector3> ringPtsA = new(256);
    private readonly List<Vector3> ringNrmA = new(256);
    private readonly List<Vector3> ringPtsB = new(256);
    private readonly List<Vector3> ringNrmB = new(256);

    private readonly List<Vector3> bandVerts = new(512);
    private readonly List<Color> bandCols = new(512);
    private readonly List<int> bandTris = new(1024);

    private static bool wideHollowLogged;

    private void Start()
    {
        var notes = GameObject.Find("Notes").transform;
        holdEffect = Instantiate(holdEffect, notes);
        holdEffect.SetActive(false);

        tapLine = Instantiate(tapLine, notes);
        tapLine.SetActive(false);
        lineSpriteRender = tapLine.GetComponent<SpriteRenderer>();

        exSpriteRender = transform.GetChild(0).GetComponent<SpriteRenderer>();

        timeProvider = GameObject.Find("AudioTimeProvider").GetComponent<AudioTimeProvider>();
        spriteRenderer = GetComponent<SpriteRenderer>();

        holdEndRender = transform.GetChild(1).GetComponent<SpriteRenderer>();

        spriteRenderer.sortingOrder += noteSortOrder;
        exSpriteRender.sortingOrder += noteSortOrder;
        holdEndRender.sortingOrder += noteSortOrder;

        spriteRenderer.sprite = tapSpr;
        exSpriteRender.sprite = exSpr;

        var anim = gameObject.AddComponent<Animator>();
        anim.enabled = false;
        animator = anim;

        if (isEX) exSpriteRender.color = exEffectTap;
        if (isMine)
        {
            spriteRenderer.sprite = mineSpr;
            lineSpriteRender.sprite = mineLine;
            holdEndRender.sprite = holdMineEnd;
        }
        if (isEach)
        {
            spriteRenderer.sprite = eachSpr;
            lineSpriteRender.sprite = eachLine;
            holdEndRender.sprite = holdEachEnd;
            if (isEX) exSpriteRender.color = exEffectEach;
        }

        if (isBreak)
        {
            spriteRenderer.sprite = breakSpr;
            lineSpriteRender.sprite = breakLine;
            holdEndRender.sprite = holdBreakEnd;
            if (isEX) exSpriteRender.color = exEffectBreak;
        }

        spriteRenderer.forceRenderingOff = true;
        exSpriteRender.forceRenderingOff = true;
        holdEndRender.enabled = false;

        if (isWide) SetupWideHold();

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

        // 判定中（realtime 在 [0, LastFor] 之间）头端必须压在判定圈上（4.8 就是 realtime=0 的位置）。
        // 负 SV 回退会把整条推离判定圈，一旦两端都跑掉，画面上就看不到"正在按住"的那一端了。
        // canSVAffect=false 的 hold 本来就不跟 SV 走，保持原来的穿过表现。
        var judging = canSVAffect && realtime >= 0f && realtime <= LastFor;
        if (judging) distance = 4.8f;

        var destScale = distance * 0.4f + 0.51f;
        if (destScale < 0f)
        {
            // 越过判定圈很远（负 SV / 回退）时必须隐藏，否则长条会停在最后的可见位置一直不动
            destScale = 0f;
            spriteRenderer.forceRenderingOff = true;
            exSpriteRender.forceRenderingOff = true;
            holdEndRender.enabled = false;
            if (tapLine != null) tapLine.SetActive(false);
            if (holdEffect != null) holdEffect.SetActive(false);
            if (isWide)
            {
                SetWideRingEnabled(wideHeadRing, false);
                if (wideBandMr != null) wideBandMr.enabled = false;
                if (tapLineB != null) tapLineB.SetActive(false);
            }
            return;
        }

        spriteRenderer.forceRenderingOff = false;
        if (isEX) exSpriteRender.forceRenderingOff = false;

        if (isBreak && !breakAnimStart)
        {
            breakAnimStart = true;
            animator.runtimeAnimatorController = BreakShine;
            animator.enabled = true; // break hold闪烁
        }

        spriteRenderer.size = new Vector2(1.22f, 1.4f);

        // 尾巴位置 == 组曲线在"现在"和"尾巴时刻"的位置差。
        // 直接用组曲线的当前位置（不再从 timing 绕），组内 hold 才不会被主谱面的 SV 带偏。
        var holdTime = (float)(timeProvider.GetScrollDistD(svGroup)
                               - timeProvider.GetPositionAtTimeD(time + LastFor, svGroup));
        var holdReal = realtime - LastFor;
        var holdDistance = holdTime * speed * timeProvider.GetScaleFor(svGroup) + 4.8f;
        var holdRealDistance = holdReal * speed * timeProvider.GetScaleFor(svGroup) + 4.8f;
        if (!canSVAffect)
        {
            holdTime = holdReal;
            holdDistance = holdRealDistance;
        }
        if (holdReal > 0)
        {
            if(!isMine) GameObject.Find("NoteEffects").GetComponent<NoteEffectManager>().PlayEffect(startPosition, isBreak);
            if (isMine)
                GameObject.Find("ObjectCounter").GetComponent<ObjectCounter>().mineCount++;
            else if (isBreak)
                GameObject.Find("ObjectCounter").GetComponent<ObjectCounter>().breakCount++;
            else
                GameObject.Find("ObjectCounter").GetComponent<ObjectCounter>().holdCount++;
            Destroy(tapLine);
            Destroy(holdEffect);
            if (tapLineB != null) Destroy(tapLineB);
            Destroy(gameObject);
        }

        // 宽音符 hold 走完全独立的一套绘制（两条环 + 一条色带），
        // 下面那套 sprite 拉伸的逻辑（1.22 宽的竖条）对它没有意义。
        if (isWide)
        {
            UpdateWideHold(distance, holdDistance, judging);
            return;
        }


        transform.rotation = Quaternion.Euler(0, 0, -22.5f + -45f * (startPosition - 1));
        tapLine.transform.rotation = transform.rotation;
        holdEffect.transform.position = getPositionFromDistance(4.8f);

        if (distance < 1.225f)
        {
            transform.localScale = new Vector3(destScale, destScale);
            spriteRenderer.size = new Vector2(1.22f, 1.42f);
            distance = 1.225f;
            var pos = getPositionFromDistance(distance);
            transform.position = pos;

        }
        else
        {
            if (judging)
            {
                // 判定中：只有头端钉在判定圈上，尾巴照 SV 算出来的位置走（所以会随 SV 上下动）。
                // 但尾巴不能画到音符生成位置之外：1.225 是所有下落脚本共用的"音符最远边界"
                // （distance < 1.225 就钳住），再往外只会拉出一条超出可见范围的长条。
                distance = 4.8f;
                if (holdDistance < 1.225f) holdDistance = 1.225f;
                if (!isMine) holdEffect.SetActive(true);
                startHoldShine();
            }
            else if (holdDistance < 1.225f && distance >= 4.8f && realDistance >= 4.8f) // 头到达 尾未出现
            {
                holdDistance = 1.225f;
                distance = 4.8f;
                if(!isMine) holdEffect.SetActive(true);
                startHoldShine();
            }
            else if (holdDistance < 1.225f && distance < 4.8f) // 头未到达 尾未出现
            {
                holdDistance = 1.225f;
            }
            else if (holdDistance >= 1.225f && distance >= 4.8f && realDistance >= 4.8f) // 头到达 尾出现
            {
                distance = 4.8f;
                if (!isMine) holdEffect.SetActive(true);
                startHoldShine();
            }
            // 最后一档（头未到达 尾出现）不需要额外处理：两端都按算出来的位置画

            // 负 SV / 回退段会让尾巴"超过"头。判定中尾巴可以跑到头的更外侧，
            // 也可以缩到判定圈内侧，都按两端实际跨度渲染；
            // 非判定中仍把尾巴压回头上，避免长度算成负数翻转。
            if (!judging && holdDistance > distance) holdDistance = distance;

            var top = Mathf.Max(distance, holdDistance);
            var bottom = Mathf.Min(distance, holdDistance);
            var size = top - bottom + 1.4f;
            transform.position = getPositionFromDistance((top + bottom) * 0.5f); //0.325
            spriteRenderer.size = new Vector2(1.22f, size);
            // 尾端标记跟着"离判定圈更远"的那一端走
            var tailOffset = size / 2 - 0.6825f;
            holdEndRender.transform.localPosition = new Vector3(0f, holdDistance >= distance ? tailOffset : -tailOffset);
            transform.localScale = new Vector3(1f, 1f);
        }

        // 尾端标记只看"尾巴本身在不在可见范围内"：1.225 是所有下落脚本共用的音符生成边界，
        // 尾巴比它更靠内就等于跑到屏幕外了。
        // 以前是分散在 if/else 各分支里开关，负 SV 把尾巴推回边界外时漏掉了关闭，
        // 于是那个点会一直浮在画面上不消失。
        holdEndRender.enabled = holdDistance > 1.225f && distance > 1.225f;

        var lineScale = Mathf.Abs(distance / 4.8f);
        lineScale = lineScale >= 1f ? 1f : lineScale;
        tapLine.transform.localScale = new Vector3(lineScale, lineScale, 1f);
        // 同 TapDrop：线的显隐每帧统一判断，避免负 SV 回弹跨帧时停在关闭状态
        // 线是从判定圈中心连到音符的，远处也要显示（靠 localScale 拉长），只按"是否退到太内侧"判断
        if (tapLine != null) tapLine.SetActive(destScale > 0.3f);
        exSpriteRender.size = spriteRenderer.size;
        //lineSpriteRender.color = new Color(1f, 1f, 1f, lineScale);
    }

    private void startHoldShine()
    {
        if (!holdAnimStart)
        {
            holdAnimStart = true;
            animator.runtimeAnimatorController = HoldShine;
            animator.enabled = true;
        }
    }

    // ==================== 宽音符 hold ====================
    // 形态：判定圈上一圈宽环（头），尾巴一圈宽环，中间一条无边框的半透明色带连接。
    // 环的几何、层厚、颜色全部照 TapDrop 的宽音符来，保证同一个 note 的头和普通宽 tap 长得一样。

    private static Material wideBodyMat;
    private static Material wideGlowMat;

    private static Shader WideShader()
    {
        // 放 Resources 里由 Resources.Load 取，避免打包时被裁掉
        var sh = Resources.Load<Shader>("WideNote");
        if (sh == null) sh = Shader.Find("Maj/WideNote");
        if (sh == null) Debug.LogError("[WideHold] 找不到 Maj/WideNote，宽音符无法绘制");
        return sh;
    }

    private static Material MakeWideMaterial(bool additive)
    {
        var sh = WideShader();
        if (sh == null) return null;

        var mat = new Material(sh) { mainTexture = Texture2D.whiteTexture };
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", additive
            ? (float)UnityEngine.Rendering.BlendMode.One
            : (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
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

    /// <summary>ex 宽音符的那圈外发光：独立网格 + 加法混合材质。</summary>
    private void SetupWideGlow(WideRing ring, int sortingOrder)
    {
        var mat = WideGlowMaterial();
        if (mat == null) return;

        var go = new GameObject(ring.Prefix + "Glow");
        go.transform.SetParent(transform, false);
        ring.GlowMesh = new Mesh { name = ring.Prefix + "Glow" };
        ring.GlowMesh.MarkDynamic();
        go.AddComponent<MeshFilter>().mesh = ring.GlowMesh;

        ring.Glow = go.AddComponent<MeshRenderer>();
        ring.Glow.material = mat;
        ring.Glow.sortingLayerID = spriteRenderer.sortingLayerID;
        ring.Glow.sortingOrder = sortingOrder;
        ring.Glow.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ring.Glow.receiveShadows = false;
        ring.Glow.enabled = false;
    }

    private void SetupWideHold()
    {
        ResolveWideColors();

        // noteSortOrder 是递减分配的。宽音符 hold 只占一条头环 + 一条色带（尾巴不用环）：
        //   头环 base / base-1 / base-2
        //   ex 发光 base-3
        //   色带 base-4（垫在最底下，下端被头环压住）
        var baseOrder = spriteRenderer.sortingOrder;
        wideHeadRing = CreateWideRing("WideHead", baseOrder);
        SetupWideBand(baseOrder - 4);

        if (tapLine != null)
        {
            tapLineB = Instantiate(tapLine, tapLine.transform.parent);
            tapLineB.SetActive(false);
            var sr = tapLineB.GetComponent<SpriteRenderer>();
            sr.sprite = lineSpriteRender.sprite;
            sr.sortingOrder = lineSpriteRender.sortingOrder;
        }
    }

    private WideRing CreateWideRing(string prefix, int baseOrder)
    {
        var mat = WideBodyMaterial();
        var ring = new WideRing
        {
            Prefix = prefix,
            BaseOrder = baseOrder,
            Line = CreateWideLine(prefix, baseOrder, mat),
            White = CreateWideLine(prefix + "White", baseOrder - 1, mat),
            Black = CreateWideLine(prefix + "Black", baseOrder - 2, mat)
        };

        // 整圈（5>5）的轮廓是外圆 + 内圆两个独立闭环，一条线画不了，每层再配一条
        if (wideEndPosition == startPosition) EnsureWideRingInner(ring, mat);

        // ex 才有那圈外发光，普通宽音符没有
        if (isEX) SetupWideGlow(ring, baseOrder - 3);

        return ring;
    }

    /// <summary>
    ///     整圈才需要的"内圆"那三条线。Start 时 wideEndPosition 未必已经由
    ///     JsonDataLoader 赋值，所以第一次画的时候还要再补一次。
    /// </summary>
    private void EnsureWideRingInner(WideRing ring, Material mat)
    {
        if (ring == null || ring.LineB != null) return;
        if (mat == null) mat = WideBodyMaterial();
        ring.LineB = CreateWideLine(ring.Prefix + "In", ring.BaseOrder, mat);
        ring.WhiteB = CreateWideLine(ring.Prefix + "WhiteIn", ring.BaseOrder - 1, mat);
        ring.BlackB = CreateWideLine(ring.Prefix + "BlackIn", ring.BaseOrder - 2, mat);
    }

    private LineRenderer CreateWideLine(string childName, int sortingOrder, Material mat)
    {
        if (mat == null) return null;

        var go = new GameObject(childName);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.alignment = LineAlignment.View;
        lr.numCapVertices = 10;     // 两端半圆
        lr.numCornerVertices = 4;
        lr.textureMode = LineTextureMode.Stretch;
        lr.positionCount = 0;       // 默认那两个 (0,0,0) 会画成屏幕中间一个圆点
        lr.loop = false;
        lr.sortingLayerID = spriteRenderer.sortingLayerID;
        lr.sortingOrder = sortingOrder;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.material = mat;
        lr.enabled = false;
        return lr;
    }

    private void SetupWideBand(int sortingOrder)
    {
        var mat = WideBodyMaterial();
        if (mat == null) return;

        var go = new GameObject("WideBand");
        go.transform.SetParent(transform, false);
        wideBandMesh = new Mesh { name = "WideBand" };
        wideBandMesh.MarkDynamic();
        go.AddComponent<MeshFilter>().mesh = wideBandMesh;

        wideBandMr = go.AddComponent<MeshRenderer>();
        wideBandMr.material = mat;
        wideBandMr.sortingLayerID = spriteRenderer.sortingLayerID;
        wideBandMr.sortingOrder = sortingOrder;
        wideBandMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        wideBandMr.receiveShadows = false;
        wideBandMr.enabled = false;
    }

    /// <summary>
    ///     颜色直接取贴图里的两层主色（tap 系列是"外浅内深"两层，中间夹白边）：
    ///     tap 254,126,214 → 255,20,120；each 255,255,0 → 255,181,0；
    ///     break 255,203,1 → 255,52,19。中间的取插值。
    /// </summary>
    private void ResolveWideColors()
    {
        if (isMine)
        {
            wideBodyA = wideBodyB = new Color(0.62f, 0.62f, 0.62f);
        }
        else if (isBreak)
        {
            wideBodyA = new Color(1f, 0.80f, 0.00f);
            wideBodyB = new Color(1f, 0.20f, 0.08f);
        }
        else if (isEach)
        {
            // each 是纯黄，别往橙偏
            wideBodyA = new Color(1f, 1f, 0f);
            wideBodyB = new Color(1f, 0.71f, 0f);
        }
        else
        {
            wideBodyA = new Color(1f, 0.49f, 0.84f);
            wideBodyB = new Color(1f, 0.08f, 0.47f);
        }

        // ex 那圈"保护套"不是纯白。普通 note 的 ex 是一层半透明淡紫白贴在主色上，
        // 视觉上透出它自己的颜色；宽音符的发光是加法叠在外面的，用纯白就只剩一圈死白。
        // 所以取主色往白提亮，粉的就是粉白、黄的就是黄白。
        var body = Color.Lerp(wideBodyA, wideBodyB, 0.5f);
        wideGlowColor = Color.Lerp(body, Color.white, 0.55f);
        wideGlowAlpha = isEX ? 0.85f : 0.32f;
        if (isMine)
        {
            wideGlowColor = new Color(0.86f, 0.86f, 0.86f);
            wideGlowAlpha = 0.18f;
        }
    }

    /// <summary>break 宽 hold 的闪光：宽音符的 sprite 是关掉的，自己按 BreakShine 的节奏算。</summary>
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
    private static readonly Color WideBlackColor = new Color(0.06f, 0.06f, 0.09f, 1f);

    private void ApplyWideRingColors(WideRing ring)
    {
        if (ring == null || ring.Line == null) return;
        if (wideMpb == null) wideMpb = new MaterialPropertyBlock();

        var k = (BreakPulse() - 1f) / 0.3f;

        var body = Color.Lerp(Color.Lerp(wideBodyA, wideBodyB, 0.5f), Color.white, k * 0.35f);
        body.a = 1f;
        wideMpb.SetColor(WideColorId, body);
        ring.Line.SetPropertyBlock(wideMpb);
        if (ring.LineB != null) ring.LineB.SetPropertyBlock(wideMpb);

        wideMpb.SetColor(WideColorId, Color.white);
        if (ring.White != null) ring.White.SetPropertyBlock(wideMpb);
        if (ring.WhiteB != null) ring.WhiteB.SetPropertyBlock(wideMpb);

        wideMpb.SetColor(WideColorId, WideBlackColor);
        if (ring.Black != null) ring.Black.SetPropertyBlock(wideMpb);
        if (ring.BlackB != null) ring.BlackB.SetPropertyBlock(wideMpb);
    }

    /// <summary>从 startPosition 走到 wideEndPosition 要跨几个键位（1~8）。</summary>
    private int WideSteps()
    {
        if (wideEndPosition < 1 || wideEndPosition > 8) return 1;
        if (wideEndPosition == startPosition) return 8; // 起止相同 = 整圈
        return isWideClockwise
            ? (wideEndPosition - startPosition + 8) % 8
            : (startPosition - wideEndPosition + 8) % 8;
    }

    private static float AngleOf(int position)
    {
        return (position * -2f + 5f) * 0.125f * Mathf.PI;
    }

    private static Vector3 Polar(float angle, float radius)
    {
        return new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
    }

    private static Vector3 PolarDir(float angle)
    {
        return new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
    }

    /// <summary>弧的中点位置。打击特效和 hold 闪光都放这儿，别贴在弧的一端。</summary>
    private Vector3 WideMidPoint(float radius)
    {
        var steps = WideSteps();
        var closed = wideEndPosition == startPosition;
        var totalDeg = closed ? 360f : steps * 45f;
        var dir = isWideClockwise ? -1f : 1f;
        var am = AngleOf(startPosition) + dir * Mathf.Deg2Rad * totalDeg * 0.5f;
        return Polar(am, radius);
    }

    private void UpdateWideRing(WideRing ring, float distance, float sizeK)
    {
        if (ring == null || ring.Line == null) return;

        var steps = WideSteps();
        var closed = wideEndPosition == startPosition;
        var totalDeg = closed ? 360f : steps * 45f;
        var dir = isWideClockwise ? -1f : 1f;
        var a0 = AngleOf(startPosition);
        var sweep = dir * Mathf.Deg2Rad * totalDeg;

        var half = WideHalf * sizeK;
        var outer = distance + half;
        var inner = Mathf.Max(distance - half, 0.01f);

        var nArc = Mathf.Clamp(Mathf.CeilToInt(totalDeg / 3f), 4, 160);
        var cap = closed ? 0 : 10;
        var filled = !TapDrop.WideHollow;

        if (closed) EnsureWideRingInner(ring, null);

        ringPtsA.Clear();
        ringNrmA.Clear();
        ringPtsB.Clear();
        ringNrmB.Clear();

        if (filled)
        {
            AppendArc(ringPtsA, ringNrmA, a0, a0 + sweep, distance, 1f, nArc, !closed);
        }
        else if (closed)
        {
            AppendArc(ringPtsA, ringNrmA, a0, a0 + sweep, outer, 1f, nArc, false);
            AppendArc(ringPtsB, ringNrmB, a0, a0 + sweep, inner, -1f, nArc, false);
        }
        else
        {
            BuildOutline(ringPtsA, ringNrmA, a0, sweep, distance, half, dir, nArc, cap);
        }

        float wBody, wWhite, wBlack;
        if (!filled)
        {
            wBody = WideLineWidth * sizeK;
            wWhite = (WideLineWidth + WideWhiteExtra * 2f) * sizeK;
            wBlack = (WideLineWidth + (WideWhiteExtra + WideBlackExtra) * 2f) * sizeK;
        }
        else
        {
            var bw = half * 2f;     // 已经跟着半径缩过了，不再乘 sizeK
            wBody = bw;
            wWhite = bw + WideWhiteExtra * 2f * sizeK;
            wBlack = bw + (WideWhiteExtra + WideBlackExtra) * 2f * sizeK;
        }

        PushWide(ring.Black, ringPtsA, wBlack, closed);
        PushWide(ring.White, ringPtsA, wWhite, closed);
        PushWide(ring.Line, ringPtsA, wBody, closed);
        PushWide(ring.BlackB, ringPtsB, wBlack, true);
        PushWide(ring.WhiteB, ringPtsB, wWhite, true);
        PushWide(ring.LineB, ringPtsB, wBody, true);

        if (ring.Glow != null)
            UploadWideGlow(ring, filled, closed, a0, sweep, dir, half, distance, nArc, cap, wBlack, sizeK);

        ApplyWideRingColors(ring);
    }

    // 发光沿宽度的衰减档位：贴本体最亮，第二档就掉到 45%，衰减要快、边界要窄。
    private static readonly float[] GlowAlphaStops = { 1f, 0.50f, 0.20f, 0f };

    /// <summary>
    ///     ex 的外发光。空心时直接沿胶囊轮廓铺；实心时画线只有一条中线，
    ///     按中线铺的话端头半圆和内侧永远没有光，所以重新按轮廓生成一遍。
    /// </summary>
    private void UploadWideGlow(WideRing ring, bool filled, bool closed,
        float a0, float sweep, float dir, float half, float distance, int nArc, int cap,
        float wBlack, float sizeK)
    {
        // 顶点是世界坐标，网格物体必须待在世界原点
        ring.Glow.transform.position = Vector3.zero;
        ring.GlowVerts.Clear();
        ring.GlowCols.Clear();
        ring.GlowTris.Clear();

        var gPtsA = ringPtsA;
        var gNrmA = ringNrmA;
        var gPtsB = ringPtsB;
        var gNrmB = ringNrmB;
        var gLoopA = closed;
        var gLoopB = true;

        if (filled)
        {
            ring.GlowPtsA.Clear();
            ring.GlowNrmA.Clear();
            ring.GlowPtsB.Clear();
            ring.GlowNrmB.Clear();

            if (closed)
            {
                AppendArc(ring.GlowPtsA, ring.GlowNrmA, a0, a0 + sweep, distance + half, 1f, nArc, false);
                AppendArc(ring.GlowPtsB, ring.GlowNrmB, a0, a0 + sweep,
                    Mathf.Max(distance - half, 0.01f), -1f, nArc, false);
                gLoopA = true;
            }
            else
            {
                BuildOutline(ring.GlowPtsA, ring.GlowNrmA, a0, sweep, distance, half, dir, nArc, cap);
                gLoopA = false;
            }

            gPtsA = ring.GlowPtsA;
            gNrmA = ring.GlowNrmA;
            gPtsB = ring.GlowPtsB;
            gNrmB = ring.GlowNrmB;
        }

        // 发光必须从"最外层可见边缘"起算，否则会被上面几层盖住：
        //   空心：轮廓点是描边中心线，推半个黑边宽才到外缘；
        //   实心：轮廓点已经在带子边缘，但白边/黑边比主体还宽出一截，
        //         推 0 的话整条发光都埋在黑边里，实心就完全看不到 ex 光。
        var edge = filled
            ? (WideWhiteExtra + WideBlackExtra) * sizeK
            : wBlack * 0.5f;
        var gw = WideGlowExtra * sizeK;
        AppendGlow(ring, gPtsA, gNrmA, edge, gw, gLoopA, 1f);
        AppendGlow(ring, gPtsB, gNrmB, edge, gw, gLoopB, 1f);

        if (ring.GlowVerts.Count < 4)
        {
            ring.Glow.enabled = false;
            return;
        }

        ring.GlowMesh.Clear();
        ring.GlowMesh.SetVertices(ring.GlowVerts);
        ring.GlowMesh.SetColors(ring.GlowCols);
        ring.GlowMesh.SetTriangles(ring.GlowTris, 0);
        ring.GlowMesh.RecalculateBounds();
        ring.Glow.enabled = true;

        if (wideMpb == null) wideMpb = new MaterialPropertyBlock();
        var k = (BreakPulse() - 1f) / 0.3f;
        var g = Color.Lerp(wideGlowColor, Color.white, k * 0.5f);
        g.a = Mathf.Clamp01(wideGlowAlpha * (1f + k * 0.6f));
        wideMpb.SetColor(WideColorId, g);
        ring.Glow.SetPropertyBlock(wideMpb);
    }

    /// <summary>把一条路径展开成发光带：从 edge 起、往外 width 宽，顶点色 alpha 逐档递减。</summary>
    private static void AppendGlow(WideRing ring, List<Vector3> pts, List<Vector3> nrm,
        float edge, float width, bool loop, float sign)
    {
        if (pts.Count < 2) return;

        var stops = GlowAlphaStops.Length;
        var baseIndex = ring.GlowVerts.Count;

        for (var i = 0; i < pts.Count; i++)
        {
            for (var k = 0; k < stops; k++)
            {
                var t = (float)k / (stops - 1);
                ring.GlowVerts.Add(pts[i] + nrm[i] * (sign * (edge + width * t)));
                ring.GlowCols.Add(new Color(1f, 1f, 1f, GlowAlphaStops[k]));
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
                ring.GlowTris.Add(a + k);
                ring.GlowTris.Add(b + k);
                ring.GlowTris.Add(b + k + 1);

                ring.GlowTris.Add(a + k);
                ring.GlowTris.Add(b + k + 1);
                ring.GlowTris.Add(a + k + 1);
            }
        }
    }

    /// <summary>
    ///     中间那条色带：头环到尾环之间的扇环区域，纯色半透明、没有描边。
    ///     两端正好落在两个环的中心线上，多出来的一半被环盖住，所以看不到裸边。
    /// </summary>
    private void UpdateWideBand(float headDist, float tailDist)
    {
        if (wideBandMr == null || wideBandMesh == null) return;

        var near = Mathf.Min(headDist, tailDist);
        var far = Mathf.Max(headDist, tailDist);
        // 两端贴在一起（hold 极短 / 尾巴压到头）时没有中间段，直接不画
        if (far - near < 0.02f)
        {
            wideBandMr.enabled = false;
            return;
        }

        var steps = WideSteps();
        var closed = wideEndPosition == startPosition;
        var totalDeg = closed ? 360f : steps * 45f;
        var dir = isWideClockwise ? -1f : 1f;
        var a0 = AngleOf(startPosition);
        var sweep = dir * Mathf.Deg2Rad * totalDeg;

        var nArc = Mathf.Clamp(Mathf.CeilToInt(totalDeg / 3f), 4, 160);

        bandVerts.Clear();
        bandCols.Clear();
        bandTris.Clear();

        // 顶点色固定纯白，颜色和透明度每帧走材质的 _Color（见 ApplyWideBandColor），
        // 这样按住时的脉冲不用重建网格。
        for (var i = 0; i <= nArc; i++)
        {
            var a = Mathf.Lerp(a0, a0 + sweep, (float)i / nArc);
            bandVerts.Add(Polar(a, near));
            bandVerts.Add(Polar(a, far));
            bandCols.Add(Color.white);
            bandCols.Add(Color.white);
        }

        for (var i = 0; i < nArc; i++)
        {
            var b = i * 2;
            bandTris.Add(b);
            bandTris.Add(b + 1);
            bandTris.Add(b + 3);

            bandTris.Add(b);
            bandTris.Add(b + 3);
            bandTris.Add(b + 2);
        }

        wideBandMesh.Clear();
        wideBandMesh.SetVertices(bandVerts);
        wideBandMesh.SetColors(bandCols);
        wideBandMesh.SetTriangles(bandTris, 0);
        wideBandMesh.RecalculateBounds();
        // 顶点是世界坐标，网格物体必须待在世界原点，否则会被父级变换再叠一次位移
        wideBandMr.transform.position = Vector3.zero;
        wideBandMr.enabled = true;
    }

    /// <summary>
    ///     hold 按住的闪光节奏。形状照 HoldShine.anim（峰值 1.3、先升后降），
    ///     周期从动画里的 0.2667s 放慢到 0.4s——照原速实际看着偏急。
    ///     普通 hold 靠 Animator 驱动材质的 _Brightness，宽音符的 sprite 是关掉的，只能自己算。
    /// </summary>
    private static float HoldShinePulse()
    {
        const float period = 0.4f;
        const float peak = 0.2f;
        var t = Mathf.Repeat(Time.time, period);
        if (t < peak) return Mathf.Lerp(1f, 1.3f, t / peak);
        return Mathf.Lerp(1.3f, 1f, (t - peak) / peak);
    }

    /// <summary>
    ///     色带的颜色。平时是纯色半透明；按住时跟着 HoldShine 的节奏整体提亮。
    ///     白度只到 30%、alpha 只到 0.55——再往上整条带子就发白了，看不出原本的颜色。
    /// </summary>
    private void ApplyWideBandColor(bool holding)
    {
        if (wideBandMr == null) return;
        if (wideMpb == null) wideMpb = new MaterialPropertyBlock();

        var col = Color.Lerp(wideBodyA, wideBodyB, 0.5f);

        if (holding)
        {
            var k = Mathf.Clamp01((HoldShinePulse() - 1f) / 0.3f);   // 0（暗）~ 1（峰值）
            // 只提亮一点点：白度再往上，色带就看不出原本是粉/黄/橙了
            col = Color.Lerp(col, Color.white, 0.15f * k);
            col.a = Mathf.Lerp(WideBandAlpha, 0.55f, k);
        }
        else
        {
            col.a = WideBandAlpha;
        }

        wideMpb.SetColor(WideColorId, col);
        wideBandMr.SetPropertyBlock(wideMpb);
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

    /// <summary>胶囊轮廓：外弧 → 终点半圆 → 内弧 → 起点半圆，首尾接回起点。</summary>
    private static void BuildOutline(List<Vector3> pts, List<Vector3> nrm,
        float a0, float sweep, float distance, float half, float dir, int nArc, int cap)
    {
        var outer = distance + half;
        var inner = Mathf.Max(distance - half, 0.01f);

        AppendArc(pts, nrm, a0, a0 + sweep, outer, 1f, nArc, true);

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

        AppendArc(pts, nrm, a0 + sweep, a0, inner, -1f, nArc, true);

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

    private static void PushWide(LineRenderer lr, List<Vector3> pts, float width, bool loop)
    {
        if (lr == null) return;
        if (pts.Count < 2)
        {
            lr.enabled = false;
            return;
        }

        // 先定 positionCount 再灌坐标，最后才设 loop：顺序反了顶点不会重建
        var arr = pts.ToArray();
        lr.positionCount = arr.Length;
        lr.widthMultiplier = width;
        lr.SetPositions(arr);
        lr.loop = loop;
        lr.enabled = true;
    }

    /// <summary>宽音符的两条 tapline，从判定圈中心指向弧的两个端点。</summary>
    private void UpdateWideTapLines(float headDist, float sizeK)
    {
        if (tapLine == null) return;

        var show = sizeK > 0.3f;
        tapLine.SetActive(show);
        if (tapLineB != null) tapLineB.SetActive(show);
        if (!show) return;

        var s = Mathf.Abs(headDist / 4.8f);
        tapLine.transform.rotation = Quaternion.Euler(0, 0, -22.5f + -45f * (startPosition - 1));
        tapLine.transform.localScale = new Vector3(s, s, 1f);

        if (tapLineB != null)
        {
            // 整圈两端重合，第二条拉到对位（5 对 1），否则两条会叠在同一根上
            var endPos = wideEndPosition == startPosition
                ? (startPosition + 3) % 8 + 1
                : wideEndPosition;
            tapLineB.transform.rotation = Quaternion.Euler(0, 0, -22.5f + -45f * (endPos - 1));
            tapLineB.transform.localScale = new Vector3(s, s, 1f);
        }
    }

    private void SetWideRingEnabled(WideRing ring, bool on)
    {
        if (ring == null) return;
        if (ring.Line != null) ring.Line.enabled = on;
        if (ring.White != null) ring.White.enabled = on;
        if (ring.Black != null) ring.Black.enabled = on;
        if (ring.LineB != null) ring.LineB.enabled = on;
        if (ring.WhiteB != null) ring.WhiteB.enabled = on;
        if (ring.BlackB != null) ring.BlackB.enabled = on;
        if (ring.Glow != null) ring.Glow.enabled = on;
    }

    /// <summary>宽音符 hold 的每帧绘制：头环钉在判定圈、尾环跟 SV 走、中间一条色带。</summary>
    private void UpdateWideHold(float headDist, float tailDist, bool judging)
    {
        spriteRenderer.forceRenderingOff = true;
        exSpriteRender.forceRenderingOff = true;
        holdEndRender.enabled = false;

        // 退到判定圈以内时和普通 tap 一样整体缩小，位置钳在 1.225
        var scale = 1f;
        if (headDist < 1.225f)
        {
            scale = Mathf.Max(headDist * 0.4f + 0.51f, 0f);
            headDist = 1.225f;
        }

        // 尾巴不用宽环表示，只让色带延伸过去。原因：尾巴走到终点时会和头环叠在一起，
        // 看起来就是两个 note；而且它在画面边缘出现时也像个孤立的宽 tap。
        if (tailDist < 1.225f) tailDist = 1.225f;
        // 非判定中时尾巴不能跑到头的内侧（负 SV 回退会让它越过），压回头上防翻转。
        // 注意方向：headDist 越大越靠判定圈，正常谱面尾巴比头更远、也就是更小。
        if (!judging && tailDist > headDist) tailDist = headDist;

        // 近细远粗只有 22% 的幅度，不会像透视
        var sizeK = Mathf.Lerp(0.78f, 1f, Mathf.Clamp01(headDist / 6f)) * scale;

        UpdateWideRing(wideHeadRing, headDist, sizeK);
        UpdateWideBand(headDist, tailDist);
        ApplyWideBandColor(judging);
        UpdateWideTapLines(headDist, sizeK);

        if (holdEffect != null)
        {
            // 头端压到判定圈（4.8）才算"按住了"；判定中当然也显示
            var active = judging || headDist >= 4.79f;
            holdEffect.SetActive(active);
            if (active) holdEffect.transform.position = WideMidPoint(4.8f);
        }

        if (judging) startHoldShine();

        if (!wideHollowLogged)
        {
            wideHollowLogged = true;
            Debug.Log("[WideHold] " + startPosition + "->" + wideEndPosition
                      + " hollow=" + TapDrop.WideHollow
                      + " head=" + headDist.ToString("F2") + " tail=" + tailDist.ToString("F2"));
        }
    }

    private Vector3 getPositionFromDistance(float distance)
    {
        return new Vector3(
            distance * Mathf.Cos((startPosition * -2f + 5f) * 0.125f * Mathf.PI),
            distance * Mathf.Sin((startPosition * -2f + 5f) * 0.125f * Mathf.PI));
    }
}